# Developer Options Catalog

Read the [shared system contract](developer-options.md) first. This reference contains only the selected subsystem; preserve its shared ownership and integration requirements.

## Editable control-to-runtime mapping

Rows use the existing catalog and stage editor values independently of the effective authoritative configuration. `DeveloperOptionsIntegrationChecks` edits **every key** through the production page over real UDP and compares the accepted host value and complete client configuration/revision. `VehicleNetworkDriverTests` checks current-state delivery on late join; `ReconnectIntegrationChecks` compares the complete configuration after three authenticated-seam resumes. The effect evidence below is additional to that common UI/authority/network coverage.

Evidence names refer to methods in `DeveloperConfigurationTests`: **Movement** = `EachVehicleOptionChangesActualSimulationCommands`; **Trajectory** = `HandlingAndSurfacesChangeTheActualVehicleTrajectory`; **Drive** = `AccelerationAndTopSpeedChangeActualHostMovementAndPrediction`; **Damage** = `DamageThresholdScaleCooldownAndMaxHpAffectActualCollisionHealth`; **Items** = `WrenchMissileSpeedLifetimeRadiusDamageAndImpulseUseLiveOwners`; **Spawns** = `PickupCooldownRadiusWeightsAndSeedReachAuthoritativeClaims`; **Seed** = `LiveSeedChangesRestartActualItemSelection`; **Lifecycle** = `RespawnAndForceStartRetainNormalLifecycleAndMinimumPlayerRules`; **Match** = `LiveKillTargetChangesScoringAndInvalidTargetDoesNotPartiallyCommit`. These compare actual simulation commands, trajectories, HP, item effects, pickup outcomes or phase/deadline state rather than just stored values.

