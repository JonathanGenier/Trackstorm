using System.Numerics;
using NUnit.Framework;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Arenas;

[TestFixture]
internal sealed class EnvironmentAuthorityTests
{
    private static readonly EnvironmentLayout Layout = new([Vector3.Zero], [new Vector3(0, 0, -1)]);

    [Test]
    public void CommittedWeaponAdmissionAndReplacementUseOneCompleteBoundary()
    {
        var old = PrototypeArena.Configuration;
        Vector3 point = old.Players[0].Position;
        var layout = new EnvironmentLayout([point], [point]);
        var arena = new ArenaConfiguration(old.Minimum, old.Maximum, old.Players, old.Items, old.Surfaces, layout);
        var host = new Core.Networking.Replication.HostVehicleSession(7, arena: arena, itemConfiguration: new() { MaximumDamage = 300 });
        host.JoinPlayer(10, 2);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Missile), Is.True);
        var held = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 7, held.Life, held.Token), Is.True);
        host.Step(default, s => new(s.Movement.Physics, Vector3.UnitY), (_, _) => 0);
        var checkpoint = host.PrepareJoin(3, 1)!;
        Assert.That(checkpoint.Environment!.Rocks[0].Stage, Is.EqualTo(2));
        Assert.That(checkpoint.Environment.Plants[0], Is.True);
        var decoded = Core.Networking.Replication.ResumeCheckpointCodec.Decode(Core.Networking.Replication.ResumeCheckpointCodec.Encode(checkpoint));
        Assert.That(EnvironmentCodec.Encode(decoded.Environment!), Is.EqualTo(EnvironmentCodec.Encode(checkpoint.Environment)));
        var world = host.Snapshot();
        var items = new ItemPublication(1, world, host.Items.Slots, host.Items.Missiles, []);
        var match = host.World.State.Match!;
        var boundary = new Core.Networking.Replication.ResumeCheckpoint(items,
            new Core.Matches.MatchState(match.Tick, match.Revision, match.KillTarget, match.Phase, match.CountdownAtTick, match.Winner, match.Players, mode: match.Mode), null, host.Configuration, checkpoint.Environment);
        var restored = Core.Networking.Replication.HostVehicleSession.Restore(boundary, host.CaptureAuthority(), 2, arena: arena);
        Assert.That(EnvironmentCodec.Encode(restored.Environment!.Snapshot(7, world.Tick)), Is.EqualTo(EnvironmentCodec.Encode(checkpoint.Environment)));
        Assert.That(restored.Items.Events, Is.Empty);
    }

    [Test]
    public void MeaningfulImpactsAdvanceOnceAndLaterStageIsEasier()
    {
        var authority = new EnvironmentAuthority(Layout);
        var items = new ItemAuthority();
        authority.Advance(1, [Impact(3)], [], items);
        Assert.That(authority.Snapshot(1, 1).Rocks[0].Damage, Is.Zero);
        authority.Advance(2, [Impact(9)], [], items);
        authority.Advance(2, [Impact(9)], [], items);
        authority.Advance(3, [Impact(9)], [], items);
        Assert.That(authority.Snapshot(1, 3).Rocks[0].Stage, Is.EqualTo(1));
        authority.Advance(14, [Impact(14)], [], items);
        Assert.That(authority.Snapshot(1, 14).Rocks[0].Stage, Is.EqualTo(2));
        authority.Advance(26, [Impact(9)], [], items);
        Assert.That(authority.Snapshot(1, 26).Rocks[0].Stage, Is.EqualTo(3));
        for (ulong tick = 27; tick < 1000; tick++) { authority.Advance(tick, [Impact(40)], [], items); }
        Assert.That(authority.Snapshot(1, 999).Rocks, Has.Count.EqualTo(EnvironmentLayout.PiecesPerRock));
        Assert.That(authority.Snapshot(1, 999).Rocks[0].Stage, Is.EqualTo(Layout.FinalStage(0)));
    }

    [Test]
    public void WeaponsUseExistingFalloffAndDoNotSkipStages()
    {
        var authority = new EnvironmentAuthority(Layout);
        var items = new ItemAuthority(new() { MaximumDamage = 300 });
        authority.Advance(1, [], [new(1, 1, HeldItem.Missile, Vector3.Zero, true)], items);
        Assert.That(authority.Snapshot(1, 1).Rocks[0].Stage, Is.EqualTo(2));
        authority.Advance(2, [], [new(2, 1, HeldItem.Salvo, Vector3.Zero, true)], items);
        Assert.That(authority.Snapshot(1, 2).Rocks[0].Stage, Is.EqualTo(3));
        Assert.That(authority.Snapshot(1, 2).Plants[0], Is.True);
    }

    [Test]
    public void PlantsPersistAndNewMatchRestoresAuthoredState()
    {
        var authority = new EnvironmentAuthority(Layout);
        authority.Advance(1, [Impact(0)], [], new());
        Assert.That(authority.Snapshot(1, 1).Plants[0], Is.True);
        var restored = new EnvironmentAuthority(Layout);
        restored.Restore(EnvironmentCodec.Decode(EnvironmentCodec.Encode(authority.Snapshot(1, 1))));
        restored.Advance(2, [], [], new());
        Assert.That(restored.Snapshot(1, 2).Plants[0], Is.True);
        Assert.That(new EnvironmentAuthority(Layout).Snapshot(2, 0).Plants[0], Is.False);
    }

    [Test]
    public void CompleteBoundaryRestoresExactDamageCooldownAndMovement()
    {
        var authority = new EnvironmentAuthority(Layout);
        authority.Advance(1, [Impact(14)], [], new());
        var copy = new EnvironmentAuthority(Layout);
        byte[] bytes = EnvironmentCodec.Encode(authority.Snapshot(7, 1));
        copy.Restore(EnvironmentCodec.Decode(bytes));
        for (ulong tick = 2; tick <= 100; tick++)
        {
            authority.Advance(tick, [Impact(14)], [], new());
            copy.Advance(tick, [Impact(14)], [], new());
        }
        Assert.That(EnvironmentCodec.Encode(copy.Snapshot(7, 100)), Is.EqualTo(EnvironmentCodec.Encode(authority.Snapshot(7, 100))));
        Assert.Throws<ArgumentException>(() => EnvironmentCodec.Decode(bytes[..^1]));
        Assert.Throws<ArgumentException>(() => new EnvironmentAuthority(new([], [])).Restore(EnvironmentCodec.Decode(bytes)));
    }

    [Test]
    public void RepeatedAreaDestructionKeepsMovementAndPacketBounded()
    {
        var layout = new EnvironmentLayout(Enumerable.Range(0, 256).Select(i => new Vector3(i % 8, 0, i / 8)), Enumerable.Repeat(Vector3.Zero, 4096));
        var authority = new EnvironmentAuthority(layout);
        var items = new ItemAuthority(new() { MaximumDamage = 300, ExplosionRadius = 100 });
        for (ulong tick = 1; tick <= 600; tick++)
        {
            authority.Advance(tick, [], [new(tick, 1, HeldItem.Missile, new(-1, 0, -1), true)], items);
            var snapshot = authority.Snapshot(1, tick);
            Assert.That(snapshot.Rocks.Count(r => r.Velocity != Vector3.Zero), Is.LessThanOrEqualTo(16));
            Assert.That(snapshot.Rocks.All(r => r.Offset.Length() <= 8.001f), Is.True);
            Assert.That(EnvironmentCodec.Encode(snapshot).Length, Is.LessThanOrEqualTo(30231));
        }
    }

    [Test]
    public void MalformedStageMotionAndPlantPaddingAreRejected()
    {
        byte[] bytes = EnvironmentCodec.Encode(new EnvironmentAuthority(Layout).Snapshot(1, 0));
        bytes[23] = 17;
        Assert.Throws<ArgumentException>(() => EnvironmentCodec.Decode(bytes));
        bytes[23] = 1; bytes[^1] = 128;
        Assert.Throws<ArgumentException>(() => EnvironmentCodec.Decode(bytes));
        Assert.Throws<ArgumentException>(() => new EnvironmentSnapshot(1, 0, [new(2, 0, 0, new(9, 0, 0), default)], []));
        Assert.Throws<ArgumentException>(() => new EnvironmentSnapshot(1, 0, [new(2, float.NaN, 0, default, default)], []));
    }

    [Test]
    public void StartingSizeDeterminesDepthAndSplitsStayBoundedThroughCompleteDestruction()
    {
        var layout = new EnvironmentLayout([Vector3.Zero, new(20, 0, 0), new(40, 0, 0)], [], [8, 2, 0.5f]);
        Assert.That(layout.FinalStage(0), Is.GreaterThan(layout.FinalStage(4)));
        Assert.That(layout.FinalStage(4), Is.GreaterThan(layout.FinalStage(8)));
        var authority = new EnvironmentAuthority(layout);
        Assert.That(authority.Snapshot(1, 0).Rocks[8].Stage, Is.EqualTo(layout.FinalStage(8)));
        var items = new ItemAuthority(new() { MaximumDamage = 300, ExplosionRadius = 100 });
        for (ulong tick = 1; tick < 300; tick += 12)
        {
            var before = authority.Snapshot(1, tick);
            authority.Advance(tick, [], [new(tick, 1, HeldItem.Missile, Vector3.Zero, true)], items);
            var after = authority.Snapshot(1, tick);
            for (int i = 0; i < after.Rocks.Count; i++)
            {
                Assert.That(after.Rocks[i].Stage, Is.LessThanOrEqualTo(layout.FinalStage(i)));
                if (before.Rocks[i].Stage > 0) { Assert.That(after.Rocks[i].Stage - before.Rocks[i].Stage, Is.InRange(0, 1)); }
                Assert.That(layout.Size(i, after.Rocks[i].Stage), Is.GreaterThanOrEqualTo(layout.MinimumSize));
            }
        }
        var final = authority.Snapshot(1, 300);
        Assert.That(final.Rocks.Take(8).Select((r, i) => r.Stage == layout.FinalStage(i)), Is.All.True);
        Assert.That(final.Rocks.Skip(8).Count(r => r.Stage > 0), Is.EqualTo(1), "Minimum rocks do not split.");
        var restored = new EnvironmentAuthority(layout);
        restored.Restore(EnvironmentCodec.Decode(EnvironmentCodec.Encode(final)));
        Assert.That(EnvironmentCodec.Encode(restored.Snapshot(1, 300)), Is.EqualTo(EnvironmentCodec.Encode(final)));
    }

    [TestCase(3, 1, false)]
    [TestCase(8, 1, false)]
    [TestCase(14, 1, true)]
    [TestCase(30, 0.1f, false)]
    [TestCase(30, 0.7f, true)]
    public void SolidClosingSpeedMattersMoreThanTangentialSpeed(float speed, float normalFraction, bool breaks)
    {
        var authority = new EnvironmentAuthority(Layout);
        var velocity = new Vector3(speed * MathF.Sqrt(1 - normalFraction * normalFraction), 0, -speed * normalFraction);
        var request = new VehicleStepRequest(1, default, new(new(new(0, 1, 0), Quaternion.Identity, velocity, Vector3.Zero), Vector3.UnitY,
            [new(velocity, Vector3.UnitZ, 0, 0, environmentRock: 1)]));
        authority.Advance(1, [request], [], new());
        Assert.That(authority.Snapshot(1, 1).Rocks[0].Stage == 2, Is.EqualTo(breaks));
        if (breaks)
        {
            var state = authority.Snapshot(1, 1);
            Assert.That(state.Rocks.Count(r => r.Stage > 0), Is.EqualTo(2));
            Assert.That(layoutSize(state), Is.GreaterThan(0.7f), "First fragments retain useful size.");
        }
        float layoutSize(EnvironmentSnapshot snapshot) => Layout.Size(0, snapshot.Rocks[0].Stage);
    }

    [Test]
    public void SlopingRockFacesRetainHorizontalSeverityAndLowStonesUseWheelFootprint()
    {
        var large = new EnvironmentLayout([Vector3.Zero], [], [4]);
        var authority = new EnvironmentAuthority(large);
        var velocity = new Vector3(0, 0, -14);
        var request = new VehicleStepRequest(1, default, new(new(new(0, 1, 0), Quaternion.Identity, velocity, Vector3.Zero), Vector3.UnitY,
            [new(velocity, Vector3.Normalize(new(0, 0.9f, 0.4f)), 0, 0, environmentRock: 1)]));
        authority.Advance(1, [request], [], new());
        Assert.That(authority.Snapshot(1, 1).Rocks[0].Stage, Is.EqualTo(2));
        var small = new EnvironmentAuthority(new([Vector3.Zero], [], [0.8f]));
        var wheel = new VehicleStepRequest(1, default, new(request.Observation.Physics, Vector3.UnitY));
        small.Advance(1, [wheel], [], new());
        Assert.That(small.Snapshot(1, 1).Rocks[0].Stage, Is.EqualTo(2));
        var minimum = new EnvironmentAuthority(new([Vector3.Zero], [], [0.6f]));
        for (ulong tick = 1; tick < 60; tick++) { minimum.Advance(tick, [wheel], [], new()); }
        Assert.That(minimum.Snapshot(1, 60).Rocks.Count(r => r.Stage > 0), Is.EqualTo(1), "Near-minimum rocks do not manufacture more nearly identical pieces.");
    }

    private static VehicleStepRequest Impact(float speed) => new(1, default,
        new VehicleObservation(new(new(0, 1, 0), Quaternion.Identity, new(0, 0, -speed), Vector3.Zero), Vector3.UnitY,
            [new(new(0, 0, -speed), Vector3.UnitZ, 0, 0, environmentRock: 1)]));
}
