# Shared Developer Options

The **Weapon aiming** category exposes the six [shared aiming](weapon-aiming.md) controls through the same validated gameplay catalog: turn rate, up/down limits, near-target cone and mouse/stick friction. Per-device sensitivity and stick response exponent are local Controls settings, outside shared tuning.

User-selected config exports are acquired by `DeveloperSettingsStore.TryReadImport` using a bounded, uncached, strict UTF-8 read. The panel retains session/epoch checks, parsing into its draft and existing status feedback; reading a file does not apply or persist settings. Local settings and config imports stay with their filesystem store rather than the Godot asset loaders.

Settings > Developer Options and F1 open Configs in the shared [DevTools shell](devtools.md). Host and admitted connected clients can edit the same session gameplay configuration. Core on the host validates every requested transaction; clients hold confirmed replicas and editor drafts, never an independent gameplay configuration owner. Force Start and item-grant APIs retain their host-only authority.

Numeric edits remain staged until **Apply Settings**. Clients show pending confirmation and keep the shell open until the host accepts or rejects the request. Invalid transactions leave gameplay unchanged and retain editable text with feedback. **Cancel** restores confirmed values; it cannot recall a request already sent. Fields without local edits refresh when other peers change configuration; explicit drafts remain marked as unsaved. Session/arena or authority-epoch changes reset drafts. Controls are unavailable during recovery or match synchronization.

Gameplay category **Reset to Defaults** and the global reset submit complete category/all-default transactions immediately through the same host validation path. A category reset preserves unrelated drafts and configuration. Active-match restrictions still apply, including mode changes and invalid kill targets: an invalid reset is rejected atomically. Local tire graphics and network simulation remain explicitly device-local; their resets remain staged until Apply. They are not exported or replicated as gameplay configuration.

Opening DevTools suppresses local driving input while simulation and networking continue. Configs/F1 follow `TrackstormDeveloperTools`, enabled in current Debug/Release QA builds; Stats and Logs keep their independent availability. Stats alone owns read-only current diagnostics, including game version, authority epoch and lease status without private credentials.

## Configs discovery and staged presentation

The host-only Force Start action remains fixed at the DevTools shell top-right, outside Configs and its scrolling content. A case-insensitive search field remains above the scrolling settings list. Search matches all entered words against labels, category names and stable keys, hiding unmatched rows and empty categories without altering editor or runtime values. Categories use the existing configuration catalog; labels are white. Staged values equal to `GameplayConfiguration.HostedDefaults` are blue and other values (including invalid text) are red. Comparison uses each setting's actual numeric type, so equivalent decimal text is not a change. Accepted session overrides remain red even before editing; dirty state instead compares with the effective editor baseline.

The shared shell keeps Reset to Defaults at bottom-left and Apply Settings, Cancel and Close at bottom-right. Reset uses a dark fill with a red border, Apply uses a green fill, Cancel uses a red fill and Close uses a blue fill. These actions, Force Start and the close-decision actions include adjacent project-owned icons with readable hover, pressed and focus states. A compact message above the actions shows amber **Unsaved changes**, green **Settings applied**, or red **Changes discarded**. Applied/discarded confirmations expire after three seconds. Client submissions explicitly wait for host confirmation; sending alone never shows success. Close/Escape protects drafts with Apply / Discard / Stay. While confirmation is pending, closure waits; after acceptance, Close is available again. Export Changes and Import Configs sit beside search. Export uses confirmed state; import stages only the listed settings for Apply.

Each catalog category is an independent accordion, initially collapsed. Headers show
an expand/collapse indicator; multiple categories may stay open. Disclosure state
lasts for the runtime panel's lifetime, including tab switches and closing/reopening
DevTools, without a persisted preference. Collapsing hides the category body and its
reset action without changing its staged values. Search temporarily expands matching
categories and disables their disclosure headers while filtering, so matches cannot
be hidden. Clearing search restores the previous open/closed state.

Every accordion contains **Reset to Defaults** inside its body. Search matches labels, categories and stable keys; filtered resets still cover the complete category. Gameplay resets use `GameplayConfiguration.HostedDefaults`; local tire and transport reset defaults remain owned by their existing local systems. Disclosure state is unaffected by resets.

## Authority and runtime application

Core `GameplayConfiguration` composes the existing vehicle, digital-input shaping, damage, item, spawn, respawn and match records plus destruction tuning and the shared environment identity. `GameplayOptions` is the explicit 222-key allowlist shared by the UI, persistence and wire codec. Each setter calls the existing owning validation through one complete candidate transaction. Local audio, graphics, bindings and display preferences never enter it. Core remains independent of Godot and storage.

Live scalar tuning additionally rejects positive values below 0.0001: subnormal mass/axle lengths can overflow fixed-step divisions despite passing older positive-only checks. Zero remains allowed where the owning rule explicitly supports it. Collision/respawn timers are bounded to one hour and the simulation clock remains fixed at 60 Hz.