| Stable key | Owning property and runtime effect | Effect evidence |
| --- | --- | --- |
| `vehicle.mass` | `VehicleConfiguration.Mass`; movement force scale and native impulse/mass | Movement |
| `vehicle.acceleration` | `.Acceleration`; forward drive force in `VehicleMovement.Step` | Movement, Drive |
| `vehicle.braking` | `.Braking`; opposing-pedal deceleration | Movement |
| `vehicle.stop_speed` | `.StopSpeed`; opposing motion snaps to rest | Movement |
| `vehicle.reverse_acceleration` | `.ReverseAcceleration`; reverse drive force | Movement |
| `vehicle.forward_speed` | `.ForwardSpeed`; forward propulsion cap | Movement, Drive |
| `vehicle.reverse_speed` | `.ReverseSpeed`; reverse propulsion cap | Movement |
| `vehicle.grip` | `.Grip`; lateral tire response | Movement, Trajectory |
| `vehicle.steering_angle` | `.SteeringAngle`; wheel-angle limit | Movement, Trajectory |
| `vehicle.steering_response` | `.SteeringResponse`; steering-memory response | Movement, Trajectory |
| `vehicle.wheelbase` | `.Wheelbase`; axle forces/inertia and native ray offsets | Movement |
| `vehicle.tire_friction` | `.TireFriction`; combined tire force budget | Movement |
| `vehicle.drive_traction_reserve` | `.DriveTractionReserve`; longitudinal rear-tire allocation | Movement |
| `vehicle.load_height` | `.LoadHeight`; longitudinal axle-load transfer | Movement |
| `vehicle.handbrake_braking` | `.HandbrakeBraking`; rear braking | Movement, Trajectory |
| `vehicle.handbrake_grip` | `.HandbrakeGrip`; rear lateral grip fraction | Movement, Trajectory |
| `vehicle.handbrake_response` | `.HandbrakeResponse`; engagement response | Movement, Trajectory |
| `vehicle.traction_recovery` | `.TractionRecovery`; handbrake release response | Movement |
| `vehicle.coast_drag` | `.CoastDrag`; rolling resistance | Movement |
| `vehicle.reference_mass` | `.ReferenceMass`; engine/brake force scale | Movement |
| `vehicle.suspension_spring` | `.SuspensionSpring`; physical pitch/roll spring | Movement |
| `vehicle.suspension_damping` | `.SuspensionDamping`; physical pitch/roll damping | Movement |
| `vehicle.chassis_compliance` | `.ChassisCompliance`; load-induced chassis target | Movement |
| `vehicle.maximum_chassis_tilt` | `.MaximumChassisTilt`; load-induced tilt bound | Movement |
| `vehicle.stability_damping` | `.StabilityDamping`; yaw damping | Movement |
| `vehicle.suspension_length` | `.SuspensionLength`; `WheelSuspension.Observe` ray extent/compression | Native UI short/long support comparison |
| `vehicle.wheel_spring` | `.WheelSpring`; compression-dependent support-normal force | Movement |
| `vehicle.wheel_damping` | `.WheelDamping`; compression point-velocity damping | Movement |
| `vehicle.wheel_rebound_damping` | `.WheelReboundDamping`; extension point-velocity damping | Direction-specific suspension tests |
| `vehicle.wheel_bump_start` | `.WheelBumpStart`; progressive resistance engagement in metres | End-stroke suspension tests |
| `vehicle.wheel_bump_spring` | `.WheelBumpSpring`; quadratic end-stroke resistance | End-stroke suspension tests |
| `vehicle.gravity` | `.Gravity`; gravity and tire capacity | Movement |
| `vehicle.maximum_physics_speed` | `.MaximumPhysicsSpeed`; total linear-velocity bound | Movement |
| `vehicle.maximum_angular_speed` | `.MaximumAngularSpeed`; angular-velocity bound | Movement |
| `damage.max_hp` | `DamageConfiguration.MaxHP`; `VehicleAuthority.Retune` health fraction/capacity | Damage, native host/client HP |
| `damage.collision_threshold` | `.CollisionThreshold`; `VehicleHealth` damaging-contact threshold | Damage |
| `damage.collision_scale` | `.CollisionScale`; contact-speed damage scale | Damage |
| `damage.maximum_collision_damage` | `.MaximumCollisionDamage`; per-contact cap | Damage |
| `damage.collision_cooldown_ticks` | `.CollisionCooldownTicks`; accepted contact cadence | Damage |
| `items.wrench_heal` | `ItemConfiguration.WrenchHeal`; `ItemAuthority` repair effect | Items: real HP gain |
| `items.missile_speed` | `.MissileSpeed`; new and in-flight velocity | Items, native projectile speed |
| `items.missile_lifetime_ticks` | `.MissileLifetimeTicks`; newly launched projectile lifetime | Items |
| `items.explosion_radius` | `.ExplosionRadius`; `ItemAuthority.Explosion` falloff extent | Items: inside/outside effect |
| `items.maximum_damage` | `.MaximumDamage`; explosion damage | Items: numeric damage effect |
| `items.maximum_impulse` | `.MaximumImpulse`; explosion impulse consumed by native physics | Items: numeric impulse effect |
| `spawns.cooldown_ticks` | `ItemSpawnConfiguration.CooldownTicks`; next successful claim deadline | Spawns: actual reactivation |
| `spawns.pickup_radius` | `.PickupRadius`; native observation and Core distance guard | Spawns: rejected/accepted claims |
| `spawns.wrench_weight` | `.Weights[HeldItem.Wrench]`; live `ItemSpawnAuthority` selector | Spawns: actual award |
| `spawns.missile_weight` | `.Weights[HeldItem.Missile]`; live selector | Spawns: actual award |
| `spawns.oil_weight` | `.Weights[HeldItem.Oil]`; live selector | Spawns: actual award |
| `spawns.nitro_weight` | `.Weights[HeldItem.Nitro]`; live selector | Spawns: actual award |
| `spawns.seed` | `.Seed`; restarts selector RNG | Seed: changed/repeated award sequence |
| `respawn.delay_ticks` | `RespawnConfiguration.DelayTicks`; future authoritative death deadline | Lifecycle: actual respawn tick |
| `respawn.clear_held_item_on_death` | `.ClearHeldItemOnDeath`; `ItemAuthority.Synchronize` ownership policy | Lifecycle: held missile retained |
| `match.mode` | `MatchConfiguration.Mode`; Circus or FirstToTarget scoring | Lobby selection and running-match rejection; complete mode checkpoint restoration |
| `match.kill_target` | `MatchConfiguration.KillTarget`; `MatchAuthority` winning threshold | Match: real kills reach edited target/winner |
| `match.countdown_ticks` | `.CountdownTicks`; authoritative activation deadline | Lifecycle and `LiveMatchRulesChangeNormalCountdownAndActivation`: Countdown/Active ticks |
| `match.minimum_players` | `.MinimumPlayers`; Waiting/Countdown roster guard | Lifecycle and normal two-player Waiting/Countdown transition |
| `match.base_kill_points` | `.BaseKillPoints`; base Circus kill award, default 100 | `CircusCollisionAndLiveTuningContinueAcrossAuthorityRestore`: tuned kill awards before/after checkpoint restore |
| `match.kill_streak_bonus_step` | `.KillStreakBonusStep`; linear bonus for consecutive kills after the first, default 25 | Same test: a second kill applies the configured streak progression and updated K/D |
| `match.collision_points_per_damage` | `.CollisionPointsPerDamage`; actual applied collision HP conversion, default 1 | Same test: nonlethal and clamped lethal damage use the configured rate |
| `vehicle.concrete.grip` | `VehicleConfiguration.Concrete.Grip`; hard-surface tire budget | Trajectory |
| `vehicle.concrete.drag` | `.Concrete.Drag`; hard-surface rolling resistance | Trajectory |
| `vehicle.concrete.acceleration` | `.Concrete.Acceleration`; hard-surface drive force | Trajectory |
| `vehicle.mud.grip` | `.Mud.Grip`; mud tire budget | Trajectory |
| `vehicle.mud.drag` | `.Mud.Drag`; mud rolling resistance | Trajectory |
| `vehicle.mud.acceleration` | `.Mud.Acceleration`; mud drive force | Trajectory |

