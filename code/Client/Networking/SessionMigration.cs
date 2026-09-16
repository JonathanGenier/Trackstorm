using System.Text.Json;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

/// <summary>Bounded checkpoint distribution and authenticated survivor agreement on the existing transport.</summary>
internal sealed class SessionMigration
{
    private readonly LobbyNetworkDriver _lobby;
    private readonly ITransportGateway _gateway;
    private readonly string _subject;
    private readonly Func<ulong, string?> _identity;
    private readonly Func<string, bool, ulong> _rebind;
    private readonly List<(MigrationCheckpoint State, byte[] Bytes, string Digest)> _retained = new();
    private readonly Dictionary<ulong, (double At, ulong Tick)> _acks = new();
    private readonly Dictionary<ulong, string[]> _offers = new();
    private readonly Dictionary<ulong, ulong> _voterPeers = new();
    private ulong[] _electorate = [];
    private double _seconds;
    private double _publishedAt = -1;
    private double _receivedAt;
    private double? _lostAt;
    private double? _attemptAt;
    private ulong _sequence;
    private ulong _candidate;
    private ulong _server;
    private bool _reported;
    private bool _failed;
    private bool _committed;
    private bool _departing;
    private bool _confirmedDeparture;
    private MigrationElection? _election;
    private string? _selection;

    /// <summary>Creates authenticated migration coordination; direct-IP remains explicitly opt-in.</summary>
    /// <param name="lobby">Existing gameplay admission authority.</param>
    /// <param name="gateway">Existing opaque transport.</param>
    /// <param name="subject">Authenticated local subject.</param>
    /// <param name="identity">Authenticated incoming peer resolver.</param>
    /// <param name="rebind">Recreates star connectivity to the selected subject without assigning gameplay authority.</param>
    internal SessionMigration(LobbyNetworkDriver lobby, ITransportGateway gateway, string subject, Func<ulong, string?> identity, Func<string, bool, ulong> rebind)
    {
        _lobby = lobby;
        _gateway = gateway;
        _subject = subject;
        _identity = identity;
        _rebind = rebind;
    }

    /// <summary>Current complete authoritative arena boundary, absent while in lobby.</summary>
    internal Func<(ResumeCheckpoint Arena, HostRestoreState Host)>? CaptureArena { get; set; }
    /// <summary>Installs the agreed gameplay boundary and resets prediction/presentation baselines.</summary>
    internal Action<MigrationCheckpoint, bool>? RestoreArena { get; set; }
    /// <summary>Notifies the provider adapter after Trackstorm has established authority.</summary>
    internal Action<string>? AuthorityChanged { get; set; }
    /// <summary>Latest locally observed world tick, used to reject excessive rollback.</summary>
    internal Func<ulong>? ObservedTick { get; set; }
    /// <summary>Blocks input, commands and gameplay advancement during lost authority or agreement.</summary>
    internal bool Frozen { get; private set; }
    /// <summary>True only while survivor transport is being coordinated.</summary>
    internal bool Negotiating => _attemptAt.HasValue && !_committed && !_failed;
    /// <summary>Player-facing bounded recovery progress.</summary>
    internal string Status => _failed ? "Host migration failed" : Negotiating ? "Migrating host — agreeing and restoring match" : Frozen ? "Host connection interrupted — waiting for recovery" : string.Empty;
    /// <summary>Last retained authenticated subject mapping, used only for migration/rebind admission.</summary>
    internal IReadOnlyDictionary<ulong, string>? Subjects => _retained.LastOrDefault().State?.Lobby.Subjects;

    /// <summary>Freezes an intentionally departing authority and gives reliable control time to drain.</summary>
    internal void AnnounceDeparture()
    {
        if (_lobby.Authority is null || _retained.Count == 0)
        {
            return;
        }

        _departing = true;
        Frozen = true;
        var state = _lobby.State!;
        foreach (ulong peer in _lobby.Authority.Peers.Keys)
        {
            try
            {
                Send(peer, new Control("leave", state.Session, state.AuthorityEpoch, state.CurrentHostId, string.Empty, []));
            }
            catch (InvalidOperationException)
            {
                _gateway.Disconnect(peer);
            }
        }
    }

