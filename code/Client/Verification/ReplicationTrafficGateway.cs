using System.Diagnostics;
using System.Text.Json;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Verification;

/// <summary>Verification-only bounded counters at the native payload seam, emitted once at teardown.</summary>
internal sealed class ReplicationTrafficGateway : ITransportGateway
{
    private readonly GameNetworkingSocketsTransport _inner = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<(ulong Peer, string Protocol, TransportDelivery Delivery), (long Bytes, long Messages, int Peak)> _sent = new();
    private bool _disposed;
    public event Action<TransportConnectionChange>? ConnectionChanged { add => _inner.ConnectionChanged += value; remove => _inner.ConnectionChanged -= value; }
    public bool IsListening => _inner.IsListening;
    public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => _inner.Connections;
    public TransportConnectionState ConnectionState => _inner.ConnectionState;
    public string Name => _inner.Name;
    public TransportCapabilities Capabilities => _inner.Capabilities;
    public void Listen(TransportEndpoint endpoint) => _inner.Listen(endpoint);
    public ulong Connect(TransportEndpoint endpoint) => _inner.Connect(endpoint);
    public void Disconnect(ulong peerId) => _inner.Disconnect(peerId);
    public void Poll() => _inner.Poll();
    public bool TryReceive(out TransportMessage message) => _inner.TryReceive(out message);
    public void Stop() => _inner.Stop();
    public void ConfigureSimulation(NetworkSimulation simulation) => _inner.ConfigureSimulation(simulation);
    public TransportStatistics GetStatistics(ulong peerId) => _inner.GetStatistics(peerId);
    public void Send(TransportMessage message)
    {
        _inner.Send(message);
        var span = message.Payload.Span;
        string protocol = span.Length >= 2 ? string.Concat((char)span[0], (char)span[1]) : "short";
        var key = (message.RemotePeerId, protocol, message.Delivery);
        if (_sent.Count >= 256 && !_sent.ContainsKey(key)) { throw new InvalidOperationException("Verification traffic bucket bound exceeded."); }
        var count = _sent.GetValueOrDefault(key);
        _sent[key] = (count.Bytes + span.Length, count.Messages + 1, Math.Max(count.Peak, span.Length));
    }
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        Godot.GD.Print("REPLICATION_TRAFFIC " + JsonSerializer.Serialize(new
        {
            seconds = _clock.Elapsed.TotalSeconds,
            note = "Native-send application payloads including connection/chunk envelopes; excludes UDP/GNS overhead/retransmissions. Full harness lifecycle, not a steady-state window.",
            sent = _sent.OrderBy(pair => pair.Key.Peer).ThenBy(pair => pair.Key.Protocol).ThenBy(pair => pair.Key.Delivery)
                .Select(pair => new { peer = pair.Key.Peer, protocol = pair.Key.Protocol, delivery = pair.Key.Delivery.ToString(), bytes = pair.Value.Bytes, messages = pair.Value.Messages, peak = pair.Value.Peak })
        }));
        _inner.Dispose();
    }
}
