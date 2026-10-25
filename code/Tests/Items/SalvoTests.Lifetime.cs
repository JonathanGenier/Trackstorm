using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Items;

internal sealed partial class SalvoTests
{
    [Test]
    public void NoContactContinuesPastAimPointAndExpiresExactlyOnceThirtySecondsAfterEachLaunch()
    {
        var host = Ready(); Use(host);
        var launches = new Dictionary<ulong, ulong>();
        var impacts = new Dictionary<ulong, ulong>();
        bool beyondWorld = false;
        for (int i = 0; i < 2150; i++)
        {
            host.Step(default, Observe);
            foreach (var e in host.Items.Events)
            {
                if (e.Impact) { impacts.Add(e.Token, host.World.State.Tick); }
                else { launches.Add(e.Token, host.World.State.Tick); }
            }
            foreach (var missile in host.Items.Missiles)
            {
                var arc = missile.Arc!;
                Assert.That(missile.RemainingTicks + arc.ElapsedTicks, Is.EqualTo(1800));
                if (arc.ElapsedTicks > arc.DurationTicks)
                { Assert.That(missile.Position, Is.Not.EqualTo(arc.Target)); }
                beyondWorld |= Math.Abs(missile.Position.Y) > host.Items.Configuration.MissileWorldLimit;
            }
            // Late-flight positions/velocities can exceed ordinary rocket bounds;
            // both reliable recovery and replaceable motion must carry them exactly.
            if (i % 137 == 0 && host.Items.Missiles.Count > 0)
            {
                var state = new ItemPublication(host.Items.Revision, host.Snapshot(), host.Items.Slots, host.Items.Missiles, []);
                Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(state)).Missiles, Is.EqualTo(state.Missiles));
            }
        }
        Assert.That(launches.Count, Is.EqualTo(5));
        Assert.That(impacts.Keys, Is.EquivalentTo(launches.Keys));
        Assert.That(impacts.All(p => p.Value - launches[p.Key] + 1 == 1800), Is.True);
        Assert.That(beyondWorld, Is.True);
        Assert.That(host.Items.Missiles, Is.Empty);
    }

    [TestCase(1, 0f)]
    [TestCase(20, .25f)]
    [TestCase(1800, .5f)]
    public void FirstSweptContactWinsIncludingOnExpiryStep(int contactStep, float fraction)
    {
        var host = Ready(); Use(host);
        ulong id = host.Items.TokenHighWater;
        Vector3 expected = default;
        int impacts = 0;
        for (int i = 0; i < 2150; i++)
        {
            host.Step(default, Observe, (missile, end) =>
            {
                if (missile.Id != id || missile.Arc!.ElapsedTicks + 1 < contactStep) { return null; }
                expected = Vector3.Lerp(missile.Position, end, fraction);
                return fraction;
            });
            foreach (var e in host.Items.Events.Where(e => e.Impact && e.Token == id))
            { impacts++; Assert.That(e.Position, Is.EqualTo(expected)); }
        }
        Assert.That(impacts, Is.EqualTo(1));
    }

    [Test]
    public void LateFlightRecoveryAndMotionRetainOriginalLaunchDeadlineAndRejectForgedVelocity()
    {
        var host = Ready(); host.Join(42); Use(host);
        for (int i = 0; i < 1700; i++) { host.Step(default, Observe); }
        var state = new ItemPublication(host.Items.Revision, host.Snapshot(), host.Items.Slots, host.Items.Missiles, []);
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(state, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        host.Step(default, Observe); restored.Step(default, Observe);
        var bytes = ProjectileMotionCodec.Encode(state, host.World.State.Tick, host.Items.Missiles);
        Assert.That(ProjectileMotionCodec.Decode(bytes, state).Missiles, Is.EqualTo(host.Items.Missiles));
        BitConverter.GetBytes(1f).CopyTo(bytes, 48); // First velocity component.
        Assert.Throws<ArgumentException>(() => ProjectileMotionCodec.Decode(bytes, state));
        for (int i = 0; i < 450; i++)
        {
            host.Step(default, Observe); restored.Step(default, Observe);
            Assert.That(restored.Items.Missiles, Is.EqualTo(host.Items.Missiles));
            Assert.That(restored.Items.Events, Is.EqualTo(host.Items.Events));
        }
        Assert.That(host.Items.Missiles, Is.Empty);
    }
}
