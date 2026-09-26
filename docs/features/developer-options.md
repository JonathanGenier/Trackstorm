# Shared Developer Options

Settings > Developer Options and F1 open Configs in the shared [DevTools shell](devtools.md). Host and admitted connected clients can edit the same session gameplay configuration. Core on the host validates every requested transaction; clients hold confirmed replicas and editor drafts, never an independent gameplay configuration owner. Force Start and item-grant APIs retain their host-only authority.

Numeric edits remain staged until **Apply Settings**. Clients show pending confirmation and keep the shell open until the host accepts or rejects the request. Invalid transactions leave gameplay unchanged and retain editable text with feedback. **Cancel** restores confirmed values; it cannot recall a request already sent. Fields without local edits refresh when other peers change configuration; explicit drafts remain marked as unsaved. Session/arena or authority-epoch changes reset drafts. Controls are unavailable during recovery or match synchronization.

Gameplay category **Reset to Defaults** and the global reset submit complete category/all-default transactions immediately through the same host validation path. A category reset preserves unrelated drafts and configuration. Active-match restrictions still apply, including mode changes and invalid kill targets: an invalid reset is rejected atomically. Local tire graphics and network simulation remain explicitly device-local; their resets remain staged until Apply. They are not exported or replicated as gameplay configuration.

Opening DevTools suppresses local driving input while simulation and networking continue. Configs/F1 follow `TrackstormDeveloperTools`, enabled in current Debug/Release QA builds; Stats and Logs keep their independent availability. Stats alone owns read-only current diagnostics, including game version, authority epoch and lease status without private credentials.

## Configs discovery and staged presentation

The host-only Force Start action remains fixed at the DevTools shell top-right, outside Configs and its scrolling content. A case-insensitive search field remains above the scrolling settings list. Search matches all entered words against labels, category names and stable keys, hiding unmatched rows and empty categories without altering editor or runtime values. Categories use the existing configuration catalog; labels are white. Staged values equal to `GameplayConfiguration.HostedDefaults` are blue and other values (including invalid text) are red. Comparison uses each setting's actual numeric type, so equivalent decimal text is not a change. Accepted session overrides remain red even before editing; dirty state instead compares with the effective editor baseline.

The shared shell keeps Reset to Defaults at bottom-left and Apply Settings, Cancel and Close at bottom-right. Reset uses a dark fill with a red border, Apply uses a green fill, Cancel uses a red fill and Close uses a blue fill. These actions, Force Start and the close-decision actions include adjacent project-owned icons with readable hover, pressed and focus states. A compact message above the actions shows amber **Unsaved changes**, green **Settings applied**, or red **Changes discarded**. Applied/discarded confirmations expire after three seconds. Client submissions explicitly wait for host confirmation; sending alone never shows success. Close/Escape protects drafts with Apply / Discard / Stay. While confirmation is pending, closure waits; after acceptance, Close is available again. Export Changes sits beside search and exports confirmed state independently of drafts.

Each catalog category is an independent accordion, initially collapsed. Headers show
an expand/collapse indicator; multiple categories may stay open. Disclosure state
lasts for the runtime panel's lifetime, including tab switches and closing/reopening
DevTools, without a persisted preference. Collapsing hides the category body and its
reset action without changing its staged values. Search temporarily expands matching
categories and disables their disclosure headers while filtering, so matches cannot
be hidden. Clearing search restores the previous open/closed state.

Every accordion contains **Reset to Defaults** inside its body. Search matches labels, categories and stable keys; filtered resets still cover the complete category. Gameplay resets use `GameplayConfiguration.HostedDefaults`; local tire and transport reset defaults remain owned by their existing local systems. Disclosure state is unaffected by resets.

## Authority and runtime application

Core `GameplayConfiguration` composes the existing vehicle, damage, item, spawn, respawn and match records plus destruction tuning and the shared environment identity. `GameplayOptions` is the explicit 170-key allowlist shared by the UI, persistence and wire codec. Each setter calls the existing owning validation through one complete candidate transaction. Local audio, graphics, bindings and display preferences never enter it. Core remains independent of Godot and storage.

