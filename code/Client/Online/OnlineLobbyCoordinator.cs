using System.Security.Cryptography;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Online;

/// <summary>Owns one online membership lifetime. EOS notifications never write authoritative gameplay state.</summary>
internal sealed class OnlineLobbyCoordinator : IDisposable
{
    /// <summary>Service round trips expire from request time; client acknowledgements cannot renew authority.</summary>
    internal const double CoordinationLeaseSeconds = 10;

    private readonly IOnlineLobbyProvider _provider;
    private readonly TimeProvider _time;
    private readonly ResumeLocatorStore? _resumeStore;
    private IDisposable? _watch;
    private OnlineSessionBinding? _binding;
    private long _epoch;
    private long _search;
    private long _started;
    private long _membership;
    private long _searchStarted;
    private bool _searching;
    private OnlineLobby? _closing;
    private bool _closingHost;
    private long? _availabilityRetry;
    private long? _pendingMembership;
    private long _completedMembership;
    private bool _disposed;
    private string? _joinCredential;
    private long _resumeRetry;
    private long _savedAt;
    private ulong _savedGeneration;
    private bool _resumePending;
    private string? _routingId;
    private ILeaseTransport? _routingTransport;
    private Task<LeaseRoute?>? _routingTask;
    private OnlineLobby? _routingLobby;
    private long _routingRequested;
    private long _routingEpoch;
    private bool _recoveringMembership;
    private bool _preserveLocator;
    private string? _migrationHost;
    private long? _migrationUpdateEpoch;
    private long? _migrationRetry;
    private bool _createdGameplaySession;
    private ResumeLocator? _returnLocator;
    private long? _coordinationConfirmed;
    private long? _coordinationRequested;
    private bool _coordinationPending;
    private bool _authorityRetired;
    private (string Subject, ulong Epoch, long At, string[] Survivors)? _retiredHost;
    private int _metadataUpdates;
    private int _ownershipUpdates;
    private int _joinedUpdates;
    private int _departedUpdates;
    private int _retiredCallbacks;
    private int _coordinationRequests;
    private int _coordinationResults;
    private int _coordinationSuccesses;
    private int _availabilityUpdates;
    private int _lobbyUpdates;
    private int _resumeAttempts;
    private string _lastCoordination = "none";
    private long? _decisionStarted;
    private bool _checkOnMenu;
    private string? _decisionOutcome;

    /// <summary>Creates one owner-thread coordination lifetime with an injectable monotonic clock.</summary>
    /// <param name="provider">Owned coordination adapter.</param>
    /// <param name="identity">Authenticated local product user identity.</param>
    /// <param name="time">Injectable monotonic deadline clock.</param>
    /// <param name="resumeStore">Optional local routing persistence; no authority or credentials are saved.</param>
    internal OnlineLobbyCoordinator(IOnlineLobbyProvider provider, OnlineProductUserId identity, TimeProvider? time = null, ResumeLocatorStore? resumeStore = null)
    {
        _provider = provider;
        Identity = identity;
        _time = time ?? TimeProvider.System;
        _resumeStore = resumeStore;
        SavedResume = _resumeStore?.Load(identity.Value);
        _routingId = SavedResume?.RoutingId;
        RetainedDecision = SavedResume is null ? RetainedSessionDecision.None : RetainedSessionDecision.Checking;
    }

    /// <summary>Production packet composition; absent in browser-only verification.</summary>
    internal Func<string?, Networking.EosP2pTransport>? TransportFactory { get; set; }
    /// <summary>Required trusted fencing transport in production; independent from EOS ownership.</summary>
    internal Func<ILeaseTransport>? LeaseFactory { get; set; }

    /// <summary>Authenticated local online identity, distinct from gameplay PlayerId.</summary>
    internal OnlineProductUserId Identity { get; }
    /// <summary>Compatible discovery cache and presentation filter.</summary>
    internal LobbyBrowser Browser { get; } = new();
    /// <summary>Current online membership, absent outside a joined lobby.</summary>
    internal OnlineLobby? Active { get; private set; }
    /// <summary>Whether the authenticated local identity owns the current lobby.</summary>
    internal bool IsHost => Active?.Owner.Equals(Identity) == true;
    /// <summary>Only explicit session creation bootstraps authority; provider promotion never does.</summary>
    internal bool StartsGameplayAuthority => _createdGameplaySession && Active?.AuthorityEpoch == 1;
    /// <summary>Whether a membership mutation awaits completion.</summary>
    internal bool Busy { get; private set; }
    /// <summary>Whether Leave can release active membership or retry pending cleanup.</summary>
    internal bool CanLeave => Active is not null || Busy || _closing is not null || _pendingMembership is not null || SavedResume is not null;
    /// <summary>Application-owned local diagnostic journal; never receives provider credentials.</summary>
    internal Core.Events.EventStream? EventLog { get; set; }