`DevelopmentSession` submits allowlisted deltas through `LobbyNetworkDriver.RequestConfiguration`. A local host follows the same apply path; a remote request carries no chosen player identity. Core `LobbyAuthority` / `HostVehicleSession` accepts only the local authority or an admitted connected transport sender. The active arena driver invokes existing Core validation, retains accepted session tuning and refreshes native observers. Unknown, nonfinite, fractional integer, inconsistent and unsafe values are rejected atomically. Changed transactions advance one monotonic revision; no-ops do not. Remote edits record the actual requesting player in the event stream.

`Simulation.ApplyConfiguration` prepares replacement vehicle owners while preserving poses, lives, damage attribution, cooldown memory and match state. Max HP changes preserve the current health fraction without reviving dead vehicles. Steering memory clamps to a reduced steering limit. Existing item and spawn authorities adopt their new records. Straight projectiles adopt changed speed, preserving direction and remaining lifetime; explosion settings apply at impact. Existing respawn and pickup deadlines retain their absolute tick, and new delays apply to future events. Seed changes restart the item selection stream. Countdown duration changes restart a running countdown from the current tick. Minimum-player changes apply at the next Waiting/Countdown step; Active matches continue. A kill target at/below an existing score, or a changed target after Finished, is rejected atomically. match.mode selects 0 = First to Target or 1 = Circus (default). It is replicated as session state through the same catalog and may change in the lobby or Waiting only. Countdown, Active and Finished reject mode changes. Scoring-tuning edits after Finished preserve its exact result and completion tick.

Native `NetworkVehicleBody` observation receives the same configuration as Core and prediction: wheel rays use live suspension length/wheelbase, and impulse conversion uses live mass/inertia. `PredictedVehicle` uses accepted host tuning for subsequent steps and correction replay instead of constructing defaults.

## Replication and recovery

The version-one `TD` session configuration channel carries bounded catalog-indexed edit deltas, acknowledgements and complete confirmed session configuration. The outer `TG` connection envelope checks session, sender/recipient connection generation and authority epoch before decoding. Requests also bind to the current match generation; reliable delivery, connected admission and current unfrozen authority are required. Repeated request IDs are rejected. The host serializes requests in receive order, so concurrent edits apply to the latest accepted configuration without replacing unrelated fields. Host acknowledgements include the committed revision or a validation error. The UI waits for the corresponding gameplay revision before confirming acceptance. Interrupted/expired confirmations report uncertainty and require checking current values before retrying.

Complete session publications reach lobby clients and late arrivals even before an arena exists. Arena gameplay retains the existing configuration-before-world boundary below; the session replica is only a projection for lobby presentation and next-arena continuity. Recovery/migration uses the existing complete checkpoint and epoch reset, with no successor-local defaults reload.

The reliable version-forty-two `TC` message carries arena generation, configuration revision and all 222 values (1797 bytes). Catalog order is part of this schema; additions/reordering require a version change. A client accepts configuration only from its established host over reliable delivery for the current arena. Lower revisions and conflicting duplicate revisions are rejected; identical duplicates are idempotent. The first complete revision-zero configuration may differ from canonical defaults because a host can start with persisted overrides.

The host sends configuration before its reliable world boundary on admission or edits. Version-fourteen `TS` world snapshots include the configuration revision; clients reject world/item boundaries for a different revision, preventing mismatched simulation. Subsequent unreliable snapshots recover normal movement after an ordered tuning change. EOS lobby metadata does not store gameplay tuning. Discovery compatibility is `trackstorm-lobby-16`.

Version-three `TR` resume checkpoints include and cross-validate the entire configuration and revision against vehicles and match target. Resume installs tuning before reconstructing prediction, resets history, and updates native bodies. Joining clients never inject their own host-local file into this path. See [reconnection](reconnection.md).

Host migration carries lobby tuning and the complete arena configuration/revision through the existing version-five migration checkpoint (a distinct envelope from the tuning message). The `TG` authority-epoch fence rejects previous-host tuning and gameplay traffic before decoding. The elected host restores the agreed tuning and item RNG position, without loading successor-local overrides. Uncheckpointed tuning can roll back with the selected complete boundary when the epoch advances; ordinary same-epoch revision guards remain unchanged. Editing requires a synchronized session; Force Start requires current unfrozen authority and uses the existing match owner and the elected stable host ID. Returning former hosts have client privileges. Epoch changes reset pending editor drafts. See [host migration](host-migration.md).

## Session ownership and Export Changes

The host may seed a newly created session from its existing `user://developer-settings.jsonl` via `DeveloperSettingsStore`. Joining clients never load that file into the session. Accepted edits and resets, whether requested by host or client, change session state only and never save back into any participant's local defaults file. Returning to lobby/rematching retains session tuning; leaving and hosting a fresh session reloads the original local seed. The existing schema-two store remains readable for compatibility and its isolated persistence tests remain applicable.

