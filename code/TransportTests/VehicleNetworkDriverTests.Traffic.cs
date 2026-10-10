using System.Numerics;
using System.Text.Json;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

internal sealed partial class VehicleNetworkDriverTests
{
    [TestCase(2, "driving")]
    [TestCase(8, "driving")]
    [TestCase(2, "missile")]
    [TestCase(8, "missile")]
    [TestCase(2, "oil")]
    [TestCase(8, "oil")]
    [TestCase(2, "nitro")]
    [TestCase(8, "nitro")]
    [TestCase(2, "circus")]
    [TestCase(8, "circus")]
    public void MeasureReplicationTraffic(int players, string scenario)
    {
        using var wire = new DriverGateway();
        for (ulong peer = 2; peer <= (ulong)players; peer++) { wire.ConnectPeer(peer); }
        using var host = new VehicleNetworkDriver(wire, Session, configuration: new Trackstorm.Core.Development.GameplayConfiguration
        { Items = new ItemConfiguration { MaximumDamage = 0, MaximumImpulse = 0 } });
        var clientWires = Enumerable.Range(1, players - 1).Select(_ => ConnectedGateway()).ToArray();
        var clients = clientWires.Select(gateway => new VehicleNetworkDriver(gateway, 0, ServerPeer)).ToArray();
        var counts = new Dictionary<string, (long Bytes, int Messages, int Peak)>();
        long decodeAllocations = 0;
        long hostAllocations = 0;
        int launches = 0;
        host.ItemsReceived += state => launches += state.Events.Count(e => !e.Impact && e.Item == HeldItem.Missile);
        host.PlaceOil = (slot, pose) => new OilPatch(slot.Token, slot.Vehicle, pose.Position + new Vector3(0, -1, 4), Vector3.UnitY, 3);
        host.CollideMissile = (missile, _) => missile.RemainingTicks < 240 ? 0.5f : null;
        try
        {
            int interval = scenario == "missile" ? 180 : 120;
            for (int tick = -240; tick < interval * 5; tick++)
            {
                if (tick >= 0 && tick % interval == 0 && scenario is "missile" or "oil" or "nitro")
                {
                    HeldItem item = scenario switch { "missile" => HeldItem.Missile, "oil" => HeldItem.Oil, _ => HeldItem.Nitro };
                    foreach (var vehicle in host.Host!.World.State.Vehicles)
                    {
                        host.Host.Items.Grant(host.Host.World, vehicle.VehicleId, item);
                        var slot = host.Host.Items.Slots.Single(s => s.Vehicle == vehicle.VehicleId);
                        if (item != HeldItem.Missile) { host.Host.Items.RequestUse(host.Host.World, vehicle.VehicleId, slot.Life, slot.Active.Token); }
                    }
                }
                if (scenario == "missile")
                {
                    foreach (var slot in host.Host!.Items.Slots.Where(s => s.Active.Item == HeldItem.Missile && host.Host.World.State.Tick >= s.MissileReadyTick))
                    { host.Host.Items.RequestUse(host.Host.World, slot.Vehicle, slot.Life, slot.Active.Token); }
                }
                InputFrame input = Drive(held: scenario == "nitro" && tick % 120 < 90 ? InputButtons.UseItem : 0);
                VehicleObservation Observation(VehicleSnapshot state)
                {
                    var observed = Observe(state);
                    return scenario == "circus" && tick >= 0 && tick % 120 < 90
                        ? new VehicleObservation(new VehiclePhysicsState(observed.Physics.Position + Vector3.UnitY * 4, observed.Physics.Orientation, observed.Physics.LinearVelocity, observed.Physics.AngularVelocity), Vector3.Zero)
                        : observed;
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                host.Advance(input, Observation);
                if (tick >= 0) { hostAllocations += GC.GetAllocatedBytesForCurrentThread() - allocated; }
                foreach (var message in wire.Sent)
                {
                    if (tick >= 0)
                    {
                        string key = $"{(char)message.Payload.Span[0]}{(char)message.Payload.Span[1]}-{message.Delivery}";
                        var count = counts.GetValueOrDefault(key);
                        counts[key] = (count.Bytes + message.Payload.Length, count.Messages + 1, Math.Max(count.Peak, message.Payload.Length));
                    }
                    clientWires[(int)message.RemotePeerId - 2].Receive(new(ServerPeer, message.Payload, message.Delivery));
                }
                wire.Sent.Clear();
                for (int i = 0; i < clients.Length; i++)
                {
                    allocated = GC.GetAllocatedBytesForCurrentThread();
                    clients[i].Advance(input, Observation);
                    if (tick >= 0) { decodeAllocations += GC.GetAllocatedBytesForCurrentThread() - allocated; }
                    foreach (var message in clientWires[i].Sent) { wire.Receive(new((ulong)i + 2, message.Payload, message.Delivery)); }
                    clientWires[i].Sent.Clear();
                }
            }
            Assert.That(clients.All(client => client.Failure.Length == 0), Is.True);
            Assert.That(clients.All(client => client.ItemState?.Revision == host.ItemState?.Revision), Is.True, "Every ordered item publication must install.");
            Assert.That(host.RejectedPackets, Is.Zero);
            if (scenario == "missile") { Assert.That(launches, Is.EqualTo(players * 5)); }
            if (scenario is "driving" or "circus")
            {
                Assert.That(counts["TM-Reliable"].Messages, Is.LessThanOrEqualTo(105 * (players - 1)), "Pending progress must not return to fixed-step reliable publication.");
            }
            if (scenario == "missile")
            {
                Assert.That(counts["TI-Reliable"].Messages, Is.EqualTo(15 * (players - 1)), "Acquisition, launch and impact remain reliable; motion is replaceable.");
                Assert.That(counts["TJ-Unreliable"].Peak, Is.LessThanOrEqualTo(ProjectileMotionCodec.MaximumBytes));
            }
            TestContext.WriteLine(JsonSerializer.Serialize(new
            {
                players, scenario, simulatedSeconds = interval * 5 / 60, hostAllocations, clientAdvanceAllocations = decodeAllocations, rejected = clients.Sum(client => client.RejectedPackets),
                note = "Application payload bytes, deterministic transport/flat observation; allocations include simulation/prediction, not isolated codec cost. Excludes session/migration and native framing.",
                publications = counts.OrderBy(pair => pair.Key).Select(pair => new { protocol = pair.Key, bytes = pair.Value.Bytes, messages = pair.Value.Messages, peak = pair.Value.Peak })
            }));
        }
        finally { foreach (var client in clients) { client.Dispose(); } }
    }
}
