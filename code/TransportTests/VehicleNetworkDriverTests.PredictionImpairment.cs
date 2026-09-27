using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

internal sealed partial class VehicleNetworkDriverTests
{
    /// <summary>Fixed packet schedules reproduce acknowledgement age independently of native random loss.</summary>
    [TestCase(2)]
    [TestCase(3)]
    public void ClusteredSnapshotLossWithInputJitterKeepsPredictionResponsive(int delayTicks)
    {
        using var hostWire = ConnectedGateway();
        using var clientWire = ConnectedGateway();
        using var host = new VehicleNetworkDriver(hostWire, Session);
        using var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer);
        var toHost = new List<(int Due, TransportMessage Message)>();
        var toClient = new List<(int Due, TransportMessage Message)>();
        int snapshots = 0, drops = 0, inputDrops = 0, holds = 0, peakPending = 0;
        uint peakHostLag = 0, peakReturnLag = 0;
        float maximumCorrection = 0;
        int tick = 0;
        var jitterSchedule = new Random(209);
        client.LocalCorrected += _ =>
        {
            if (tick > 120) { maximumCorrection = Math.Max(maximumCorrection, client.Prediction!.PredictionError); }
        };
        for (; tick < 1500; tick++)
        {
            Deliver(toHost, hostWire, tick);
            host.Advance(default, Observe);
            foreach (var packet in hostWire.Sent)
            {
                bool snapshot = packet.Delivery == TransportDelivery.Unreliable && (packet.Payload.Span[0] == 'T' && packet.Payload.Span[1] == 'S' && packet.Payload.Span[3] == VehicleNetworkCodec.Snapshot);
                if (snapshot) { snapshots++; }
                // Exactly three adjacent lost publications in 150 (2%), after warm-up.
                if (snapshot && snapshots % 150 is >= 100 and <= 102) { drops++; continue; }
                int jitter = snapshot ? jitterSchedule.Next(-1, 2) : 0;
                int reorder = snapshot && snapshots % 10 == 0 ? 2 : 0;
                toClient.Add((tick + delayTicks + jitter + reorder, packet));
            }
            hostWire.Sent.Clear();
            Deliver(toClient, clientWire, tick);
            client.Advance(tick < 1200 ? Drive((short)(Math.Sin(tick / 60.0) * 18000)) : default, Observe);
            foreach (var packet in clientWire.Sent)
            {
                if (tick % 50 == 0) { inputDrops++; continue; }
                toHost.Add((tick + delayTicks + jitterSchedule.Next(-1, 2) + (tick % 10 == 0 ? 2 : 0), packet));
            }
            clientWire.Sent.Clear();
            if (client.Inputs is { } inputs && host.Latest!.Vehicles.Count == 2)
            {
                uint sent = unchecked(inputs.NextSequence - 1);
                uint acknowledged = host.Latest.Vehicles.Single(v => v.State.VehicleId == client.LocalVehicleId).AcknowledgedInput;
                peakHostLag = Math.Max(peakHostLag, unchecked(sent - acknowledged));
                peakReturnLag = Math.Max(peakReturnLag, unchecked(acknowledged - inputs.LastAcknowledged));
                peakPending = Math.Max(peakPending, inputs.Pending.Count);
                if (client.Prediction?.IsPredictionLimited == true) { holds++; }
            }
            Assert.That(client.Failure, Is.Empty);
            Assert.That(client.History?.Snapshots.Count ?? 0, Is.LessThanOrEqualTo(SnapshotHistory.Capacity));
        }
        TestContext.WriteLine($"delay ticks={delayTicks}; holds={holds}; peak pending={peakPending}; input transit/host lag={peakHostLag}; return acknowledgement lag={peakReturnLag}; steady max={maximumCorrection:F4}; snapshot drops={drops}/{snapshots}; input drops={inputDrops}");
        Assert.Multiple(() =>
        {
            Assert.That(drops, Is.GreaterThan(0));
            Assert.That(client.RejectedPackets, Is.GreaterThan(0), "Reordering must exercise stale rejection.");
            Assert.That(holds, Is.Zero);
            Assert.That(maximumCorrection, Is.LessThan(0.5f));
            Assert.That(peakPending, Is.LessThan(InputHistory.Capacity));
            Assert.That(client.Inputs!.Pending.Count, Is.LessThan(15), "Loss must not accumulate a permanent backlog.");
        });
    }

    private static void Deliver(List<(int Due, TransportMessage Message)> packets, DriverGateway wire, int tick)
    {
        foreach (var packet in packets.Where(p => p.Due <= tick).OrderBy(p => p.Due).ToArray())
        {
            wire.Receive(packet.Message);
            packets.Remove(packet);
        }
    }
}