**Export Changes** is available to synchronized hosts and clients. It captures one confirmed configuration boundary when clicked, opens the native Windows Save As dialog with `configs.txt`, and writes UTF-8 text to the chosen destination. Cancel writes nothing; write failure leaves a retryable message. The file includes the actual `GameVersion.Current` and an ISO-8601 UTC timestamp, then only values different from `GameplayConfiguration.HostedDefaults`, grouped by existing Configs category in catalog order. Stable allowlisted keys avoid ambiguous labels; invariant round-trip numeric formatting avoids locale/precision drift. Equal host/client boundaries produce equal configuration sections (timestamps may differ). Drafts, local preferences, credentials and transport simulation are excluded. Export never promotes values into production defaults.

### Import Configs

**Import Configs** opens the native file picker with `configs.txt` and reads the same UTF-8 header, version, ISO-8601 timestamp, category headings and stable `key=value` entries produced by Export Changes. Only listed keys are staged; omitted settings retain current values, including nondefault session tuning. An export containing no differences is a no-op. Imports leave unrelated drafts and local preferences intact. Review the highlighted values and use **Apply Settings** to submit them through ordinary host validation, atomic application and replication. Cancel discards the draft normally.

Core validates the complete imported edit set against current tuning without changing gameplay. Unknown/incorrect categories or keys, duplicate categories/keys, malformed metadata, invalid/nonfinite numbers, incompatible bounds and files over 64 KiB are rejected without partially changing the draft. UTF-8 BOM and CRLF are accepted; numeric parsing is invariant and preserves binary32/binary64 owners. Files from another canonical game version are accepted only for supported keys and current validation, with visible source-version feedback; only the explicit historical Shield aliases are migrated; other renamed or removed keys remain unsupported.

Explicit imported keys remain pending even if initially equal to their baseline, so another peer's intervening edit cannot silently replace the imported intent. Unlisted untouched fields continue to refresh. A pending host request or open export picker blocks import; a session/arena owner or authority change while choosing a file rejects the stale selection. File cancellation/read errors leave drafts and gameplay unchanged. Import never saves permanent defaults. The host repeats live validation when Apply arrives, so a formerly valid file can still be rejected atomically by active-match restrictions.

## Host-local persistence

Existing host seed files remain readable for compatibility; normal DevTools edits no longer save them. Their format and canonical baseline are retained below.

Schema two begins with `{"schema":2}` followed by independent `{"key":"vehicle.mass","value":900}` records. Missing keys retain canonical defaults; headerless/schema-zero files are supported. The targeted alias `vehicle.top_speed` migrates to `vehicle.forward_speed`. The eight historical Shield tuning/spawn aliases are recognized only when reading host seeds or config exports; writes and live requests use `items.shield_*` and `spawns.shield_weight`. Historical export category headings become Shield. Duplicate canonical/alias entries or headings reject an import atomically; in seed files an explicit valid canonical record takes precedence regardless of order. Unknown keys are not broadly rewritten. Unknown keys retain their raw JSON values when saved, without becoming editable or affecting gameplay. Malformed records are skipped independently. Related valid bounds apply together first; a damaged transaction salvages independently valid values. Files are bounded to 64 KiB/depth eight. Unsupported future schemas and oversized files use defaults and disable rewriting. Actions, diagnostics and network impairment have no persistent keys. Accepted balance changes still require explicit promotion into repository defaults.

Host-local schema 2 migrates the old default wheelbase, load height and suspension length to the rescaled vehicle dimensions. It preserves deliberately customized spatial values and every unrelated override; schema-2 values are never re-migrated. Custom spatial overrides can intentionally depart from the canonical asset geometry.

`GameplayConfiguration.HostedDefaults` is the canonical hosted-game preset, including **1000 Max HP** and the approved driving, collision and missile tuning below. `NetworkVehicleArena`, the session fallback, `DeveloperSettingsStore` (including missing persisted keys), and Reset all use that definition. Fresh profiles receive the complete preset without a saved file. Valid saved keys override it; Reset applies it to the shared session through the normal host-validated transaction; export is explicit and leaves local defaults untouched. Its vehicle record uses the same asphalt defaults as `new VehicleConfiguration()` and practice; generic damage fixtures retain 100 HP. Surface categories use the same catalog, with nine additional Dirt, Grass and Deep Mud keys in the version-eight configuration layout. Player-local graphics, audio, bindings and display settings remain separate.