    /// <summary>Presentation-safe progress or actionable failure; never includes credential input.</summary>
    internal string Status { get; private set; } = "Browse or host a game.";
    /// <summary>Validated local routing hint until the restored assignment is acknowledged.</summary>
    internal ResumeLocator? SavedResume { get; private set; }
    /// <summary>Explicit menu choice; Choose is reached only after the current host validates the reservation.</summary>
    internal RetainedSessionDecision RetainedDecision { get; private set; }
    /// <summary>Whether the retained-session flow owns the menu instead of normal discovery.</summary>
    internal bool HasRetainedDecision => RetainedDecision != RetainedSessionDecision.None;
    /// <summary>Monotonic clock shared with the authority lifecycle.</summary>
    internal TimeProvider Clock => _time;
    /// <summary>Credential-free EOS coordination state for Developer Options and manual diagnosis.</summary>
    internal string Diagnostics
    {
        get
        {
            OnlineLobby? lobby = Active;
            double? proofAge = _coordinationConfirmed is long confirmed ? _time.GetElapsedTime(confirmed).TotalSeconds : null;
            bool coordinationAvailable = !_authorityRetired && proofAge < CoordinationLeaseSeconds && lobby?.MemberIds.Contains(Identity) == true;
            string members = lobby is null ? "none" : string.Join(",", lobby.MemberIds.Select(member => member.ToString()).Order());
            string locator = SavedResume is { } saved
                ? $"{saved.Lobby}/{saved.Session}/P{saved.Player}/g{saved.Generation}/e{saved.AuthorityEpoch}"
                : _returnLocator is { } retained
                    ? $"{retained.Lobby}/{retained.Session}/P{retained.Player}/g{retained.Generation}/e{retained.AuthorityEpoch}"
                    : "none";
            return $"EOS lobby: {lobby?.Id ?? "none"}; Trackstorm SessionId: {lobby?.Session.ToString() ?? "none"}; access: {lobby?.Access.ToString() ?? "none"}\n" +
                $"Local PUID: {Identity}; owner: {lobby?.Owner.ToString() ?? "none"}; gameplay host: {lobby?.HostIdentity.ToString() ?? "none"}; EOS members: [{members}]\n" +
                $"Coordinator active: {lobby is not null}; busy: {Busy}; membership generation: {_membership}; metadata/ownership/joined/departed callbacks: {_metadataUpdates}/{_ownershipUpdates}/{_joinedUpdates}/{_departedUpdates}; retired callbacks: {_retiredCallbacks}\n" +
                $"Coordination proof: {_lastCoordination}; pending: {_coordinationPending}; requests/results/successes: {_coordinationRequests}/{_coordinationResults}/{_coordinationSuccesses}; available: {coordinationAvailable}; age: {proofAge?.ToString("0.0") ?? "none"}s; lease: {CoordinationLeaseSeconds:0}s; authority retired: {_authorityRetired}\n" +
                $"Lobby/availability updates: {_lobbyUpdates}/{_availabilityUpdates}; recovering membership: {_recoveringMembership}; resume pending/attempts: {_resumePending}/{_resumeAttempts}; locator: {locator}; status: {Status}";
        }
    }

    /// <summary>A departed arena player can explicitly resume its still-reserved identity.</summary>
    internal bool CanResumeRetained => Active is null && !Busy && _closing is null && _returnLocator is not null;

    /// <summary>Fresh local service membership, independent of peer connectivity or provider ownership.</summary>
    internal bool CoordinationAvailable
    {
        get
        {
            bool fresh = _coordinationConfirmed.HasValue && _time.GetElapsedTime(_coordinationConfirmed.Value).TotalSeconds < CoordinationLeaseSeconds;
            if (!fresh && _coordinationConfirmed.HasValue && _binding?.Driver.Authority is not null)
            {
                _authorityRetired = true;
            }

            return !_authorityRetired && fresh && Active?.MemberIds.Contains(Identity) == true;
        }
    }