    /// <summary>Advances caller-controlled time and bounded host-loss policy.</summary>
    /// <param name="seconds">Elapsed monotonic seconds.</param>
    internal void Advance(double seconds)
    {
        _seconds += seconds;
        if (_failed || _departing || _lobby.State is not { } state)
        {
            return;
        }

        if (Negotiating)
        {
            if (_seconds - _attemptAt!.Value >= 20)
            {
                Fail("survivors could not agree on a recoverable checkpoint");
            }
            else if (_candidate != _lobby.LocalPlayerId && !_reported && Connected(_server))
            {
                Send(_server, new Control("offer", state.Session, state.AuthorityEpoch, _candidate, string.Empty, _retained.Select(entry => entry.Digest).ToArray()));
                _reported = true;
            }

            return;
        }

        if (_lobby.Authority is not null)
        {
            // A partitioned old host cannot keep advancing while the other players transfer authority.
            var electorate = _electorate;
            if (electorate.Length >= 3)
            {
                ulong now = (ulong)(_seconds * 60);
                int alive = 1 + electorate.Count(id => id != state.CurrentHostId && _acks.TryGetValue(id, out var ack) && _seconds - ack.At <= 2 && now >= ack.Tick && now - ack.Tick <= 120);
                Frozen = _seconds > 2 && alive <= electorate.Length / 2;
                _lostAt = Frozen ? _lostAt ?? _seconds : null;
                if (_lostAt.HasValue && _seconds - _lostAt.Value >= state.GraceTicks / 60.0)
                {
                    Fail("authority lost its survivor quorum");
                    return;
                }
            }
            else
            {
                Frozen = false;
                _lostAt = null;
            }

            if (_seconds - _publishedAt >= 0.5)
            {
                Publish();
            }

            return;
        }

        if (_retained.Count == 0)
        {
            return;
        }

        bool lost = _confirmedDeparture || !Connected(_lobby.ServerPeer) || _seconds - _receivedAt >= 2;
        if (!lost)
        {
            _lostAt = null;
            Frozen = false;
            return;
        }

        Frozen = true;
        if (!_lostAt.HasValue && !_confirmedDeparture && Connected(_lobby.ServerPeer))
        {
            _gateway.Disconnect(_lobby.ServerPeer);
        }

        _lostAt ??= _seconds;
        if (!_confirmedDeparture && _seconds - _lostAt.Value < Math.Max(3, state.GraceTicks / 60.0))
        {
            return;
        }

        StartAttempt();
    }

