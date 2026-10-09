using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

internal sealed class CombatCollisionTests
{
    [TestCase(30f, 60f, false)]
    [TestCase(30f, 60f, true)]
    [TestCase(60f, 30f, false)]
    [TestCase(60f, 30f, true)]
    [TestCase(0f, 60f, false)]
    [TestCase(60f, 0f, true)]
    public void MomentumAdvantageDamagesBothAndDoesNotDependOnTheReportingCollider(float firstKph, float secondKph, bool reverseReport)
    {
        var result = Impact(firstKph / 3.6f, secondKph / 3.6f, reverseReport);
        float first = 1000 - result[0].Snapshot.Damage.CurrentHP, second = 1000 - result[1].Snapshot.Damage.CurrentHP;
        Assert.That(first, Is.GreaterThan(0));
        Assert.That(second, Is.GreaterThan(0));
        Assert.That(firstKph > secondKph ? second : first, Is.GreaterThan(firstKph > secondKph ? first : second));
        Assert.That(result[0].DamageEvents.Single().Attribution.InstigatorId, Is.EqualTo(2));
        Assert.That(result[1].DamageEvents.Single().Attribution.InstigatorId, Is.EqualTo(1));
        Assert.That(result[0].Snapshot.ObservedPhysics.LinearVelocity.Z,
            Is.EqualTo((secondKph - firstKph) / 7.2f).Within(.0001f));
    }

