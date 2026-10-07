# Held-Item Combat

[Shared weapon aiming](weapon-aiming.md) supplies life/token-scoped desired-direction validation, accepted articulation, near-target input friction and the square/corner HUD for direct-fire identities. Its replaceable item message kinds are separate from reliable ownership/outcomes; Salvo stays on its existing path.

## Behavior and Acquisition

Network arenas register Wrench, Missile, Oil, Nitro, Proxy Mine, Salvo, Machine Gun and Shield, with two fixed held slots per player. Acquisition fills the first empty slot without changing selection or replacing either held item. **E** or controller **X / left face button** switches the active slot, including empty slots; use targets only the selected item. Switching and use together in a captured frame switch first. Oil deploys a persistent terrain-aligned hazard. Nitro retains its slot as a percentage resource: hold use to boost, release to preserve charge, and reuse until exhausted. Use **LMB** or controller **Y / top face button** through the remappable **UseItem** action. One press generates one capability-bound request; discrete items do not repeat while held. Nitro and Machine Gun require held input to sustain use; a tap released before the next tick consumes neither charge nor ammunition. Empty-slot requests fail safely. The local HUD and vehicle rack show confirmed selected ownership; remote racks use the same accepted item publication. The host development grant API grants registered items to the living host's first empty slot; they cannot overwrite either occupied slot. The internal all-vehicle grant seam remains for integration fixtures. Normal acquisition happens by driving within range of an available arena pickup, as described in [item spawning](item-spawns.md). Local rigid-body practice retains its generic blast demonstration on the same developer page.

Wrench repairs 500 HP by default, clamped to the vehicle's existing maximum. A living player can always consume it at full health: zero effective healing still clears the slot and produces a confirmed use outcome. It cannot revive a destroyed vehicle.

Missile launches from the host-observed vehicle center along its forward (-Z) direction at the hosted default of 120 m/s, independent of vehicle velocity and subsequent steering. The collision adapter excludes the owner's body and sweeps the entire 60 Hz segment, including the launch segment. Starting inside the owner's body avoids a muzzle offset skipping a nearby wall. Hits against vehicle proxies, static world, native props and perimeter walls produce one explosion at the hit fraction. There is no homing, gravity, direct-hit bonus or additional direct-hit damage. A projectile that survives 300 ticks expires without exploding. At most 16 missiles can be active; reaching the cap leaves the requested item held.

The explosion linearly fades from 300 HP and 15,000 N·s at the center to zero at/outside a twelve-metre radius. `ItemConfiguration` validates configurable healing, speed, lifetime, radius, maximum damage and impulse; the zero edge is the falloff curve's defined minimum. Vehicles and movable objects use the same Core distance/direction helper, including a stable upward direction at the exact center. Every living vehicle in range receives one radial effect, including the owner; walls do not occlude the radial blast. Old Map demonstration props receive the host-calculated physical impulse without HP. Production dressing rocks and plants consume the same committed Missile/Salvo impacts through [destructible environment authority](destructible-environment.md). Secondary native collisions can still cause the existing collision damage.

## Permanent selected-slot discard

**X** on keyboard or controller **D-pad Left** requests permanent deletion of the selected held item. Discard uses the remappable `DiscardItem` action and one captured press edge. The host clears only that physical slot immediately on acceptance; selection and the other slot's identity, capability and resources remain unchanged. Empty-slot discard has no item effect. It creates no pickup, projectile, hazard, world wall, use sound or use outcome. Existing deployed entities are unaffected.

The existing `ItemAuthority` validates sender-owned vehicle, participation, arena generation, life, selected grant token and exact selection command watermark. Accepted deletion retires the capability, clears that slot's charge/ammunition/Salvo state, cancels only its pending use, disengages sustained use and removes its attached Shield pool. The same reliable publication updates HUD, rack and remote representations. A remote player sees confirmation after network delivery; there is no predicted inventory. Switching and discard in one frame select first; discard precedes use, so the deleted capability cannot fire or deploy. A delayed command for a replacement grant, former selection boundary or old life fails closed.

