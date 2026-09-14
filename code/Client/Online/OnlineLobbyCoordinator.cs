using System.Security.Cryptography;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Online;

/// <summary>Owns one online membership lifetime. EOS notifications never write authoritative gameplay state.</summary>
internal sealed class OnlineLobbyCoordinator : IDisposable
{
    private readonly IOnlineLobbyProvider _provider;
    private readonly TimeProvider _time;
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

    /// <summary>Creates one owner-thread coordination lifetime with an injectable monotonic clock.</summary>
    /// <param name="provider">Owned coordination adapter.</param>
    /// <param name="identity">Authenticated local product user identity.</param>
    /// <param name="time">Injectable monotonic deadline clock.</param>
    internal OnlineLobbyCoordinator(IOnlineLobbyProvider provider, OnlineProductUserId identity, TimeProvider? time = null)
    {
        _provider = provider;
        Identity = identity;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Authenticated local online identity, distinct from gameplay PlayerId.</summary>
    internal OnlineProductUserId Identity { get; }
    /// <summary>Compatible discovery cache and presentation filter.</summary>
    internal LobbyBrowser Browser { get; } = new();
    /// <summary>Current online membership, absent outside a joined lobby.</summary>
    internal OnlineLobby? Active { get; private set; }
    /// <summary>Whether the authenticated local identity owns the current lobby.</summary>
    internal bool IsHost => Active?.Owner.Equals(Identity) == true;
    /// <summary>Whether a membership mutation awaits completion.</summary>
    internal bool Busy { get; private set; }
    /// <summary>Whether Leave can release active membership or retry pending cleanup.</summary>
    internal bool CanLeave => Active is not null || Busy || _closing is not null || _pendingMembership is not null;
    /// <summary>Presentation-safe progress or actionable failure; never includes credential input.</summary>
    internal string Status { get; private set; } = "Browse or host a game.";

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

    /// <summary>Refreshes compatible lobbies without accepting an obsolete search completion.</summary>
    internal void Refresh()
    {
        if (_disposed || Busy || _searching)
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
        if (_disposed || Busy || Active is not null || _closing is not null || _pendingMembership is not null)
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
        if (_disposed || Busy || Active is not null || _closing is not null || _pendingMembership is not null)
        {
            return;
        }

        var lobby = Browser.Find(id);
        if (lobby is null || !lobby.Joinable)
        {
            Status = lobby is null ? "Lobby closed or not found. Refresh the browser." : !lobby.Compatible ? "Build/protocol incompatible." : lobby.Members == 8 ? "Lobby full." : "Lobby closed.";
            return;
        }

        if (lobby.Access == LobbyAccess.Locked && !lobby.Credential!.Verify(credential))
        {
            Status = "Incorrect password/access code.";
            return;
        }

        long epoch = Begin("Joining lobby…");
        _pendingMembership = epoch;
        _provider.Join(id, (joined, failure) =>
        {
            // Revalidate fresh provider state; an ID must never redirect admission into a replacement session.
            if (joined is not null && (!joined.Compatible || joined.Session != lobby.Session || joined.Access != lobby.Access || joined.Credential?.ExportVerifier() != lobby.Credential?.ExportVerifier()))
            {
                failure = "Lobby changed or is incompatible. Refresh and join again.";
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
        _provider.Update(Active! with { Name = name }, (updated, failure) =>
        {
            if (_disposed || epoch != _epoch)
            {
                return;
            }

            Busy = false;
            if (updated is not null && failure is null)
            {
                ApplyUpdate(updated);
            }

            Status = failure ?? "Lobby renamed.";
        });
    }

    /// <summary>TS-44 supplies a connected authenticated gateway; fake gateways exercise this boundary today.</summary>
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
        return _binding;
    }

    /// <summary>Enforces a monotonic deadline on pending coordination work.</summary>
    internal void Tick()
    {
        if (!Busy && _pendingMembership is not null && _time.GetElapsedTime(_started).TotalSeconds >= 60)
        {
            Status = "EOS membership cancellation is unresolved. Log out and log in to reset online services.";
        }

        if (!_disposed && !Busy && Active is not null && _binding?.Driver.Authority is { } authority && (_availabilityRetry is null || _time.GetElapsedTime(_availabilityRetry.Value).TotalSeconds >= 5))
        {
            bool open = authority.State.Phase == Trackstorm.Core.Sessions.SessionPhase.Lobby;
            if (Active.Open != open)
            {
                long epoch = Begin("Updating lobby availability…");
                _provider.SetJoinable(Active.Id, open, (updated, failure) =>
                {
                    if (!_disposed && epoch == _epoch)
                    {
                        Busy = false;
                        _availabilityRetry = failure is null ? null : _time.GetTimestamp();
                        if (updated is not null && failure is null)
                        {
                            ApplyUpdate(updated);
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
        var old = Active ?? _closing;
        bool destroy = Active is not null ? IsHost : _closingHost;
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

                    Status = failure is null ? (destroy ? "Lobby closed." : "Left lobby.") : "EOS leave/close failed. Retry Leave before creating another lobby.";
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
            if (lobby is not null)
            {
                _closing = lobby;
                _closingHost = host;
                Leave();
            }

            Status = failure ?? "EOS service failure.";
            return;
        }

        Active = lobby;
        Browser.Update(lobby);
        long membership = ++_membership;
        try
        {
            _watch = _provider.Watch(lobby.Id, updated =>
            {
                if (!_disposed && membership == _membership && Active?.Id == lobby.Id && Active.Session == lobby.Session)
                {
                    if (updated is null || !updated.Owner.Equals(lobby.Owner))
                    {
                        Leave();
                        Status = "Lobby closed.";
                    }
                    else
                    {
                        ApplyUpdate(updated);
                    }
                }
            });
        }
        catch (InvalidOperationException)
        {
            Leave();
            Status = "EOS lobby notifications unavailable. Refresh and retry.";
            return;
        }

        Status = "Online lobby joined. Gameplay connection is not available in this build.";
    }

    private void ApplyUpdate(OnlineLobby lobby)
    {
        if (Active is null || lobby.Id != Active.Id || lobby.Session != Active.Session || !lobby.Compatible)
        {
            return;
        }

        Active = lobby;
        Browser.Update(lobby);
        _binding?.MembershipChanged(lobby);
    }
}