    [TestCase(8.333333f)]
    [TestCase(16.666666f)]
    public void EqualOpposingImpactsRemainBalancedAndInelastic(float speed)
    {
        var result = Impact(speed, speed, false);
        Assert.That(result[0].Snapshot.Damage.CurrentHP, Is.EqualTo(result[1].Snapshot.Damage.CurrentHP));
        Assert.That(result[0].Snapshot.ObservedPhysics.LinearVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(result[1].Snapshot.ObservedPhysics.LinearVelocity, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void MassChangesTheAdvantageWithoutChangingReportingOrder()
    {
        var result = Impact(10, 10, false, 4500, 3000);
        Assert.That(result[0].Snapshot.Damage.CurrentHP, Is.GreaterThan(result[1].Snapshot.Damage.CurrentHP));
        Assert.That(result[1].Snapshot.ObservedPhysics.LinearVelocity.Z, Is.LessThan(0));
        var reversed = Impact(10, 10, true, 4500, 3000);
        Assert.That(reversed.Select(r => r.Snapshot.Damage), Is.EqualTo(result.Select(r => r.Snapshot.Damage)));
    }

    [Test]
    public void MatchedTouchingIgnoresLargeSupportImpulseAndDoesNotConsumeTheCooldown()
    {
        var world = new Core.Simulation.Simulation(new(60));
        var first = new VehiclePhysicsState(new(0, 0, 5), Quaternion.Identity, new(0, 0, -15), Vector3.Zero);
        var second = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, first.LinearVelocity, Vector3.Zero);
        world.AddVehicle(1, new(), new(), first); world.AddVehicle(2, new(), new(), second);
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        var result = world.Step(input, [
            new(1, input, new(first, Vector3.Zero, [new(Vector3.Zero, Vector3.UnitZ, 100000, 2)])),
            new(2, input, new(second, Vector3.Zero))]);
        Assert.That(result.All(r => r.DamageEvents.Count == 0 && r.Snapshot.Damage.LastCollisionTick is null), Is.True);
    }

    [Test]
    public void DuplicateReportsAndAnAlreadyResolvedContactCannotApplyDamageTwice()
    {
        var world = new Core.Simulation.Simulation(new(60));
        var first = new VehiclePhysicsState(new(0, 0, 5), Quaternion.Identity, new(0, 0, -20), Vector3.Zero);
        var second = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        world.AddVehicle(1, new(), new() { MaxHP = 1000 }, first); world.AddVehicle(2, new(), new() { MaxHP = 1000 }, second);
        for (ulong tick = 1; tick <= 60; tick++)
        {
            var a = world.GetVehicle(1).Movement.Physics; var b = world.GetVehicle(2).Movement.Physics;
            var contact = new VehicleContact(a.LinearVelocity - b.LinearVelocity, Vector3.UnitZ, 100000, 2, localPosition: new(0, 0, -2.5f));
            var observations = VehicleCollision.ResolveContacts(new Dictionary<ulong, VehicleObservation> {
                [1] = new(a, Vector3.Zero, [contact, contact]), [2] = new(b, Vector3.Zero)
            }, _ => new());
            var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
            var results = world.Step(input, observations.Select(p => new VehicleStepRequest(p.Key, input, p.Value)).ToArray());
            Assert.That(results.All(r => r.Snapshot.Damage.LastDamage?.Sequence == 1), Is.True);
            if (tick > 1) { Assert.That(results.All(r => r.DamageEvents.Count == 0), Is.True); }
        }
    }

    [TestCase(-1.8f)]
    [TestCase(1.8f)]
    public void ParkedOffCenterSideHitRotatesMoreThanCenterAndPreservesMomentum(float offset)
    {
        var tuning = new VehicleConfiguration();
        var target = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        (VehiclePhysicsState First, VehiclePhysicsState Second) Hit(float z) =>
            VehicleCollision.ResolvePair(new(new(-3, 0, z), Quaternion.Identity, new(10, 0, 0), Vector3.Zero), tuning,
                target, tuning, -Vector3.UnitX, new(-1.2f, 0, z));
        var center = Hit(0); var eccentric = Hit(offset);
        Assert.That(center.Second.AngularVelocity.Y, Is.Zero);
        Assert.That(center.Second.LinearVelocity.X, Is.GreaterThan(4));
        Assert.That(Math.Abs(eccentric.Second.AngularVelocity.Y), Is.GreaterThan(.5f));
        Assert.That(Math.Sign(eccentric.Second.AngularVelocity.Y), Is.EqualTo(Math.Sign(offset)));
        Assert.That(eccentric.First.LinearVelocity.X, Is.InRange(1, 9));
        Assert.That(eccentric.First.LinearVelocity.X + eccentric.Second.LinearVelocity.X, Is.EqualTo(10).Within(.0001f));
        float inertia = tuning.Mass * tuning.Wheelbase * tuning.Wheelbase / 3;
        float Energy(VehiclePhysicsState p) => .5f * (tuning.Mass * p.LinearVelocity.LengthSquared() + inertia * p.AngularVelocity.LengthSquared());
        Assert.That(Energy(eccentric.First) + Energy(eccentric.Second), Is.LessThan(150000));
    }

    [TestCase(0f)]
    [TestCase(.2f)]
    [TestCase(.75f)]
    [TestCase(1.5f)]
    public void OrdinarySideRubbingCannotQualifyForExtraRotation(float closing)
    {
        var tuning = new VehicleConfiguration();
        var a = new VehiclePhysicsState(new(-3, 0, 1.8f), Quaternion.Identity, new(closing, 0, -15), Vector3.Zero);
        var b = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -15), Vector3.Zero);
        var ordinary = tuning with { VehicleImpactYawResponse = 0, PitYawResponse = 0 };
        Assert.That(VehicleCollision.ResolvePair(a, tuning, b, tuning, -Vector3.UnitX, new(-1.2f, 0, 1.8f)),
            Is.EqualTo(VehicleCollision.ResolvePair(a, ordinary, b, ordinary, -Vector3.UnitX, new(-1.2f, 0, 1.8f))));
    }