    /// <summary>The existing membership-proof boundary permanently retired this local authority.</summary>
    internal bool AuthorityRetired
    {
        get
        {
            _ = CoordinationAvailable;
            return _authorityRetired;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Leave();
        _disposed = true;
        _provider.Dispose();
    }

    /// <summary>Only a service departure event plus the full old-host lease window permits promotion.</summary>
    /// <param name="checkpoint">Exact prior authority and eligible survivor cohort.</param>
    /// <returns>Whether that authority is safely retired.</returns>
    internal bool HostRetired(Core.Sessions.MigrationCheckpoint checkpoint) => CoordinationAvailable && _retiredHost is { } retired &&
        retired.Subject == checkpoint.Lobby.Subjects[checkpoint.Lobby.State.CurrentHostId] && retired.Epoch == checkpoint.Lobby.State.AuthorityEpoch &&
        _time.GetElapsedTime(retired.At).TotalSeconds >= CoordinationLeaseSeconds &&
        retired.Survivors.ToHashSet(StringComparer.Ordinal).SetEquals(checkpoint.Lobby.State.Players.Where(player => player.Connected && player.Id != checkpoint.Lobby.State.CurrentHostId).Select(player => checkpoint.Lobby.Subjects[player.Id]));

    /// <summary>Returns the original monotonic service-retirement event boundary after all retirement checks pass.</summary>
    /// <param name="checkpoint">Exact prior authority and eligible survivor cohort.</param>
    /// <returns>The local monotonic event timestamp, or null while retirement is unsafe.</returns>
    internal long? HostRetiredAt(Core.Sessions.MigrationCheckpoint checkpoint) => HostRetired(checkpoint) ? _retiredHost!.Value.At : null;

    /// <summary>Records agreed gameplay authority before any EOS ownership coordination.</summary>
    /// <param name="subject">Authenticated identity chosen by Trackstorm election.</param>
    internal void MigrationCompleted(string subject)
    {
        ++_epoch;
        Busy = false;
        _migrationHost = subject;
        _migrationRetry = null;
        if (Active is not null && _binding?.Driver.State is { } state)
        {
            Active = Active with { GameplayHost = new OnlineProductUserId(subject), AuthorityEpoch = state.AuthorityEpoch };
            Browser.Update(Active);
        }

        CoordinateMigration();
    }

    /// <summary>Transfers the transient join credential into the owned packet connection.</summary>
    /// <returns>New gateway tied to the current platform.</returns>
    internal Networking.EosP2pTransport CreateTransport()
    {
        try
        {
            return TransportFactory?.Invoke(_joinCredential) ?? throw new InvalidOperationException("Online gameplay transport unavailable. Retry login.");
        }
        finally
        {
            _joinCredential = null;
        }
    }

    /// <summary>Refreshes compatible lobbies without accepting an obsolete search completion.</summary>
    internal void Refresh()
    {
        if (_disposed || Busy || _searching || HasRetainedDecision)
        {
            return;
        }

        long request = ++_search;
        _searchStarted = _time.GetTimestamp();
        _searching = true;
        Status = "Refreshing lobbies…";
        _provider.Search((lobbies, failure) =>
        {
            if (_disposed || request != _search)
            {
                return;
            }

            if (failure is null)
            {
                Browser.Replace(lobbies);
            }

            _searching = false;
            Status = failure ?? "Lobby browser updated.";
        });
    }

    /// <summary>Validates host input and creates a new lobby with a fresh session lifetime.</summary>
    /// <param name="name">Untrusted lobby display name.</param>
    /// <param name="access">Public or credential-gated admission policy.</param>
    /// <param name="credential">Transient access code; never retained or logged.</param>
    internal void Create(string name, LobbyAccess access, string? credential)
    {
        if (_disposed || Busy || Active is not null || _closing is not null || _pendingMembership is not null || HasRetainedDecision)
        {
            return;
        }

        name = LobbyName.Sanitize(name);
        if (name.Length == 0 || !Enum.IsDefined(access))
        {
            Status = "Enter a lobby name containing letters or digits (maximum 48 characters).";
            return;
        }

        LobbyCredential? verifier;
        try
        {
            verifier = access == LobbyAccess.Locked ? LobbyCredential.Create(credential ?? string.Empty) : null;
        }
        catch (ArgumentException)
        {
            Status = "Locked lobbies require a 4–64 character access code.";
            return;
        }

        ulong session = (BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) >> 1) | 1;
        var lobby = new OnlineLobby(string.Empty, name, Identity, session, access, 1, 8, OnlineLobby.CurrentProtocol, true, verifier);
        long epoch = Begin("Creating lobby…");
        _pendingMembership = epoch;
        _provider.Create(lobby, (created, failure) => CompleteMembership(epoch, created, failure, true));
    }

    /// <summary>Validates access and joins a fresh compatible online lobby.</summary>
    /// <param name="id">Logical EOS lobby identity.</param>
    /// <param name="credential">Transient access code; never retained or logged.</param>
    internal void Join(string id, string? credential = null)
    {
        if (_disposed || Busy || Active is not null || _closing is not null || _pendingMembership is not null || HasRetainedDecision)
        {
            return;
        }

        if (CanResumeRetained && _returnLocator!.Lobby == id)
        {
            ResumeRetained();
            return;
        }

        var lobby = Browser.Find(id);
        if (lobby is null || !lobby.Joinable)
        {
            Status = lobby is null ? "Lobby closed or not found. Refresh the browser." : lobby.VersionMismatch.Length > 0 ? lobby.VersionMismatch : !lobby.Compatible ? "Build/protocol incompatible." : lobby.Members == 8 ? "Lobby full." : "Lobby closed.";
            return;
        }

        if (lobby.Access == LobbyAccess.Locked && !lobby.Credential!.Verify(credential))
        {
            EventLog?.Record(Core.Events.EventCategory.Network, "Online join rejected", cause: "access denied", local: true);
            Status = "Incorrect password/access code.";
            return;
        }

        long epoch = Begin("Joining lobby…");
        _joinCredential = credential;
        _pendingMembership = epoch;
        _provider.Join(id, (joined, failure) =>
        {
            // Revalidate fresh provider state; an ID must never redirect admission into a replacement session.
            if (joined is not null && (!joined.Compatible || joined.VersionMismatch.Length > 0 || joined.Session != lobby.Session || joined.Access != lobby.Access || joined.Credential?.ExportVerifier() != lobby.Credential?.ExportVerifier()))
            {
                failure = joined.VersionMismatch.Length > 0 ? joined.VersionMismatch : "Lobby changed or is incompatible. Refresh and join again.";
            }

            CompleteMembership(epoch, joined, failure, false);
        });
    }

    /// <summary>Requests a host-only name update preserving session and admission state.</summary>
    /// <param name="name">Untrusted lobby display name.</param>
    internal void Rename(string name)
    {
        if (_disposed || Busy)
        {
            return;
        }

        if (!IsHost)
        {
            Status = "Only the host can rename this lobby.";
            return;
        }

        name = LobbyName.Sanitize(name);
        if (name.Length == 0)
        {
            Status = "Rename rejected: enter a non-empty lobby name (maximum 48 characters).";
            return;
        }

        long epoch = Begin("Renaming lobby…");
        _lobbyUpdates++;
        _provider.Update(Active! with { Name = name }, (updated, failure) =>
        {
            if (_disposed || epoch != _epoch)
            {
                return;
            }

            Busy = false;
            if (updated is not null && failure is null)
            {
                ApplyMetadataUpdate(updated);
            }

            Status = failure ?? "Lobby renamed.";
        });
    }

