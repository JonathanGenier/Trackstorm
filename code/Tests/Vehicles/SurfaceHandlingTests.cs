using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Surface tuning, production authority, portable state and deterministic movement coverage.</summary>
[TestFixture]
internal sealed class SurfaceHandlingTests
{
    /// <summary>Both surfaces resolve their configured values and defaults intentionally differ.</summary>
    [Test]
    public void Configuration_ResolvesConfiguredAndDefaultMultipliers()
    {
        var defaults = new VehicleConfiguration();
        Assert.That(defaults.ResolveSurface(SurfaceType.Concrete), Is.EqualTo(new SurfaceModifiers(1, 1, 1)));
        Assert.That(defaults.ResolveSurface(SurfaceType.Mud), Is.EqualTo(new SurfaceModifiers(0.55f, 3, 0.6f)));
        var custom = defaults with { Concrete = new(0.8f, 2, 0.7f), Mud = new(0.3f, 5, 0.2f) };
        Assert.That(custom.ResolveSurface(SurfaceType.Concrete), Is.EqualTo(custom.Concrete));
        Assert.That(custom.ResolveSurface(SurfaceType.Mud), Is.EqualTo(custom.Mud));
        Assert.Throws<ArgumentOutOfRangeException>(() => custom.ResolveSurface((SurfaceType)2));
    }

    /// <summary>Unsafe tuning is rejected before multiplication; zero and maximum legal values remain bounded.</summary>
    [Test]
    public void Multipliers_RejectUnsafeValuesAndComposeWithoutNegativeHandling()
    {
        foreach (float invalid in new[] { -1, float.NaN, float.NegativeInfinity, float.PositiveInfinity, 101 })
        {
            Assert.Throws<ArgumentException>(() => new SurfaceModifiers(invalid, 1, 1));
            Assert.Throws<ArgumentException>(() => new SurfaceModifiers(1, invalid, 1));
            Assert.Throws<ArgumentException>(() => new SurfaceModifiers(1, 1, invalid));
        }

        foreach (float value in new[] { 0f, 100f })
        {
            var tuning = new VehicleConfiguration { Mud = new(value, value, value) };
            var movement = new VehicleMovement(tuning, Physics());
            VehicleState result = movement.Step(Frame(1), Physics(new Vector3(5, 0, -10)), Vector3.UnitY, surface: SurfaceType.Mud);
            Assert.That(result.Physics.LinearVelocity.X, Is.InRange(0, 5));
            Assert.That(-result.Physics.LinearVelocity.Z, Is.InRange(0, tuning.MaximumPhysicsSpeed));
            Assert.That(result.CommandSpeed, Is.LessThanOrEqualTo(tuning.MaximumPhysicsSpeed));
        }
    }

    /// <summary>Mud scales acceleration and grip, adds resistance, and restoring Concrete restores identical baseline commands.</summary>
    [Test]
    public void Movement_ComposesTuningAndRestoresBaseline()
    {
        var tuning = new VehicleConfiguration();
        var movement = new VehicleMovement(tuning, Physics());
        VehicleState concrete = movement.Step(Frame(1), Physics(new Vector3(5, 0, -10)), Vector3.UnitY);
        VehicleState mud = movement.Step(Frame(2), Physics(new Vector3(5, 0, -10)), Vector3.UnitY, surface: SurfaceType.Mud);
        VehicleState restored = movement.Step(Frame(3), Physics(new Vector3(5, 0, -10)), Vector3.UnitY);
        Assert.That(mud.Physics.LinearVelocity.X, Is.GreaterThan(concrete.Physics.LinearVelocity.X));
        Assert.That(-mud.Physics.LinearVelocity.Z, Is.LessThan(-concrete.Physics.LinearVelocity.Z));

        Assert.That(restored.Physics.LinearVelocity.X, Is.LessThan(mud.Physics.LinearVelocity.X));
        Assert.That(restored.CurrentSurface, Is.EqualTo(SurfaceType.Concrete));
        var reverse = new InputFrame(4, 0, 0, 65535, InputButtons.None, InputButtons.None, InputButtons.None);
        Assert.That(
            movement.Step(reverse, Physics(), Vector3.UnitY, surface: SurfaceType.Mud).Physics.LinearVelocity.Z,
            Is.InRange(0.001f, 8 * 0.6f / 60));
    }

    /// <summary>Nondefault surface tuning controls coasting, ordinary grip and drift grip through the same response.</summary>
    [Test]
    public void CustomTuning_ComposesCoastingAndTireGrip()
    {
        var tuning = new VehicleConfiguration { Concrete = new(0.4f, 2, 0.3f), Mud = new(0.2f, 4, 0.1f) };
        foreach (SurfaceType surface in Enum.GetValues<SurfaceType>())
        {
            SurfaceModifiers modifiers = tuning.ResolveSurface(surface);
            foreach (bool drift in new[] { false, true })
            {
                var movement = new VehicleMovement(tuning, Physics());
                var input = new InputFrame(1, 18000, 0, 0, drift ? InputButtons.Drift : InputButtons.None, InputButtons.None, InputButtons.None);
                VehicleState result = movement.Step(input, Physics(new Vector3(5, 0, -18)), Vector3.UnitY, surface: surface);
                float coastingSpeed = 18 * MathF.Exp(-tuning.CoastDrag * modifiers.Drag / tuning.TicksPerSecond);
                if (drift)
                {
                    Assert.That(-result.Physics.LinearVelocity.Z, Is.LessThan(coastingSpeed));
                }
                else
                {
                    Assert.That(-result.Physics.LinearVelocity.Z, Is.EqualTo(coastingSpeed).Within(0.00001f));
                }

                Assert.That(result.Physics.LinearVelocity.X, Is.InRange(4.8f, 5f));
                Assert.That(result.FrontSlip, Is.InRange(0, 1));
                Assert.That(result.RearSlip, Is.InRange(0, 1));
            }
        }
    }

