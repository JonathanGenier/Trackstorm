namespace Trackstorm.Core.Development;

/// <summary>Allowlisted gameplay controls; local preferences, fixed rate and actions are deliberately absent.</summary>
public static class GameplayOptions
{
    /// <summary>Stable allowlist of editable gameplay settings.</summary>
    public static IReadOnlyList<GameplayOption> All { get; } = Array.AsReadOnly<GameplayOption>(
    [
        new("environment.preset", "Sky / Environment", "Environment preset", true, c => (int)c.Environment, (c, v) => c with { Environment = (EnvironmentPreset)checked((int)v) }),
        new("vehicle.mass", "Vehicle", "Mass", false, c => c.Vehicle.Mass, (c, v) => c with { Vehicle = c.Vehicle with { Mass = checked((float)v) } }),
        new("vehicle.acceleration", "Vehicle", "Acceleration", false, c => c.Vehicle.Acceleration, (c, v) => c with { Vehicle = c.Vehicle with { Acceleration = checked((float)v) } }),
        new("vehicle.braking", "Vehicle", "Braking", false, c => c.Vehicle.Braking, (c, v) => c with { Vehicle = c.Vehicle with { Braking = checked((float)v) } }),
        new("vehicle.stop_speed", "Vehicle", "StopSpeed", false, c => c.Vehicle.StopSpeed, (c, v) => c with { Vehicle = c.Vehicle with { StopSpeed = checked((float)v) } }),
        new("vehicle.reverse_acceleration", "Vehicle", "ReverseAcceleration", false, c => c.Vehicle.ReverseAcceleration, (c, v) => c with { Vehicle = c.Vehicle with { ReverseAcceleration = checked((float)v) } }),
        new("vehicle.forward_speed", "Vehicle", "ForwardSpeed", false, c => c.Vehicle.ForwardSpeed, (c, v) => c with { Vehicle = c.Vehicle with { ForwardSpeed = checked((float)v) } }),
        new("vehicle.reverse_speed", "Vehicle", "ReverseSpeed", false, c => c.Vehicle.ReverseSpeed, (c, v) => c with { Vehicle = c.Vehicle with { ReverseSpeed = checked((float)v) } }),
        new("vehicle.grip", "Vehicle", "Grip", false, c => c.Vehicle.Grip, (c, v) => c with { Vehicle = c.Vehicle with { Grip = checked((float)v) } }),
        new("vehicle.steering_angle", "Vehicle", "SteeringAngle", false, c => c.Vehicle.SteeringAngle, (c, v) => c with { Vehicle = c.Vehicle with { SteeringAngle = checked((float)v) } }),
        new("vehicle.steering_speed", "Vehicle", "SteeringSpeed", false, c => c.Vehicle.SteeringSpeed, (c, v) => c with { Vehicle = c.Vehicle with { SteeringSpeed = checked((float)v) } }),
        new("vehicle.steering_response", "Vehicle", "SteeringResponse", false, c => c.Vehicle.SteeringResponse, (c, v) => c with { Vehicle = c.Vehicle with { SteeringResponse = checked((float)v) } }),
        new("vehicle.steering_smoothing", "Vehicle", "Steering smoothing", false, c => c.Vehicle.SteeringSmoothing, (c, v) => c with { Vehicle = c.Vehicle with { SteeringSmoothing = checked((float)v) } }),
        new("vehicle.dirt_power_slip", "Vehicle", "Dirt power slip", false, c => c.Vehicle.DirtPowerSlip, (c, v) => c with { Vehicle = c.Vehicle with { DirtPowerSlip = checked((float)v) } }),
        new("vehicle.power_slip_response", "Vehicle", "Power slip buildup", false, c => c.Vehicle.PowerSlipResponse, (c, v) => c with { Vehicle = c.Vehicle with { PowerSlipResponse = checked((float)v) } }),
        new("vehicle.power_slip_recovery", "Vehicle", "Power slip recovery", false, c => c.Vehicle.PowerSlipRecovery, (c, v) => c with { Vehicle = c.Vehicle with { PowerSlipRecovery = checked((float)v) } }),
        new("vehicle.wheelbase", "Vehicle", "Wheelbase", false, c => c.Vehicle.Wheelbase, (c, v) => c with { Vehicle = c.Vehicle with { Wheelbase = checked((float)v) } }),
        new("vehicle.tire_friction", "Vehicle", "TireFriction", false, c => c.Vehicle.TireFriction, (c, v) => c with { Vehicle = c.Vehicle with { TireFriction = checked((float)v) } }),
        new("vehicle.drive_traction_reserve", "Vehicle", "DriveTractionReserve", false, c => c.Vehicle.DriveTractionReserve, (c, v) => c with { Vehicle = c.Vehicle with { DriveTractionReserve = checked((float)v) } }),
        new("vehicle.load_height", "Vehicle", "LoadHeight", false, c => c.Vehicle.LoadHeight, (c, v) => c with { Vehicle = c.Vehicle with { LoadHeight = checked((float)v) } }),
        new("vehicle.handbrake_braking", "Vehicle", "HandbrakeBraking", false, c => c.Vehicle.HandbrakeBraking, (c, v) => c with { Vehicle = c.Vehicle with { HandbrakeBraking = checked((float)v) } }),
        new("vehicle.handbrake_grip", "Vehicle", "HandbrakeGrip", false, c => c.Vehicle.HandbrakeGrip, (c, v) => c with { Vehicle = c.Vehicle with { HandbrakeGrip = checked((float)v) } }),
        new("vehicle.handbrake_response", "Vehicle", "HandbrakeResponse", false, c => c.Vehicle.HandbrakeResponse, (c, v) => c with { Vehicle = c.Vehicle with { HandbrakeResponse = checked((float)v) } }),
        new("vehicle.traction_recovery", "Vehicle", "TractionRecovery", false, c => c.Vehicle.TractionRecovery, (c, v) => c with { Vehicle = c.Vehicle with { TractionRecovery = checked((float)v) } }),
        new("vehicle.coast_drag", "Vehicle", "CoastDrag", false, c => c.Vehicle.CoastDrag, (c, v) => c with { Vehicle = c.Vehicle with { CoastDrag = checked((float)v) } }),
        new("vehicle.reference_mass", "Vehicle", "ReferenceMass", false, c => c.Vehicle.ReferenceMass, (c, v) => c with { Vehicle = c.Vehicle with { ReferenceMass = checked((float)v) } }),
        new("vehicle.suspension_spring", "Vehicle", "SuspensionSpring", false, c => c.Vehicle.SuspensionSpring, (c, v) => c with { Vehicle = c.Vehicle with { SuspensionSpring = checked((float)v) } }),
        new("vehicle.suspension_damping", "Vehicle", "SuspensionDamping", false, c => c.Vehicle.SuspensionDamping, (c, v) => c with { Vehicle = c.Vehicle with { SuspensionDamping = checked((float)v) } }),
        new("vehicle.chassis_compliance", "Vehicle", "ChassisCompliance", false, c => c.Vehicle.ChassisCompliance, (c, v) => c with { Vehicle = c.Vehicle with { ChassisCompliance = checked((float)v) } }),
        new("vehicle.maximum_chassis_tilt", "Vehicle", "MaximumChassisTilt", false, c => c.Vehicle.MaximumChassisTilt, (c, v) => c with { Vehicle = c.Vehicle with { MaximumChassisTilt = checked((float)v) } }),
        new("vehicle.stability_damping", "Vehicle", "StabilityDamping", false, c => c.Vehicle.StabilityDamping, (c, v) => c with { Vehicle = c.Vehicle with { StabilityDamping = checked((float)v) } }),
        new("vehicle.suspension_length", "Vehicle", "SuspensionLength", false, c => c.Vehicle.SuspensionLength, (c, v) => c with { Vehicle = c.Vehicle with { SuspensionLength = checked((float)v) } }),
        new("vehicle.wheel_spring", "Vehicle", "WheelSpring", false, c => c.Vehicle.WheelSpring, (c, v) => c with { Vehicle = c.Vehicle with { WheelSpring = checked((float)v) } }),
        new("vehicle.wheel_damping", "Vehicle", "WheelDamping", false, c => c.Vehicle.WheelDamping, (c, v) => c with { Vehicle = c.Vehicle with { WheelDamping = checked((float)v) } }),
        new("vehicle.wheel_rebound_damping", "Vehicle", "Wheel rebound damping", false, c => c.Vehicle.WheelReboundDamping, (c, v) => c with { Vehicle = c.Vehicle with { WheelReboundDamping = checked((float)v) } }),
        new("vehicle.wheel_bump_start", "Vehicle", "Wheel bump engagement (m)", false, c => c.Vehicle.WheelBumpStart, (c, v) => c with { Vehicle = c.Vehicle with { WheelBumpStart = checked((float)v) } }),
        new("vehicle.wheel_bump_spring", "Vehicle", "Wheel progressive bump spring", false, c => c.Vehicle.WheelBumpSpring, (c, v) => c with { Vehicle = c.Vehicle with { WheelBumpSpring = checked((float)v) } }),
        new("vehicle.gravity", "Vehicle", "Gravity", false, c => c.Vehicle.Gravity, (c, v) => c with { Vehicle = c.Vehicle with { Gravity = checked((float)v) } }),
        new("vehicle.maximum_physics_speed", "Vehicle", "MaximumPhysicsSpeed", false, c => c.Vehicle.MaximumPhysicsSpeed, (c, v) => c with { Vehicle = c.Vehicle with { MaximumPhysicsSpeed = checked((float)v) } }),
        new("vehicle.maximum_angular_speed", "Vehicle", "MaximumAngularSpeed", false, c => c.Vehicle.MaximumAngularSpeed, (c, v) => c with { Vehicle = c.Vehicle with { MaximumAngularSpeed = checked((float)v) } }),
        new("damage.max_hp", "Damage", "MaxHP", false, c => c.Damage.MaxHP, (c, v) => c with { Damage = c.Damage with { MaxHP = checked((float)v) } }),
        new("damage.collision_threshold", "Damage", "CollisionThreshold", false, c => c.Damage.CollisionThreshold, (c, v) => c with { Damage = c.Damage with { CollisionThreshold = checked((float)v) } }),
        new("damage.collision_scale", "Damage", "CollisionScale", false, c => c.Damage.CollisionScale, (c, v) => c with { Damage = c.Damage with { CollisionScale = checked((float)v) } }),
        new("damage.maximum_collision_damage", "Damage", "MaximumCollisionDamage", false, c => c.Damage.MaximumCollisionDamage, (c, v) => c with { Damage = c.Damage with { MaximumCollisionDamage = checked((float)v) } }),
        new("damage.collision_cooldown_ticks", "Damage", "CollisionCooldownTicks", true, c => c.Damage.CollisionCooldownTicks, (c, v) => c with { Damage = c.Damage with { CollisionCooldownTicks = checked((ulong)v) } }),
        new("items.mine_damage", "Proxy Mine", "MineDamage", false, c => c.Items.MineDamage, (c, v) => c with { Items = c.Items with { MineDamage = checked((float)v) } }),
        new("items.mine_attraction_radius", "Proxy Mine", "MineAttractionRadius", false, c => c.Items.MineAttractionRadius, (c, v) => c with { Items = c.Items with { MineAttractionRadius = checked((float)v) } }),
        new("items.mine_minimum_force", "Proxy Mine", "MineMinimumForce", false, c => c.Items.MineMinimumForce, (c, v) => c with { Items = c.Items with { MineMinimumForce = checked((float)v) } }),
        new("items.mine_maximum_force", "Proxy Mine", "MineMaximumForce", false, c => c.Items.MineMaximumForce, (c, v) => c with { Items = c.Items with { MineMaximumForce = checked((float)v) } }),
        new("items.mine_falloff", "Proxy Mine", "MineFalloff", false, c => c.Items.MineFalloff, (c, v) => c with { Items = c.Items with { MineFalloff = checked((float)v) } }),
        new("items.mine_knockback", "Proxy Mine", "MineKnockback", false, c => c.Items.MineKnockback, (c, v) => c with { Items = c.Items with { MineKnockback = checked((float)v) } }),
        new("items.nitro_duration_ticks", "Items", "NitroDurationTicks", true, c => c.Items.NitroDurationTicks, (c, v) => c with { Items = c.Items with { NitroDurationTicks = checked((int)v) } }),
        new("items.nitro_acceleration_multiplier", "Items", "NitroAccelerationMultiplier", false, c => c.Items.NitroAccelerationMultiplier, (c, v) => c with { Items = c.Items with { NitroAccelerationMultiplier = checked((float)v) } }),
        new("items.nitro_speed_multiplier", "Items", "NitroSpeedMultiplier", false, c => c.Items.NitroSpeedMultiplier, (c, v) => c with { Items = c.Items with { NitroSpeedMultiplier = checked((float)v) } }),
        new("match.nitro_points_per_second", "Match", "NitroPointsPerSecond", false, c => c.Match.NitroPointsPerSecond, (c, v) => c with { Match = c.Match with { NitroPointsPerSecond = v } }, doublePrecision: true),
        new("items.maximum_oil_patches", "Items", "MaximumOilPatches", true, c => c.Items.MaximumOilPatches, (c, v) => c with { Items = c.Items with { MaximumOilPatches = checked((int)v) } }),
        new("items.wrench_heal", "Items", "WrenchHeal", false, c => c.Items.WrenchHeal, (c, v) => c with { Items = c.Items with { WrenchHeal = checked((float)v) } }),
        new("items.missile_speed", "Items", "MissileSpeed", false, c => c.Items.MissileSpeed, (c, v) => c with { Items = c.Items with { MissileSpeed = checked((float)v) } }),
        new("items.explosion_radius", "Items", "ExplosionRadius", false, c => c.Items.ExplosionRadius, (c, v) => c with { Items = c.Items with { ExplosionRadius = checked((float)v) } }),
        new("items.maximum_damage", "Items", "MaximumDamage", false, c => c.Items.MaximumDamage, (c, v) => c with { Items = c.Items with { MaximumDamage = checked((float)v) } }),
        new("items.maximum_impulse", "Items", "MaximumImpulse", false, c => c.Items.MaximumImpulse, (c, v) => c with { Items = c.Items with { MaximumImpulse = checked((float)v) } }),
        new("items.missile_lifetime_ticks", "Items", "MissileLifetimeTicks", true, c => c.Items.MissileLifetimeTicks, (c, v) => c with { Items = c.Items with { MissileLifetimeTicks = checked((int)v) } }),
        new("spawns.cooldown_ticks", "Item spawns", "CooldownTicks", true, c => c.Spawns.CooldownTicks, (c, v) => c with { Spawns = c.Spawns with { CooldownTicks = checked((int)v) } }),
        new("spawns.seed", "Item spawns", "Seed", true, c => c.Spawns.Seed, (c, v) => c with { Spawns = c.Spawns with { Seed = checked((int)v) } }),
        new("spawns.pickup_radius", "Item spawns", "PickupRadius", false, c => c.Spawns.PickupRadius, (c, v) => c with { Spawns = c.Spawns with { PickupRadius = checked((float)v) } }),
        new("respawn.delay_ticks", "Respawn", "DelayTicks", true, c => c.Respawn.DelayTicks, (c, v) => c with { Respawn = c.Respawn with { DelayTicks = checked((ulong)v) } }),
        new("respawn.clear_held_item_on_death", "Respawn", "ClearHeldItemOnDeath", true, c => c.Respawn.ClearHeldItemOnDeath ? 1 : 0, (c, v) => c with { Respawn = c.Respawn with { ClearHeldItemOnDeath = v == 1 } }),
        new("match.mode", "Match", "Mode (0 = First to Target, 1 = Circus)", true, c => (byte)c.Match.Mode, (c, v) => c with { Match = c.Match with { Mode = (Matches.MatchMode)checked((byte)v) } }),
        new("match.kill_target", "Match", "KillTarget", true, c => c.Match.KillTarget, (c, v) => c with { Match = c.Match with { KillTarget = checked((int)v) } }),
        new("match.minimum_players", "Match", "MinimumPlayers", true, c => c.Match.MinimumPlayers, (c, v) => c with { Match = c.Match with { MinimumPlayers = checked((int)v) } }),
        new("match.countdown_ticks", "Match", "CountdownTicks", true, c => c.Match.CountdownTicks, (c, v) => c with { Match = c.Match with { CountdownTicks = checked((ulong)v) } }),
        new("match.base_kill_points", "Match", "BaseKillPoints", false, c => c.Match.BaseKillPoints, (c, v) => c with { Match = c.Match with { BaseKillPoints = v } }, doublePrecision: true),
        new("match.kill_streak_bonus_step", "Match", "KillStreakBonusStep", false, c => c.Match.KillStreakBonusStep, (c, v) => c with { Match = c.Match with { KillStreakBonusStep = v } }, doublePrecision: true),
        new("match.collision_points_per_damage", "Match", "CollisionPointsPerDamage", false, c => c.Match.CollisionPointsPerDamage, (c, v) => c with { Match = c.Match with { CollisionPointsPerDamage = v } }, doublePrecision: true),
        new("match.drift_minimum_speed", "Circus stunts", "DriftMinimumSpeed", false, c => c.Match.DriftMinimumSpeed, (c, v) => c with { Match = c.Match with { DriftMinimumSpeed = v } }, doublePrecision: true),
        new("match.drift_minimum_seconds", "Circus stunts", "DriftMinimumSeconds", false, c => c.Match.DriftMinimumSeconds, (c, v) => c with { Match = c.Match with { DriftMinimumSeconds = v } }, doublePrecision: true),
        new("match.drift_rate", "Circus stunts", "DriftRate", false, c => c.Match.DriftRate, (c, v) => c with { Match = c.Match with { DriftRate = v } }, doublePrecision: true),
        new("match.drift_tier_step", "Circus stunts", "DriftTierStep", false, c => c.Match.DriftTierStep, (c, v) => c with { Match = c.Match with { DriftTierStep = v } }, doublePrecision: true),
        new("match.drift_tier_seconds", "Circus stunts", "DriftTierSeconds", false, c => c.Match.DriftTierSeconds, (c, v) => c with { Match = c.Match with { DriftTierSeconds = v } }, doublePrecision: true),
        new("match.airtime_minimum_seconds", "Circus stunts", "AirtimeMinimumSeconds", false, c => c.Match.AirtimeMinimumSeconds, (c, v) => c with { Match = c.Match with { AirtimeMinimumSeconds = v } }, doublePrecision: true),
        new("match.airtime_rate", "Circus stunts", "AirtimeRate", false, c => c.Match.AirtimeRate, (c, v) => c with { Match = c.Match with { AirtimeRate = v } }, doublePrecision: true),
        new("match.airtime_tier_step", "Circus stunts", "AirtimeTierStep", false, c => c.Match.AirtimeTierStep, (c, v) => c with { Match = c.Match with { AirtimeTierStep = v } }, doublePrecision: true),
        new("match.airtime_tier_seconds", "Circus stunts", "AirtimeTierSeconds", false, c => c.Match.AirtimeTierSeconds, (c, v) => c with { Match = c.Match with { AirtimeTierSeconds = v } }, doublePrecision: true),
        new("match.jump_points_per_metre", "Circus stunts", "JumpPointsPerMetre", false, c => c.Match.JumpPointsPerMetre, (c, v) => c with { Match = c.Match with { JumpPointsPerMetre = v } }, doublePrecision: true),
        new("match.top_speed_enter_ratio", "Circus stunts", "TopSpeedEnterRatio", false, c => c.Match.TopSpeedEnterRatio, (c, v) => c with { Match = c.Match with { TopSpeedEnterRatio = v } }, doublePrecision: true),
        new("match.top_speed_exit_ratio", "Circus stunts", "TopSpeedExitRatio", false, c => c.Match.TopSpeedExitRatio, (c, v) => c with { Match = c.Match with { TopSpeedExitRatio = v } }, doublePrecision: true),
        new("match.stunt_maximum_tier", "Circus stunts", "StuntMaximumTier", true, c => c.Match.StuntMaximumTier, (c, v) => c with { Match = c.Match with { StuntMaximumTier = checked((int)v) } }),
        new("vehicle.concrete.grip", "Concrete", "Grip multiplier", false, c => c.Vehicle.Concrete.Grip, (c, v) => c with { Vehicle = c.Vehicle with { Concrete = new(checked((float)v), c.Vehicle.Concrete.Drag, c.Vehicle.Concrete.Acceleration) } }),
        new("vehicle.concrete.drag", "Concrete", "Drag multiplier", false, c => c.Vehicle.Concrete.Drag, (c, v) => c with { Vehicle = c.Vehicle with { Concrete = new(c.Vehicle.Concrete.Grip, checked((float)v), c.Vehicle.Concrete.Acceleration) } }),
        new("vehicle.concrete.acceleration", "Concrete", "Acceleration multiplier", false, c => c.Vehicle.Concrete.Acceleration, (c, v) => c with { Vehicle = c.Vehicle with { Concrete = new(c.Vehicle.Concrete.Grip, c.Vehicle.Concrete.Drag, checked((float)v)) } }),
        new("vehicle.mud.grip", "Mud", "Grip multiplier", false, c => c.Vehicle.Mud.Grip, (c, v) => c with { Vehicle = c.Vehicle with { Mud = new(checked((float)v), c.Vehicle.Mud.Drag, c.Vehicle.Mud.Acceleration) } }),
        new("vehicle.mud.drag", "Mud", "Drag multiplier", false, c => c.Vehicle.Mud.Drag, (c, v) => c with { Vehicle = c.Vehicle with { Mud = new(c.Vehicle.Mud.Grip, checked((float)v), c.Vehicle.Mud.Acceleration) } }),
        new("vehicle.mud.acceleration", "Mud", "Acceleration multiplier", false, c => c.Vehicle.Mud.Acceleration, (c, v) => c with { Vehicle = c.Vehicle with { Mud = new(c.Vehicle.Mud.Grip, c.Vehicle.Mud.Drag, checked((float)v)) } }),
        .. Items.ItemRegistry.Categories.Select(category => new GameplayOption($"spawns.category_{category.Key}_weight", "Item categories", $"{category.Identity}Weight", true,
            c => c.Spawns.CategoryWeights[category.Identity], (c, v) => c with { Spawns = c.Spawns with { CategoryWeights = c.Spawns.CategoryWeights.SetItem(category.Identity, checked((int)v)) } })),
        .. Items.ItemRegistry.All.Select(item => new GameplayOption($"spawns.{item.Key}_weight", "Item spawns", $"{item.DisplayName}Weight", true,
            c => c.Spawns.Weights[item.Identity], (c, v) => c with { Spawns = c.Spawns with { Weights = c.Spawns.Weights.SetItem(item.Identity, checked((int)v)) } })),
        new("vehicle.dirt.grip", "Dirt", "Grip multiplier", false, c => c.Vehicle.Dirt.Grip, (c, v) => c with { Vehicle = c.Vehicle with { Dirt = new(checked((float)v), c.Vehicle.Dirt.Drag, c.Vehicle.Dirt.Acceleration) } }),
        new("vehicle.dirt.drag", "Dirt", "Drag multiplier", false, c => c.Vehicle.Dirt.Drag, (c, v) => c with { Vehicle = c.Vehicle with { Dirt = new(c.Vehicle.Dirt.Grip, checked((float)v), c.Vehicle.Dirt.Acceleration) } }),
        new("vehicle.dirt.acceleration", "Dirt", "Acceleration multiplier", false, c => c.Vehicle.Dirt.Acceleration, (c, v) => c with { Vehicle = c.Vehicle with { Dirt = new(c.Vehicle.Dirt.Grip, c.Vehicle.Dirt.Drag, checked((float)v)) } }),
        new("vehicle.grass.grip", "Grass", "Grip multiplier", false, c => c.Vehicle.Grass.Grip, (c, v) => c with { Vehicle = c.Vehicle with { Grass = new(checked((float)v), c.Vehicle.Grass.Drag, c.Vehicle.Grass.Acceleration) } }),
        new("vehicle.grass.drag", "Grass", "Drag multiplier", false, c => c.Vehicle.Grass.Drag, (c, v) => c with { Vehicle = c.Vehicle with { Grass = new(c.Vehicle.Grass.Grip, checked((float)v), c.Vehicle.Grass.Acceleration) } }),
        new("vehicle.grass.acceleration", "Grass", "Acceleration multiplier", false, c => c.Vehicle.Grass.Acceleration, (c, v) => c with { Vehicle = c.Vehicle with { Grass = new(c.Vehicle.Grass.Grip, c.Vehicle.Grass.Drag, checked((float)v)) } }),
        new("vehicle.deep_mud.grip", "Deep Mud", "Grip multiplier", false, c => c.Vehicle.DeepMud.Grip, (c, v) => c with { Vehicle = c.Vehicle with { DeepMud = new(checked((float)v), c.Vehicle.DeepMud.Drag, c.Vehicle.DeepMud.Acceleration) } }),
        new("vehicle.deep_mud.drag", "Deep Mud", "Drag multiplier", false, c => c.Vehicle.DeepMud.Drag, (c, v) => c with { Vehicle = c.Vehicle with { DeepMud = new(c.Vehicle.DeepMud.Grip, checked((float)v), c.Vehicle.DeepMud.Acceleration) } }),
        new("vehicle.deep_mud.acceleration", "Deep Mud", "Acceleration multiplier", false, c => c.Vehicle.DeepMud.Acceleration, (c, v) => c with { Vehicle = c.Vehicle with { DeepMud = new(c.Vehicle.DeepMud.Grip, c.Vehicle.DeepMud.Drag, checked((float)v)) } }),
        new("vehicle.water.grip", "Water", "Grip multiplier", false, c => c.Vehicle.Water.Grip, (c, v) => c with { Vehicle = c.Vehicle with { Water = new(checked((float)v), c.Vehicle.Water.Drag, c.Vehicle.Water.Acceleration) } }),
        new("vehicle.water.drag", "Water", "Drag multiplier", false, c => c.Vehicle.Water.Drag, (c, v) => c with { Vehicle = c.Vehicle with { Water = new(c.Vehicle.Water.Grip, checked((float)v), c.Vehicle.Water.Acceleration) } }),
        new("vehicle.water.acceleration", "Water", "Acceleration multiplier", false, c => c.Vehicle.Water.Acceleration, (c, v) => c with { Vehicle = c.Vehicle with { Water = new(c.Vehicle.Water.Grip, c.Vehicle.Water.Drag, checked((float)v)) } }),
        new("vehicle.water.depth", "Water", "Deep-water threshold (m)", false, c => c.Vehicle.DeepWaterDepth, (c, v) => c with { Vehicle = c.Vehicle with { DeepWaterDepth = checked((float)v) } }),
        new("vehicle.water.damage", "Water", "Deep-water damage (HP/s)", false, c => c.Vehicle.WaterDamagePerSecond, (c, v) => c with { Vehicle = c.Vehicle with { WaterDamagePerSecond = checked((float)v) } }),
    ]);

    /// <summary>Applies a complete edit transaction through the existing owning validation rules.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="current">Previously accepted configuration.</param>
    /// <param name="edits">Stable gameplay keys and requested values.</param>
    /// <param name="result">Accepted candidate, or the unchanged current configuration.</param>
    /// <param name="error">Safe validation feedback.</param>
    public static bool TryApply(GameplayConfiguration current, IReadOnlyDictionary<string, double> edits, out GameplayConfiguration result, out string error)
    {
        result = current;
        error = string.Empty;
        try
        {
            var candidate = current;
            foreach (var pair in edits)
            {
                var option = All.SingleOrDefault(option => option.Key == pair.Key);
                if (option is null)
                {
                    error = "Unknown gameplay setting.";
                    return false;
                }

                candidate = option.Set(candidate, pair.Value);
            }

            candidate.Validate();
            result = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            error = exception.Message;
            return false;
        }
    }
}
