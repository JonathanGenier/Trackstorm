# Host Developer Options

Settings > Developer Options and F1 use the same `DeveloperOptionsPanel`. F1 toggles that page; normal Back navigation remains available. Numeric fields accept invariant decimal input. **Apply Settings** is the single tuning commit action: it validates the requested configuration, applies accepted values live and automatically saves the accepted host-local tuning. Submitting text with Enter does not commit it. **Discard Changes** restores the editor from the currently active authoritative configuration. **Reset to Defaults** stages the complete production hosted-game preset in the editor. Neither Discard nor Reset changes gameplay, configuration revision or persistence. Reset remains pending until Apply Settings, which uses the normal authority/synchronization path and replaces persisted tuning with the defaults.

`DeveloperOptionsDraft` owns only editor text and pending changes; it has no gameplay or storage authority. Reset requests the complete preset even if authority has changed since the editor was opened. Opening the page suppresses local driving input while simulation and networking continue. Host controls disappear when authority is unavailable; clients retain read-only diagnostics. The build flag `TrackstormDeveloperTools` defaults to true in Debug and false in Release. A Release build can explicitly opt in for development.

## Authority and runtime application

Core `GameplayConfiguration` composes the existing vehicle, damage, item, spawn, respawn and match records. `GameplayOptions` is the explicit 59-key allowlist shared by the UI, persistence and wire codec. Each setter calls the existing owning validation through one complete candidate transaction. Local audio, graphics, bindings and display preferences never enter it. Core remains independent of Godot and storage.

Live scalar tuning additionally rejects positive values below 0.0001: subnormal mass/axle lengths can overflow fixed-step divisions despite passing older positive-only checks. Zero remains allowed where the owning rule explicitly supports it. Collision/respawn timers are bounded to one hour and the simulation clock remains fixed at 60 Hz.

`DevelopmentSession` checks current host authority and forwards an arena edit to `VehicleNetworkDriver.TryConfigure` and `HostVehicleSession.TryConfigure`. No client tuning-request message exists. Core rejects unauthorized, unknown, nonfinite, fractional integer, inconsistent or unsafe values before committing. A successful changed transaction advances a checked monotonic revision once; a no-op does not publish. The UI displays rejected edits without claiming success.

`Simulation.ApplyConfiguration` prepares replacement vehicle owners while preserving poses, lives, damage attribution, cooldown memory and match state. Max HP changes preserve the current health fraction without reviving dead vehicles. Steering memory clamps to a reduced steering limit. Existing item and spawn authorities adopt their new records. Current projectiles adopt changed speed, preserving direction and remaining lifetime; explosion settings apply at impact. Existing respawn and pickup deadlines retain their absolute tick, and new delays apply to future events. Seed changes restart the item selection stream. Countdown duration changes restart a running countdown from the current tick. Minimum-player changes apply at the next Waiting/Countdown step; Active matches continue. A kill target at/below an existing score, or a changed target after Finished, is rejected atomically.

Native `NetworkVehicleBody` observation receives the same configuration as Core and prediction: wheel rays use live suspension length/wheelbase, and impulse conversion uses live mass/inertia. `PredictedVehicle` uses accepted host tuning for subsequent steps and correction replay instead of constructing defaults.

## Replication and recovery

The reliable version-one `TC` message carries arena generation, configuration revision and all 59 values (493 bytes). Catalog order is part of this schema; additions/reordering require a version change. A client accepts configuration only from its established host over reliable delivery for the current arena. Lower revisions and conflicting duplicate revisions are rejected; identical duplicates are idempotent. The first complete revision-zero configuration may differ from canonical defaults because a host can start with persisted overrides.

The host sends configuration before its reliable world boundary on admission or edits. Version-six `TS` world snapshots include the configuration revision; clients reject world/item boundaries for a different revision, preventing mismatched simulation. Subsequent unreliable snapshots recover normal movement after an ordered tuning change. EOS lobby metadata does not store gameplay tuning. Discovery compatibility is `trackstorm-lobby-6`.

Version-two `TR` resume checkpoints include and cross-validate the entire configuration and revision against vehicles and match target. Resume installs tuning before reconstructing prediction, resets history, and updates native bodies. Joining clients never inject their own host-local file into this path. See [reconnection](reconnection.md).

