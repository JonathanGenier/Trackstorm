using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Development;

/// <summary>Authority, persistence and actual gameplay effects of live host configuration.</summary>
[TestFixture]
internal sealed class DeveloperConfigurationTests
{
    /// <summary>Verifies host transactions validate atomically and exclude preferences.</summary>
    [Test]
    public void HostTransactionsValidateAtomicallyAndExcludePreferences()
    {
        var host = new HostVehicleSession(9);
        host.Join(42);
        var before = host.Configuration;
        Assert.That(host.TryConfigure(42, new Dictionary<string, double> { ["vehicle.acceleration"] = 20 }, out _), Is.False);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["vehicle.acceleration"] = 20, ["vehicle.mass"] = -1 }, out _), Is.False);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["vehicle.acceleration"] = 20, ["vehicle.mass"] = 1e-40 }, out _), Is.False);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["vehicle.wheelbase"] = 1e-40 }, out _), Is.False);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["audio.volume"] = 0 }, out _), Is.False);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["vehicle.ticks_per_second"] = 120 }, out _), Is.False);
        Assert.That(host.Configuration, Is.EqualTo(before));
        Edit(host, ("vehicle.acceleration", 20), ("damage.max_hp", 200));
        Assert.That(host.Configuration.Revision, Is.EqualTo(1));
        Assert.That(host.World.State.Vehicles.All(vehicle => vehicle.Damage.MaxHP == 200), Is.True);
        host.Join(43);
        Assert.That(host.World.GetVehicle(3).Damage.MaxHP, Is.EqualTo(200));
        Edit(host, ("vehicle.acceleration", 20));
        Assert.That(host.Configuration.Revision, Is.EqualTo(1), "No-op transactions do not publish another revision.");
    }

    /// <summary>Verifies configuration codec and checkpoint preserve all values and reject invalid revisions.</summary>
    [Test]
    public void ConfigurationCodecAndCheckpointPreserveAllValuesAndRejectInvalidRevisions()
    {
        var host = new HostVehicleSession(9);
        Edit(host, ("vehicle.acceleration", 20), ("vehicle.mud.grip", 0.3), ("items.missile_speed", 90), ("match.kill_target", 8));
        var decoded = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(9, host.Configuration));
        Assert.That(decoded.Session, Is.EqualTo(9));
        Assert.That(decoded.State, Is.EqualTo(host.Configuration));
        Assert.That(decoded.State.CanReplace(host.Configuration), Is.True);
        Assert.That(new GameplayConfigurationState(0, new()).CanReplace(host.Configuration), Is.False);
        Assert.That(new GameplayConfigurationState(1, new()).CanReplace(host.Configuration), Is.False);
        var checkpoint = new ResumeCheckpoint(new ItemPublication(1, host.Snapshot(), [], [], []), host.World.State.Match!, null, host.Configuration);
        var restored = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint));
        Assert.That(restored.Configuration, Is.EqualTo(host.Configuration));
        Assert.That(restored.Items.World.ConfigurationRevision, Is.EqualTo(1));
        var payload = GameplayConfigurationCodec.Encode(9, host.Configuration);
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(payload.AsSpan(0, payload.Length - 1)));
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(payload.AsSpan(21), double.NaN);
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(payload));
    }

    /// <summary>Verifies persistence salvages records migrates aliases and preserves unknown keys.</summary>
    [Test]
    public void PersistenceSalvagesRecordsMigratesAliasesAndPreservesUnknownKeys()
    {
        string text = """
            {"schema":0}
            {"key":"vehicle.top_speed","value":35}
            {"key":"damage.max_hp","value":250}
            {"key":"vehicle.mass","value":-3}
            {broken record
            {"key":"future.setting","value":{"enabled":true}}
            {"key":"respawn.clear_held_item_on_death","value":false}
            """;
        var file = DeveloperSettingsFile.Read(text);
        Assert.That(file.Configuration.Vehicle.ForwardSpeed, Is.EqualTo(35));
        Assert.That(file.Configuration.Damage.MaxHP, Is.EqualTo(250));
        Assert.That(file.Configuration.Vehicle.Mass, Is.EqualTo(900));
        Assert.That(file.Configuration.Respawn.ClearHeldItemOnDeath, Is.False);
        Assert.That(file.RejectedRecords, Is.EqualTo(2));
        string written = file.Write(file.Configuration);
        Assert.That(written, Does.Contain("future.setting").And.Contain("\"schema\":2"));
        Assert.That(written, Does.Not.Contain("vehicle.top_speed").And.Not.Contain("ForceStart").And.Not.Contain("volume"));
        Assert.That(DeveloperSettingsFile.Read(written).Configuration, Is.EqualTo(file.Configuration));
        var future = DeveloperSettingsFile.Read("{\"schema\":999}\n{\"key\":\"vehicle.mass\",\"value\":500}");
        Assert.That(future.CanSave, Is.False);
        Assert.That(future.Configuration.Vehicle.Mass, Is.EqualTo(900));
        Assert.Throws<InvalidOperationException>(() => future.Write(new()));
    }

    /// <summary>Verifies persistence applies related bounds together without depending on file order.</summary>
    [Test]
    public void PersistenceAppliesRelatedBoundsTogetherWithoutDependingOnFileOrder()
    {
        var file = DeveloperSettingsFile.Read("""
            {"key":"vehicle.forward_speed","value":90}
            {"key":"vehicle.reverse_speed","value":80}
            {"key":"vehicle.maximum_physics_speed","value":100}
            """);
        Assert.That(file.Configuration.Vehicle.ForwardSpeed, Is.EqualTo(90));
        Assert.That(file.Configuration.Vehicle.ReverseSpeed, Is.EqualTo(80));
        Assert.That(file.RejectedRecords, Is.Zero);
    }

    /// <summary>Verifies acceleration and top speed change actual host movement and prediction.</summary>
    [Test]
    public void AccelerationAndTopSpeedChangeActualHostMovementAndPrediction()
    {
        var baseline = new HostVehicleSession(9);
        var tuned = new HostVehicleSession(9);
        Edit(tuned, ("vehicle.acceleration", 3));
        for (int tick = 0; tick < 60; tick++)
        {
            baseline.Step(Drive(), Observe);
            tuned.Step(Drive(), Observe);
        }

        Assert.That(tuned.World.GetVehicle(1).Movement.CommandSpeed, Is.LessThan(baseline.World.GetVehicle(1).Movement.CommandSpeed));
        Edit(tuned, ("vehicle.forward_speed", 8), ("vehicle.reverse_speed", 5), ("vehicle.acceleration", 20));
        for (int tick = 0; tick < 600; tick++)
        {
            tuned.Step(Drive(), Observe);
        }

        var predicted = new PredictedVehicle(tuned.Snapshot().Vehicles[0], tuned.Configuration.Configuration);
        for (int tick = 0; tick < 60; tick++)
        {
            tuned.Step(Drive(), Observe);
            predicted.Predict(Drive(), Observe);
        }

        Assert.That(tuned.World.GetVehicle(1).Movement.Physics.LinearVelocity.Z, Is.EqualTo(-8).Within(0.2));
        Assert.That(predicted.State.Movement.Physics.LinearVelocity, Is.EqualTo(tuned.World.GetVehicle(1).Movement.Physics.LinearVelocity));
    }

    /// <summary>Verifies handling and surfaces change the actual vehicle trajectory.</summary>
    /// <param name="key">Runtime handling or surface key under test.</param>
    /// <param name="value">Alternate validated value whose physical effect must be measurable.</param>
    [TestCase("vehicle.grip", 2)]
    [TestCase("vehicle.steering_angle", 0.2)]
    [TestCase("vehicle.steering_response", 0.5)]
    [TestCase("vehicle.handbrake_braking", 2)]
    [TestCase("vehicle.handbrake_grip", 0.1)]
    [TestCase("vehicle.handbrake_response", 1)]
    [TestCase("vehicle.concrete.grip", 0.2)]
    [TestCase("vehicle.concrete.drag", 8)]
    [TestCase("vehicle.concrete.acceleration", 0.2)]
    [TestCase("vehicle.mud.grip", 0.1)]
    [TestCase("vehicle.mud.drag", 8)]
    [TestCase("vehicle.mud.acceleration", 0.1)]
    public void HandlingAndSurfacesChangeTheActualVehicleTrajectory(string key, double value)
    {
        var baseline = new HostVehicleSession(9);
        var tuned = new HostVehicleSession(9);
        var physics = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(4, 0, -12), new Vector3(0, 0.2f, 0));
        Pose(baseline, 1, physics);
        Pose(tuned, 1, physics);
        Edit(tuned, (key, value));
        SurfaceType surface = key.Contains(".mud.", StringComparison.Ordinal) ? SurfaceType.Mud : SurfaceType.Concrete;
        bool handbrake = key.Contains("handbrake", StringComparison.Ordinal);
        var input = new InputFrame(0, 16000, 50000, 0, handbrake ? InputButtons.Drift : 0, 0, 0);
        for (int tick = 0; tick < 30; tick++)
        {
            baseline.Step(input, state => Observe(state, surface));
            tuned.Step(input, state => Observe(state, surface));
        }

        Assert.That(Vector3.Distance(tuned.World.GetVehicle(1).Movement.Physics.LinearVelocity, baseline.World.GetVehicle(1).Movement.Physics.LinearVelocity), Is.GreaterThan(0.00001f));
    }

    /// <summary>Verifies damage threshold scale cooldown and max hp affect actual collision health.</summary>
    [Test]
    public void DamageThresholdScaleCooldownAndMaxHpAffectActualCollisionHealth()
    {
        var host = new HostVehicleSession(9);
        Edit(host, ("damage.max_hp", 200), ("damage.collision_threshold", 10));
        VehicleObservation Contact(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY, [new VehicleContact(new Vector3(8, 0, 0), -Vector3.UnitX, 0, 0)]);
        host.Step(default, Contact);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(200));
        Edit(host, ("damage.collision_threshold", 4), ("damage.collision_scale", 2), ("damage.maximum_collision_damage", 15), ("damage.collision_cooldown_ticks", 2));
        host.Step(default, Contact);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(192));
        host.Step(default, Contact);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(192));
        host.Step(default, Contact);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(184));
        Edit(host, ("damage.collision_scale", 5));
        host.Step(default, Contact);
        host.Step(default, Contact);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(169));
        Edit(host, ("damage.max_hp", 100));
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(84.5f));
    }

    /// <summary>Verifies wrench missile speed lifetime radius damage and impulse use live owners.</summary>
    [Test]
    public void WrenchMissileSpeedLifetimeRadiusDamageAndImpulseUseLiveOwners()
    {
        var host = new HostVehicleSession(9);
        Hit(host, 1, 50);
        Edit(host, ("items.wrench_heal", 7));
        Assert.That(host.GiveItem(42, HeldItem.Wrench), Is.False);
        Assert.That(host.GiveItem(0, HeldItem.Wrench), Is.True);
        Assert.That(host.GiveItem(0, HeldItem.Missile), Is.False);
        Use(host);
        host.Step(default, Observe);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(57));
        Edit(host, ("items.missile_speed", 90), ("items.missile_lifetime_ticks", 8), ("items.explosion_radius", 4), ("items.maximum_damage", 80), ("items.maximum_impulse", 2000));
        Assert.That(host.GiveItem(0, HeldItem.Missile), Is.True);
        Use(host);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles.Single().Velocity.Length(), Is.EqualTo(90).Within(0.001));
        Assert.That(host.Items.Missiles.Single().RemainingTicks, Is.EqualTo(7));
        var effect = host.Items.Explosion(Vector3.Zero, new Vector3(2, 0, 0));
        Assert.That(effect.Damage, Is.EqualTo(40));
        Assert.That(effect.Impulse.Length(), Is.EqualTo(1000).Within(0.001));
        Assert.That(host.Items.Explosion(Vector3.Zero, new Vector3(5, 0, 0)).Damage, Is.Zero);
        Edit(host, ("items.missile_speed", 30), ("items.explosion_radius", 10));
        Assert.That(host.Items.Missiles.Single().Velocity.Length(), Is.EqualTo(30).Within(0.001));
        Assert.That(host.Items.Explosion(Vector3.Zero, new Vector3(5, 0, 0)).Damage, Is.GreaterThan(0));
    }

    /// <summary>Verifies pickup cooldown radius weights and seed reach authoritative claims.</summary>
    [Test]
    public void PickupCooldownRadiusWeightsAndSeedReachAuthoritativeClaims()
    {
        var host = new HostVehicleSession(9);
        host.RegisterSpawns(PrototypeArena.Configuration);
        var marker = PrototypeArena.Configuration.Items[0];
        Pose(host, 1, new VehiclePhysicsState(marker.Position + new Vector3(2, 0, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Edit(host, ("spawns.pickup_radius", 1));
        Assert.That(host.Spawns!.TryPickup(host.World, marker.Id, 1), Is.False);
        Edit(host, ("spawns.pickup_radius", 3), ("spawns.cooldown_ticks", 2), ("spawns.wrench_weight", 1000000), ("spawns.missile_weight", 1), ("spawns.seed", 99));
        Assert.That(host.Spawns.TryPickup(host.World, marker.Id, 1), Is.True);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Wrench));
        Assert.That(host.Spawns.States.Single(state => state.Id == marker.Id).NextActivationTick, Is.EqualTo(2));
        Use(host);
        host.Step(default, Observe);
        Assert.That(host.Spawns.States.Single(state => state.Id == marker.Id).Available, Is.False);
        host.Step(default, Observe);
        Assert.That(host.Spawns.States.Single(state => state.Id == marker.Id).Available, Is.True);
        Edit(host, ("spawns.wrench_weight", 1), ("spawns.missile_weight", 1000000), ("spawns.seed", 11));
        Assert.That(host.Spawns.TryPickup(host.World, marker.Id, 1), Is.True);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Missile));
    }

    /// <summary>Verifies respawn and force start retain normal lifecycle and minimum player rules.</summary>
    [Test]
    public void RespawnAndForceStartRetainNormalLifecycleAndMinimumPlayerRules()
    {
        var host = new HostVehicleSession(9);
        Edit(host, ("match.countdown_ticks", 2), ("match.minimum_players", 3), ("respawn.delay_ticks", 3), ("respawn.clear_held_item_on_death", 0));
        host.Step(default, Observe);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Waiting));
        Assert.That(host.ForceStart(42), Is.False);
        Assert.That(host.ForceStart(0), Is.True);
        host.Step(default, Observe);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Countdown));
        host.Step(default, Observe);
        host.Step(default, Observe);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
        Assert.That(host.Configuration.Configuration.Match.MinimumPlayers, Is.EqualTo(3));
        host.GiveItem(0, HeldItem.Missile);
        Hit(host, 1, 100);
        ulong death = host.World.State.Tick;
        Assert.That(host.World.GetVehicle(1).RespawnAtTick, Is.EqualTo(death + 3));
        Assert.That(host.GiveItem(0, HeldItem.Wrench), Is.False);
        host.Step(default, Observe);
        host.Step(default, Observe);
        Assert.That(host.World.GetVehicle(1).CanInteract, Is.False);
        host.Step(default, Observe);
        Assert.That(host.World.GetVehicle(1).CanInteract, Is.True);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Missile));
    }

    /// <summary>Verifies live kill target changes scoring and invalid target does not partially commit.</summary>
    [Test]
    public void LiveKillTargetChangesScoringAndInvalidTargetDoesNotPartiallyCommit()
    {
        var host = new HostVehicleSession(9);
        host.Join(42);
        Edit(host, ("match.countdown_ticks", 1), ("match.kill_target", 2), ("respawn.delay_ticks", 1));
        host.Step(default, Observe);
        host.Step(default, Observe);
        Hit(host, 2, 100);
        Assert.That(host.World.State.Match!.Players.Single(player => player.Player == 1).Kills, Is.EqualTo(1));
        var before = host.Configuration;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["match.kill_target"] = 1, ["vehicle.mass"] = 500 }, out _), Is.False);
        Assert.That(host.Configuration, Is.EqualTo(before));
        Edit(host, ("match.kill_target", 3));
        Assert.That(host.World.State.Match.KillTarget, Is.EqualTo(3));
        for (int kill = 2; kill <= 3; kill++)
        {
            host.Step(default, Observe);
            Hit(host, 2, 100);
            Assert.That(host.World.State.Match!.Phase, Is.EqualTo(kill == 3 ? MatchPhase.Finished : MatchPhase.Active));
        }

        Assert.That(host.World.State.Match!.Winner, Is.EqualTo(1));
    }

    /// <summary>Every movement control must change a real command or handling-memory result across physical scenarios.</summary>
    /// <param name="key">Stable vehicle option.</param>
    /// <param name="value">Alternate owning configuration value.</param>
    [TestCase("vehicle.mass", 450)]
    [TestCase("vehicle.acceleration", 4)]
    [TestCase("vehicle.braking", 4)]
    [TestCase("vehicle.stop_speed", 0.4)]
    [TestCase("vehicle.reverse_acceleration", 3)]
    [TestCase("vehicle.forward_speed", 20)]
    [TestCase("vehicle.reverse_speed", 4)]
    [TestCase("vehicle.grip", 3)]
    [TestCase("vehicle.steering_angle", 0.3)]
    [TestCase("vehicle.steering_speed", 8)]
    [TestCase("vehicle.steering_response", 2)]
    [TestCase("vehicle.wheelbase", 1.3)]
    [TestCase("vehicle.tire_friction", 0.5)]
    [TestCase("vehicle.drive_traction_reserve", 0.1)]
    [TestCase("vehicle.load_height", 1.2)]
    [TestCase("vehicle.handbrake_braking", 3)]
    [TestCase("vehicle.handbrake_grip", 0.15)]
    [TestCase("vehicle.handbrake_response", 2)]
    [TestCase("vehicle.traction_recovery", 1)]
    [TestCase("vehicle.coast_drag", 2)]
    [TestCase("vehicle.reference_mass", 600)]
    [TestCase("vehicle.suspension_spring", 9)]
    [TestCase("vehicle.suspension_damping", 3)]
    [TestCase("vehicle.chassis_compliance", 0.003)]
    [TestCase("vehicle.maximum_chassis_tilt", 0.03)]
    [TestCase("vehicle.stability_damping", 0.1)]
    [TestCase("vehicle.wheel_spring", 50)]
    [TestCase("vehicle.wheel_damping", 4)]
    [TestCase("vehicle.gravity", 3)]
    [TestCase("vehicle.maximum_physics_speed", 50)]
    [TestCase("vehicle.maximum_angular_speed", 1)]
    public void EachVehicleOptionChangesActualSimulationCommands(string key, double value)
    {
        bool changed = false;
        for (int scenario = 0; scenario < 30 && !changed; scenario++)
        {
            var baseline = new HostVehicleSession(9);
            var tuned = new HostVehicleSession(9);
            var physics = new VehiclePhysicsState(
                Vector3.Zero,
                Quaternion.CreateFromYawPitchRoll(0, 0.12f, -0.09f),
                new Vector3(scenario % 3 == 0 ? 25 : 3, scenario % 4 == 0 ? -1 : 0.1f, scenario % 5 == 0 ? -0.2f : scenario % 2 == 0 ? 12 : -55),
                new Vector3(0.3f, 3, -0.4f));
            bool straight = scenario >= 18;
            if (straight)
            {
                physics = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, scenario % 2 == 0 ? -24 : 10), Vector3.Zero);
            }

            foreach (var host in new[] { baseline, tuned })
            {
                var old = host.World.GetVehicle(1);
                var movement = new VehicleState(0, physics, true, true, straight ? 0 : 0.1f, straight ? 0 : 0.7f, longitudinalAcceleration: straight ? 0 : 2, lateralAcceleration: straight ? 0 : 8);
                host.World.Restore(new SimulationState(0, default, [new VehicleSnapshot(1, 1, movement, old.Damage, physics)], host.World.State.Match));
            }

            Edit(tuned, (key, value));
            ushort throttle = scenario % 3 == 0 ? ushort.MaxValue : (ushort)0;
            ushort brake = scenario % 3 == 1 ? ushort.MaxValue : (ushort)0;
            var input = new InputFrame(0, straight ? (short)0 : (short)20000, throttle, brake, !straight && scenario % 2 == 0 ? InputButtons.Drift : 0, 0, 0);
            VehicleObservation Observation(VehicleSnapshot state) => new(physics, scenario % 7 == 0 ? Vector3.Zero : Vector3.UnitY, wheels: new WheelSupport(new Vector4(0.08f, 0.13f, 0.05f, 0.1f)));
            baseline.Step(input, Observation);
            tuned.Step(input, Observation);
            changed = baseline.World.GetVehicle(1).Movement != tuned.World.GetVehicle(1).Movement;
        }

        Assert.That(changed, Is.True, key + " must affect actual movement, not merely stored configuration.");
    }


    /// <summary>Changing the seed restarts the actual claim sequence, independent of previous draws.</summary>
    [Test]
    public void LiveSeedChangesRestartActualItemSelection()
    {
        var host = new HostVehicleSession(9);
        host.RegisterSpawns(PrototypeArena.Configuration);
        var marker = PrototypeArena.Configuration.Items[0];
        Pose(host, 1, new VehiclePhysicsState(marker.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero));
        Edit(host, ("spawns.cooldown_ticks", 1), ("spawns.wrench_weight", 1), ("spawns.missile_weight", 1), ("items.missile_lifetime_ticks", 1));
        var sequences = new List<HeldItem[]>();
        foreach (int seed in new[] { 99, 11, 99 })
        {
            Edit(host, ("spawns.seed", seed));
            var draws = new List<HeldItem>();
            for (int i = 0; i < 12; i++)
            {
                Assert.That(host.Spawns!.TryPickup(host.World, marker.Id, 1), Is.True);
                draws.Add(host.Items.Slots.Single().Item);
                Use(host);
                host.Step(default, Observe);
            }

            sequences.Add(draws.ToArray());
        }

        Assert.That(sequences[0], Is.Not.EqualTo(sequences[1]));
        Assert.That(sequences[2], Is.EqualTo(sequences[0]));
    }

    /// <summary>Minimum-player and in-progress countdown edits change the normal lifecycle without Force Start.</summary>
    [Test]
    public void LiveMatchRulesChangeNormalCountdownAndActivation()
    {
        var host = new HostVehicleSession(9);
        host.Join(42);
        Edit(host, ("match.minimum_players", 3));
        host.Step(default, Observe);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Waiting));
        Edit(host, ("match.minimum_players", 2));
        host.Step(default, Observe);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Countdown));
        Edit(host, ("match.countdown_ticks", 4));
        Assert.That(host.World.State.Match!.CountdownAtTick, Is.EqualTo(host.World.State.Tick + 4));
        for (int i = 0; i < 3; i++)
        {
            host.Step(default, Observe);
            Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Countdown));
        }

        host.Step(default, Observe);
        Assert.That(host.World.State.Match!.Phase, Is.EqualTo(MatchPhase.Active));
    }

    private static void Edit(HostVehicleSession host, params (string Key, double Value)[] values) =>
        Assert.That(host.TryConfigure(0, values.ToDictionary(pair => pair.Key, pair => pair.Value), out string error), Is.True, error);

    private static InputFrame Drive() => new(0, 0, ushort.MaxValue, 0, 0, 0, 0);

    private static VehicleObservation Observe(VehicleSnapshot state) => Observe(state, SurfaceType.Concrete);

    private static VehicleObservation Observe(VehicleSnapshot state, SurfaceType surface)
    {
        var physics = state.Movement.Physics;
        return new(new VehiclePhysicsState(physics.Position + (physics.LinearVelocity / 60), physics.Orientation, new Vector3(physics.LinearVelocity.X, 0, physics.LinearVelocity.Z), physics.AngularVelocity), Vector3.UnitY, surface: surface);
    }

    private static void Pose(HostVehicleSession host, ulong id, VehiclePhysicsState physics)
    {
        var state = host.World.State;
        var vehicles = state.Vehicles.Select(vehicle => vehicle.VehicleId != id ? vehicle
            : new VehicleSnapshot(id, vehicle.LifeId, new VehicleState(state.Tick, physics, true, false, 0, 0), vehicle.Damage, physics));
        host.World.Restore(new SimulationState(state.Tick, state.LastInput, vehicles, state.Match));
    }

    private static void Hit(HostVehicleSession host, ulong target, float damage)
    {
        ulong tick = host.World.State.Tick + 1;
        var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        var requests = host.World.State.Vehicles.Select(vehicle =>
        {
            VehicleEffectRequest[] effects = vehicle.VehicleId == target
                ? [new VehicleEffectRequest(new DamageEffect(damage, Vector3.Zero, Vector3.Zero), new DamageContext("missile", 1, "test"))] : [];
            return new VehicleStepRequest(vehicle.VehicleId, input, Observe(vehicle), effects);
        }).ToArray();
        host.World.Step(input, requests);
    }

    private static void Use(HostVehicleSession host)
    {
        var slot = host.Items.Slots.Single(slot => slot.Vehicle == 1);
        Assert.That(host.UseItem(0, host.SessionId, slot.Life, slot.Token), Is.True);
    }
}