    /// <summary>Attaches the session-scoped authenticated packet gateway to existing lobby authority.</summary>
    /// <param name="gateway">Separately supplied authenticated packet gateway.</param>
    /// <param name="serverPeer">Established server peer on clients, zero for a host.</param>
    /// <param name="playerName">Requested local gameplay display name.</param>
    /// <returns>The validated result, or an explicit failure/absence.</returns>
    internal OnlineSessionBinding AttachTransport(ITransportGateway gateway, ulong serverPeer, string playerName)
    {
        if (_disposed || Active is null || _binding is not null)
        {
            throw new InvalidOperationException("Transport requires one active, unbound online lobby.");
        }

        _binding = new OnlineSessionBinding(this, gateway, serverPeer, playerName);
        if (!StartsGameplayAuthority && SavedResume is { } resume && resume.Session == Active.Session)
        {
            _binding.Driver.InspectReservation(resume.Player, resume.Generation);
        }

        return _binding;
    }

    /// <summary>Enforces a monotonic deadline on pending coordination work.</summary>
    internal void Tick()
    {
        if (!_disposed && _checkOnMenu && CanResumeRetained)
        {
            _checkOnMenu = false;
            ResumeRetained();
        }

        TickDecision();
        TickCoordination();
        TickRouting();
        if (!_disposed && ((SavedResume is not null && Active is null && RetainedDecision == RetainedSessionDecision.Checking) || _recoveringMembership))
        {
            TickResume();
        }

        if (!_disposed && Active is not null && _binding?.Driver.State?.ReconnectPolicy == Core.Sessions.SessionReconnectPolicy.FreshJoin &&
            (_savedGeneration != 0 || SavedResume is not null || _returnLocator is not null))
        {
            _resumeStore?.Clear();
            SavedResume = null;
            _returnLocator = null;
            _savedGeneration = 0;
        }

        if (!_disposed && Active is not null && _binding?.Driver is { State.ReconnectPolicy: Core.Sessions.SessionReconnectPolicy.RetainedResume, Reconnecting: false, CanResume: true, Failure.Length: 0 } driver &&
            (_savedGeneration != driver.Generation || (_binding.RoutingId is { } routingId && routingId != _routingId) || _time.GetElapsedTime(_savedAt).TotalSeconds >= 5))
        {
            _savedAt = _time.GetTimestamp();
            _savedGeneration = driver.Generation;
            _routingId = _binding.RoutingId ?? _routingId;
            _resumeStore?.Save(new ResumeLocator(Active.Id, Active.Session, driver.LocalPlayerId, driver.Generation, Identity.Value, Active.AuthorityEpoch, Active.HostIdentity.Value, _routingId));
            SavedResume = null;
        }

        if (!_disposed && !Busy)
        {
            CoordinateMigration();
        }

        if (!Busy && _pendingMembership is not null && _time.GetElapsedTime(_started).TotalSeconds >= 60)
        {
            Status = "EOS membership cancellation is unresolved. Log out and log in to reset online services.";
        }

        if (!_disposed && !Busy && IsHost && Active is not null && _binding?.Driver.Authority is { } authority && (_availabilityRetry is null || _time.GetElapsedTime(_availabilityRetry.Value).TotalSeconds >= 5))
        {
            bool open = authority.CanJoin && _binding.Driver.Migration?.Frozen != true;
            if (Active.Open != open)
            {
                long epoch = Begin("Updating lobby availability…");
                _availabilityUpdates++;
                _provider.SetJoinable(Active.Id, open, (updated, failure) =>
                {
                    if (!_disposed && epoch == _epoch)
                    {
                        Busy = false;
                        _availabilityRetry = failure is null ? null : _time.GetTimestamp();
                        if (updated is not null && failure is null)
                        {
                            ApplyMetadataUpdate(updated);
                        }

                        Status = failure ?? "Lobby availability updated.";
                    }
                });
            }
        }

        if (_searching && _time.GetElapsedTime(_searchStarted).TotalSeconds >= 60)
        {
            ++_search;
            _searching = false;
            Status = "EOS search timed out. Refresh to retry.";
        }

        if (Busy && _time.GetElapsedTime(_started).TotalSeconds >= 60)
        {
            if (_closing is not null)
            {
                ++_epoch;
                Busy = false;
                Status = "EOS leave/close timed out. Retry Leave before creating another lobby.";
                return;
            }

            Leave();
            Status = "EOS operation timed out. Refresh or retry.";
        }
    }