Host migration and AuthorityEpoch are not implemented in the current session architecture. Diagnostics state that explicitly. The resume checkpoint's tuning is a restoration boundary, but does not itself implement replacement authority, epoch fencing or transfer of item RNG state. Those remain prerequisites for migration support; no migration success is simulated.

## Host-local persistence

`DeveloperSettingsStore` uses `user://developer-settings.jsonl`, separately from player preferences. Each accepted host transaction saves to a sibling temporary file and atomically replaces the destination. Save failure preserves accepted runtime tuning and the previous file, reports the failure, and directs the user to press Apply Settings again. Applying unchanged values retries persistence without advancing the authoritative revision; there is no separate normal Save button. Invalid input remains in the editor with validation feedback and does not save. Values become the starting configuration of later hosted arenas, including after process restart. Joined clients do not apply these overrides authoritatively.

Schema one begins with `{"schema":1}` followed by independent `{"key":"vehicle.mass","value":900}` records. Missing keys retain canonical defaults; headerless/schema-zero files are supported. The targeted alias `vehicle.top_speed` migrates to `vehicle.forward_speed`. Unknown keys retain their raw JSON values when saved, without becoming editable or affecting gameplay. Malformed records are skipped independently. Related valid bounds apply together first; a damaged transaction salvages independently valid values. Files are bounded to 64 KiB/depth eight. Unsupported future schemas and oversized files use defaults and disable rewriting. Actions, diagnostics and network impairment have no persistent keys. Accepted balance changes still require explicit promotion into repository defaults.

`GameplayConfiguration.HostedDefaults` is the canonical production 0.0.1 hosted-game preset, including **1000 Max HP**. `NetworkVehicleArena`, the session fallback, `DeveloperSettingsStore` (including missing persisted keys), and Reset all use that definition. It composes the owning configuration defaults with the production multiplayer HP override; plain Core fixtures retain their 100 HP default. Developer overrides do not silently change either canonical default.

## Actions and diagnostics

The [Statistic Panel](statistics.md) provides the full-screen F2 read-only view with
per-player selection. Developer Options retains the existing mutation controls
and diagnostic summary; no actions are copied into the Statistic Panel. F2 is
available independently of the Developer Options build flag.

**FORCE START MATCH** has a larger accented button. In a lobby it readies the host and uses the ordinary Start request; other players must still be connected and ready. In a Waiting arena it arms a one-shot minimum-player override inside the existing match authority, which then runs the normal Countdown → Active transition. It never changes persisted MinimumPlayers or individually enables scoring, items, missiles or music. **Give Wrench** and **Give Missile** use the existing host inventory grant path and require a living host with an empty slot.

The separate Arena Tools UI is removed. Local practice Reset and Detonate remain available on this same page. Match phase and combat HUD remain ordinary gameplay presentation. Read-only diagnostics show transport/capabilities, safe EOS lifecycle and a one-way PUID fingerprint, lobby/session/host identity, connection/failure state, RTT/quality, configuration revision, prediction error, snapshot age, interpolation delay, acknowledgements, reconnect/grace and resume checkpoint state. Raw provider errors, access codes, tokens, credentials and lobby metadata are never passed to this formatter.

Direct-IP GameNetworkingSockets exposes latency, jitter, loss, reorder percentage and reorder delay as explicit local Apply controls. They affect the provider's process-wide sockets, are not saved, and require host authority. `NetworkSimulationControl` checks the provider capability before invoking it. EOS P2P advertises no simulation capability, so those editors are hidden and availability is described accurately.

| Local transport control | Existing GNS runtime setting in `GameNetworkingSocketsTransport.ConfigureSimulation` | Verification |
| --- | --- | --- |
| Latency (ms) | `FakePacketLag_Send` | Native impaired UDP runs and guarded provider invocation |
| Jitter (ms) | `FakePacketJitter_Send_Avg`, twice-value maximum, nonzero percentage | Native impaired UDP runs and guarded provider invocation |
| Loss (%) | `FakePacketLoss_Send` | Native impaired UDP runs and guarded provider invocation |
| Reorder (%) | `FakePacketReorder_Send` | Direct native setter; UI/provider capability tests |
| Reorder delay (ms) | `FakePacketReorder_Time` | Direct native setter; UI/provider capability tests |

These controls use `NetworkSimulation`'s existing bounds. Native rejection is reported as failure. Local practice actions call the existing `VehicleArena.ResetVehicles` and `VehicleArena.Explode`; host item actions call `HostVehicleSession.GiveItem` → `ItemAuthority.Grant`, and Force Start calls `Simulation.ForceStart` → `MatchAuthority.Advance`. No action creates a second gameplay owner.