Live scalar tuning additionally rejects positive values below 0.0001: subnormal mass/axle lengths can overflow fixed-step divisions despite passing older positive-only checks. Zero remains allowed where the owning rule explicitly supports it. Collision/respawn timers are bounded to one hour and the simulation clock remains fixed at 60 Hz.

`DevelopmentSession` submits allowlisted deltas through `LobbyNetworkDriver.RequestConfiguration`. A local host follows the same apply path; a remote request carries no chosen player identity. Core `LobbyAuthority` / `HostVehicleSession` accepts only the local authority or an admitted connected transport sender. The active arena driver invokes existing Core validation, retains accepted session tuning and refreshes native observers. Unknown, nonfinite, fractional integer, inconsistent and unsafe values are rejected atomically. Changed transactions advance one monotonic revision; no-ops do not. Remote edits record the actual requesting player in the event stream.

`Simulation.ApplyConfiguration` prepares replacement vehicle owners while preserving poses, lives, damage attribution, cooldown memory and match state. Max HP changes preserve the current health fraction without reviving dead vehicles. Steering memory clamps to a reduced steering limit. Existing item and spawn authorities adopt their new records. Straight projectiles adopt changed speed, preserving direction and remaining lifetime; explosion settings apply at impact. Existing respawn and pickup deadlines retain their absolute tick, and new delays apply to future events. Seed changes restart the item selection stream. Countdown duration changes restart a running countdown from the current tick. Minimum-player changes apply at the next Waiting/Countdown step; Active matches continue. A kill target at/below an existing score, or a changed target after Finished, is rejected atomically. match.mode selects 0 = First to Target or 1 = Circus (default). It is replicated as session state through the same catalog and may change in the lobby or Waiting only. Countdown, Active and Finished reject mode changes. Scoring-tuning edits after Finished preserve its exact result and completion tick.

Native `NetworkVehicleBody` observation receives the same configuration as Core and prediction: wheel rays use live suspension length/wheelbase, and impulse conversion uses live mass/inertia. `PredictedVehicle` uses accepted host tuning for subsequent steps and correction replay instead of constructing defaults.

## Replication and recovery

The version-one `TD` session configuration channel carries bounded catalog-indexed edit deltas, acknowledgements and complete confirmed session configuration. The outer `TG` connection envelope checks session, sender/recipient connection generation and authority epoch before decoding. Requests also bind to the current match generation; reliable delivery, connected admission and current unfrozen authority are required. Repeated request IDs are rejected. The host serializes requests in receive order, so concurrent edits apply to the latest accepted configuration without replacing unrelated fields. Host acknowledgements include the committed revision or a validation error. The UI waits for the corresponding gameplay revision before confirming acceptance. Interrupted/expired confirmations report uncertainty and require checking current values before retrying.

Complete session publications reach lobby clients and late arrivals even before an arena exists. Arena gameplay retains the existing configuration-before-world boundary below; the session replica is only a projection for lobby presentation and next-arena continuity. Recovery/migration uses the existing complete checkpoint and epoch reset, with no successor-local defaults reload.

The reliable version-twenty-three `TC` message carries arena generation, configuration revision and all 170 values (1381 bytes). Catalog order is part of this schema; additions/reordering require a version change. A client accepts configuration only from its established host over reliable delivery for the current arena. Lower revisions and conflicting duplicate revisions are rejected; identical duplicates are idempotent. The first complete revision-zero configuration may differ from canonical defaults because a host can start with persisted overrides.

The host sends configuration before its reliable world boundary on admission or edits. Version-ten `TS` world snapshots include the configuration revision; clients reject world/item boundaries for a different revision, preventing mismatched simulation. Subsequent unreliable snapshots recover normal movement after an ordered tuning change. EOS lobby metadata does not store gameplay tuning. Discovery compatibility is `trackstorm-lobby-15`.

Version-three `TR` resume checkpoints include and cross-validate the entire configuration and revision against vehicles and match target. Resume installs tuning before reconstructing prediction, resets history, and updates native bodies. Joining clients never inject their own host-local file into this path. See [reconnection](reconnection.md).

