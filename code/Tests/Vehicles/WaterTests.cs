using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Water uses the existing atomic health, movement, tuning and recovery boundaries.</summary>
[TestFixture]
internal sealed class WaterTests
{
    private static VehiclePhysicsState Pose => new(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);

    [Test]
    public void ShallowWaterTraversesMoreSlowlyThanDeepMudWithoutDamage()
    {
        float mud = Drive(SurfaceType.DeepMud, 0);
        float water = Drive(SurfaceType.Water, 0.5f);
        Assert.That(water, Is.GreaterThan(0.5f).And.LessThan(mud * 0.8f));
    }

    [Test]
    public void DeepWaterDamagesWithoutGroundContactAndRespawnsThroughExistingLifecycle()
    {
        var world = Create();
        for (int cycle = 0; cycle < 4; cycle++)
        {
            ulong life = world.GetVehicle(1).LifeId;
            int deaths = 0;
            for (int i = 0; i < 250; i++)
            {
                Step(world, 1.2f);
                deaths += world.LifecycleChanges.Count(v => v.Lifecycle == VehicleLifecycle.Dead);
                if (!world.GetVehicle(1).CanInteract) { break; }
            }
            var dead = world.GetVehicle(1);
            Assert.That(dead.Damage.CurrentHP, Is.Zero);
            Assert.That(dead.Damage.LastDamage!.Attribution.Source, Is.EqualTo("water"));
            Assert.That(deaths, Is.EqualTo(1));
            ulong deadline = dead.RespawnAtTick!.Value;
            while (world.State.Tick < deadline) { Step(world, 1.2f); }
            Assert.That(world.GetVehicle(1).LifeId, Is.EqualTo(life + 1));
            Assert.That(world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
            Assert.That(world.GetVehicle(1).CanInteract, Is.True);
        }
    }

    [Test]
    public void ExitsStopDamageReentryDoesNotHealAndRestorePreservesTheHazardOutcome()
    {
        var world = Create();
        for (int i = 0; i < 30; i++) { Step(world, 1); }
        float hp = world.GetVehicle(1).Damage.CurrentHP;
        Assert.That(hp, Is.EqualTo(875).Within(0.01));
        for (int i = 0; i < 120; i++) { Step(world, i % 2 == 0 ? 0 : 0.999f); }
        Assert.That(world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(hp));
        var snapshot = VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(world.GetVehicle(1)));
        var restored = Create();
        restored.Restore(new Core.Simulation.SimulationState(world.State.Tick, world.State.LastInput, [snapshot]));
        Step(world, 1); Step(restored, 1);
        Assert.That(restored.GetVehicle(1).Damage, Is.EqualTo(world.GetVehicle(1).Damage));
        Assert.That(restored.GetVehicle(1).Damage.CurrentHP, Is.LessThan(hp));
        var frame = new InputFrame(world.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        world.Step(frame, [new VehicleStepRequest(1, frame, new(Pose, Vector3.Zero, waterDepth: 2), reset: Pose)]);
        Assert.That(world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
    }

    [Test]
    public void WaterTuningRoundTripsAndPredictionCannotOriginateDamage()
    {
        var original = GameplayConfiguration.HostedDefaults;
        foreach (var option in GameplayOptions.All.Where(o => o.Group == "Water"))
        {
            Assert.That(GameplayOptions.TryApply(original, new Dictionary<string, double> { [option.Key] = option.Read(original) * 0.8 }, out var edited, out var error), Is.True, error);
            Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(1, new(1, edited))).State.Configuration, Is.EqualTo(edited));
            Assert.That(DeveloperSettingsFile.Read(DeveloperSettingsFile.Read("").Write(edited)).Configuration, Is.EqualTo(edited));
            Assert.That(GameplayOptions.TryApply(original, new Dictionary<string, double> { [option.Key] = double.NaN }, out _, out _), Is.False);
        }
        var world = Create();
        var prediction = new PredictedVehicle(new ReplicatedVehicle(world.GetVehicle(1), 0), original);
        prediction.Predict(new InputFrame(1, 0, 65535, 0, 0, 0, 0), _ => new(Pose, Vector3.UnitY, waterDepth: 2));
        Assert.That(prediction.State.Damage.CurrentHP, Is.EqualTo(1000));
        Assert.That(prediction.State.Movement.CurrentSurface, Is.EqualTo(SurfaceType.Water));
    }

    [Test]
    public void InvalidDepthIsRejectedBeforeAuthority()
    {
        foreach (float depth in new[] { -1, float.NaN, float.PositiveInfinity, 1001 })
        { Assert.Throws<ArgumentOutOfRangeException>(() => new VehicleObservation(Pose, Vector3.Zero, waterDepth: depth)); }
    }

    [Test]
    public void HostValidatedClientEditsChangeDepthAndDamage()
    {
        var host = new HostVehicleSession(9);
        host.Join(42);
        var edits = new Dictionary<string, double> { ["vehicle.water.depth"] = 2, ["vehicle.water.damage"] = 600 };
        Assert.That(host.TryConfigure(999, edits, out _), Is.False);
        Assert.That(host.TryConfigure(42, edits, out _), Is.True);
        void Advance(float depth)
        {
            var frame = new InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
            host.World.Step(frame, host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, frame,
                new VehicleObservation(v.ObservedPhysics, Vector3.Zero, waterDepth: v.VehicleId == 1 ? depth : 0))).ToArray());
        }
        Advance(1.5f);
        float hp = host.World.GetVehicle(1).Damage.CurrentHP;
        Advance(2);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(hp - 10).Within(0.001));
        Assert.That(host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(host.World.GetVehicle(2).Damage.MaxHP));
        var checkpoint = new ResumeCheckpoint(new Core.Items.ItemPublication(1, host.Snapshot(), [], [], []), host.World.State.Match!, null, host.Configuration);
        var restored = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint));
        Assert.That(restored.Configuration, Is.EqualTo(host.Configuration));
        Assert.That(restored.Items.World.Vehicles[0].State.Damage, Is.EqualTo(host.World.GetVehicle(1).Damage));
    }

    private static Core.Simulation.Simulation Create()
    {
        var world = new Core.Simulation.Simulation(new(60), new RespawnConfiguration { DelayTicks = 6 });
        world.AddVehicle(1, new(), new() { MaxHP = 1000 }, Pose);
        return world;
    }

    private static void Step(Core.Simulation.Simulation world, float depth)
    {
        var frame = new InputFrame(world.State.Tick + 1, 0, 65535, 0, 0, 0, 0);
        world.Step(frame, [new VehicleStepRequest(1, frame, new(Pose, Vector3.Zero, waterDepth: depth))]);
    }

    private static float Drive(SurfaceType surface, float depth)
    {
        var world = Create();
        var physics = Pose;
        for (int tick = 1; tick <= 600; tick++)
        {
            var frame = new InputFrame((ulong)tick, 0, 65535, 0, 0, 0, 0);
            world.Step(frame, [new VehicleStepRequest(1, frame, new(physics, Vector3.UnitY, surface: surface, waterDepth: depth))]);
            var velocity = world.GetVehicle(1).Movement.Physics.LinearVelocity;
            velocity.Y = 0;
            physics = new(physics.Position + velocity / 60, Quaternion.Identity, velocity, Vector3.Zero);
        }
        Assert.That(world.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
        return -physics.LinearVelocity.Z;
    }
}