| Gameplay key | Canonical owning property in the hosted preset | Release default |
| --- | --- | --- |
| `vehicle.acceleration` | `VehicleConfiguration.Acceleration` | 51.42857 |
| `vehicle.stop_speed` | `VehicleConfiguration.StopSpeed` | 0.05f |
| `vehicle.reverse_acceleration` | `VehicleConfiguration.ReverseAcceleration` | 8 |
| `vehicle.forward_speed` | `VehicleConfiguration.ForwardSpeed` | 44.44f |
| `vehicle.steering_angle` | `VehicleConfiguration.SteeringAngle` | 0.9f |
| `vehicle.steering_response` | `VehicleConfiguration.SteeringResponse` | 0.95f |
| `vehicle.tire_friction` | `VehicleConfiguration.TireFriction` | 1.9f |
| `vehicle.coast_drag` | `VehicleConfiguration.CoastDrag` | 0.28f |
| `vehicle.suspension_length` | `VehicleConfiguration.SuspensionLength` | VehicleDimensions.RideHeight + 11 / 22 |
| `vehicle.wheel_spring` | `VehicleConfiguration.WheelSpring` | 22 |
| `vehicle.wheel_damping` | `VehicleConfiguration.WheelDamping` | 12 |
| `vehicle.wheel_rebound_damping` | `VehicleConfiguration.WheelReboundDamping` | 16 |
| `vehicle.wheel_bump_start` | `VehicleConfiguration.WheelBumpStart` | 0.55 m |
| `vehicle.wheel_bump_spring` | `VehicleConfiguration.WheelBumpSpring` | 3500 |
| `vehicle.suspension_damping` | `VehicleConfiguration.SuspensionDamping` | 8 |
| `vehicle.handbrake_response` | `VehicleConfiguration.HandbrakeResponse` | 1 |
| `vehicle.traction_recovery` | `VehicleConfiguration.TractionRecovery` | 3 |
| `vehicle.chassis_compliance` | `VehicleConfiguration.ChassisCompliance` | 0.004f |
| `damage.collision_scale` | `DamageConfiguration.CollisionScale` | 5 |
| `items.missile_speed` | `ItemConfiguration.MissileSpeed` | 120 |
| `items.explosion_radius` | `ItemConfiguration.ExplosionRadius` | 12 |
| `items.maximum_damage` | `ItemConfiguration.MaximumDamage` | 300 |

The remaining persisted values use their owning Core defaults, including HP, item spawning, respawn and match rules. The approved tuning sets dirt steering reserve to 0.95; Asphalt grip to 3 and Dirt grip to 2 and Grass to 1.9; Concrete grip to 1.5; Oil grip reduction and recovery to 0.1 and 0.5 seconds; Wrench healing to 500 HP; Proxy Mine damage, radius, force range and impulse to 250 HP, 30 m, 1000–2000 N and 18000 N s; and Salvo speed, blast radius, damage and marker scale/width/lift to 150 m/s, 10 m, 250 HP and 0.5/0.5 m/0.05 m. The canonical spawn seed is 34272265; a fresh application match generates its own authoritative seed as described in [item spawning](item-spawns.md). The complete stable-key ownership map below applies to the catalog. Decimal float literals retain exact binary32 values represented by persisted JSON doubles. `ReleaseDefaultsTests` checks approved numeric values exactly and verifies validation, persistence and network round trips.

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

[Read this section](developer-options-catalog.md#editable-control-to-runtime-mapping).

## Progressive handling controls

[Read this section](developer-options-catalog.md#progressive-handling-controls).

## Surface tuning

[Read this section](developer-options-catalog.md#surface-tuning).

## Proxy Mine tuning

[Read this section](developer-options-catalog.md#proxy-mine-tuning).

## Live distribution tuning

[Read this section](developer-options-catalog.md#live-distribution-tuning).

## Machine gun controls

[Read this section](developer-options-catalog.md#machine-gun-controls).

## Air-control tuning

[Read this section](developer-options-catalog.md#air-control-tuning).

## Collision and destruction tuning

[Read this section](developer-options-catalog.md#collision-and-destruction-tuning).

## Circus match duration

[Read this section](developer-options-catalog.md#circus-match-duration).

## Oil tuning

[Read this section](developer-options-catalog.md#oil-tuning).

## Car deployment

<a id="ts-197-latest-handling-correction-tuning"></a>
<a id="rear-drive-handling-controls"></a>
<a id="digital-controls-and-asphalt-purchase"></a>
<a id="progressive-braking-and-rear-breakaway"></a>
<a id="controller-steering-precision-and-vehicle-mass"></a>
[Read this section](developer-options-catalog.md#car-deployment).

## Ground steering and retired power drift

<a id="surface-braking-and-stationary-holding"></a>
<a id="shield-wall-deployment"></a>
[Read this section](developer-options-catalog.md#ground-steering-and-retired-power-drift).

The tuning catalog is a reference, not mandatory background for ordinary UI work. Locate the affected heading or stable key and read that section plus the shared authority/persistence contract.