    [TestCase(0f, true)]
    [TestCase(3f, true)]
    [TestCase(6f, false)]
    [TestCase(20f, false)]
    [TestCase(44.44f, false)]
    public void LowSpeedDriveAddsBoundedSupportedForceAndLeavesHighSpeedCommandsExact(float speed, bool stronger)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
        VehicleState Drive(VehicleConfiguration tuning, Vector3 support) {
            var movement = new VehicleMovement(tuning, pose);
            movement.Restore(new VehicleState(0, pose, true, false, 0, 0, throttle: 1));
            return movement.Step(new(1, 0, ushort.MaxValue, 0, 0, 0, 0), pose, support, true, SurfaceType.Asphalt);
        }
        var current = new VehicleConfiguration(); var baseline = current with { LowSpeedDriveMultiplier = 1 };
        var result = Drive(current, Vector3.UnitY); var before = Drive(baseline, Vector3.UnitY);
        if (stronger) {
            Assert.That(result.LongitudinalAcceleration, Is.GreaterThan(before.LongitudinalAcceleration));
            Assert.That(result.LongitudinalAcceleration, Is.LessThan(current.TireFriction * current.Gravity * .5f * current.RearDriveGrip * current.AsphaltGrip));
        }
        else { Assert.That(result, Is.EqualTo(before)); }
        Assert.That(Drive(current, Vector3.Zero), Is.EqualTo(Drive(baseline, Vector3.Zero)));
    }

    [Test]
    public void CombatTuningRoundTripsAndRejectsUnboundedControls()
    {
        var edits = new Dictionary<string, double> { ["vehicle.low_speed_drive_multiplier"] = 2, ["vehicle.low_speed_drive_fade_speed"] = 5,
            ["vehicle.impact_yaw_response"] = .7, ["damage.momentum_bias"] = .8 };
        Assert.That(GameplayOptions.TryApply(new(), edits, out var changed, out _), Is.True);
        byte[] wire = GameplayConfigurationCodec.Encode(1, new(1, changed));
        Assert.That(GameplayConfigurationCodec.Decode(wire).State.Configuration, Is.EqualTo(changed));
        wire[2] = 43;
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(wire));
        foreach (var bad in new[] { ("vehicle.low_speed_drive_multiplier", 4d), ("vehicle.low_speed_drive_fade_speed", 0d),
            ("vehicle.impact_yaw_response", 2d), ("damage.momentum_bias", 1d) }) {
            Assert.That(GameplayOptions.TryApply(changed, new Dictionary<string, double> { [bad.Item1] = bad.Item2 }, out _, out _), Is.False);
        }
    }

    [Test]
    public void CenteredContactManifoldDoesNotChooseACornerOrCountDuplicates()
    {
        var first = new VehiclePhysicsState(new(-3, 0, 0), Quaternion.Identity, new(10, 0, 0), Vector3.Zero);
        var second = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var front = new VehicleContact(first.LinearVelocity, -Vector3.UnitX, 0, 2, localPosition: new(1.8f, 0, -1));
        var rear = new VehicleContact(first.LinearVelocity, -Vector3.UnitX, 0, 2, localPosition: new(1.8f, 0, 1));
        var observations = new Dictionary<ulong, VehicleObservation> {
            [1] = new(first, Vector3.Zero, [front, rear, rear]), [2] = new(second, Vector3.Zero)
        };
        var result = VehicleCollision.ResolveContacts(observations, _ => new());
        Assert.That(result[1].Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(result[2].Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(result[2].Physics.LinearVelocity.X, Is.EqualTo(5));
        Assert.That(observations[1].Contacts.Count, Is.EqualTo(3), "Raw observations remain available.");
    }

    [Test]
    public void ResetSeparatesTheOldContactFromBothVehicleLives()
    {
        var world = new Core.Simulation.Simulation(new(60));
        var first = new VehiclePhysicsState(new(0, 0, 5), Quaternion.Identity, new(0, 0, -20), Vector3.Zero);
        var second = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        world.AddVehicle(1, new(), new(), first); world.AddVehicle(2, new(), new(), second);
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        var results = world.Step(input, [
            new(1, input, new(first, Vector3.Zero), reset: first),
            new(2, input, new(second, Vector3.Zero, [new(-first.LinearVelocity, -Vector3.UnitZ, 0, 1)]))
        ]);
        Assert.That(results.All(result => result.DamageEvents.Count == 0), Is.True);
        Assert.That(results[0].Snapshot.LifeId, Is.EqualTo(2));
    }

    [Test]
    public void ExtraLowSpeedDemandCannotLoadABlockingRockButRetainsTangentialAndReverseEscape()
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        VehicleState Drive(VehicleConfiguration tuning, Vector3 normal, bool reverse = false, bool side = true) {
            var movement = new VehicleMovement(tuning, pose);
            movement.Restore(new VehicleState(0, pose, true, false, 0, 0, throttle: reverse ? 0 : 1));
            var contact = new VehicleContact(Vector3.Zero, normal, 0, 0, staticObstacle: side, environmentRock: 1);
            return movement.Step(new(1, 0, reverse ? (ushort)0 : ushort.MaxValue, reverse ? ushort.MaxValue : (ushort)0, 0, 0, 0),
                pose, Vector3.UnitY, true, SurfaceType.Asphalt, contacts: [contact]);
        }
        var tuning = new VehicleConfiguration(); var baseline = tuning with { LowSpeedDriveMultiplier = 1 };
        var blocked = Drive(tuning, Vector3.UnitZ);
        Assert.That(blocked.Physics.LinearVelocity.Z, Is.EqualTo(0).Within(0.000001));
        Assert.That(blocked.Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
        Assert.That(blocked.Physics, Is.EqualTo(Drive(baseline, Vector3.UnitZ).Physics));
        Assert.That(Drive(tuning, Vector3.UnitX).LongitudinalAcceleration, Is.GreaterThan(Drive(baseline, Vector3.UnitX).LongitudinalAcceleration));
        Assert.That(Drive(tuning, Vector3.UnitZ, reverse: true).LongitudinalAcceleration,
            Is.LessThan(Drive(baseline, Vector3.UnitZ, reverse: true).LongitudinalAcceleration));
        Assert.That(Drive(tuning, Vector3.UnitY, side: false).LongitudinalAcceleration,
            Is.GreaterThan(Drive(baseline, Vector3.UnitY, side: false).LongitudinalAcceleration));
    }

    [TestCase(0)]
    [TestCase(65535)]
    public void CentralNitroCannotTurnCanceledRockForceIntoOppositeChassisPitch(int throttle)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        var contact = new VehicleContact(Vector3.Zero, Vector3.UnitZ, 0, 0, staticObstacle: true, environmentRock: 1);
        var input = new InputFrame(1, 0, (ushort)throttle, 0, InputButtons.UseItem, 0, 0);
        var ordinary = new VehicleMovement(new(), pose).Step(input, pose, Vector3.UnitY, contacts: [contact]);
        var boosted = new VehicleMovement(new(), pose).Step(input, pose, Vector3.UnitY, contacts: [contact],
            nitro: new NitroState(60, 18000, 1.4f, 1));
        Assert.That(boosted.Physics.LinearVelocity, Is.EqualTo(ordinary.Physics.LinearVelocity));
        Assert.That(boosted.Physics.AngularVelocity, Is.EqualTo(ordinary.Physics.AngularVelocity));
        Assert.That(boosted.Physics.AngularVelocity, Is.EqualTo(Vector3.Zero));
    }

    private static IReadOnlyList<VehicleStepResult> Impact(float firstSpeed, float secondSpeed, bool reverseReport, float firstMass = 3000, float secondMass = 3000)
    {
        var world = new Core.Simulation.Simulation(new(60));
        var first = new VehiclePhysicsState(new(0, 0, 5), Quaternion.Identity, new(0, 0, -firstSpeed), Vector3.Zero);
        var second = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, secondSpeed), Vector3.Zero);
        var a = new VehicleConfiguration { Mass = firstMass }; var b = new VehicleConfiguration { Mass = secondMass };
        world.AddVehicle(1, a, new() { MaxHP = 1000, CollisionScale = 5 }, first);
        world.AddVehicle(2, b, new() { MaxHP = 1000, CollisionScale = 5 }, second);
        var contact = new VehicleContact(first.LinearVelocity - second.LinearVelocity, Vector3.UnitZ, 0, 2, localPosition: new(0, 0, -2.5f));
        var mirrored = new VehicleContact(-contact.RelativeVelocity, -contact.Normal, 0, 1, localPosition: new(0, 0, 2.5f));
        var observations = VehicleCollision.ResolveContacts(new Dictionary<ulong, VehicleObservation> {
            [1] = new(first, Vector3.Zero, reverseReport ? [] : [contact]),
            [2] = new(second, Vector3.Zero, reverseReport ? [mirrored] : [])
        }, id => id == 1 ? a : b);
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        return world.Step(input, observations.Select(p => new VehicleStepRequest(p.Key, input, p.Value)).ToArray());
    }
}