    /// <summary>Invalidates local membership and releases or destroys the associated online lobby.</summary>
    internal void Leave()
    {
        if (HasRetainedDecision && SavedResume is not null)
        {
            _returnLocator = SavedResume;
        }

        RetainedDecision = RetainedSessionDecision.None;
        _decisionStarted = null;
        bool retainLobby = _binding?.Driver.Migration?.Subjects is not null || _binding?.Driver.State?.AuthorityEpoch > 1;
        bool retainPlayer = Active is not null && _binding?.Driver is { State.ReconnectPolicy: Core.Sessions.SessionReconnectPolicy.RetainedResume, CanResume: true } && _binding.Driver.ResumeStatus != "Resume rejected" && (_binding.Driver.Authority is null || retainLobby);
        if (retainPlayer)
        {
            var driver = _binding!.Driver;
            _returnLocator = new ResumeLocator(Active!.Id, Active.Session, driver.LocalPlayerId, driver.Generation, Identity.Value, driver.State!.AuthorityEpoch, Active.HostIdentity.Value, _binding.RoutingId ?? _routingId);
            _resumeStore?.Save(_returnLocator);
            _checkOnMenu = true;
        }

        SavedResume = null;
        if (!_preserveLocator && !retainPlayer && _returnLocator is null)
        {
            _resumeStore?.Clear();
        }

        _recoveringMembership = false;
        _resumePending = false;
        _routingTransport?.Dispose();
        _routingTransport = null;
        _routingTask = null;
        _routingLobby = null;
        _routingId = null;
        _savedGeneration = 0;
        _joinCredential = null;
        if (_closing is not null && Busy)
        {
            return;
        }

        ++_membership;
        ++_epoch;
        ++_search;
        _searching = false;
        _watch?.Dispose();
        _watch = null;
        _binding?.Dispose();
        _binding = null;
        _availabilityRetry = null;
        _migrationHost = null;
        _migrationUpdateEpoch = null;
        _migrationRetry = null;
        _createdGameplaySession = false;
        var old = Active ?? _closing;
        bool destroy = Active is not null ? IsHost && !retainLobby : _closingHost;
        _closing = old;
        _closingHost = destroy;
        Active = null;
        Busy = false;
        Status = _pendingMembership is null ? "Left lobby." : "Canceling online membership…";
        if (old is not null)
        {
            Browser.Remove(old.Id);
            long epoch = Begin(destroy ? "Closing lobby…" : "Leaving lobby…");
            _provider.Leave(old.Id, destroy, failure =>
            {
                if (!_disposed && epoch == _epoch)
                {
                    Busy = false;
                    if (failure is null)
                    {
                        _closing = null;
                    }

                    EventLog?.Record(Core.Events.EventCategory.Session, failure is null ? (destroy ? "Online lobby closed" : "Online lobby left") : "Online lobby cleanup failed", local: true);
                    Status = failure is null ? _decisionOutcome ?? (destroy ? "Lobby closed." : "Left lobby.") : "EOS leave/close failed. Retry Leave before creating another lobby.";
                }
            });
        }
    }

    /// <summary>Retains only an acknowledged routing hint when the application exits without choosing Leave.</summary>
    internal void PreserveResumeOnShutdown()
    {
        if (Active is not null && _binding?.Driver is { State.ReconnectPolicy: Core.Sessions.SessionReconnectPolicy.RetainedResume, CanResume: true, Failure.Length: 0 } driver)
        {
            _preserveLocator = true;
            _resumeStore?.Save(new ResumeLocator(Active.Id, Active.Session, driver.LocalPlayerId, driver.Generation, Identity.Value, Active.AuthorityEpoch, Active.HostIdentity.Value, _binding.RoutingId ?? _routingId));
        }
    }

    /// <summary>Explicitly resumes a player's retained identity through membership and Core authorization.</summary>
    internal void ResumeRetained()
    {
        if (!CanResumeRetained)
        {
            return;
        }

        SavedResume = _returnLocator;
        _decisionOutcome = null;
        _routingId = SavedResume!.RoutingId;
        _returnLocator = null;
        _resumeRetry = 0;
        RetainedDecision = RetainedSessionDecision.Checking;
        _decisionStarted = _time.GetTimestamp();
        TickResume();
    }

    /// <summary>Submits exactly one explicit choice to the existing authenticated lobby stream.</summary>
    /// <param name="reconnect">Reconnect or permanently Leave Match.</param>
    internal void DecideRetained(bool reconnect)
    {
        if (RetainedDecision != RetainedSessionDecision.Choose || _binding?.Driver.DecideReservation(reconnect) != true)
        {
            return;
        }

        RetainedDecision = reconnect ? RetainedSessionDecision.Reconnecting : RetainedSessionDecision.Leaving;
        _decisionStarted = _time.GetTimestamp();
        Status = reconnect ? "Reconnecting…" : "Leaving match — waiting for confirmation…";
    }

    /// <summary>Retries a failed check without overlapping an earlier connection or membership operation.</summary>
    internal void RetryRetained()
    {
        if (RetainedDecision == RetainedSessionDecision.Failed && !Busy)
        {
            if (_closing is not null)
            {
                Leave();
                RetainedDecision = RetainedSessionDecision.Failed;
                _checkOnMenu = true;
                return;
            }

            RetainedDecision = RetainedSessionDecision.None;
            ResumeRetained();
        }
    }

    /// <summary>Returns from an unavailable check to the browser without claiming abandonment or erasing its hint.</summary>
    internal void DismissRetainedFailure()
    {
        if (RetainedDecision == RetainedSessionDecision.Failed)
        {
            RetainedDecision = RetainedSessionDecision.None;
            Status = "Reservation release was not confirmed. Resume previous session can retry.";
        }
    }

    /// <summary>Returns a failed startup transport to a recoverable decision without claiming a release.</summary>
    internal void FailRetainedConnection()
    {
        if (HasRetainedDecision)
        {
            string? mismatch = _binding?.Driver.ResumeStatus == "Game version mismatch" ? _binding.Driver.Failure : null;
            EndDecision(mismatch ?? "Connection failed. Match release was not confirmed. Retry or return to the browser.", mismatch is not null);
        }
    }

