using Trackstorm.Core.Development;
using Trackstorm.Core.Events;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

internal sealed partial class LobbyNetworkDriver
{
    private GameplayConfigurationState? _sessionConfiguration;
    private readonly Dictionary<ulong, (ulong Generation, ulong Revision)> _configurationSent = new();
    private readonly Dictionary<ulong, ulong> _configurationRequests = new();
    private ulong _nextConfigurationRequest;
    private ulong _pendingConfigurationRequest;
    private double _configurationRequestedAt;

    internal GameplayConfigurationState? Configuration => Authority?.Configuration ?? _sessionConfiguration;
    internal Func<ulong, IReadOnlyDictionary<string, double>, (bool Accepted, string Error)>? ConfigureArena { get; set; }
    internal bool ConfigurationPending => _pendingConfigurationRequest != 0;
    internal (ulong Revision, string Error)? ConfigurationResult { get; private set; }
    internal bool CanConfigure => State is not null && LocalPlayerId != 0 && Configuration is not null &&
        Failure.Length == 0 && !Reconnecting && !JoiningArena && !NeedsArenaCheckpoint && !_leaveAt.HasValue && Migration?.Frozen != true;

    internal bool RequestConfiguration(IReadOnlyDictionary<string, double> edits, out string error)
    {
        error = "Session configuration is unavailable or a request is pending.";
        if (!CanConfigure || ConfigurationPending) return false;
        ConfigurationResult = null;
        if (Authority is not null)
        {
            var result = ApplyConfiguration(0, edits);
            error = result.Error;
            return result.Accepted;
        }
        try
        {
            ulong request = checked(++_nextConfigurationRequest);
            byte[] payload = ConfigurationRequestCodec.Encode(request, State!.Match, edits);
            _pendingConfigurationRequest = request;
            _configurationRequestedAt = _seconds;
            SendConfiguration(ServerPeer, Generation, payload);
            error = string.Empty;
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private (bool Accepted, string Error) ApplyConfiguration(ulong peer, IReadOnlyDictionary<string, double> edits)
    {
        if (Authority is null || Migration?.Frozen == true || _leaveAt.HasValue ||
            (peer != 0 && (Authority.PlayerId(peer) == 0 || Authority.IsPendingJoin(peer))))
            return (false, "Session authority unavailable.");
        if (State!.Phase == SessionPhase.Arena)
            return ConfigureArena?.Invoke(peer, edits) ?? (false, "Arena configuration is synchronizing.");
        var previous = Authority.Configuration.Configuration;
        bool accepted = Authority.TryConfigure(peer, edits, out string error);
        if (accepted)
        {
            foreach (var option in GameplayOptions.All.Where(option => option.Read(previous) != option.Read(Authority.Configuration.Configuration)))
                Events.Record(EventCategory.Developer, "Setting changed", actor: peer == 0 ? LocalPlayerId : Authority.PlayerId(peer), context: option.Key, amount: option.Read(Authority.Configuration.Configuration), previous: option.Read(previous));
        }
        return (accepted, error);
    }

    private static bool IsConfigurationChannel(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 'T' && bytes[1] == 'D';

    private void SendConfiguration(ulong peer, ulong generation, byte[] payload) =>
        Send(peer, [(byte)'T', (byte)'D', 1, .. ConnectionEnvelope.Encode(State!.Session, generation, payload, State.AuthorityEpoch)]);

    private void PublishConfiguration()
    {
        if (Authority is null || Migration?.Frozen == true) return;
        foreach (ulong retired in _configurationSent.Keys.Where(peer => !Authority.Peers.ContainsKey(peer)).ToArray())
        {
            _configurationSent.Remove(retired);
            _configurationRequests.Remove(retired);
        }
        foreach (var (peer, player) in Authority.Peers.ToArray())
        {
            if (Authority.IsPendingJoin(peer)) continue;
            ulong generation = State!.Players.Single(value => value.Id == player).Generation;
            var boundary = (generation, Authority.Configuration.Revision);
            if (_configurationSent.TryGetValue(peer, out var sent) && sent == boundary) continue;
            SendConfiguration(peer, generation, [3, .. GameplayConfigurationCodec.Encode(State.Session, Authority.Configuration)]);
            _configurationSent[peer] = boundary;
        }
    }

    private void ReceiveConfiguration(TransportMessage message)
    {
        try
        {
            if (message.Payload.Length < 4 || message.Payload.Span[2] != 1 || State is null ||
                message.Delivery != TransportDelivery.Reliable || Migration?.Frozen == true ||
                (Reconnecting && !NeedsArenaCheckpoint)) throw new ArgumentException("Unavailable configuration channel.");
            ulong player = Authority?.PlayerId(message.RemotePeerId) ?? (message.RemotePeerId == ServerPeer ? LocalPlayerId : 0);
            var record = State.Players.SingleOrDefault(value => value.Id == player);
            if (record is null || !record.Connected || Authority?.IsPendingJoin(message.RemotePeerId) == true)
                throw new ArgumentException("Unadmitted configuration sender.");
            var payload = ConnectionEnvelope.Decode(message.Payload.Span[3..], State.Session, record.Generation, State.AuthorityEpoch);
            if (payload.Length == 0) throw new ArgumentException("Empty configuration message.");
            if (Authority is not null)
            {
                var request = ConfigurationRequestCodec.Decode(payload);
                if (request.Request <= _configurationRequests.GetValueOrDefault(message.RemotePeerId)) throw new ArgumentException("Repeated configuration request.");
                _configurationRequests[message.RemotePeerId] = request.Request;
                var result = request.Match == State.Match ? ApplyConfiguration(message.RemotePeerId, request.Edits) : (Accepted: false, Error: "Match changed; retry the configuration edit.");
                PublishConfiguration();
                SendConfiguration(message.RemotePeerId, record.Generation, ConfigurationRequestCodec.EncodeResult(request.Request, Authority.Configuration.Revision, result.Accepted ? string.Empty : result.Error));
            }
            else if (payload[0] == 3)
            {
                var publication = GameplayConfigurationCodec.Decode(payload.AsSpan(1));
                if (publication.Session != State.Session || (_sessionConfiguration is not null && !publication.State.CanReplace(_sessionConfiguration)))
                    throw new ArgumentException("Stale session configuration.");
                _sessionConfiguration = publication.State;
            }
            else
            {
                var result = ConfigurationRequestCodec.DecodeResult(payload);
                if (result.Request != _pendingConfigurationRequest || _pendingConfigurationRequest == 0) throw new ArgumentException("Unexpected configuration result.");
                ConfigurationResult = (result.Revision, result.Error);
                _pendingConfigurationRequest = 0;
            }
        }
        catch (ArgumentException) { RejectedPackets++; }
    }

    private void AdvanceConfigurationRequest()
    {
        if (ConfigurationPending && (!CanConfigure || _seconds - _configurationRequestedAt > 10))
        {
            _pendingConfigurationRequest = 0;
            ConfigurationResult = (0, "Configuration confirmation interrupted. Check current values before retrying.");
        }
    }

    private void ResetConfigurationChannel(GameplayConfigurationState configuration)
    {
        _sessionConfiguration = configuration;
        _configurationSent.Clear();
        _configurationRequests.Clear();
        _pendingConfigurationRequest = 0;
        ConfigurationResult = (0, "Authority changed; check current configuration.");
    }
}