`DiscardRevision` is a monotonic deletion watermark on the existing item authority/publication, serialized in item protocol 21 and nested complete checkpoints. It is not another inventory. Resume and ordinary publication reject a regressed watermark. Migration excludes retained checkpoints before either the currently confirmed watermark or a newer retained checkpoint's watermark; when no safe common checkpoint remains, recovery fails closed. This preserves confirmed deletion rather than rolling it back. A request interrupted before authoritative confirmation is not a committed client outcome; as with other host-authoritative requests, remote deletion is confirmed by the reliable boundary.

`check-input.ps1` exercises native synthetic defaults, short taps, suppression, remapping and saved preferences. `check-items.ps1` exercises both selected slots, empty/repeated discard and switch/discard ordering on eight native UDP peers. `check-death-respawn.ps1`, `check-reconnect.ps1` and three-player `check-migration.ps1` include deleted-slot continuation; Core and transport tests additionally exercise delayed/foreign requests, stale publications, sustained-use cancellation, both death retention policies and stale recovery rejection. Physical controller ergonomics and Internet/EOS acceptance require separate device evidence.

## Registry and extension boundaries

`ItemRegistry` is the single immutable roster in stable selection order. Each `ItemDefinition` connects the stable byte identity, behavior/tuning key, display and presentation identity, default within-category spawn weight, category identity, and audio/VFX hooks. `None` is only an empty-slot sentinel. Wrench, Missile, Oil, Nitro, Proxy Mine and Salvo use internal stateless handlers to stage effects into the existing authority transaction; Machine Gun uses the same authority transaction through its sustained short-range ray path. The vehicle world remains the sole repair/damage authority. Damaging definitions register their authoritative `DamageSource` identity for the shared [Circus item-damage conversion](matches.md#circus-combat-score); weapon handlers never award points. Nitro stages a boost intent into that transaction, and Oil stages placement there as well; there is no second inventory, spawn authority or item envelope.

`ItemSpawnConfiguration.Weights` is an immutable map keyed by registered identities. Developer tuning keys are generated from registry keys and travel through the existing version-forty-one gameplay configuration codec. The item codec uses version twenty-one; checkpoint envelopes compose these existing codecs without a parallel serialization path. Identical game versions remain required.

Client HUD icons/names, use VFX and pickup/use audio resolve the registry metadata. Stats and structured events retain the shared item identity. Client assets and native effects remain Client-owned; Core has no Godot references. Oil and Nitro reuse the existing weapon pickup cue and have original project-created HUD silhouettes. Oil has reconstructable persistent world presentation; Nitro carries reconstructable active state on the existing vehicle boundary.

## Authority and Replication

### Replaceable projectile motion

Missile and Salvo simulation and segment collision remain authoritative at 60 Hz. Their ordinary motion no longer advances the reliable item-outcome revision. Launch, impact, expiry, owner removal, changed inventory, Oil, mines, tuning and other outcomes still publish complete reliable item state. Recovery always captures live authority, including exact current projectile positions and lifetimes; it never captures the client's presentation sample or an older reliable publication.

Between reliable boundaries, version-one `TJ` samples carry session, exact reliable item-publication revision, tick, and the existing projectile IDs with current position, velocity, remaining ticks and optional arc progress. At most sixteen projectiles fit in 668 bytes (28-byte header plus 40 bytes each). They remain at 60 Hz to preserve the previous positional update cadence, but use unreliable delivery and omit the duplicate world, inventory, Oil, pickup and outcome state. No additional motion packet is sent on a reliable item-publication tick. Simultaneous Nitro or other item outcomes can still require reliable full publications.

The receiver validates the current host/connection/epoch, exact reliable membership baseline, count, IDs, finite bounded velocities, lifetime progression and analytic arc consistency. Only newer motion ticks reach existing projectile visuals and travel audio. Samples cannot create/remove an entity, apply damage, spend inventory, advance vehicle prediction or replace a checkpoint baseline. A lost sample holds the displayed pose until another valid sample or reliable boundary arrives; no motion backlog or extrapolated hit is introduced. Samples arriving before their reliable launch or after removal fail the baseline guard. Presentation is still stepped at received samples, without adding an interpolation delay. Long unreliable outages can freeze a flying visual while reliable outcomes remain correct.

Nitro percentages, Machine Gun ammunition/outcomes and moving Proxy Mines retain their reliable publication behavior. Untouched Oil remains event-driven with exact prior-revision reuse. These paths preserve exact committed resources, scoring and existing recovery bounds; the separate motion sample is intentionally limited to continuing ballistic projectiles.

`ItemSlot` is the existing per-player publication record extended with a second physical item/token, active index (0 or 1), and life-scoped selection watermark. Missing records mean two empty slots with slot 0 selected. Empty selected inventories can be published before any grant. Both tokens are independently validated, retired on consumption and bounded by the same match token high-water mark on authority restore. The record remains owned by `ItemAuthority`; checkpoints embed it through the same item codec.

Reliable selection commands carry match generation, life and a monotonic switch count, never a player identity. The host resolves the sender and enforces the same participation gate as use. Duplicate/older counts are rejected; skipped counts preserve toggle parity, permitting continuation after a locally submitted command was rejected during a phase transition. Client retains only pending command order to target an already-known capability when switching then immediately using before confirmation; the HUD still displays confirmed selection and ownership. Checkpoint installation discards pending command bookkeeping. Accepted use binds the active capability at acceptance, so a subsequent switch before the fixed step cannot retarget that use. One pending use per player per step remains the bound.

`Core.Items.ItemAuthority`, owned by `HostVehicleSession`, owns slots, match-unique grant tokens, pending uses, projectiles, persistent Oil patches, magnetic Proxy Mines, entry latches and committed events. Use requests identify the arena generation, vehicle life and exact grant token. The host resolves player identity from the actual connected sender; clients send neither a target player nor damage, healing, launch pose or velocity. Invalid generation, departed/destroyed player, wrong life/token, empty slot and duplicate pending use are rejected. Clearing a slot keeps its token retired; regranting uses a new token, so a delayed request cannot spend the replacement item. Removal and explicit development resets clear stale ownership. Death clears the held item by default in the lethal batch; the optional retention policy transfers it to a fresh life/token on automatic respawn. Inactive players cannot use retained inventory.

Core evaluates item candidates against the complete observation batch, queues repairs/damage through the existing vehicle authority, and commits inventory/projectile changes only after the world step succeeds. Core never references Godot. The Client host supplies segment hit fractions from native ray queries and applies Core-calculated prop impulses. Network vehicle observations apply retained central impulses to velocity before the next sweep using accepted host mass and speed bounds (canonical defaults: 900 kg and 65 m/s). Client prediction can reconstruct those physical effects but cannot originate authoritative item outcomes.

`ItemCodec` uses `TI` magic, version twenty-one and bounded binary messages up to 64 MiB, transferred in native-sized chunks when required. Complete publications contain increasing revision, the current-generation vehicle snapshot, at most eight per-player inventory records, each containing two physical slots and selection, at most 16 projectiles and at most 64 use/impact events, at most 16 Proxy Mines, the complete lifetime-bounded Oil patch set, consumed pass counts and current overlap latches, and the complete configured spawn state (up to 27 markers) when a layout is registered. Older item envelopes are rejected; peers must run the same item protocol version. Clients accept them only reliably from the established host with a newer revision in the current arena. They update item presentation once per publication; vehicle snapshot tick ordering prevents a delayed reliable outcome from rewinding a newer world snapshot. Reliable use requests and complete ownership/outcome publications guarantee delivery through the existing ordered transport. Projectile launches, impacts, expiry, owner removal and speed edits publish reliably. Continuing Missile/Salvo motion instead uses the bounded presentation samples described below. Scene reconstruction on lobby return starts a fresh authority and token space under a new arena generation.

## Presentation and Assets

Client `ItemPresentation` reconstructs the Kenney Weapon Pack rocket mesh with a project-created dark metallic orange-emissive `StandardMaterial3D`. The pickup marker retains its rust-colored bright material; selected inventory is mounted on the Car rack; Nitro rides the hydraulic lift as a physical jet and extends once raised. Kenney Particle Pack fire, smoke and spark textures drive GPU particle launch/impact effects and a world-space `GpuParticles3D` trail. The parameter-controlled `DamageFlash.gdshader` flashes the chassis on confirmed HP loss. [Arena audio](audio.md) separately consumes confirmed item outcomes for missile fire/travel/impact/explosion and Wrench use; new grant tokens drive distinct pickup sounds. These effects have no collision or HP authority. All presentation nodes are owned by the arena and removed on teardown; transient bursts have bounded lifetimes.

`assets/items/sources.json` records source URLs, archive/file hashes, selected files, CC0 licenses and the original author's Weapon Pack mirror. Native materials and the damage shader are project-created; no plugin or optional dissolve shader is introduced.

## Verification and Limits

Core tests cover repair/clamping/full-health consumption, invalid/duplicate/stale requests, two-slot capacity, player isolation, empty selection, ordered switching and active-capability use, destroyed/departed players, straight launch velocity, lifetime, monotonic radial damage/impulse, exact-center safety, one impact per projectile, atomic rejected collision queries and reliable codec corruption. Driver tests check trusted host/reliable delivery and reject forged or duplicate publications without predicting consumption.

`check-items.ps1 -GodotPath <Godot .NET executable>` runs eight actual UDP peers in isolated native worlds. It exercises full and partial HP Wrench uses on every peer, repeated requests, the real remote input-use edge, matching launch/impact identities and points on all clients, vehicle distance falloff, native vehicle/prop impulse response, and static-container/perimeter collision. `-Impaired` adds 30 ms outbound delay, 5 ms jitter and 2% loss; `-Visual` renders a client and saves impact images under `.godot/item-checks`. The native prop assertion waits for the next solver steps after confirmed impact. Existing lobby and network-vehicle harnesses cover surrounding session lifecycle and movement regressions.

The native item harness uses one Windows process with eight sockets/worlds; it does not establish multi-machine/NAT compatibility, cross-platform deterministic physics or long-session performance. Projectiles are swept points, the radial blast does not use line-of-sight cover, and there is no combat-specific client prediction. Reliable projectile movement may pause visibly under delayed delivery. Reconnect uses the complete current item state in the session resume checkpoint described in [reconnection](reconnection.md).

See [reconnection and session resume](reconnection.md) for authenticated grace, rebind and checkpoint semantics.

Authority restoration, epoch fencing, checkpoint cadence and migration limits are described in [host migration](host-migration.md).

Host [Developer Options](developer-options.md) updates `ItemAuthority.Configuration` without a second inventory owner. Wrench healing and explosion radius/damage/impulse use current tuning; existing missiles adopt changed speed while retaining direction and remaining lifetime. Lifetime edits affect newly launched missiles. Developer item grants use the ordinary grant path and cannot replace an occupied slot or revive a dead player.

[Feature index](README.md)

The [Event Log](event-log.md) stages use/impact outcomes with the world commit, reports Wrench applied healing, and records acquisition and relevant inventory/projectile removal without per-frame projectile telemetry.

Fresh active admission uses the same complete checkpoint as resume: held slots/tokens, per-slot Machine Gun ammunition/firing phase, current missiles, Oil patches and current overlap latches, and complete Proxy Mine state are included, with historical item events omitted. Preparing or aborting admission does not mutate inventory or create a simulated vehicle. See [session activation](sessions.md#fresh-admission-during-an-arena).

## Sustained Machine Gun resource

[Read this section](items-machine-gun.md#sustained-machine-gun-resource).

## Persistent Oil

[Read this section](items-oil.md#persistent-oil).

## Sustained Nitro resource

[Read this section](items-nitro.md#sustained-nitro-resource).

## Magnetic Proxy Mine

[Read this section](items-proxy-mine.md#magnetic-proxy-mine).

## Arcing Salvo

[Read this section](items-salvo.md#arcing-salvo).

## Vehicle rack presentation

[Read this section](items-rack.md#vehicle-rack-presentation).

## Shield rear armor and persistent health

[Read this section](items-shield.md#shield-rear-armor-and-persistent-health).

## Movable Shield world walls

[Read this section](items-shield.md#movable-shield-world-walls).
