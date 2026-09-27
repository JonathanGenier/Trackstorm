using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

/// <summary>Test-only receive seam separating delivered stale probes from deliberately lost unreliable snapshots.</summary>
internal sealed class ObservedImpairmentGateway(ITransportGateway inner) : ITransportGateway
{
    private int _snapshots;
    internal byte[]? StaleProbe { get; set; }
    internal bool DropSnapshots { get; init; }
    internal int DeliveredStale { get; private set; }
    internal int DroppedSnapshots { get; private set; }
    internal Action? BeforeReceive { get; set; }
    public event Action<TransportConnectionChange>? ConnectionChanged { add => inner.ConnectionChanged += value; remove => inner.ConnectionChanged -= value; }
    public bool IsListening => inner.IsListening;
    public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => inner.Connections;
    public TransportConnectionState ConnectionState => inner.ConnectionState;
    public void Listen(TransportEndpoint endpoint) => inner.Listen(endpoint);
    public ulong Connect(TransportEndpoint endpoint) => inner.Connect(endpoint);
    public void Disconnect(ulong peerId) => inner.Disconnect(peerId);
    public void Poll() => inner.Poll();
    public void Stop() => inner.Stop();
    public TransportStatistics GetStatistics(ulong peerId) => inner.GetStatistics(peerId);
    public void ConfigureSimulation(NetworkSimulation simulation) => inner.ConfigureSimulation(simulation);
    public void Send(TransportMessage message) => inner.Send(message);
    public void Dispose() => inner.Dispose();
    public bool TryReceive(out TransportMessage message)
    {
        BeforeReceive?.Invoke();
        while (inner.TryReceive(out message))
        {
            if (message.Delivery == TransportDelivery.Unreliable && StaleProbe is not null && message.Payload.Span.SequenceEqual(StaleProbe))
            {
                DeliveredStale++;
                return true;
            }

            var bytes = message.Payload.Span;
            if (DropSnapshots && message.Delivery == TransportDelivery.Unreliable && bytes.Length > 3 && bytes[0] == 'T' && bytes[1] == 'S' &&
                bytes[3] == Trackstorm.Core.Networking.Replication.VehicleNetworkCodec.Snapshot && ++_snapshots % 50 == 0)
            {
                // Exactly one in fifty received ordinary snapshots is withheld before the driver.
                // This is controlled application loss, separately identified from unobservable native drops.
                DroppedSnapshots++;
                continue;
            }
            return true;
        }
        return false;
    }
}