No editable drift boost, collision recoil or missile falloff setting exists because the current runtime has no corresponding configuration property. Fixed 60 Hz simulation, snapshot cadence, input-history bounds and match-long reservation policy remain architecture/session-lifetime invariants; changing them live would require coordinated reconstruction. Arena geometry/spawn locations and projectile capacity remain authored constants. Camera/audio/display/bindings are local presentation or preferences, outside synchronized tuning. Additional runtime inspection did not identify another safe game-level configuration owner requiring a live control.

`check-developer-options.ps1 -GodotPath <exe>` runs production UI/UDP coverage and session ownership in two separate processes. It exercises client-originated edits, host rejection, three-peer convergence, late join, immediate shared category/global reset, repeated changes, draft rebasing, actual HP/native observer consumption, equal host/client exports, and fresh-session isolation. Headless checks drive the real FileDialog save/cancel signals to verify the export callback and error feedback; native OS dialog interaction remains a separate manual check. `-Visual` captures the actions and numeric fields. `check.ps1` covers authority, ordering, persistence, gameplay effects and capability/secret-safe projections in Debug/Release, plus deterministic editor draft/default/file recovery tests. Existing menu, reconnect, item, spawn, lifecycle, match and network harnesses cover integration regressions. Migration-specific deterministic and native UDP checks cover successive replacements, tuning/revision and RNG continuation, stale-host rejection and former-host return. These checks do not establish real multi-PC EOS behavior or subjective game balance, or physical-controller ergonomics.

[Feature index](README.md) · [Settings](settings.md) · [Vehicle networking](vehicle-networking.md)

The read-only [Event Log](event-log.md) records accepted tuning keys with old/new values, configuration revisions/rejections, Give Item and Force Start results, and practice reset/blast actions. F3 provides history and no mutation controls.

Item spawn weights are generated from the shared item registry (spawns.wrench_weight, spawns.missile_weight, spawns.oil_weight, spawns.nitro_weight, spawns.proxy_mine_weight, spawns.salvo_weight, spawns.machine_gun_weight). Zero excludes an item; the complete pool must retain positive total weight. All seven default to one. Oil uses the normal deployment path; Nitro uses the normal activation path and retains its slot while already boosted. Oil grip, recovery duration, vehicle-pass budget and cleanup lifetime are editable in the Oil category; deployment has no global active-patch gate. The version-forty-two gameplay configuration payload includes the complete distribution in resume and migration checkpoints.

