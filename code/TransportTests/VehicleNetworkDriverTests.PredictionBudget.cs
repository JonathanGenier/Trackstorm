using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

internal sealed partial class VehicleNetworkDriverTests
{
    [TestCase(4, false)]
    [TestCase(12, true)]
    public void DelayedAcknowledgementsRecoverWithinBudgetAndHoldDuringOutage(int lostPublications, bool expectHold)
    {
        using var hostWire = ConnectedGateway();
        using var clientWire = ConnectedGateway();
        double seconds = 0;
        using var host = new VehicleNetworkDriver(hostWire, Session, seconds: () => seconds);
        using var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer, seconds: () => seconds);
        var pending = new List<(int Due, bool ToHost, TransportMessage Packet)>();
        int dropped = 0, limited = 0, peakPending = 0;
        for (int tick = 0; tick < 360; tick++)
        {
            seconds = tick / 60.0;
            foreach (var delivery in pending.Where(value => value.Due <= tick).ToArray())
            {
                (delivery.ToHost ? hostWire : clientWire).Receive(delivery.Packet);
                pending.Remove(delivery);
            }
            host.Advance(default, Observe);
            client.Advance(Drive(), Observe);
            if (client.Prediction?.IsPredictionLimited == true) limited++;
            peakPending = Math.Max(peakPending, client.Inputs?.Pending.Count ?? 0);
            foreach (var packet in hostWire.Sent)
            {
                if (packet.Payload.Length > 3 && packet.Payload.Span[0] == 'T' &&
                    packet.Payload.Span[1] == 'S' && packet.Payload.Span[3] == VehicleNetworkCodec.Snapshot &&
                    packet.Delivery == TransportDelivery.Unreliable)
                {
                    ulong boundary = VehicleNetworkCodec.DecodeSnapshot(packet.Payload.Span).Tick;
                    if (boundary >= 180 && boundary < (ulong)(180 + lostPublications * 3)) { dropped++; continue; }
                }
                pending.Add((tick + 3, false, packet));
            }
            foreach (var packet in clientWire.Sent) pending.Add((tick + 3, true, packet));
            hostWire.Sent.Clear();
            clientWire.Sent.Clear();
        }
        Assert.That(dropped, Is.EqualTo(lostPublications));
        Assert.That(peakPending, Is.GreaterThan(18), "Exercise the acknowledgement gap that exhausted the prior 300 ms budget.");
        Assert.That(limited > 0, Is.EqualTo(expectHold));
        Assert.That(client.Prediction!.IsPredictionLimited, Is.False, "The bounded hold recovers once acknowledgements resume.");
        Assert.That(client.Failure, Is.Empty);
        Assert.That(client.Prediction.PredictionError, Is.LessThan(0.5));
        Assert.That(client.History!.Snapshots.Count, Is.LessThanOrEqualTo(SnapshotHistory.Capacity));
        TestContext.WriteLine($"RTT=100ms; dropped publications={dropped}; peak pending={peakPending}; held ticks={limited}");
    }
}