    /// <summary>Consumes only migration packets; ordinary traffic remains with existing drivers.</summary>
    /// <param name="message">Actual gateway message.</param>
    /// <returns>Whether this packet belongs to migration control.</returns>
    internal bool Receive(TransportMessage message)
    {
        var bytes = message.Payload.Span;
        if (bytes.Length < 3 || bytes[0] != 'T' || bytes[1] != 'X')
        {
            return false;
        }

        if (_failed || message.Delivery != TransportDelivery.Reliable || !Connected(message.RemotePeerId))
        {
            return true;
        }

        try
        {
            if (bytes[2] == 1)
            {
                if (_lobby.Authority is not null || Negotiating || message.RemotePeerId != _lobby.ServerPeer)
                {
                    return true;
                }

                var checkpoint = MigrationCheckpointCodec.Decode(bytes[3..]);
                var state = _lobby.State!;
                if (checkpoint.Lobby.State.Session != state.Session || checkpoint.Lobby.State.AuthorityEpoch != state.AuthorityEpoch ||
                    checkpoint.Lobby.State.CurrentHostId != state.CurrentHostId || checkpoint.Lobby.State.Match != state.Match ||
                    checkpoint.Lobby.State.Revision > state.Revision || checkpoint.Lobby.State.Players.All(player => player.Id != _lobby.LocalPlayerId) ||
                    (_retained.Count > 0 && checkpoint.Sequence <= _retained[^1].State.Sequence))
                {
                    return true;
                }

                Retain(checkpoint, bytes[3..].ToArray());
                _receivedAt = _seconds;
                Send(message.RemotePeerId, new Control("ack", state.Session, state.AuthorityEpoch, state.CurrentHostId, _retained[^1].Digest, []));
                return true;
            }

            if (bytes[2] != 2 || bytes.Length > 2048)
            {
                return true;
            }

            var control = JsonSerializer.Deserialize<Control>(bytes[3..], new JsonSerializerOptions { MaxDepth = 4 });
            if (control is null || _lobby.State is not { } current || control.Session != current.Session || control.Epoch != current.AuthorityEpoch || control.Digest is null || control.Offers is null || control.Offers.Length > 4)
            {
                return true;
            }

            if (control.Kind == "ack" && _lobby.Authority is not null && _retained.Any(entry => entry.Digest == control.Digest))
            {
                ulong player = _lobby.Authority.PlayerId(message.RemotePeerId);
                if (player != 0)
                {
                    ulong tick = _retained.Single(entry => entry.Digest == control.Digest).State.Lobby.Tick;
                    _acks[player] = (_seconds, tick);
                }

                return true;
            }

            if (control.Kind == "leave" && _lobby.Authority is null && message.RemotePeerId == _lobby.ServerPeer && !Negotiating)
            {
                _confirmedDeparture = true;
                Frozen = true;
                return true;
            }

            if (!Negotiating || control.Candidate != _candidate)
            {
                return true;
            }

            ulong voter = Subjects!.FirstOrDefault(pair => pair.Value == _identity(message.RemotePeerId)).Key;
            if (_candidate == _lobby.LocalPlayerId && control.Kind == "offer" && voter != 0 && voter != current.CurrentHostId)
            {
                _offers[voter] = control.Offers;
                _voterPeers[voter] = message.RemotePeerId;
                Propose();
            }
            else if (_candidate != _lobby.LocalPlayerId && message.RemotePeerId == _server && control.Kind == "propose")
            {
                var selected = _retained.SingleOrDefault(entry => entry.Digest == control.Digest);
                if (selected.State is not null && Recoverable(selected.State) && (_selection is null || _selection == control.Digest))
                {
                    _election = new MigrationElection(selected.State, control.Digest);
                    if (_election.Candidate != _candidate)
                    {
                        throw new ArgumentException("Candidate mismatch.");
                    }

                    _selection = control.Digest;
                    Send(_server, control with { Kind = "vote" });
                }
            }
            else if (_candidate == _lobby.LocalPlayerId && control.Kind == "vote" && _election is not null)
            {
                _election.Vote(voter, control.Session, control.Epoch, control.Candidate, control.Digest);
                if (_election.Agreed)
                {
                    _election.Commit();
                    foreach (ulong peer in _voterPeers.Values)
                    {
                        Send(peer, control with { Kind = "commit" });
                    }

                    Install(control.Digest);
                }
            }
            else if (_candidate != _lobby.LocalPlayerId && message.RemotePeerId == _server && control.Kind == "commit" && _selection == control.Digest)
            {
                Install(control.Digest);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException or OverflowException)
        {
            if (Negotiating)
            {
                Fail("invalid migration agreement");
            }
        }

        return true;
    }

    private bool Connected(ulong peer) => _gateway.Connections.TryGetValue(peer, out var state) && state == TransportConnectionState.Connected;

    private bool Recoverable(MigrationCheckpoint checkpoint)
    {
        ulong tick = checkpoint.Arena?.Items.World.Tick ?? 0;
        ulong observed = ObservedTick?.Invoke() ?? tick;
        return checkpoint.Lobby.State.Match == _lobby.State!.Match && checkpoint.Lobby.State.Phase == _lobby.State.Phase && tick <= observed && observed - tick <= 240;
    }

    private void Publish()
    {
        _publishedAt = _seconds;
        var authority = _lobby.Authority!;
        if (authority.State.Players.Any(player => player.Connected && player.Id != authority.State.CurrentHostId && !authority.Peers.Values.Contains(player.Id)))
        {
            return;
        }

        try
        {
            var arena = authority.State.Phase == SessionPhase.Arena ? CaptureArena?.Invoke() : null;
            if (authority.State.Phase == SessionPhase.Arena && arena is null)
            {
                return;
            }

            var checkpoint = new MigrationCheckpoint(checked(++_sequence), authority.Capture(_subject), arena?.Arena, arena?.Host);
            if (_electorate.Length == 0 || !Frozen)
            {
                _electorate = authority.State.Players.Select(player => player.Id).ToArray();
            }

            byte[] bytes = MigrationCheckpointCodec.Encode(checkpoint);
            Retain(checkpoint, bytes);
            byte[] packet = new byte[bytes.Length + 3];
            packet[0] = (byte)'T';
            packet[1] = (byte)'X';
            packet[2] = 1;
            bytes.CopyTo(packet, 3);
            foreach (ulong peer in authority.Peers.Keys)
            {
                _gateway.Send(new TransportMessage(peer, packet, TransportDelivery.Reliable));
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Roster changes are captured after the existing vehicle driver reaches its next boundary.
        }
    }

    private void Retain(MigrationCheckpoint checkpoint, byte[] bytes)
    {
        if (_retained.Count > 0 && (_retained[^1].State.Lobby.State.AuthorityEpoch != checkpoint.Lobby.State.AuthorityEpoch || _retained[^1].State.Lobby.State.Match != checkpoint.Lobby.State.Match || _retained[^1].State.Lobby.State.Phase != checkpoint.Lobby.State.Phase))
        {
            _retained.Clear();
        }

        _retained.Add((checkpoint, bytes, MigrationCheckpointCodec.Digest(bytes)));
        if (_retained.Count > 4)
        {
            _retained.RemoveAt(0);
        }
    }

    private void StartAttempt()
    {
        try
        {
            var latest = _retained[^1];
            if (!Recoverable(latest.State))
            {
                throw new InvalidOperationException("No checkpoint of the current phase.");
            }

            _candidate = new MigrationElection(latest.State, latest.Digest).Candidate;
            _attemptAt = _seconds;
            _committed = false;
            _reported = false;
            _offers.Clear();
            _voterPeers.Clear();
            _selection = null;
            _election = null;
            _server = _rebind(latest.State.Lobby.Subjects[_candidate], _candidate == _lobby.LocalPlayerId);
            if (_candidate == _lobby.LocalPlayerId)
            {
                _offers[_candidate] = _retained.Select(entry => entry.Digest).ToArray();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Fail("no eligible replacement or recoverable checkpoint");
        }
    }

    private void Propose()
    {
        if (_selection is not null)
        {
            return;
        }

        foreach (var entry in _retained.AsEnumerable().Reverse())
        {
            if (!Recoverable(entry.State))
            {
                continue;
            }

            var voters = entry.State.Lobby.State.Players.Where(player => player.Connected && player.Id != entry.State.Lobby.State.CurrentHostId).Select(player => player.Id).ToArray();
            if (voters.Any(id => !_offers.TryGetValue(id, out var hashes) || !hashes.Contains(entry.Digest)))
            {
                continue;
            }

            _election = new MigrationElection(entry.State, entry.Digest);
            if (_election.Candidate != _candidate)
            {
                continue;
            }

            _selection = entry.Digest;
            _election.Vote(_candidate, _election.Session, _election.Epoch, _candidate, entry.Digest);
            foreach (ulong id in voters.Where(id => id != _candidate))
            {
                Send(_voterPeers[id], new Control("propose", _election.Session, _election.Epoch, _candidate, entry.Digest, []));
            }

            return;
        }
    }

    private void Install(string digest)
    {
        var checkpoint = _retained.Single(entry => entry.Digest == digest).State;
        bool host = _lobby.LocalPlayerId == _candidate;
        _lobby.InstallMigration(checkpoint, _candidate, _server);
        RestoreArena?.Invoke(checkpoint, host);
        _committed = true;
        _confirmedDeparture = false;
        Frozen = false;
        _lostAt = null;
        _attemptAt = null;
        _receivedAt = _seconds;
        _seconds = checkpoint.Lobby.Tick / 60.0;
        _receivedAt = _seconds;
        _publishedAt = _seconds;
        _acks.Clear();
        _electorate = [];
        _retained.Clear();
        AuthorityChanged?.Invoke(checkpoint.Lobby.Subjects[_candidate]);
    }

    private void Send(ulong peer, Control control)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(control);
        byte[] packet = new byte[body.Length + 3];
        packet[0] = (byte)'T';
        packet[1] = (byte)'X';
        packet[2] = 2;
        body.CopyTo(packet, 3);
        _gateway.Send(new TransportMessage(peer, packet, TransportDelivery.Reliable));
    }

    private void Fail(string reason)
    {
        _failed = true;
        Frozen = true;
        _lobby.FailMigration(reason);
    }

    private sealed record Control(string Kind, ulong Session, ulong Epoch, ulong Candidate, string Digest, string[] Offers);
}