Future game-level tunable additions must extend this catalog, runtime application, persistence migration, wire version and effect coverage together. A configuration property alone is insufficient reason to add an editor.

## Editable control-to-runtime mapping

All rows are driven by the actual configuration properties, not duplicated UI state. `DeveloperOptionsIntegrationChecks` edits **every key** through the production page over real UDP and compares the accepted host value and complete client configuration/revision. `VehicleNetworkDriverTests` checks current-state delivery on late join; `ReconnectIntegrationChecks` compares the complete configuration after three authenticated-seam resumes. The effect evidence below is additional to that common UI/authority/network coverage.

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
| `vehicle.steering_speed` | `.SteeringSpeed`; speed-dependent steering authority | Movement |
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
| `vehicle.wheel_spring` | `.WheelSpring`; compression-dependent vertical force | Movement |
| `vehicle.wheel_damping` | `.WheelDamping`; wheel vertical-velocity damping | Movement |
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
| `spawns.wrench_weight` | `.WrenchWeight`; live `ItemSpawnAuthority` selector | Spawns: actual award |
| `spawns.missile_weight` | `.MissileWeight`; live selector | Spawns: actual award |
| `spawns.seed` | `.Seed`; restarts selector RNG | Seed: changed/repeated award sequence |
| `respawn.delay_ticks` | `RespawnConfiguration.DelayTicks`; future authoritative death deadline | Lifecycle: actual respawn tick |
| `respawn.clear_held_item_on_death` | `.ClearHeldItemOnDeath`; `ItemAuthority.Synchronize` ownership policy | Lifecycle: held missile retained |
| `match.kill_target` | `MatchConfiguration.KillTarget`; `MatchAuthority` winning threshold | Match: real kills reach edited target/winner |
| `match.countdown_ticks` | `.CountdownTicks`; authoritative activation deadline | Lifecycle and `LiveMatchRulesChangeNormalCountdownAndActivation`: Countdown/Active ticks |
| `match.minimum_players` | `.MinimumPlayers`; Waiting/Countdown roster guard | Lifecycle and normal two-player Waiting/Countdown transition |
| `vehicle.concrete.grip` | `VehicleConfiguration.Concrete.Grip`; hard-surface tire budget | Trajectory |
| `vehicle.concrete.drag` | `.Concrete.Drag`; hard-surface rolling resistance | Trajectory |
| `vehicle.concrete.acceleration` | `.Concrete.Acceleration`; hard-surface drive force | Trajectory |
| `vehicle.mud.grip` | `.Mud.Grip`; mud tire budget | Trajectory |
| `vehicle.mud.drag` | `.Mud.Drag`; mud rolling resistance | Trajectory |
| `vehicle.mud.acceleration` | `.Mud.Acceleration`; mud drive force | Trajectory |

No editable drift boost, collision recoil or missile falloff setting exists because the current runtime has no corresponding configuration property. Fixed 60 Hz simulation, snapshot cadence, input-history bounds and reconnect grace remain architecture/session-lifetime invariants; changing them live would require coordinated reconstruction. Arena geometry/spawn locations and projectile capacity remain authored constants. Camera/audio/display/bindings are local presentation or preferences, outside synchronized tuning. Additional runtime inspection did not identify another safe game-level configuration owner requiring a live control.

`check-developer-options.ps1 -GodotPath <exe>` runs production UI/UDP coverage and host persistence in two separate processes. It also exercises Discard/Reset isolation, complete production defaults, explicit Apply validation, Reset + Apply synchronization/persistence, and a deterministic save failure followed by an unchanged Apply retry. `-Visual` captures the actions and numeric fields. `check.ps1` covers authority, ordering, persistence, gameplay effects and capability/secret-safe projections in Debug/Release, plus deterministic editor draft/default/file recovery tests. Existing menu, reconnect, item, spawn, lifecycle, match and network harnesses cover integration regressions. These checks do not establish real multi-PC EOS behavior, migration, subjective game balance, or physical-controller ergonomics.

[Feature index](README.md) · [Settings](settings.md) · [Vehicle networking](vehicle-networking.md)

The read-only [Event Log](event-log.md) records accepted tuning keys with old/new values, configuration revisions/rejections, Give Item and Force Start results, and practice reset/blast actions. F3 provides history and no mutation controls.