Host migration carries lobby tuning and the complete arena configuration/revision through the existing version-five migration checkpoint (a distinct envelope from the tuning message). The `TG` authority-epoch fence rejects previous-host tuning and gameplay traffic before decoding. The elected host restores the agreed tuning and item RNG position, without loading successor-local overrides. Uncheckpointed tuning can roll back with the selected complete boundary when the epoch advances; ordinary same-epoch revision guards remain unchanged. Editing requires a synchronized session; Force Start requires current unfrozen authority and uses the existing match owner and the elected stable host ID. Returning former hosts have client privileges. Epoch changes reset pending editor drafts. See [host migration](host-migration.md).

## Session ownership and Export Changes

The host may seed a newly created session from its existing `user://developer-settings.jsonl` via `DeveloperSettingsStore`. Joining clients never load that file into the session. Accepted edits and resets, whether requested by host or client, change session state only and never save back into any participant's local defaults file. Returning to lobby/rematching retains session tuning; leaving and hosting a fresh session reloads the original local seed. The existing schema-two store remains readable for compatibility and its isolated persistence tests remain applicable.

**Export Changes** is available to synchronized hosts and clients. It captures one confirmed configuration boundary when clicked, opens the native Windows Save As dialog with `configs.txt`, and writes UTF-8 text to the chosen destination. Cancel writes nothing; write failure leaves a retryable message. The file includes the actual `GameVersion.Current` and an ISO-8601 UTC timestamp, then only values different from `GameplayConfiguration.HostedDefaults`, grouped by existing Configs category in catalog order. Stable allowlisted keys avoid ambiguous labels; invariant round-trip numeric formatting avoids locale/precision drift. Equal host/client boundaries produce equal configuration sections (timestamps may differ). Drafts, local preferences, credentials and transport simulation are excluded. Export does not import or promote values into production defaults.

## Host-local persistence

Existing host seed files remain readable for compatibility; normal DevTools edits no longer save them. Their format and canonical baseline are retained below.

Schema two begins with `{"schema":2}` followed by independent `{"key":"vehicle.mass","value":900}` records. Missing keys retain canonical defaults; headerless/schema-zero files are supported. The targeted alias `vehicle.top_speed` migrates to `vehicle.forward_speed`. Unknown keys retain their raw JSON values when saved, without becoming editable or affecting gameplay. Malformed records are skipped independently. Related valid bounds apply together first; a damaged transaction salvages independently valid values. Files are bounded to 64 KiB/depth eight. Unsupported future schemas and oversized files use defaults and disable rewriting. Actions, diagnostics and network impairment have no persistent keys. Accepted balance changes still require explicit promotion into repository defaults.

Host-local schema 2 migrates the old default wheelbase, load height and suspension length to the rescaled vehicle dimensions. It preserves deliberately customized spatial values and every unrelated override; schema-2 values are never re-migrated. Custom spatial overrides can intentionally depart from the canonical asset geometry.

`GameplayConfiguration.HostedDefaults` is the canonical hosted-game preset, including **1000 Max HP** and the approved driving, collision and missile tuning below. `NetworkVehicleArena`, the session fallback, `DeveloperSettingsStore` (including missing persisted keys), and Reset all use that definition. Fresh profiles receive the complete preset without a saved file. Valid saved keys override it; Reset applies it to the shared session through the normal host-validated transaction; export is explicit and leaves local defaults untouched. Its vehicle record uses the same asphalt defaults as `new VehicleConfiguration()` and practice; generic damage fixtures retain 100 HP. Surface categories use the same catalog, with nine additional Dirt, Grass and Deep Mud keys in the version-eight configuration layout. Player-local graphics, audio, bindings and display settings remain separate.

| Gameplay key | Canonical owning property in the hosted preset | Release default |
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

The `Circus stunts` group extends the existing allowlist, host validation, Apply/Cancel/Reset, session ownership and legacy seed compatibility, complete reliable configuration and checkpoint/migration paths. Missing saved keys use defaults. Fractional match/scoring properties retain binary64 precision in the editor; vehicle properties retain their owning binary32 semantics, so default/dirty comparisons do not narrow scoring thresholds accidentally.

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

