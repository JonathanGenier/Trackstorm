using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

internal sealed partial class VehicleNetworkDriverTests
{
    [Test]
    public void TerrainCorrectionIsHostOnlyAndCurvedSamplesReachPeers()
    {
        using var hostWire = ConnectedGateway();
        using var clientWire = ConnectedGateway();
        using var host = new VehicleNetworkDriver(hostWire, Session);
        using var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer);
        int probes = 0, samples = 0;
        IReadOnlyList<MissileState>? motion = null;
        host.QueryMissileTerrain = (a, b) =>
        {
            probes++;
            return a.Y >= -1 && b.Y <= -1 ? new(new(a.X, -1, a.Z), Vector3.UnitY) : null;
        };
        client.QueryMissileTerrain = (_, _) => throw new InvalidOperationException("A client must not steer authoritative missiles.");
        client.ProjectileMotionReceived += missiles => { samples++; motion = missiles; };
        void Transfer()
        {
            foreach (var packet in hostWire.Sent) { clientWire.Receive(packet); }
            hostWire.Sent.Clear(); client.Advance(default, Observe);
            foreach (var packet in clientWire.Sent) { hostWire.Receive(packet); }
            clientWire.Sent.Clear();
        }
        host.Advance(default, Observe); Transfer();
        Assert.That(host.GiveDeveloperItem(HeldItem.Missile), Is.True);
        Assert.That(host.RequestItemUse(), Is.True);
        for (int i = 0; i < 24; i++) { host.Advance(default, Observe); Transfer(); }
        Assert.That(probes, Is.GreaterThan(20));
        Assert.That(samples, Is.GreaterThan(0));
        Assert.That(motion!.Single().Velocity.Y, Is.LessThan(0));
        Assert.That(motion!.Single().Position.Y, Is.LessThan(1));
        Assert.That(motion!.Single().Velocity.X, Is.Zero);
        Assert.That(client.ItemState!.Missiles.Select(m => m.Id), Is.EqualTo(host.ItemState!.Missiles.Select(m => m.Id)));
    }

    [Test]
    public void DroppedReorderedMotionCannotLoseImpactOrResurrectProjectile()
    {
        using var hostWire = ConnectedGateway();
        using var clientWire = ConnectedGateway();
        using var host = new VehicleNetworkDriver(hostWire, Session);
        using var client = new VehicleNetworkDriver(clientWire, 0, ServerPeer);
        int motions = 0, impacts = 0;
        client.ProjectileMotionReceived += _ => motions++;
        client.ItemsReceived += state => impacts += state.Events.Count(e => e.Impact);
        void Transfer(bool dropMotion = false)
        {
            foreach (var packet in hostWire.Sent)
            {
                if (!dropMotion || !ProjectileMotionCodec.IsMotion(packet.Payload.Span)) { clientWire.Receive(packet); }
            }
            hostWire.Sent.Clear();
            client.Advance(default, Observe);
            foreach (var packet in clientWire.Sent) { hostWire.Receive(packet); }
            clientWire.Sent.Clear();
        }
        host.Advance(default, Observe); Transfer();
        Assert.That(host.GiveDeveloperItem(HeldItem.Missile), Is.True);
        Assert.That(host.RequestItemUse(), Is.True);
        host.Advance(default, Observe); Transfer();
        var baseline = client.ItemState!;
        for (int i = 0; i < 12; i++) { host.Advance(default, Observe); Transfer(dropMotion: true); }
        Assert.That(client.ItemState, Is.SameAs(baseline));
        Assert.That(motions, Is.Zero);
        host.Advance(default, Observe);
        var stale = hostWire.Sent.Single(packet => ProjectileMotionCodec.IsMotion(packet.Payload.Span));
        Transfer();
        Assert.That(motions, Is.EqualTo(1));
        clientWire.Receive(stale); client.Advance(default, Observe);
        Assert.That(motions, Is.EqualTo(1), "Duplicate sample is not presented twice.");
        host.CollideMissile = (_, _) => 0.5f;
        host.Advance(default, Observe); Transfer(dropMotion: true);
        Assert.That(client.ItemState!.Missiles, Is.Empty);
        Assert.That(impacts, Is.EqualTo(1));
        clientWire.Receive(stale); client.Advance(default, Observe);
        Assert.That(motions, Is.EqualTo(1), "An old membership baseline cannot resurrect the removed rocket.");
        Assert.That(client.ItemState.Missiles, Is.Empty);
        Assert.That(client.ItemState.World.Vehicles, Is.EqualTo(host.ItemState!.World.Vehicles));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MotionCodecRejectsMalformedAndForeignBaselines(bool arcing)
    {
        using var wire = ConnectedGateway();
        using var host = new VehicleNetworkDriver(wire, Session);
        host.Advance(default, Observe);
        host.ProjectSalvoGround = point => new Vector3(point.X, 0, point.Z);
        host.GiveDeveloperItem(arcing ? HeldItem.Salvo : HeldItem.Missile); host.RequestItemUse();
        host.Advance(default, Observe);
        var baseline = host.ItemState!;
        host.Advance(default, Observe);
        byte[] valid = ProjectileMotionCodec.Encode(baseline, host.Latest!.Tick, host.Host!.Items.Missiles);
        var decoded = ProjectileMotionCodec.Decode(valid, baseline);
        Assert.That(decoded.Missiles, Is.EqualTo(host.Host.Items.Missiles));
        Assert.That(valid.Length, Is.EqualTo(68));
        foreach (int length in new[] { 0, 27, 28, valid.Length - 1 })
        { Assert.Throws<ArgumentException>(() => ProjectileMotionCodec.Decode(valid.AsSpan(0, length), baseline)); }
        foreach (int offset in new[] { 2, 3, 11, 27, 28, 60, 64 })
        {
            var corrupt = valid.ToArray(); corrupt[offset] ^= 0xff;
            Assert.Throws<ArgumentException>(() => ProjectileMotionCodec.Decode(corrupt, baseline));
        }
        var nonFinite = valid.ToArray();
        BitConverter.GetBytes(float.NaN).CopyTo(nonFinite, 36);
        Assert.Throws<ArgumentException>(() => ProjectileMotionCodec.Decode(nonFinite, baseline));
        Assert.Throws<ArgumentException>(() => ProjectileMotionCodec.Decode([.. valid, 0], baseline));
        Assert.That(baseline.Missiles[0].Position, Is.Not.EqualTo(decoded.Missiles[0].Position), "Decoding never mutates the reliable baseline.");
    }

    [TestCase(2)]
    [TestCase(8)]
    public void MeasureProjectileCodecAllocation(int players)
    {
        using var wire = new DriverGateway();
        for (ulong peer = 2; peer <= (ulong)players; peer++) { wire.ConnectPeer(peer); }
        using var host = new VehicleNetworkDriver(wire, Session);
        host.Advance(default, Observe);
        foreach (var vehicle in host.Host!.World.State.Vehicles)
        {
            host.Host.Items.Grant(host.Host.World, vehicle.VehicleId, HeldItem.Missile);
            var slot = host.Host.Items.Slots.Single(slot => slot.Vehicle == vehicle.VehicleId);
            host.Host.Items.RequestUse(host.Host.World, vehicle.VehicleId, slot.Life, slot.Active.Token);
        }
        host.Advance(default, Observe);
        var baseline = host.ItemState!;
        host.Advance(default, Observe);
        var missiles = host.Host.Items.Missiles;
        var complete = new ItemPublication(baseline.Revision + 1, host.Latest!, host.Host.Items.Slots, missiles, []);
        // Warm both exact codecs before measuring; the old full-state representation remains used for outcomes.
        ItemCodec.DecodeState(ItemCodec.EncodeState(complete, baseline), baseline);
        ProjectileMotionCodec.Decode(ProjectileMotionCodec.Encode(baseline, host.Latest!.Tick, missiles), baseline);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { ItemCodec.DecodeState(ItemCodec.EncodeState(complete, baseline), baseline); }
        long full = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { ProjectileMotionCodec.Decode(ProjectileMotionCodec.Encode(baseline, host.Latest!.Tick, missiles), baseline); }
        long motion = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.WriteLine($"CODEC_ALLOCATION players={players}, fullEncodeDecodeBytesPerPair={full / 1000.0}, motionEncodeDecodeBytesPerPair={motion / 1000.0}; 1000 repetitions; excludes publication construction/native marshalling.");
        Assert.That(motion, Is.LessThan(full));
    }
}
