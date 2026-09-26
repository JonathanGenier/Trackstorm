# Host Developer Options

The existing Configs shell also contains explicitly local [tire-effect graphics
controls](terrain-effects.md#local-configs-tuning), available without host authority.
They share search, staged Apply/Cancel/Reset and close protection, but persist through
player preferences and never change a synchronized configuration revision. Host-only
gameplay sections and actions retain the authority rules below.

The Stats tab's Network Diagnostics show the canonical [Trackstorm game version](game-versioning.md), including when no multiplayer session is active.

Those online migration diagnostics include trusted fencing status, granted epoch and remaining conservative local permission, separately from the EOS membership proof. They never display the private lease session key, fencing token or identity JWT. See [authority leases](authority-leases.md).

Settings > Developer Options and F1 open the same `DeveloperOptionsPanel` in the shared [DevTools shell](devtools.md) Configs tab. F1 selects Configs without toggling or reconstructing the shell. Numeric fields accept invariant decimal input. Sky / Environment exposes a named five-choice dropdown for the session environment. **Apply Settings** is the single tuning commit action: it validates the requested configuration, applies accepted values live and automatically saves the accepted host-local tuning. Submitting text with Enter does not commit it. **Cancel** restores the editor from the currently active authoritative configuration. **Reset to Defaults** stages the complete production hosted-game preset in the editor. Neither Cancel nor Reset changes gameplay, configuration revision or persistence. Reset remains pending until Apply Settings, which uses the normal authority/synchronization path and replaces persisted tuning with the defaults.

`DeveloperOptionsDraft` owns only editor text and pending changes; it has no gameplay or storage authority. Reset requests the complete preset even if authority has changed since the editor was opened. Opening DevTools suppresses local driving input while simulation and networking continue. Host controls disappear when authority is unavailable. Read-only current state, including Network Diagnostics, belongs exclusively to Stats rather than Configs. The build flag `TrackstormDeveloperTools` defaults to true in current Debug and Release QA builds. Future shipping builds can explicitly disable Configs and its F1 entry with `-p:TrackstormDeveloperTools=false`; the independent read-only Stats and Logs tabs retain their existing availability.

## Configs discovery and staged presentation

The host-only Force Start action remains fixed at the DevTools shell top-right, outside Configs and its scrolling content. A case-insensitive search field remains above the scrolling settings list. Search matches all entered words against labels, category names and stable keys, hiding unmatched rows and empty categories without altering editor or runtime values. Categories use the existing configuration catalog; labels are white. Staged values equal to `GameplayConfiguration.HostedDefaults` are blue and other values (including invalid text) are red. Comparison uses each setting's actual numeric type, so equivalent decimal text is not a change. Accepted persisted overrides remain red even before editing; dirty state instead compares with the effective editor baseline.

The shared shell keeps Reset to Defaults at bottom-left and Apply Settings, Cancel and Close at bottom-right. Reset uses a dark fill with a red border, Apply uses a green fill, Cancel uses a red fill and Close uses a blue fill. These actions, Force Start and the close-decision actions include adjacent project-owned icons with readable hover, pressed and focus states. A compact message directly above Apply/Cancel/Close projects the existing draft and operation result: amber **Unsaved changes** remains while staged gameplay or local-network values differ from their effective baselines, green **Settings applied** appears after the authoritative runtime configuration succeeds even if its subsequent host-local save fails, and red **Changes discarded** follows Cancel. Applied/discarded confirmations clear after three seconds; unsaved feedback does not time out. This is presentation state, not another configuration owner.

Configuration actions and their feedback disappear on Stats and Logs and for non-hosts, while shell-owned Close remains available. Close, Escape and logical Cancel/Pause protect pending edits even after switching tabs, using an in-panel Apply / Discard / Stay decision. Stay (or Escape on the decision) retains drafts and focus. Discard restores effective values and closes. A validation or authoritative rejection returns to Configs with its error and no green confirmation. An authoritative success shows green **Settings applied**; if host-local persistence then fails, the persistence failure remains visible and close remains incomplete so the user can retry. Authority/session changes retain the existing draft-reset policy; staging never becomes a second configuration authority.

## Authority and runtime application

Core `GameplayConfiguration` composes the existing vehicle, damage, item, spawn, respawn and match records plus the shared environment identity. `GameplayOptions` is the explicit 155-key allowlist shared by the UI, persistence and wire codec. Each setter calls the existing owning validation through one complete candidate transaction. Local audio, graphics, bindings and display preferences never enter it. Core remains independent of Godot and storage.

Live scalar tuning additionally rejects positive values below 0.0001: subnormal mass/axle lengths can overflow fixed-step divisions despite passing older positive-only checks. Zero remains allowed where the owning rule explicitly supports it. Collision/respawn timers are bounded to one hour and the simulation clock remains fixed at 60 Hz.

`DevelopmentSession` checks current host authority and forwards an arena edit to `VehicleNetworkDriver.TryConfigure` and `HostVehicleSession.TryConfigure`. No client tuning-request message exists. Core rejects unauthorized, unknown, nonfinite, fractional integer, inconsistent or unsafe values before committing. A successful changed transaction advances a checked monotonic revision once; a no-op does not publish. The UI displays rejected edits without claiming success.

`Simulation.ApplyConfiguration` prepares replacement vehicle owners while preserving poses, lives, damage attribution, cooldown memory and match state. Max HP changes preserve the current health fraction without reviving dead vehicles. Steering memory clamps to a reduced steering limit. Existing item and spawn authorities adopt their new records. Straight projectiles adopt changed speed, preserving direction and remaining lifetime; explosion settings apply at impact. Existing respawn and pickup deadlines retain their absolute tick, and new delays apply to future events. Seed changes restart the item selection stream. Countdown duration changes restart a running countdown from the current tick. Minimum-player changes apply at the next Waiting/Countdown step; Active matches continue. A kill target at/below an existing score, or a changed target after Finished, is rejected atomically. match.mode selects 0 = First to Target or 1 = Circus (default). It is persisted/replicated through the same catalog and may change in the lobby or Waiting only. Countdown, Active and Finished reject mode changes. Scoring-tuning edits after Finished preserve its exact result and completion tick.

Native `NetworkVehicleBody` observation receives the same configuration as Core and prediction: wheel rays use live suspension length/wheelbase, and impulse conversion uses live mass/inertia. `PredictedVehicle` uses accepted host tuning for subsequent steps and correction replay instead of constructing defaults.

## Replication and recovery

The reliable version-nineteen `TC` message carries arena generation, configuration revision and all 155 values (1261 bytes). Catalog order is part of this schema; additions/reordering require a version change. A client accepts configuration only from its established host over reliable delivery for the current arena. Lower revisions and conflicting duplicate revisions are rejected; identical duplicates are idempotent. The first complete revision-zero configuration may differ from canonical defaults because a host can start with persisted overrides.

The host sends configuration before its reliable world boundary on admission or edits. Version-six `TS` world snapshots include the configuration revision; clients reject world/item boundaries for a different revision, preventing mismatched simulation. Subsequent unreliable snapshots recover normal movement after an ordered tuning change. EOS lobby metadata does not store gameplay tuning. Discovery compatibility is `trackstorm-lobby-9`.

Version-two `TR` resume checkpoints include and cross-validate the entire configuration and revision against vehicles and match target. Resume installs tuning before reconstructing prediction, resets history, and updates native bodies. Joining clients never inject their own host-local file into this path. See [reconnection](reconnection.md).

Host migration carries lobby tuning and the complete arena configuration/revision through the existing version-two migration checkpoint (a distinct envelope from the tuning message). The `TG` authority-epoch fence rejects previous-host tuning and gameplay traffic before decoding. The elected host restores the agreed tuning and item RNG position, without loading successor-local overrides. Uncheckpointed tuning can roll back with the selected complete boundary when the epoch advances; ordinary same-epoch revision guards remain unchanged. Controls require current unfrozen authority; Force Start uses the existing match owner and the elected stable host ID. Returning former hosts have client privileges. Epoch changes reset pending editor drafts. See [host migration](host-migration.md).

## Host-local persistence

`DeveloperSettingsStore` uses `user://developer-settings.jsonl`, separately from player preferences. Each accepted host transaction saves to a sibling temporary file and atomically replaces the destination. Save failure preserves accepted runtime tuning and the previous file, reports the failure, and directs the user to press Apply Settings again. Applying unchanged values retries persistence without advancing the authoritative revision; there is no separate normal Save button. Invalid input remains in the editor with validation feedback and does not save. Values become the starting configuration of newly created hosted sessions, including after process restart. Later arenas inherit session tuning. Joined clients and migration successors do not apply their local overrides authoritatively.

Schema two begins with `{"schema":2}` followed by independent `{"key":"vehicle.mass","value":900}` records. Missing keys retain canonical defaults; headerless/schema-zero files are supported. The targeted alias `vehicle.top_speed` migrates to `vehicle.forward_speed`. Unknown keys retain their raw JSON values when saved, without becoming editable or affecting gameplay. Malformed records are skipped independently. Related valid bounds apply together first; a damaged transaction salvages independently valid values. Files are bounded to 64 KiB/depth eight. Unsupported future schemas and oversized files use defaults and disable rewriting. Actions, diagnostics and network impairment have no persistent keys. Accepted balance changes still require explicit promotion into repository defaults.

Host-local schema 2 migrates the old default wheelbase, load height and suspension length to the rescaled vehicle dimensions. It preserves deliberately customized spatial values and every unrelated override; schema-2 values are never re-migrated. Custom spatial overrides can intentionally depart from the canonical asset geometry.

`GameplayConfiguration.HostedDefaults` is the canonical hosted-game preset, including **1000 Max HP** and the approved driving, collision and missile tuning below. `NetworkVehicleArena`, the session fallback, `DeveloperSettingsStore` (including missing persisted keys), and Reset all use that definition. Fresh profiles receive the complete preset without a saved file. Valid saved keys override it; Reset stages it and Reset + Apply persists it through the normal transaction. Its vehicle record uses the same asphalt defaults as `new VehicleConfiguration()` and practice; generic damage fixtures retain 100 HP. Surface categories use the same catalog, with nine additional Dirt, Grass and Deep Mud keys in the version-eight configuration layout. Player-local graphics, audio, bindings and display settings remain separate.

| Persisted gameplay key | Canonical owning property in the hosted preset | Release default |
| --- | --- | --- |
| `vehicle.acceleration` | `VehicleConfiguration.Acceleration` | 16 |
| `vehicle.stop_speed` | `VehicleConfiguration.StopSpeed` | 0.05f |
| `vehicle.reverse_acceleration` | `VehicleConfiguration.ReverseAcceleration` | 8 |
| `vehicle.forward_speed` | `VehicleConfiguration.ForwardSpeed` | 44.44f |
| `vehicle.steering_angle` | `VehicleConfiguration.SteeringAngle` | 0.6f |
| `vehicle.steering_speed` | `VehicleConfiguration.SteeringSpeed` | 12 |
| `vehicle.steering_response` | `VehicleConfiguration.SteeringResponse` | 2.4f |
| `vehicle.tire_friction` | `VehicleConfiguration.TireFriction` | 1.65f |
| `vehicle.coast_drag` | `VehicleConfiguration.CoastDrag` | 0.28f |
| `vehicle.suspension_length` | `VehicleConfiguration.SuspensionLength` | 1.472f (1.155 + 9.81 / 30) |
| `vehicle.wheel_spring` | `VehicleConfiguration.WheelSpring` | 30 |
| `vehicle.wheel_damping` | `VehicleConfiguration.WheelDamping` | 7 |
| `vehicle.wheel_rebound_damping` | `VehicleConfiguration.WheelReboundDamping` | 15 |
| `vehicle.wheel_bump_start` | `VehicleConfiguration.WheelBumpStart` | 0.55 m |
| `vehicle.wheel_bump_spring` | `VehicleConfiguration.WheelBumpSpring` | 140 |
| `vehicle.suspension_damping` | `VehicleConfiguration.SuspensionDamping` | 8 |
| `vehicle.handbrake_response` | `VehicleConfiguration.HandbrakeResponse` | 4 |
| `vehicle.traction_recovery` | `VehicleConfiguration.TractionRecovery` | 3 |
| `vehicle.chassis_compliance` | `VehicleConfiguration.ChassisCompliance` | 0.004f |
| `damage.collision_scale` | `DamageConfiguration.CollisionScale` | 5 |
| `items.missile_speed` | `ItemConfiguration.MissileSpeed` | 70 |
| `items.explosion_radius` | `ItemConfiguration.ExplosionRadius` | 12 |
| `items.maximum_damage` | `ItemConfiguration.MaximumDamage` | 300 |

The remaining persisted values already match this preset, including HP, item spawning, respawn and match rules. The complete stable-key ownership map below applies to the complete catalog. Decimal float literals retain the exact binary32 values represented by persisted JSON doubles; no tolerance or approximate tuning is used. `ReleaseDefaultsTests` checks every approved numeric value exactly and verifies validation, persistence and network round trips.

### Circus stunt tuning

The `Circus stunts` group extends the existing allowlist, host validation, Apply/Cancel/Reset, schema-two host persistence, complete reliable configuration and checkpoint/migration paths. Missing saved keys use defaults. Fractional match/scoring properties retain binary64 precision in the editor; vehicle properties retain their owning binary32 semantics, so default/dirty comparisons do not narrow scoring thresholds accidentally.

| Key suffix under `match.` | Default | Effect |
| --- | --- | --- |
| `drift_minimum_speed` | 5 | Minimum observed horizontal m/s for physical sliding |
| `drift_minimum_seconds` | 0.25 | Minimum uninterrupted duration to bank a supported drift exit |
| `drift_rate`, `drift_tier_step`, `drift_tier_seconds` | 5, 5, 2 | Initial points/s, added points/s per tier and uninterrupted seconds per tier |
| `airtime_minimum_seconds` | 0.25 | Minimum flight duration for Airtime and Long Jump landing awards |
| `airtime_rate`, `airtime_tier_step`, `airtime_tier_seconds` | 5, 5, 2 | Independent airborne rate escalation |
| `stunt_maximum_tier` | 4 | Maximum additional tiers for both sustained categories |
| `jump_points_per_metre` | 2 | Horizontal takeoff-to-landing conversion |
| `top_speed_enter_ratio`, `top_speed_exit_ratio` | 0.95, 0.92 | Fractions of registered forward cap; exit must be below entry |

Points/rates are finite 0–1,000,000; tier count is integer 1–100; qualification/tier seconds are 0.01–60; drift minimum speed is 0.1–65 m/s; speed ratios are 0.5–1 with strict hysteresis ordering. Top Speed's 5 base points/s is a fixed requirement. The [stunt contract](matches.md#circus-stunt-detection-and-banking) defines completion, cancellation and live-edit semantics. `StuntScoringTests`, `ResumeCheckpointTests` and the native harness described there exercise actual scoring effects; the existing Developer Options runtime harness applies every catalog key through the production UI and UDP replication.

## Actions and diagnostics

The [Statistic Panel](statistics.md) provides the full-screen F2 read-only view with
per-player selection and the existing safe Network Diagnostics projection. Developer
Options retains only editable configuration and mutation controls; no actions are
copied into the Statistic Panel. F2 is available independently of the Developer
Options build flag.

**Force Start** is an accented shell-header button. In a lobby it readies the host and uses the ordinary Start request; other players must still be connected and ready. In a Waiting arena it arms a one-shot minimum-player override inside the existing match authority, which then runs the normal Countdown → Active transition. It never changes persisted MinimumPlayers or individually enables scoring, items, missiles or music. Developer-only Give Wrench, Give Missile and equivalent grant buttons are absent from Configs. The underlying inventory authority and ordinary pickups/item use remain unchanged.

The separate Arena Tools UI is removed. Local practice Reset and Detonate remain available on this same page. Match phase and combat HUD remain ordinary gameplay presentation. The former Developer Options Network Diagnostics section now appears only in [Stats](statistics.md); its existing safe formatter and runtime owners remain authoritative. Raw provider errors, access codes, tokens, credentials and lobby metadata are never passed to that formatter.

Direct-IP GameNetworkingSockets exposes latency, jitter, loss, reorder percentage and reorder delay as staged local controls committed by **Apply Settings**. They affect the provider's process-wide sockets, are not saved, and require host authority. Cancel restores the last accepted local values; Reset stages zero impairment. Search and pending-change protection cover these fields too. Gameplay validation runs first; if a provider rejects local simulation after accepting gameplay tuning, the UI reports that boundary explicitly and retains the unapplied local edits. `NetworkSimulationControl` checks the provider capability before invoking it. EOS P2P advertises no simulation capability, so those editors are hidden and availability is described accurately.

| Local transport control | Existing GNS runtime setting in `GameNetworkingSocketsTransport.ConfigureSimulation` | Verification |
| --- | --- | --- |
| Latency (ms) | `FakePacketLag_Send` | Native impaired UDP runs and guarded provider invocation |
| Jitter (ms) | `FakePacketJitter_Send_Avg`, twice-value maximum, nonzero percentage | Native impaired UDP runs and guarded provider invocation |
| Loss (%) | `FakePacketLoss_Send` | Native impaired UDP runs and guarded provider invocation |
| Reorder (%) | `FakePacketReorder_Send` | Direct native setter; UI/provider capability tests |
| Reorder delay (ms) | `FakePacketReorder_Time` | Direct native setter; UI/provider capability tests |

These controls use `NetworkSimulation`'s existing bounds. Native rejection is reported as failure. Local practice actions call the existing `VehicleArena.ResetVehicles` and `VehicleArena.Explode`; Force Start calls `Simulation.ForceStart` → `MatchAuthority.Advance`. No action creates a second gameplay owner.

Future game-level tunable additions must extend this catalog, runtime application, persistence migration, wire version and effect coverage together. A configuration property alone is insufficient reason to add an editor.

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

`check-developer-options.ps1 -GodotPath <exe>` runs production UI/UDP coverage and host persistence in two separate processes. It also exercises Discard/Reset isolation, complete production defaults, explicit Apply validation, Reset + Apply synchronization/persistence, and a deterministic save failure followed by an unchanged Apply retry. `-Visual` captures the actions and numeric fields. `check.ps1` covers authority, ordering, persistence, gameplay effects and capability/secret-safe projections in Debug/Release, plus deterministic editor draft/default/file recovery tests. Existing menu, reconnect, item, spawn, lifecycle, match and network harnesses cover integration regressions. Migration-specific deterministic and native UDP checks cover successive replacements, tuning/revision and RNG continuation, stale-host rejection and former-host return. These checks do not establish real multi-PC EOS behavior or subjective game balance, or physical-controller ergonomics.

[Feature index](README.md) · [Settings](settings.md) · [Vehicle networking](vehicle-networking.md)

The read-only [Event Log](event-log.md) records accepted tuning keys with old/new values, configuration revisions/rejections, Give Item and Force Start results, and practice reset/blast actions. F3 provides history and no mutation controls.

Item spawn weights are generated from the shared item registry (spawns.wrench_weight, spawns.missile_weight, spawns.oil_weight, spawns.nitro_weight, spawns.proxy_mine_weight, spawns.salvo_weight, spawns.machine_gun_weight). Zero excludes an item; the complete pool must retain positive total weight. All seven default to one. Oil uses the normal deployment path; Nitro uses the normal activation path and retains its slot while already boosted. The Oil patch bound is editable as items.maximum_oil_patches (1–32, default 16); lowering it preserves existing patches. The version-nineteen gameplay configuration payload includes the complete distribution in resume and migration checkpoints.

Nitro exposes `items.nitro_consumption_per_second`, `items.nitro_forward_thrust`, `items.nitro_speed_multiplier`, `items.nitro_airborne_thrust_scale`, `vehicle.overspeed_deceleration`, and `match.nitro_points_per_second` through the existing catalog. Live edits affect subsequent authoritative intervals without refilling charge. Ordinary validation, persistence, version-nineteen configuration replication and recovery apply. See [Nitro](items.md#sustained-nitro-resource).

## Progressive handling controls

The Dirt category exposes `vehicle.dirt_steering_reserve` (default 0.65, range 0–1) and `vehicle.dirt_recovery` (default 4/s, range 0–10/s). These control front-wheel authority during saturated slides and bounded slide-yaw recovery respectively; zero disables the corresponding contribution. They use the existing complete host Apply/Cancel/Reset transaction, schema-two file defaults for missing keys, configuration version seventeen, and resume/migration configuration boundary. `DirtRecoveryTests` checks isolation, recovery and continuation; `DeveloperConfigurationTests` checks each control's actual trajectory effect. No additional serialized vehicle memory is introduced.

The Vehicle category exposes `vehicle.steering_smoothing` (0.1 seconds), `vehicle.dirt_power_slip` (0.22 maximum rear lateral grip reduction), `vehicle.power_slip_response` (2/s) and `vehicle.power_slip_recovery` (2.5/s). They use the same host Apply/Reset, persistence, reliable revision and checkpoint paths. Steering smoothing accepts 0.01–1 seconds, power slip 0–0.8, and the two rates 0.1–20/s. Existing braking defaults to 17 m/s² and grass grip to 0.62. [Vehicles](vehicles.md) owns the force and recovery semantics.

## Surface tuning

Concrete, Dirt, Grass, Mud and Deep Mud each expose Grip, Drag and Acceleration multipliers under their searchable category. Keys are `vehicle.concrete.*`, `vehicle.dirt.*`, `vehicle.grass.*`, `vehicle.mud.*` and `vehicle.deep_mud.*`, with suffixes `grip`, `drag`, `acceleration`. Each maps directly to the matching `VehicleConfiguration` record used by movement; [vehicles](vehicles.md#terrain-handling-profiles) owns default values and force semantics. Asphalt retains existing vehicle controls rather than a second surface authority. Host overrides may intentionally depart from the default ordering.

Apply, Cancel, Reset, validation, host-local schema-two persistence, version-nineteen complete reliable configuration and existing resume/migration checkpoints carry all 155 values. New keys missing from saved files use canonical defaults. Old explicit Concrete/Mud overrides remain user tuning; Reset plus Apply selects the new defaults. No file migration silently overwrites those choices. The existing UI/UDP/persistence harness iterates all catalog controls; Core trajectory tests exercise every surface multiplier and checkpoint round trips.
The **Water** category adds five [water controls](water.md#gameplay-and-tuning) through the same authority, validation, persistence and recovery path. The complete configuration layout is described above.

Item category target weights use the same Configs catalog and persistence/replication path. See [per-player category credit](item-spawns.md#per-player-category-credit) for normalization, empty-category behavior and live tuning continuity.

## Proxy Mine tuning

The six `items.mine_*` controls and registry-generated `spawns.proxy_mine_weight` use the same Configs draft/apply, host validation, local schema-two persistence, replication and recovery owners. The catalog contains 155 keys and uses gameplay configuration wire version nineteen. [Magnetic Proxy Mine](items.md#magnetic-proxy-mine) defines defaults, units, force curve and live-effect semantics. Invalid minimum/maximum force pairs reject the complete transaction. No presentation-only damage or force configuration exists.

The [environment preset](terrain-effects.md) uses "environment.preset" in the same configuration, persistence, revision and recovery boundary. Its identity is session-owned; Godot lighting values remain Client presentation.

Open **F1 → Configs → Salvo** for `items.salvo_*` controls through the same catalog, persistence and configuration boundary. **Shot cooldown (ticks; 30 = 0.5 seconds)** defaults to 30; 60 means a one-second minimum between separately pressed shots. **Shots per pickup** sets ammunition for new grants; it does not refill held items. Range, launch height, arc height, flight speed, count, radius, damage, falloff, impulse and marker scale/width/lift are also editable. Apply Settings commits host tuning; existing saved overrides remain in effect until edited or reset. See [arcing Salvo](items.md#arcing-salvo) for defaults, bounds and live-edit semantics. Marker parameters are shared tuning; marker nodes remain local presentation and never replicated entities.

## Machine gun controls

The existing Configs catalog exposes `items.machine_gun_capacity` (800, 1–10000), `fire_rate` (80 rounds/s, 1–120), `range` (75 m, 1–100), `damage` (2.25 HP, 0–1000), `falloff_start` (12 m, nonnegative and below range), `falloff` (1.5, 0.1–8), `spread` (6 degrees half-angle, 0–30), `knockback` (8 N s, 0–1000), and `tracer_every` (2 rounds, 1–25); all suffixes share the `items.machine_gun_` prefix. The generated `spawns.machine_gun_weight` defaults to one. Capacity is captured on pickup; live edits never refill held magazines. The existing transaction, persistence, configuration replication and checkpoints carry these values. See [machine gun](items.md#sustained-machine-gun-resource).

## Air-control tuning

The Air control category uses the same staged Apply/Cancel/Reset, validation, host persistence and reliable configuration boundary as other vehicle controls. Values below are production defaults, including fresh profiles and Reset to Defaults. Valid existing saved overrides remain authoritative until reset.

| Key (`vehicle.` prefix) | Default | Unit / purpose |
| --- | --- | --- |
| `air_delay` | 0.15 | Continuous unsupported seconds before activation |
| `air_pitch_rate` / `air_yaw_rate` / `air_roll_rate` | 2.8 / 2.4 / 3.6 | Full-input rad/s, also sets axis sensitivity |
| `air_pitch_acceleration` / `air_yaw_acceleration` / `air_roll_acceleration` | 16 / 14 / 20 | Maximum angular change in rad/s² |
| `air_stabilization` | 8 | Released-axis damping per second; zero disables it |
| `air_stabilization_response` | 0.08 | Stabilization ramp time constant, seconds |
| `air_input_response` | 0.06 | Command smoothing time constant, seconds |
| `air_dead_zone` | 0.08 | Additional airborne logical-axis dead zone |
| `support_normal_minimum` | 0.55 | Ground/wheel support minimum world-normal Y; range 0.55–1 |

`VehicleMovement` consumes the first eleven values through the existing authority and prediction paths; the last value also reaches both native adapters and wheel ray observations. No orientation target, leveling strength or redundant torque multiplier exists. `AirControlTests`, native `check-air-control.ps1`, and release-default coverage verify this mapping and continuation. Configuration version 19 carries the complete 155-key catalog; old layouts are rejected.