`check-developer-options.ps1 -GodotPath <exe>` runs production UI/UDP coverage and session ownership in two separate processes. It exercises client-originated edits, host rejection, three-peer convergence, late join, immediate shared category/global reset, repeated changes, draft rebasing, actual HP/native observer consumption, equal host/client exports, and fresh-session isolation. Headless checks drive the real FileDialog save/cancel signals to verify the export callback and error feedback; native OS dialog interaction remains a separate manual check. `-Visual` captures the actions and numeric fields. `check.ps1` covers authority, ordering, persistence, gameplay effects and capability/secret-safe projections in Debug/Release, plus deterministic editor draft/default/file recovery tests. Existing menu, reconnect, item, spawn, lifecycle, match and network harnesses cover integration regressions. Migration-specific deterministic and native UDP checks cover successive replacements, tuning/revision and RNG continuation, stale-host rejection and former-host return. These checks do not establish real multi-PC EOS behavior or subjective game balance, or physical-controller ergonomics.

[Feature index](README.md) · [Settings](settings.md) · [Vehicle networking](vehicle-networking.md)

The read-only [Event Log](event-log.md) records accepted tuning keys with old/new values, configuration revisions/rejections, Give Item and Force Start results, and practice reset/blast actions. F3 provides history and no mutation controls.

Item spawn weights are generated from the shared item registry (spawns.wrench_weight, spawns.missile_weight, spawns.oil_weight, spawns.nitro_weight, spawns.proxy_mine_weight, spawns.salvo_weight, spawns.machine_gun_weight). Zero excludes an item; the complete pool must retain positive total weight. All seven default to one. Oil uses the normal deployment path; Nitro uses the normal activation path and retains its slot while already boosted. Oil grip, recovery duration, vehicle-pass budget and cleanup lifetime are editable in the Oil category; deployment has no global active-patch gate. The version-twenty-three gameplay configuration payload includes the complete distribution in resume and migration checkpoints.