Nitro exposes `items.nitro_consumption_per_second`, `items.nitro_forward_thrust`, `items.nitro_speed_multiplier`, `items.nitro_airborne_thrust_scale`, `vehicle.overspeed_deceleration`, and `match.nitro_points_per_second` through the existing catalog. Live edits affect subsequent authoritative intervals without refilling charge. Ordinary validation, persistence, version-forty-two configuration replication and recovery apply. See [Nitro](items.md#sustained-nitro-resource).

## Progressive handling controls

The Dirt category exposes `vehicle.dirt_steering_reserve` (default 0.95, range 0–1) and `vehicle.dirt_recovery` (default 4/s, range 0–10/s). These control front-wheel authority during saturated slides and bounded slide-yaw recovery respectively; zero disables the corresponding contribution. They use the existing complete shared Apply/Cancel and authoritative Reset transaction, schema-two file defaults for missing keys, configuration version forty-two, and resume/migration configuration boundary. `DirtRecoveryTests` checks isolation, recovery and continuation; `DeveloperConfigurationTests` checks each control's actual trajectory effect. No additional serialized vehicle memory is introduced.

The Vehicle category exposes `vehicle.steering_smoothing` (0.3 seconds; accepts 0.01–1). Automatic power-slip buildup controls are retired; current ground-steering tuning is documented below. [Vehicles](vehicles.md) owns force and recovery semantics.

## Surface tuning

Concrete, Dirt, Grass, Mud and Deep Mud each expose Grip, Drag and Acceleration multipliers under their searchable category. Keys are `vehicle.concrete.*`, `vehicle.dirt.*`, `vehicle.grass.*`, `vehicle.mud.*` and `vehicle.deep_mud.*`, with suffixes `grip`, `drag`, `acceleration`. Each maps directly to the matching `VehicleConfiguration` record used by movement; [vehicles](vehicles.md#terrain-handling-profiles) owns default values and force semantics. Asphalt retains existing vehicle controls rather than a second surface authority. Host overrides may intentionally depart from the default ordering.

Apply, Cancel, Reset, validation, host-local schema-two persistence, version-forty-two complete reliable configuration and existing resume/migration checkpoints carry all 222 values. New keys missing from saved files use canonical defaults. Old explicit Concrete/Mud overrides remain user tuning; Reset plus Apply selects the new defaults. No file migration silently overwrites those choices. The existing UI/UDP/persistence harness iterates all catalog controls; Core trajectory tests exercise every surface multiplier and checkpoint round trips.
The **Water** category adds five [water controls](water.md#gameplay-and-tuning) through the same authority, validation, persistence and recovery path. The complete configuration layout is described above.

Item category target weights use the same Configs catalog and session replication path. See [per-player category credit](item-spawns.md#per-player-category-credit) for normalization, empty-category behavior and live tuning continuity.

## Proxy Mine tuning

The six `items.mine_*` controls and registry-generated `spawns.proxy_mine_weight` use the same Configs draft/apply, host validation, shared session configuration, replication and recovery owners. The catalog contains 222 keys and uses gameplay configuration wire version forty-two. [Magnetic Proxy Mine](items.md#magnetic-proxy-mine) defines defaults, units, force curve and live-effect semantics. Invalid minimum/maximum force pairs reject the complete transaction. No presentation-only damage or force configuration exists.

The [environment preset](terrain-effects.md) uses "environment.preset" in the same session configuration, revision and recovery boundary. Its identity is session-owned; Godot lighting values remain Client presentation.

Open **F1 → Configs → Salvo** for `items.salvo_*` controls through the same catalog and session configuration boundary. **Shot cooldown (ticks; 30 = 0.5 seconds)** defaults to 30; 60 means a one-second minimum between separately pressed shots. **Shots per pickup** sets ammunition for new grants; it does not refill held items. Range, launch height, arc height, flight speed, count, radius, damage, falloff, impulse and marker scale/width/lift are also editable. Apply Settings submits shared tuning through the host; existing saved overrides remain in effect until edited or reset. See [arcing Salvo](items.md#arcing-salvo) for defaults, bounds and live-edit semantics. Marker parameters are shared tuning; marker nodes remain local presentation and never replicated entities.

## Live distribution tuning

F1 → Configs exposes the registry-generated item weights under **Item spawns** and category targets under **Item categories**. Search for `weight` to show both. Weights are nonnegative integers, not percentages: within a selected category, an item weighted 2 has twice the probability of one weighted 1. Category defaults remain Weapon 2, Consumable 1 and Droppable 1; every registered item defaults to 1. An all-disabled effective pool is rejected atomically.

Use **Apply Settings** to submit edited fields through the host. Subsequent rolls for all players use it immediately. Existing inventory, world items, committed pickup awards, cooldown deadlines, category history and RNG position remain unchanged by weight edits. Category history stays per player; the weights and RNG stream stay match-owned. There is no client-local distribution setting. Existing persisted host overrides remain effective until changed or reset.

The item registry supplies both selection candidates and configuration controls. Adding an item to its category automatically participates in the shared weight mechanism; the ordinary item/protocol integration still belongs to that item's implementation. Recovery carries the complete accepted configuration and migration restores the exact RNG continuation. Native pickup verification changes the pool midway through eight-peer pickup rounds, while native reconnect/migration fixtures retain unequal per-item weights.

## Machine gun controls

The existing Configs catalog exposes `items.machine_gun_capacity` (800, 1–10000), `fire_rate` (80 rounds/s, 1–120), `range` (225 m, 1–300), `damage` (2.25 HP, 0–1000), `falloff_start` (12 m, nonnegative and below range), `falloff` (1.5, 0.1–8), `spread` (6 degrees half-angle, 0–30), `knockback` (8 N s, 0–1000), and `tracer_every` (2 rounds, 1–25); all suffixes share the `items.machine_gun_` prefix. The generated `spawns.machine_gun_weight` defaults to one. Capacity is captured on pickup; live edits never refill held magazines. The existing transaction, session state, configuration replication and checkpoints carry these values. See [machine gun](items.md#sustained-machine-gun-resource).

## Air-control tuning

The Air control category uses the same staged Apply/Cancel and immediate Reset, validation, session ownership and reliable configuration boundary as other vehicle controls. Values below are production defaults, including fresh profiles and Reset to Defaults. Valid existing saved overrides remain authoritative until reset.

| Key (`vehicle.` prefix) | Default | Unit / purpose |
| --- | --- | --- |
| `air_pitch_rate` / `air_yaw_rate` / `air_roll_rate` | 2.52 / 2.16 / 3.24 | Full-input rad/s, also sets axis sensitivity |
| `air_pitch_acceleration` / `air_yaw_acceleration` / `air_roll_acceleration` | 16 / 14 / 20 | Maximum angular acceleration at full input in rad/s² |
| `air_input_response` | 0.06 | Command smoothing time constant, seconds |
| `support_normal_minimum` | 0.55 | Ground/wheel support minimum world-normal Y; range 0.55–1 |

`VehicleMovement` consumes the first seven values through the existing authority and prediction paths; the last value also reaches both native adapters and wheel ray observations. No orientation target, leveling strength or redundant torque multiplier exists. `AirControlTests`, native `check-air-control.ps1`, and release-default coverage verify this mapping and continuation. Configuration version 42 carries the complete 222-key catalog; old layouts are rejected.
## Collision and destruction tuning

Vehicle defaults target a 3000 kg arcade trophy truck: acceleration 24 and braking
95 m/s² at the retained 900 kg reference mass, with progressive steering and rear-lock
handbrake tuning. `vehicle.front_drive_share` defaults to 0, range 0–1, and uses
the existing complete validation, Apply/Reset, persistence and recovery boundary.
Configuration wire version 42 carries that key and the dirt-corner/crash-recovery controls (222 values). Existing saved overrides
remain effective until reset. Suspension length, spring, compression/rebound damping
and progressive bump controls continue through the same catalog; no separate tuning
file or new airborne settings exist. `crash_rotation` and `crash_angular_limit` now
also bound network vehicle contact torque; zero angular limit suppresses added torque.

Collision and Destruction use the existing shared Apply/Cancel and authoritative Reset, validation,
session configuration, reliable revision and resume/migration boundary. New keys
missing from saved files use production defaults; explicit overrides remain until
Reset. Configuration wire version 42 carries all 222 keys.

| Key | Default | Range / unit |
| --- | ---: | --- |
| `vehicle.wall_drag` | 0.18 | 0–5 /s |
| `vehicle.crash_dissipation` | 0.95 | 0–1 residual lateral-motion removal |
| `vehicle.crash_rotation` | 0.08 | 0–1 eccentric-impact multiplier |
| `vehicle.crash_angular_limit` | 1.2 | 0–3 rad/s angular change per manifold |
| `environment.health_scale` | 1 | 0.1–10 stage-health multiplier |
| `environment.impact_threshold` | 3 | 0–20 m/s harmless vehicle approach |
| `environment.impact_scale` | 10 | 0–100 vehicle damage coefficient |
| `environment.piece_speed` | 6 | 0–6 m/s broken-piece speed |
| `environment.push_scale` | 0.35 | 0–1 impact-speed transfer |
| `environment.velocity_retention` | 0.9 | 0–0.99 velocity retained per fixed tick |

Collision edits affect subsequent native contact resolution through both production
adapters. Destruction edits reach the host environment authority at its next accepted
boundary. Damage stays in canonical stage-health units: health edits change future
damage without replaying impacts or rescaling existing partial damage. Lowering piece
speed also clamps already-moving pieces on the next step.

The tuning audit retains structural invariants: static response cannot add rebound
or lift; incidence/contact-normal classification and solver penetration tolerances
protect geometry interpretation. Fracture stage ratios, minimum piece size, four slots
per root, sixteen moving pieces, eight-metre displacement, six-metre/second wire ceiling,
damage cap, twelve-tick contact coalescing, deterministic split directions and plant
footprints define layout, bounded replication or contact semantics. These are not live
balance controls; changing them safely requires coordinated layout/codec or collision
validation. The exposed multipliers provide balance adjustment within those bounds.
Fixed timestep, wheel count, unilateral support ceiling and authored collision hull
likewise remain physics invariants. Surface, suspension, handling and air-control values
use the existing vehicle catalog; local tire graphics use the separate table in
[terrain effects](terrain-effects.md#local-configs-tuning).

## Circus match duration

`match.duration_ticks` uses `MatchConfiguration.DurationTicks`: default 36,000 (10 minutes at fixed 60 Hz), range 1–216,000. It uses the ordinary shared Apply/Cancel and authoritative Reset, session configuration, reliable complete configuration publication and resume/migration paths. Missing saved keys use the production default. Mode remains fixed once countdown begins.

During Active, changing duration changes the total budget from the original Active entry tick, retaining elapsed gameplay and recovery time. It never restarts the clock. A shorter already-exhausted budget completes through the next authoritative Game Loop commit before further scoring. Finished retains its frozen clock/results; edits then configure the next match. The existing top-center HUD reads the accepted authoritative duration and time boundary. Clients request changes to the same authoritative duration.

Item damage contributes to Circus through `match.item_points_per_damage` (default 1, range 0–1,000,000), using actual rival HP removed and authoritative attacker K/D. Live edits affect future damage only; zero disables damage contribution without changing kill scoring. The setting uses the existing host-validated transaction, session configuration and complete recovery configuration. Missing persisted keys default to 1.

## Oil tuning

The Oil accordion contains four host-authoritative keys, using the ordinary staged Apply/Cancel, per-category Reset, session configuration and complete configuration/recovery paths:

| Key | Default | Range |
| --- | ---: | --- |
| `vehicle.oil_grip_reduction` | 0.1 | 0–0.8 fraction of lateral force removed |
| `vehicle.oil_recovery_seconds` | 0.5 | 0.1–10 seconds after supported overlap ends |
| `items.oil_enemy_contacts` | 2 | 1–7 vehicle passes, including the owner |
| `items.oil_lifetime_seconds` | 60 | 1–600 seconds |

Grip edits affect subsequent movement; recovery-duration edits preserve the current recovery fraction with fixed-step rounding. New deployments capture pass budget and lifetime; changing settings never refreshes deadlines or refunds consumed passes. Every new supported entry counts, including the owner and the same vehicle after exit. Continuous overlap counts once; owner entry scores zero. The existing `items.oil_enemy_contacts` persistence key is retained to preserve saved tuning, but the UI now labels it "Vehicle passes before removal" and its meaning includes all vehicles. The obsolete `items.maximum_oil_patches` key has no gameplay effect; unknown persisted keys follow the existing preservation policy. The catalog has 222 keys in configuration protocol version 42.
**Arena boundary** exposes `vehicle.oob.damage` (default 100, 0–10000 HP/s) through the same host-only transaction and persistence path. See [out-of-bounds damage](out-of-bounds.md).

## Car deployment

`vehicle.trunk_deployment_speed` and `vehicle.rack_deployment_speed` appear in
**Car deployment**, each defaulting to 3 with a finite 0.1–10 multiplier range.
Trunk baseline travel is 0.72 s and rack baseline travel is 0.88 s; dividing each
by its own speed gives the actual opening/extension duration. Closing/retraction
uses the same respective rates. Trunk opens fully before rack lift, and rack
retracts fully before trunk closing, including reversals and frames spanning the
phase boundary. Live edits preserve pose and change subsequent motion.

The fields belong to the existing immutable vehicle configuration and use the
ordinary staged Apply, authoritative validation, category/global reset, export,
file defaults, replication and recovery paths. `VehicleVisual` supplies each
mechanism with its owning body's accepted configuration; lobby-only previews
use canonical defaults. Item authority and firing timing are unchanged. Native
Developer Options checks measure both host and observer mechanisms after UI
edits; articulation checks separately measure each duration and reversal.

Low/medium Dirt corner strength (`vehicle.dirt_cornering`, 0–2), body-supported crash delay (`vehicle.crash_recovery_delay`, 0.5–10 seconds) and recovery roll rate (`vehicle.crash_recovery_rate`, 0–2 rad/s, zero disables) are ordinary host-validated vehicle options. They use the same draft/apply, persistence, reliable replication and retune path. Missing saved keys acquire defaults. Movement tests cover delayed activation/restoration and native trophy-truck trials cover powered cornering, release and crash recovery on both adapters.

### TS-197 latest handling correction tuning

The complete 222-key catalog uses wire version 42 (1797 bytes). `vehicle.steering_speed` remains a retired legacy key; the current speed envelope uses the explicit full/fade/scale controls below. Old saved unknown keys do not affect handling. Retired `vehicle.air_delay`, `vehicle.air_dead_zone`, `vehicle.air_stabilization` and `vehicle.air_stabilization_response` overrides are ignored. Never-controlled neutral flight preserves inertia. After aerial input, releasing all axes holds the selected orientation until wheel/body contact; this fixed behavior does not use the retired stabilization sliders. Rate limits bound player-added spin without reducing faster existing rotation. Aerial intent is accepted immediately without tire support outside the existing crash latch; player controller deadzone and sensitivity live in Controls.

The following previously fixed values now use the same Configs draft, validation, persistence, host replication and authority/prediction path:

| Vehicle key suffix | Default | Meaning |
| --- | ---: | --- |
| `dirt_corner_full_speed` | 8 | m/s through which corner assistance is full |
| `dirt_corner_fade_speed` | 28 | m/s where extra dirt assistance ends; greater than full speed |
| `dirt_corner_grip` | 0.2 | additional supported tire-budget gain |
| `front_brake_share` | 0.65 | front axle share of service braking |
| `wheel_deep_damping` | 8 | squared deep-travel damping gain |
| `landing_rebound_decay` | 3 | landing envelope decay per second |
| `crash_recovery_ramp` | 0.75 | seconds to build assisted roll rate |
| `crash_slide_damping` | 2 | scraping velocity damping per second |
| `crash_roll_damping` | 0.65 | bad-contact angular damping per second |

`TrophyTuningTests` checks all ten through file/wire round trips and movement trajectories, plus atomic rejection of invalid tuning. Existing steering, grip, braking, suspension, gravity, aerial authority/delay and crash controls remain in the same catalog.

### Rear-drive handling controls

The Vehicle category exposes `vehicle.rear_drive_grip` (1.6, range 0.1–4),
`vehicle.brake_grip` (2, 0.1–4), `vehicle.power_oversteer` (0.8, 0–0.9), and
`vehicle.spin_drive_loss` (0.7, 0–0.9). The first two scale longitudinal tire
capacity; the latter two govern steering/torque-induced rear grip loss through
existing PowerSlip continuation. These use the same 222-value host-owned
configuration, Apply/Reset, persistence, prediction and reliable version-34 codec.
Steering rate is 0.95 rad/s, smoothing is 0.3 s, lateral response is 55.71429/s;
the full maximum wheel angle remains available at all speeds.

The Vehicle category also exposes `vehicle.throttle_rise_time` (0.65 s) and
`vehicle.throttle_fall_time` (0.12 s), each validated from 0.01–3 seconds.
These are engine-demand time constants, independent of maximum engine force,
steering angle, steering transition rate and chassis response. Braking/disabled
drive cut engine demand immediately; aerial controls retain raw pedal input.
`ThrottleResponseTests` covers gradual buildup, release/reapply restoration,
invalid state/configuration and unchanged airborne authority. Explicit saved
steering overrides are retained; Reset + Apply selects the new response defaults.


### Digital controls and asphalt purchase

The existing shared configuration includes `input.steering_rise` (0.45/s),
`input.steering_return` (0.8/s) and `input.steering_reversal` (1/s), each finite
in 0.1–60/s. `GameplayConfiguration.Input` is the existing `DrivingInputShaping`
record, consumed by local capture before integer frames are recorded. The same group
exposes `input.throttle_rise`/`input.throttle_release` (2.5/4 per second) and
`input.brake_rise`/`input.brake_release` (3/10 per second), with the same bounds. Accepted
host tuning applies to each peer's capture, while prediction reuses recorded
frames. Analog input bypasses the digital ramp. Active aerial steering retains
its approved fast digital response independently of these ground controls, including
the original pedal rise/release rates. True analog pedals and steering bypass these
ramps and supersede released keyboard tails. Brake tire purchase uses the existing
`vehicle.brake_grip` control (default 2); peak service-brake demand is 96.42857.

`vehicle.asphalt_grip` defaults to 3 (range 0–100). Existing Dirt and Grass grip
controls default to 2 and 1.9. These scale supported tire capacity; no extra
vertical force, steering-angle restriction or parallel settings store is used.
Saved overrides remain authoritative until Reset/Apply.

Forward power buildup uses existing `vehicle.acceleration` (51.42857 at reference mass), `vehicle.throttle_rise_time` (0.65 seconds) and `vehicle.throttle_fall_time` (0.12 seconds). The forward cap remains 44.44 m/s. Deliberate reverse gating is an input/drive rule rather than a tunable alternative mode; it does not change braking force. No configuration keys or payload fields were added for this refinement.

### Progressive braking and rear breakaway

Service braking uses `vehicle.braking` (96.42857 at reference mass) and digital buildup
`input.brake_rise` (3/s); analog pedals retain direct pressure control. The existing
`vehicle.handbrake_response` now defaults to 1/s (1 s to full application), while
`vehicle.traction_recovery` remains 3/s (about 0.333 s full release). Rear braking,
traction release and engine interruption follow the same progressive state.
`vehicle.handbrake_grip` retains 50% rear lateral grip at full application, giving
more time to meter rotation without changing ordinary tire grip.

Reverse engagement uses the same catalog and configuration v37:

| Key | Default | Bounds / meaning |
| --- | ---: | --- |
| `vehicle.reverse_engagement_speed` | 0.35 m/s | 0–1 m/s; forward creep treated as rest on a new press. |

Automatic powered rear breakaway is retired. Speed-dependent ground steering now uses the separate wheel-range envelope described below; handbrake remains independent.

### Controller steering precision and vehicle mass

`input.controller_steering_exponent` defaults to 3.5, with finite bounds 1–4. After
the analog dead zone, signed steering magnitude is raised to this exponent. One
is linear; larger values give finer small corrections. Center and full stick
remain exactly zero and full intent. This is independent of keyboard ramps,
maximum wheel angle and the existing wheel-rate/filter response. Active aerial
control retains a linear stick mapping. Analog pedals remain direct. The same
host-owned Apply/Reset, persistence and v37 configuration carry this setting.

Mass defaults to 3000 kg. Reference mass stays 900 kg. Acceleration, reverse
acceleration, service braking, handbrake braking and lateral response are scaled
by 3000/1400 from their preceding 24, 8, 45, 30 and 26 values: approximately
51.42857, 17.14286, 96.42857, 64.28571 and 55.71429. This preserves their force
per unit mass and approved pacing while increasing collision momentum/inertia
and resistance to a fixed external impulse. Suspension support and gravity are
accelerations; mass alone does not change their terrain-following trajectories.
Explicit saved overrides remain unchanged until Reset and Apply.

## Ground steering and retired power drift

Configs exposes `vehicle.steering_full_speed` (8 m/s), `vehicle.steering_fade_speed` (35 m/s), `vehicle.high_speed_steering_scale` (0.25; range 0.05–1), and `vehicle.steering_counter_response` (2.8 rad/s; range 0.1–10). Full/fade speeds must be ordered, nonnegative and within the physics speed bound; invalid pairs reject the entire transaction. Existing wheel angle, steer-in response/filter, digital rise/return/reversal rates and controller precision exponent retain the shared catalog, persistence, reset and reliable replication owners. The TC schema is version 42 with 222 keys (1797 bytes).

The automatic power-drift options `power_oversteer`, `power_slip_full_speed`, `power_slip_fade_speed`, `dirt_corner_power_slip`, `dirt_power_slip`, `power_slip_response`, `spin_drive_loss` and `power_slip_recovery` are retired from the live catalog (all formerly under `vehicle.`). Old host-local records remain preserved as unknown keys and cannot re-enable automatic power drift. Legacy configuration properties retain neutral round-trip defaults for source compatibility; only existing snapshot slip decay still reads recovery/drive-loss defaults. Surface-specific grip and handbrake tuning retain their separate controls. Player Controller Deadzone/Steering Sensitivity UI and persistence belong to TS-269, separate from these shared internal controls.

### Surface braking and stationary holding

The Asphalt, Dirt and Grass groups expose `vehicle.<surface>.braking` (0–2), `vehicle.<surface>.brake_lateral_grip` (0–1) and `vehicle.<surface>.handbrake_lateral_grip` (0–2). Their owning immutable `SurfaceBraking` values validate atomically alongside ordinary grip/drag/drive. Zero braking effectiveness removes the braking contribution; zero released lateral purchase remains a valid deliberate override. Handbrake purchase is capped at the ordinary lateral budget when combined with the global handbrake tuning.

Grass also exposes `vehicle.grass_steering_reserve` (default 0.65, range 0–1) and `vehicle.grass_recovery` (3/s, range 0–10). The Vehicle group exposes `vehicle.handbrake_hold_speed` (0.5 m/s, range 0–1). Dirt's extra low-speed corner budget defaults to 0.2 and applies per supported Dirt wheel. See [surface braking](vehicles.md#deliberate-surface-braking-and-stationary-handbrake) for behavior and defaults.

These twelve appended keys use the existing Apply/Cancel/Reset, host validation, saved-key fallback, reliable revision ordering and resume/migration configuration paths. The complete TC42 layout carries 222 values in 1797 bytes; older layouts are rejected. Old saved files missing the new keys inherit defaults, while explicit existing grip overrides remain effective until reset. Player controller settings remain outside this gameplay catalog.

### Shield wall deployment

The Shield category exposes width, height, depth, mass and rear clearance through
`items.shield_*`. [Held items](items.md#movable-shield-world-walls) documents bounds,
defaults and native clearance behavior. Dimensions and mass affect subsequent deployments;
existing walls retain their captured physical values. Configuration protocol 41 includes
these controls in complete replication and recovery.

The Shield category also exposes `items.shield_lifetime` (120 seconds, range 1–600) and `items.shield_tip_speed` (20 m/s of impact impulse per wall mass, range 1–100). Lifetime is captured as an absolute simulation deadline at deployment; edits affect future deployments. The knock-over threshold applies to subsequent vehicle impacts. Both use the existing host validation, persistence, replication and recovery paths.