    /// <summary>Surface changes preserve handbrake recovery; flight ignores retained surface handling.</summary>
    [Test]
    public void HandbrakeAndFlight_RemainContinuousAcrossSurfaces()
    {
        var movement = new VehicleMovement(new(), Physics());
        VehiclePhysicsState observed = Physics(new Vector3(0, 0, -18));
        for (ulong tick = 1; tick <= 45; tick++)
        {
            movement.Step(Frame(tick, true), observed, Vector3.UnitY, surface: tick < 20 ? SurfaceType.Concrete : SurfaceType.Mud);
        }

        Assert.That(movement.State.Handbrake, Is.EqualTo(1));
        Assert.That(movement.Step(Frame(46), observed, Vector3.UnitY, surface: SurfaceType.Mud).Handbrake, Is.InRange(0.9f, 0.99f));
        VehicleState beforeFlight = movement.State;
        var comparison = new VehicleMovement(new(), observed);
        comparison.Restore(beforeFlight);
        VehicleState flight = movement.Step(Frame(47, true), observed, Vector3.Zero);
        Assert.That(comparison.Step(Frame(47, true), observed, Vector3.Zero, surface: SurfaceType.Mud), Is.EqualTo(flight));
        Assert.That(flight.CurrentSurface, Is.EqualTo(SurfaceType.Mud));
        Assert.That(flight.Grounded || flight.Drifting, Is.False);
        Assert.That(flight.Handbrake, Is.EqualTo(1));
        Assert.That(movement.Step(Frame(48), observed, Vector3.UnitY).CurrentSurface, Is.EqualTo(SurfaceType.Concrete));
    }

    /// <summary>The real simulation commits, serializes and restores surface memory without a second movement path.</summary>
    [Test]
    public void Authority_ReplayAndRoundTripPreserveSurfaceAndCommands()
    {
        var first = new Trackstorm.Core.Simulation.Simulation(new SimulationConfiguration(60));
        var second = new Trackstorm.Core.Simulation.Simulation(new SimulationConfiguration(60));
        first.AddVehicle(1, new(), new(), Physics());
        second.AddVehicle(1, new(), new(), Physics());
        for (ulong tick = 1; tick <= 600; tick++)
        {
            SurfaceType surface = tick % 40 < 20 ? SurfaceType.Concrete : SurfaceType.Mud;
            Vector3 support = tick % 60 < 50 ? Vector3.UnitY : Vector3.Zero;
            InputFrame input = Frame(tick, tick % 60 < 45);
            var observation = new VehicleObservation(Physics(new Vector3(3, 0, -16)), support, surface: surface);
            first.Step(input, [new VehicleStepRequest(1, input, observation)]);
            second.Step(input, [new VehicleStepRequest(1, input, observation)]);
            VehicleSnapshot snapshot = first.GetVehicle(1);
            Assert.That(second.GetVehicle(1).Movement, Is.EqualTo(snapshot.Movement));
            if (support != Vector3.Zero)
            {
                Assert.That(snapshot.Movement.CurrentSurface, Is.EqualTo(surface));
            }

            VehicleSnapshot decoded = VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(snapshot));
            Assert.That(decoded.Movement, Is.EqualTo(snapshot.Movement));
            second.Restore(new SimulationState(tick, input, [decoded]));
        }

        InputFrame reset = Frame(601);
        first.Step(reset, [new VehicleStepRequest(1, reset, new VehicleObservation(Physics(), Vector3.UnitY, surface: SurfaceType.Mud), reset: Physics())]);
        Assert.That(first.GetVehicle(1).Movement.CurrentSurface, Is.EqualTo(SurfaceType.Concrete));
    }

    /// <summary>Unknown IDs fail at observations and portable state boundaries before they can affect movement.</summary>
    [Test]
    public void InvalidSurface_IsRejectedWithoutMutatingMovement()
    {
        var movement = new VehicleMovement(new(), Physics());
        VehicleState before = movement.State;
        Assert.Throws<ArgumentOutOfRangeException>(() => movement.Step(Frame(1), Physics(), Vector3.UnitY, surface: (SurfaceType)2));
        Assert.That(movement.State, Is.EqualTo(before));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VehicleObservation(Physics(), Vector3.UnitY, surface: (SurfaceType)255));
        byte[] payload = VehicleStateCodec.Encode(before);
        payload[70] = 255;
        Assert.Throws<ArgumentOutOfRangeException>(() => VehicleStateCodec.Decode(payload));
    }

    private static VehiclePhysicsState Physics(Vector3 velocity = default) => new(Vector3.Zero, Quaternion.Identity, velocity, Vector3.Zero);

    private static InputFrame Frame(ulong tick, bool drift = false) => new(tick, 18000, 65535, 0, drift ? InputButtons.Drift : InputButtons.None, InputButtons.None, InputButtons.None);
}