Nitro exposes `items.nitro_consumption_per_second`, `items.nitro_forward_thrust`, `items.nitro_speed_multiplier`, `items.nitro_airborne_thrust_scale`, `vehicle.overspeed_deceleration`, and `match.nitro_points_per_second` through the existing catalog. Live edits affect subsequent authoritative intervals without refilling charge. Ordinary validation, session state, version-twenty-three configuration replication and recovery apply. See [Nitro](items.md#sustained-nitro-resource).

## Progressive handling controls

The Dirt category exposes `vehicle.dirt_steering_reserve` (default 0.65, range 0–1) and `vehicle.dirt_recovery` (default 4/s, range 0–10/s). These control front-wheel authority during saturated slides and bounded slide-yaw recovery respectively; zero disables the corresponding contribution. They use the existing complete shared Apply/Cancel and authoritative Reset transaction, schema-two file defaults for missing keys, configuration version twenty-two, and resume/migration configuration boundary. `DirtRecoveryTests` checks isolation, recovery and continuation; `DeveloperConfigurationTests` checks each control's actual trajectory effect. No additional serialized vehicle memory is introduced.

The Vehicle category exposes `vehicle.steering_smoothing` (0.1 seconds), `vehicle.dirt_power_slip` (0.22 maximum rear lateral grip reduction), `vehicle.power_slip_response` (2/s) and `vehicle.power_slip_recovery` (2.5/s). They use the same shared Apply/Reset, session state, reliable revision and checkpoint paths. Steering smoothing accepts 0.01–1 seconds, power slip 0–0.8, and the two rates 0.1–20/s. Existing braking defaults to 17 m/s² and grass grip to 0.62. [Vehicles](vehicles.md) owns the force and recovery semantics.

## Surface tuning

Concrete, Dirt, Grass, Mud and Deep Mud each expose Grip, Drag and Acceleration multipliers under their searchable category. Keys are `vehicle.concrete.*`, `vehicle.dirt.*`, `vehicle.grass.*`, `vehicle.mud.*` and `vehicle.deep_mud.*`, with suffixes `grip`, `drag`, `acceleration`. Each maps directly to the matching `VehicleConfiguration` record used by movement; [vehicles](vehicles.md#terrain-handling-profiles) owns default values and force semantics. Asphalt retains existing vehicle controls rather than a second surface authority. Host overrides may intentionally depart from the default ordering.

Apply, Cancel, Reset, validation, host-authoritative session configuration, version-twenty-three complete reliable configuration and existing resume/migration checkpoints carry all 170 values. New keys missing from saved files use canonical defaults. Old explicit Concrete/Mud overrides remain user tuning; Reset selects the new defaults. No file migration silently overwrites those choices. The existing UI/UDP/persistence harness iterates all catalog controls; Core trajectory tests exercise every surface multiplier and checkpoint round trips.
The **Water** category adds five [water controls](water.md#gameplay-and-tuning) through the same authority, validation and recovery path. The complete configuration layout is described above.

Item category target weights use the same Configs catalog and session replication path. See [per-player category credit](item-spawns.md#per-player-category-credit) for normalization, empty-category behavior and live tuning continuity.

## Proxy Mine tuning

The six `items.mine_*` controls and registry-generated `spawns.proxy_mine_weight` use the same Configs draft/apply, host validation, shared session configuration, replication and recovery owners. The catalog contains 170 keys and uses gameplay configuration wire version twenty-two. [Magnetic Proxy Mine](items.md#magnetic-proxy-mine) defines defaults, units, force curve and live-effect semantics. Invalid minimum/maximum force pairs reject the complete transaction. No presentation-only damage or force configuration exists.

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
| `air_delay` | 0.15 | Continuous unsupported seconds before activation |
| `air_pitch_rate` / `air_yaw_rate` / `air_roll_rate` | 2.8 / 2.4 / 3.6 | Full-input rad/s, also sets axis sensitivity |
| `air_pitch_acceleration` / `air_yaw_acceleration` / `air_roll_acceleration` | 16 / 14 / 20 | Maximum angular change in rad/s² |
| `air_stabilization` | 8 | Released-axis damping per second; zero disables it |
| `air_stabilization_response` | 0.08 | Stabilization ramp time constant, seconds |
| `air_input_response` | 0.06 | Command smoothing time constant, seconds |
| `air_dead_zone` | 0.08 | Additional airborne logical-axis dead zone |
| `support_normal_minimum` | 0.55 | Ground/wheel support minimum world-normal Y; range 0.55–1 |

`VehicleMovement` consumes the first eleven values through the existing authority and prediction paths; the last value also reaches both native adapters and wheel ray observations. No orientation target, leveling strength or redundant torque multiplier exists. `AirControlTests`, native `check-air-control.ps1`, and release-default coverage verify this mapping and continuation. Configuration version 23 carries the complete 170-key catalog; old layouts are rejected.
## Collision and destruction tuning

Collision and Destruction use the existing shared Apply/Cancel and authoritative Reset, validation,
session configuration, reliable revision and resume/migration boundary. New keys
missing from saved files use production defaults; explicit overrides remain until
Reset. Configuration wire version 23 carries all 170 keys.

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
| `vehicle.oil_grip_reduction` | 0.5 | 0–0.8 fraction of lateral force removed |
| `vehicle.oil_recovery_seconds` | 1.75 | 0.1–10 seconds after supported overlap ends |
| `items.oil_enemy_contacts` | 2 | 1–7 vehicle passes, including the owner |
| `items.oil_lifetime_seconds` | 60 | 1–600 seconds |

Grip edits affect subsequent movement; recovery-duration edits preserve the current recovery fraction with fixed-step rounding. New deployments capture pass budget and lifetime; changing settings never refreshes deadlines or refunds consumed passes. Every new supported entry counts, including the owner and the same vehicle after exit. Continuous overlap counts once; owner entry scores zero. The existing `items.oil_enemy_contacts` persistence key is retained to preserve saved tuning, but the UI now labels it "Vehicle passes before removal" and its meaning includes all vehicles. The obsolete `items.maximum_oil_patches` key has no gameplay effect; unknown persisted keys follow the existing preservation policy. The catalog has 170 keys in configuration protocol version 23.