    private void TickDecision()
    {
        if (_disposed || RetainedDecision is RetainedSessionDecision.None or RetainedSessionDecision.Failed)
        {
            return;
        }

        _decisionStarted ??= _time.GetTimestamp();
        var driver = _binding?.Driver;
        if (driver?.Reservation is Core.Sessions.ReservationResult.Missing or Core.Sessions.ReservationResult.Abandoned)
        {
            EndDecision(driver.Reservation == Core.Sessions.ReservationResult.Abandoned ? "Left match. Your final statistics remain with that match." : "The retained match reservation is no longer available.", true);
        }
        else if (RetainedDecision == RetainedSessionDecision.Checking && driver?.Reservation == Core.Sessions.ReservationResult.Available)
        {
            RetainedDecision = RetainedSessionDecision.Choose;
            Status = "You are still part of a match. Reconnect to continue, or Leave Match to release your place. Your match statistics will remain.";
        }
        else if (RetainedDecision == RetainedSessionDecision.Reconnecting && driver is { State: not null, Reconnecting: false, NeedsArenaCheckpoint: false, Failure.Length: 0 })
        {
            RetainedDecision = RetainedSessionDecision.None;
            _decisionStarted = null;
        }
        else if (driver?.ResumeStatus == "Game version mismatch")
        {
            EndDecision(driver.Failure, true);
        }
        else if (driver?.Failure.Length > 0 || (RetainedDecision != RetainedSessionDecision.Choose && _time.GetElapsedTime(_decisionStarted.Value).TotalSeconds >= 20))
        {
            EndDecision("The retained match could not be reached. Release was not confirmed. Retry or return to the browser.", false);
        }
    }

    private void EndDecision(string status, bool resolved)
    {
        _checkOnMenu = false;
        _decisionOutcome = status;
        ResumeLocator? hint = SavedResume ?? _returnLocator;
        if (!resolved && Active is not null && _binding?.Driver is { State: not null } driver)
        {
            hint = new ResumeLocator(Active.Id, Active.Session, driver.LocalPlayerId, driver.Generation, Identity.Value, driver.State.AuthorityEpoch, Active.HostIdentity.Value, _binding.RoutingId ?? _routingId);
        }

        _binding?.Dispose();
        _binding = null;
        SavedResume = null;
        _returnLocator = resolved ? null : hint;
        if (resolved)
        {
            _resumeStore?.Clear();
        }
        else if (hint is not null)
        {
            _resumeStore?.Save(hint);
        }

        Leave();
        RetainedDecision = resolved ? RetainedSessionDecision.None : RetainedSessionDecision.Failed;
        Status = status;
    }

    private void CoordinateMigration()
    {
        if (_migrationUpdateEpoch != _epoch && (_migrationRetry is null || _time.GetElapsedTime(_migrationRetry.Value).TotalSeconds >= 5) && IsHost && Active is not null && _migrationHost is { } subject && _binding?.Driver.State is { } state)
        {
            _migrationUpdateEpoch = _epoch;
            long epoch = _epoch;
            var updated = Active with { GameplayHost = new OnlineProductUserId(subject), AuthorityEpoch = state.AuthorityEpoch };
            _lobbyUpdates++;
            _provider.Update(updated, (lobby, failure) =>
            {
                if (!_disposed && epoch == _epoch && _migrationHost == subject)
                {
                    if (lobby is not null && failure is null)
                    {
                        ApplyMetadataUpdate(lobby);
                        if (Active?.AuthorityEpoch != state.AuthorityEpoch || Active.HostIdentity.Value != subject)
                        {
                            _migrationUpdateEpoch = null;
                            _migrationRetry = _time.GetTimestamp();
                            Status = "Host migration routing update was not confirmed; retrying.";
                            return;
                        }

                        Status = "Host migration completed.";
                        if (Identity.Value != subject)
                        {
                            _migrationUpdateEpoch = epoch;
                            _provider.Promote(lobby.Id, new OnlineProductUserId(subject), promotionFailure =>
                            {
                                if (!_disposed && epoch == _epoch && _migrationHost == subject)
                                {
                                    _migrationUpdateEpoch = null;
                                    _migrationRetry = promotionFailure is null ? null : _time.GetTimestamp();
                                    _migrationHost = promotionFailure is null ? null : subject;
                                    Status = promotionFailure ?? "Host migration completed.";
                                }
                            });
                        }
                        else
                        {
                            _migrationUpdateEpoch = null;
                            _migrationHost = null;
                        }
                    }
                    else
                    {
                        _migrationUpdateEpoch = null;
                        _migrationRetry = _time.GetTimestamp();
                        Status = failure ?? "Host migration routing update was not confirmed; retrying.";
                    }
                }
            });
        }
    }

    private long Begin(string status)
    {
        ++_search;
        _searching = false;
        Busy = true;
        _started = _time.GetTimestamp();
        Status = status;
        return ++_epoch;
    }

    private void CompleteMembership(long epoch, OnlineLobby? lobby, string? failure, bool host)
    {
        if (epoch <= _completedMembership)
        {
            return;
        }

        _completedMembership = epoch;
        if (_pendingMembership == epoch)
        {
            _pendingMembership = null;
        }

        if (_disposed || epoch != _epoch)
        {
            if (!_disposed && lobby is not null && !(Active?.Id == lobby.Id && Active.Session == lobby.Session) && !(_closing?.Id == lobby.Id && _closing.Session == lobby.Session))
            {
                _closing = lobby;
                _closingHost = host;
                Busy = false;
                Leave();
            }

            return;
        }

        if (!Busy)
        {
            return;
        }

        Busy = false;
        if (lobby is null || failure is not null)
        {
            _joinCredential = null;
            if (lobby is not null)
            {
                _closing = lobby;
                _closingHost = host;
                Leave();
            }

            EventLog?.Record(Core.Events.EventCategory.Network, "Online membership failed", cause: "service operation failed", local: true);
            Status = failure ?? "EOS service failure.";
            return;
        }

        Active = lobby;
        _closing = null;
        _returnLocator = null;
        _coordinationConfirmed = null;
        _coordinationRequested = null;
        _coordinationPending = false;
        _authorityRetired = false;
        _retiredHost = null;
        _createdGameplaySession = host;
        EventLog?.Record(Core.Events.EventCategory.Session, host ? "Online lobby created" : "Online lobby joined", local: true);
        Browser.Update(lobby);
        long membership = ++_membership;
        try
        {
            _watch = _provider.Watch(
                lobby.Id,
                (updated, update) =>
            {
                if (!_disposed && membership == _membership && Active?.Id == lobby.Id && Active.Session == lobby.Session)
                {
                    OnlineLobbyUpdateKind kind = update.Kind;
                    if (kind == OnlineLobbyUpdateKind.Metadata)
                    {
                        _metadataUpdates++;
                    }
                    else if (kind == OnlineLobbyUpdateKind.Ownership)
                    {
                        _ownershipUpdates++;
                    }
                    else if (kind == OnlineLobbyUpdateKind.Joined)
                    {
                        _joinedUpdates++;
                    }
                    else if (kind == OnlineLobbyUpdateKind.Departed)
                    {
                        _departedUpdates++;
                    }

                    if (kind == OnlineLobbyUpdateKind.Closure)
                    {
                        if (HasRetainedDecision)
                        {
                            FailRetainedConnection();
                        }
                        else if (!IsHost && _binding?.Driver.State?.ReconnectPolicy == Core.Sessions.SessionReconnectPolicy.RetainedResume)
                        {
                            _recoveringMembership = true;
                            Active = Active with { MemberIds = Active.MemberIds.Where(member => !member.Equals(Identity)).ToArray() };
                            Status = "Connection interrupted";
                        }
                        else
                        {
                            Leave();
                            Status = "Lobby closed.";
                        }
                    }
                    else if (kind is OnlineLobbyUpdateKind.Joined or OnlineLobbyUpdateKind.Departed)
                    {
                        ApplyMembershipUpdate(updated, update);
                    }
                    else if (updated is null && kind is OnlineLobbyUpdateKind.Metadata or OnlineLobbyUpdateKind.Ownership)
                    {
                        Status = $"Ignored an incomplete {kind.ToString().ToLowerInvariant()} refresh; membership is unchanged.";
                    }
                    else if (updated is not null)
                    {
                        ApplyMetadataUpdate(updated);
                    }
                }
            },
                subject =>
            {
                if (_disposed || membership != _membership || Active?.Id != lobby.Id || Active.Session != lobby.Session)
                {
                    return;
                }

                _retiredCallbacks++;
                if (subject.Equals(Identity) && _binding?.Driver.Authority is not null)
                {
                    _authorityRetired = true;
                }

                var state = _binding?.Driver.State;
                string host = state is not null && _binding?.Driver.Migration?.Subjects?.TryGetValue(state.CurrentHostId, out string? established) == true ? established : Active.HostIdentity.Value;
                if (subject.Value == host)
                {
                    _retiredHost = (host, state?.AuthorityEpoch ?? Active.AuthorityEpoch, _time.GetTimestamp(), Active.MemberIds.Where(member => member.Value != host).Select(member => member.Value).ToArray());
                }
            });
        }
        catch (InvalidOperationException)
        {
            if (HasRetainedDecision)
            {
                FailRetainedConnection();
            }
            else
            {
                Leave();
                Status = "EOS lobby notifications unavailable. Refresh and retry.";
            }

            return;
        }

        Status = "Online lobby joined. Connecting gameplay…";
        TickCoordination();
    }

    private void ApplyUpdate(OnlineLobby lobby)
    {
        if (Active is null || lobby.Id != Active.Id || lobby.Session != Active.Session || !lobby.Compatible)
        {
            return;
        }

        OnlineLobby current = Active;
        bool olderAuthority = lobby.AuthorityEpoch < current.AuthorityEpoch;
        bool conflictingCurrentAuthority = lobby.AuthorityEpoch == current.AuthorityEpoch && !lobby.HostIdentity.Equals(current.HostIdentity);
        if (olderAuthority || conflictingCurrentAuthority)
        {
            lobby = lobby with { GameplayHost = current.HostIdentity, AuthorityEpoch = current.AuthorityEpoch };
            Status = olderAuthority
                ? "Ignored stale lobby routing metadata; retained the current Trackstorm host."
                : "Ignored conflicting lobby routing metadata; retained the current Trackstorm host.";
        }

        Active = lobby;
        Browser.Update(lobby);
        _binding?.MembershipChanged(lobby);
        CoordinateMigration();
    }

    private void ApplyMetadataUpdate(OnlineLobby lobby)
    {
        if (Active is not null)
        {
            lobby = lobby with { Members = Active.Members, MemberIds = Active.MemberIds };
        }

        ApplyUpdate(lobby);
    }

    private void ApplyMembershipUpdate(OnlineLobby? refreshed, OnlineLobbyUpdate update)
    {
        if (Active is null || update.Target is not { } target)
        {
            Status = $"Ignored an incomplete {update.Kind.ToString().ToLowerInvariant()} event; membership is unchanged.";
            return;
        }

        OnlineLobby current = Active;
        OnlineLobby lobby = refreshed is { } snapshot && snapshot.Id == current.Id && snapshot.Session == current.Session && snapshot.Compatible
            ? snapshot
            : current;
        var members = update.Kind == OnlineLobbyUpdateKind.Joined
            ? current.MemberIds.Append(target).Distinct().ToArray()
            : current.MemberIds.Where(member => !member.Equals(target)).ToArray();
        ApplyUpdate(lobby with { Members = members.Length, MemberIds = members });
    }

    private void TickCoordination()
    {
        if (_disposed || Active is null || _coordinationPending ||
            (_coordinationRequested.HasValue && _time.GetElapsedTime(_coordinationRequested.Value).TotalSeconds < 5))
        {
            return;
        }

        _ = CoordinationAvailable;
        long membership = _membership;
        long requested = _time.GetTimestamp();
        _coordinationRequested = requested;
        _coordinationPending = true;
        _coordinationRequests++;
        _lastCoordination = "requested";
        _provider.ConfirmMembership(Active.Id, accepted =>
        {
            if (_disposed || membership != _membership)
            {
                return;
            }

            _ = CoordinationAvailable;
            _coordinationPending = false;
            _coordinationResults++;
            if (accepted && _time.GetElapsedTime(requested).TotalSeconds < CoordinationLeaseSeconds)
            {
                _coordinationConfirmed = requested;
                _coordinationSuccesses++;
                _lastCoordination = "accepted";
            }
            else
            {
                _lastCoordination = accepted ? "late" : "rejected";
            }
        });
    }

    private void TickResume()
    {
        if (_resumePending || (_resumeRetry != 0 && _time.GetElapsedTime(_resumeRetry).TotalSeconds < 2))
        {
            return;
        }

        string id = Active?.Id ?? SavedResume!.Lobby;
        ulong session = Active?.Session ?? SavedResume!.Session;
        string host = Active?.HostIdentity.Value ?? SavedResume!.Host;
        ulong authorityEpoch = Active?.AuthorityEpoch ?? SavedResume!.AuthorityEpoch;
        long epoch = _epoch;
        _resumeRetry = _time.GetTimestamp();
        _resumePending = true;
        _resumeAttempts++;
        Status = "Reconnecting";
        _provider.Resume(id, (lobby, failure) =>
        {
            if (_disposed || epoch != _epoch)
            {
                if (lobby is not null && Active?.Id != lobby.Id)
                {
                    _provider.Leave(lobby.Id, false, _ => { });
                }

                return;
            }

            _resumePending = false;
            if (failure is not null || lobby is null)
            {
                Status = "Session unavailable — retrying";
                return;
            }

            if (!lobby.Compatible || lobby.VersionMismatch.Length > 0 || lobby.Session != session || !lobby.MemberIds.Contains(Identity))
            {
                RejectResume(lobby);
                return;
            }

            if (Active is null && SavedResume?.RoutingId is { } routingId && LeaseFactory is { } factory)
            {
                _closing = lobby;
                _closingHost = false;
                _resumePending = true;
                _routingLobby = lobby;
                _routingEpoch = epoch;
                _routingRequested = _time.GetTimestamp();
                _routingTransport = factory();
                _routingTask = _routingTransport.Resolve(routingId);
                Status = "Resolving trusted gameplay host…";
                return;
            }

            bool invalidRestartAuthority = Active is null && (lobby.AuthorityEpoch < authorityEpoch || (lobby.AuthorityEpoch == authorityEpoch && lobby.HostIdentity.Value != host));
            if (Active is null && host == Identity.Value && lobby.HostIdentity.Equals(Identity))
            {
                _closing = lobby;
                _closingHost = false;
                Status = "Waiting for replacement host routing…";
                return;
            }

            if (invalidRestartAuthority)
            {
                RejectResume(lobby);
                return;
            }

            _recoveringMembership = false;
            if (Active is null)
            {
                long operation = Begin("Reconnecting");
                CompleteMembership(operation, lobby, null, false);
            }
            else
            {
                ApplyUpdate(lobby);
            }
        });
    }

    private void TickRouting()
    {
        if (_routingTask is null || (!_routingTask.IsCompleted && _time.GetElapsedTime(_routingRequested).TotalSeconds < 3))
        {
            return;
        }

        var route = _routingTask.IsCompletedSuccessfully ? _routingTask.Result : null;
        var lobby = _routingLobby;
        _routingTask = null;
        _routingLobby = null;
        _routingTransport?.Dispose();
        _routingTransport = null;
        _resumePending = false;
        double age = _time.GetElapsedTime(_routingRequested).TotalSeconds;
        if (_disposed || _routingEpoch != _epoch || Active is not null || SavedResume is not { } saved || lobby is null)
        {
            return;
        }

        if (route is null || age >= 3 || route.RoutingId != saved.RoutingId ||
            route.Epoch < saved.AuthorityEpoch || route.Epoch > 9007199254740991 ||
            (route.Epoch == saved.AuthorityEpoch && route.Holder != saved.Host) ||
            route.RemainingSeconds <= age || route.RemainingSeconds > Core.Sessions.AuthorityLease.DurationSeconds || !double.IsFinite(route.RemainingSeconds) ||
            route.Holder is not { Length: 32 } || !route.Holder.All(char.IsAsciiHexDigit) || route.Holder.All(c => c == '0') ||
            string.Equals(route.Holder, Identity.Value, StringComparison.OrdinalIgnoreCase))
        {
            Status = "Waiting for replacement host routing…";
            return;
        }

        // A read only selects the authenticated connection target. Core Resume still owns admission.
        _routingId = saved.RoutingId;
        lobby = lobby with { GameplayHost = new OnlineProductUserId(route.Holder), AuthorityEpoch = route.Epoch };
        _recoveringMembership = false;
        _closing = null;
        long operation = Begin("Reconnecting");
        CompleteMembership(operation, lobby, null, false);
    }

    private void RejectResume(OnlineLobby lobby)
    {
        _closing = lobby;
        _closingHost = false;
        EndDecision(lobby.VersionMismatch.Length > 0 ? lobby.VersionMismatch : "Resume rejected. Session changed or identity is unavailable.", true);
    }
}
