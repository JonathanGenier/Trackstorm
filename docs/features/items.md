# Held-Item Combat

[Shared weapon aiming](weapon-aiming.md) supplies life/token-scoped desired-direction validation, accepted articulation, near-target input friction and the square/corner HUD for direct-fire identities. Its replaceable item message kinds are separate from reliable ownership/outcomes; Salvo stays on its existing path.

## Behavior and Acquisition

Network arenas register Wrench, Missile, Oil, Nitro, Proxy Mine, Salvo, Machine Gun and Shield, with two fixed held slots per player. Acquisition fills the first empty slot without changing selection or replacing either held item. **E** or controller **X / left face button** switches the active slot, including empty slots; use targets only the selected item. Switching and use together in a captured frame switch first. Oil deploys a persistent terrain-aligned hazard. Nitro retains its slot as a percentage resource: hold use to boost, release to preserve charge, and reuse until exhausted. Use **LMB** or controller **Y / top face button** through the remappable **UseItem** action. One press generates one capability-bound request; discrete items do not repeat while held. Nitro and Machine Gun require held input to sustain use; a tap released before the next tick consumes neither charge nor ammunition. Empty-slot requests fail safely. The local HUD and vehicle rack show confirmed selected ownership; remote racks use the same accepted item publication. The host development grant API grants registered items to the living host's first empty slot; they cannot overwrite either occupied slot. The internal all-vehicle grant seam remains for integration fixtures. Normal acquisition happens by driving within range of an available arena pickup, as described in [item spawning](item-spawns.md). Local rigid-body practice retains its generic blast demonstration on the same developer page.

Wrench repairs 500 HP by default, clamped to the vehicle's existing maximum. A living player can always consume it at full health: zero effective healing still clears the slot and produces a confirmed use outcome. It cannot revive a destroyed vehicle.

Missile launches from the host-observed vehicle center along its current forward direction at the hosted default of 120 m/s. Camera-to-turret firing integration remains a separate aiming integration; the traversal rule consumes the resulting launch velocity without owning camera input or target selection. The collision adapter excludes the owner's body and sweeps each complete 60 Hz segment, including launch, against vehicles, world geometry, props and perimeter walls. A hit creates one radial explosion at the hit fraction. It remains non-homing, with no gravity or direct-hit bonus. At most 16 projectiles can be active; reaching the cap leaves the item held.

The production [Blender Missile and launcher](../../assets/items/missile/README.md) use a captured 1.6-second deployment: trunk/rack first, cradle lift, body extension, then four symmetric fin hinges. Nested front/rear barrels overlap a fixed central shell throughout extension, retaining the compact envelope without an open body gap. Both local input and authority reject early Missile use; an early tap is never queued. Each pickup holds exactly one round. Successful launch leaves an empty cradle, centers articulation, nests the mount, lowers the rack and fully closes the trunk over the captured default 0.9-second return. Another pickup keeps its capability while that complete return finishes. A subsequent Missile captures its deployment start after the authoritative stow boundary. Switching/discard also starts a return; death/life reset clears presentation. Captured deploy/ready/stow ticks travel with inventory in TI22 publications and recovery, independent of transient aim. Late reconstruction conservatively replays the mechanism before enabling local use.

The deployed rack remains fixed while its geared turret follows accepted shared yaw/pitch. The visual model never changes forward launch, terrain probes, collision, damage or explosion behavior. Its independently owned flight presentation smooths accepted samples with at most 100 ms extrapolation, carries orange exhaust, short red paper fragments, dark crimson smoke and red embers, and frees all child emitters when the authoritative projectile disappears. A continuous tapered flame core adds gold/yellow highlights within the orange exhaust. Default confetti uses 56 short-lived 85 mm red paper fragments; total default GPU particle capacity is 148 plus three flame surfaces. Particle budgets are bounded; local tuning is described in [Developer Options](developer-options.md#missile-presentation).

`check-missile-presentation.ps1 -GodotPath <editor> -Visual` exercises production Car/arena deployment, early input, driving, fixture-authored airborne launch/native landing, empty stow with Oil/Missile/Nitro replacement, two real UDP peers and sixteen simultaneous production flight effects. `check-missile-terrain.ps1` remains the terrain/collision/latency regression harness.

Standard missiles receive at most four bounded host-only vertical terrain probes per step: beneath the projectile and at quarter, half and full look-ahead distances along its current velocity. Only authored static drivable terrain is suitable; vehicles, unauthored props, water and excessive slopes cannot attract a missile. Core changes pitch only, preserving horizontal heading and speed. Proximity fades correction continuously, with a bounded first-order response, a hard pitch-rate cap and a limit on requested terrain pitch. There is no binary launch-angle classification. Steep skyward shots naturally have no viable forward support; steep downward shots impact before limited correction can rescue them.

A missing current support sample leaves current velocity unchanged. Forward samples reject excessive drops beyond local tangents; nearer samples can anticipate an actual rise when the far probe overshoots a short bank. A distant downslope cannot request a pitch below the current supporting tangent and clearance response, preventing early descent through a still-flat tabletop. Actual rising support can sustain upward correction even when all forward probes are beyond the road edge. Cliffs therefore produce committed airborne flight rather than a dive toward a lower platform. Nearby feasible support can engage correction again, repeatedly, without teleporting or steering toward a vehicle. Only position, velocity and remaining ticks are continuation state; existing reliable publications, motion samples and complete resume/migration checkpoints preserve them. Clients render host samples and never query terrain to steer a projectile. Salvo retains its separate fixed arc.

Missile tuning lives in the dedicated [Missile configuration category](developer-options.md#missile-traversal-and-cleanup). Initial defaults are 1 m clearance, 42-degree requested pitch, 210 degrees/s maximum pitch rate, 24/s response, 30 m forward sensing, 10 m forward vertical range and 3 m reacquisition height. Actual car-hit clearance varies during limited-rate ramp transitions. New projectiles use the smaller of the existing 300-tick budget and the additional 60-second cap; ordinary default misses still expire after five seconds. Lifetime expiry and departure beyond the 4096 m world-coordinate bound retire without generating impact damage. Existing death/departure and arena teardown cleanup remain in place.

The hosted Missile explosion linearly fades from 300 HP and 15,000 N·s at the center to zero at/outside a 5.06-metre radial reach: one production Car collision-hull length (`VehicleDimensions.Length`), rather than a full diameter. `ItemConfiguration` validates configurable healing, speed, lifetime, radius, maximum damage and impulse; the zero edge is the falloff curve's defined minimum. Vehicles and movable objects use the same Core distance/direction helper, including a stable upward direction at the exact center. Every living vehicle in range receives one radial effect, including the owner; walls do not occlude the radial blast. Old Map demonstration props receive the host-calculated physical impulse without HP. Production dressing rocks and plants consume the same committed Missile/Salvo impacts through [destructible environment authority](destructible-environment.md). Secondary native collisions can still cause the existing collision damage. Salvo retains its independent radius and damage rules.

Only a reliable confirmed Missile collision creates the dedicated impact effect. A short, lobed orange/gold fire bloom stays inside the shared blast envelope, followed by sparse red, gold and ivory-white sparks expanding to a default 2.25 Car lengths (11.385 m) radially. A faint, short smoke layer leaves vehicles, hazards and HUD visible. Decorative fragments use deterministic token-seeded radial directions, varied drag/lifetimes and downward-curving paths. Short tapered spark meshes align with actual travel, shrink and cool from a hot initial burst, then burn out independently; they have no collision, damage, impulse, terrain queries or secondary impacts. Each default effect owns seven shared-mesh fire lobes, one 160-instance fragment batch and 18 smoke particles. Local density caps fragments at 320. At most 32 effect owners coexist; additional impacts replace the oldest cosmetic owner. Effects expire at their captured local duration or arena teardown. Lifetime/world-bound projectile retirement produces no impact. Local tuning is independent of the authoritative blast control; saved host radius overrides remain valid until edited/reset.

`check-missile-explosion.ps1 -GodotPath <editor> [-Visual]` exercises native production Missile impacts and reliable agreement across two UDP peers, latency/jitter/loss, native DevTools editor signals, simultaneous/sustained presentation fixtures, duration and cleanup. Cosmetic fixture events are separate from real native collision evidence. It supplements the existing item and Missile terrain harnesses.

## Permanent selected-slot discard

**X** on keyboard or controller **D-pad Left** requests permanent deletion of the selected held item. Discard uses the remappable `DiscardItem` action and one captured press edge. The host clears only that physical slot immediately on acceptance; selection and the other slot's identity, capability and resources remain unchanged. Empty-slot discard has no item effect. It creates no pickup, projectile, hazard, world wall, use sound or use outcome. Existing deployed entities are unaffected.

The existing `ItemAuthority` validates sender-owned vehicle, participation, arena generation, life, selected grant token and exact selection command watermark. Accepted deletion retires the capability, clears that slot's charge/ammunition/Salvo state, cancels only its pending use, disengages sustained use and removes its attached Shield pool. The same reliable publication updates HUD, rack and remote representations. A remote player sees confirmation after network delivery; there is no predicted inventory. Switching and discard in one frame select first; discard precedes use, so the deleted capability cannot fire or deploy. A delayed command for a replacement grant, former selection boundary or old life fails closed.

`DiscardRevision` is a monotonic deletion watermark on the existing item authority/publication, serialized in item protocol 21 and nested complete checkpoints. It is not another inventory. Resume and ordinary publication reject a regressed watermark. Migration excludes retained checkpoints before either the currently confirmed watermark or a newer retained checkpoint's watermark; when no safe common checkpoint remains, recovery fails closed. This preserves confirmed deletion rather than rolling it back. A request interrupted before authoritative confirmation is not a committed client outcome; as with other host-authoritative requests, remote deletion is confirmed by the reliable boundary.

`check-input.ps1` exercises native synthetic defaults, short taps, suppression, remapping and saved preferences. `check-items.ps1` exercises both selected slots, empty/repeated discard and switch/discard ordering on eight native UDP peers. `check-death-respawn.ps1`, `check-reconnect.ps1` and three-player `check-migration.ps1` include deleted-slot continuation; Core and transport tests additionally exercise delayed/foreign requests, stale publications, sustained-use cancellation, both death retention policies and stale recovery rejection. Physical controller ergonomics and Internet/EOS acceptance require separate device evidence.

## Registry and extension boundaries

`ItemRegistry` is the single immutable roster in stable selection order. Each `ItemDefinition` connects the stable byte identity, behavior/tuning key, display and presentation identity, default within-category spawn weight, category identity, and audio/VFX hooks. `None` is only an empty-slot sentinel. Wrench, Missile, Oil, Nitro, Proxy Mine and Salvo use internal stateless handlers to stage effects into the existing authority transaction; Machine Gun uses the same authority transaction through its sustained short-range ray path. The vehicle world remains the sole repair/damage authority. Damaging definitions register their authoritative `DamageSource` identity for the shared [Circus item-damage conversion](matches.md#circus-combat-score); weapon handlers never award points. Nitro stages a boost intent into that transaction, and Oil stages placement there as well; there is no second inventory, spawn authority or item envelope.

`ItemSpawnConfiguration.Weights` is an immutable map keyed by registered identities. Developer tuning keys are generated from registry keys and travel through the existing version-forty-six gameplay configuration codec. The item codec uses version twenty-two; checkpoint envelopes compose these existing codecs without a parallel serialization path. Identical game versions remain required.

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

`ItemCodec` uses `TI` magic, version twenty-two and bounded binary messages up to 64 MiB, transferred in native-sized chunks when required. Complete publications contain increasing revision, the current-generation vehicle snapshot, at most eight per-player inventory records, each containing two physical slots and selection, at most 16 projectiles and at most 64 use/impact events, at most 16 Proxy Mines, the complete lifetime-bounded Oil patch set, consumed pass counts and current overlap latches, and the complete configured spawn state (up to 27 markers) when a layout is registered. Older item envelopes are rejected; peers must run the same item protocol version. Clients accept them only reliably from the established host with a newer revision in the current arena. They update item presentation once per publication; vehicle snapshot tick ordering prevents a delayed reliable outcome from rewinding a newer world snapshot. Reliable use requests and complete ownership/outcome publications guarantee delivery through the existing ordered transport. Projectile launches, impacts, expiry, owner removal and speed edits publish reliably. Continuing Missile/Salvo motion instead uses the bounded presentation samples described below. Scene reconstruction on lobby return starts a fresh authority and token space under a new arena generation.

## Presentation and Assets

Client `ItemPresentation` reconstructs the Kenney Weapon Pack rocket mesh with a project-created dark metallic orange-emissive `StandardMaterial3D`. The pickup marker retains its rust-colored bright material; selected inventory is mounted on the Car rack; Nitro rides the hydraulic lift as a physical jet and extends once raised. Kenney Particle Pack fire, smoke and spark textures drive GPU particle launch/impact effects and a world-space `GpuParticles3D` trail. The parameter-controlled `DamageFlash.gdshader` flashes the chassis on confirmed HP loss. [Arena audio](audio.md) separately consumes confirmed item outcomes for missile fire/travel/impact/explosion and Wrench use; new grant tokens drive distinct pickup sounds. These effects have no collision or HP authority. All presentation nodes are owned by the arena and removed on teardown; transient bursts have bounded lifetimes.

`assets/items/sources.json` records source URLs, archive/file hashes, selected files, CC0 licenses and the original author's Weapon Pack mirror. Native materials and the damage shader are project-created; no plugin or optional dissolve shader is introduced.

## Verification and Limits

Core tests cover repair/clamping/full-health consumption, invalid/duplicate/stale requests, two-slot capacity, player isolation, empty selection, ordered switching and active-capability use, destroyed/departed players, straight launch velocity, lifetime, monotonic radial damage/impulse, exact-center safety, one impact per projectile, atomic rejected collision queries and reliable codec corruption. Driver tests check trusted host/reliable delivery and reject forged or duplicate publications without predicting consumption.

`check-items.ps1 -GodotPath <Godot .NET executable>` runs eight actual UDP peers in isolated native worlds. It exercises full and partial HP Wrench uses on every peer, repeated requests, the real remote input-use edge, matching launch/impact identities and points on all clients, vehicle distance falloff, native vehicle/prop impulse response, and static-container/perimeter collision. `-Impaired` adds 30 ms outbound delay, 5 ms jitter and 2% loss; `-Visual` renders a client and saves impact images under `.godot/item-checks`. The native prop assertion waits for the next solver steps after confirmed impact. Existing lobby and network-vehicle harnesses cover surrounding session lifecycle and movement regressions.

The native item harness uses one Windows process with eight sockets/worlds; it does not establish multi-machine/NAT compatibility, cross-platform deterministic physics or long-session performance. Projectiles are swept points, the radial blast does not use line-of-sight cover, and there is no combat-specific client prediction. Replaceable projectile motion samples may pause visibly under delayed delivery; reliable membership and outcomes still retire the projectile. Reconnect uses the complete current item state in the session resume checkpoint described in [reconnection](reconnection.md).

See [reconnection and session resume](reconnection.md) for authenticated grace, rebind and checkpoint semantics.

Authority restoration, epoch fencing, checkpoint cadence and migration limits are described in [host migration](host-migration.md).

Host [Developer Options](developer-options.md) updates `ItemAuthority.Configuration` without a second inventory owner. Wrench healing and explosion radius/damage/impulse use current tuning; existing missiles adopt changed speed while retaining direction and remaining lifetime. Lifetime edits affect newly launched missiles. Developer item grants use the ordinary grant path and cannot replace an occupied slot or revive a dead player.

[Feature index](README.md)

The [Event Log](event-log.md) stages use/impact outcomes with the world commit, reports Wrench applied healing, and records acquisition and relevant inventory/projectile removal without per-frame projectile telemetry.

Fresh active admission uses the same complete checkpoint as resume: held slots/tokens, per-slot Machine Gun ammunition/firing phase, current missiles, Oil patches and current overlap latches, and complete Proxy Mine state are included, with historical item events omitted. Preparing or aborting admission does not mutate inventory or create a simulated vehicle. See [session activation](sessions.md#fresh-admission-during-an-arena).

## Sustained Machine Gun resource

Every newly acquired Machine Gun starts with the configured discrete ammunition capacity (800 rounds by default) in its physical held slot. Holding **UseItem** after an accepted capability-bound use sustains fire; releasing, switching slots, losing input ownership or disconnecting stops further shots while preserving the exact remaining rounds and fractional firing phase. At the default 80 rounds/second (4,800 RPM), 800 rounds deplete in approximately 10 seconds of actual firing. Exhaustion clears only that physical slot through the ordinary item lifecycle.

The host constructs each shot from the current authoritative vehicle position/orientation and deterministic spread, then uses the Client host's closest native ray hit along the configured maximum range. Static world geometry terminates the ray; valid living vehicle hits stage low damage and low impulse through the existing authoritative vehicle-damage transaction with the firing vehicle as instigator. Defaults are 225 m maximum range, falloff beginning at 12 m, 2.25 HP per round and 8 N·s knockback. Each shot samples a direction inside a 6-degree half-angle (12-degree full) cone from the current firing orientation; there is no homing, drift or mid-flight steering. The widening cone rewards tracking within the intended 5–15 m effective range; near maximum range it produces spray and occasional hits. Exponent-1.5 damage falloff reaches zero at 225 m. Perfect close-range hits total 1800 HP per default magazine against a 1000 HP vehicle; this is initial runtime tuning, not a health-derived balance rule. Confirmed bullet impacts create six short-lived orange sparks at the hit point on vehicles and scenery, independently of tracer frequency. The existing presentation burst cap bounds catch-up work; particles fade in 0.22 seconds and their nodes retire at 0.3 seconds. They scatter back along the incoming ray because the publication does not carry a surface normal. They are cosmetic and do not replay from recovery checkpoints. Machine Gun applied rival HP loss uses the shared Circus item-damage conversion and K/D authority.

Confirmed shot outcomes drive bounded tracer/audio presentation. The HUD shows authoritative remaining magazine percentage and never predicts ammunition. Remaining rounds, acquisition capacity and fractional firing phase serialize in item protocol version twenty-two and survive fresh admission, reconnect/resume and host migration without replaying historical shots. Developer Options expose capacity, fire rate, range, damage, falloff start/exponent, spread, knockback and tracer frequency.

`check-machine-gun.ps1 -GodotPath <exe>` is the feature-specific native harness for sustained host/remote firing and short-range native rays. Core tests cover cadence, depletion/reuse, falloff/attribution, invalid ray observations, tuning, two-slot state and checkpoint continuation. These are local-machine checks, not authenticated EOS/WAN/multi-device or long-session balance evidence.

## Persistent Oil

The ordinary registered handler deploys a three-metre-radius patch four metres behind the host-observed vehicle. Client supplies a native ground ray and sixteen footprint support samples. Core validates the numeric placement and commits it only with a successful world step. The surface normal must be driveable (Y at least 0.55). Missing ground, movable supports, ledges, sharply changing normals or more than six centimetres of deviation across the planar footprint reject placement; the exact held slot remains available. Banking is supported without a flat-world assumption. This intentionally accepts a supported local plane rather than bridging discontinuous terrain.

Patches use their consumed grant token as a match-unique ID. They survive deployer death, respawn, disconnect and permanent departure. Deployment has no global active-patch gate: a valid supported placement consumes the held Oil even when other patches exist. Each new patch captures the configured vehicle-pass budget (default two) and an absolute cleanup deadline (default sixty seconds). Changing either setting affects future deployments. Finished clears patches and their overlap latches; disposal/rematch starts a fresh authority.

Core contact detection projects the supported vehicle centre onto the patch plane, with a 0–1.6 m support-height window and aligned support normal. Airborne vehicles and different road levels are excluded. Supported overlap refreshes the recovery timer. Each uninterrupted overlap consumes one successful pass; leaving and returning consumes another, even for the same vehicle or the owner. Owner passes score zero. The patch retains its consumed pass count separately from current per-life overlap latches. Death, reset or departure releases a latch without refunding prior passes. A new lethal/reset candidate cannot consume a pass. The patch disappears at the committed boundary when its captured pass budget is spent, or its cleanup deadline is reached.

Oil adds no yaw impulse and never changes heading directly. It reduces lateral tire force by ten percent at the default tuning, while retaining longitudinal force, ordinary steering, countersteering and physical angular momentum. Spins can only emerge through the normal vehicle forces and observations. After leaving all supported Oil contact, the reduction declines linearly to zero over the configured recovery duration (default 0.5 seconds). Re-entry refreshes the reduction; overlapping patches do not multiply it. Movement snapshots retain the recovery timer for prediction/restoration; death/reset clears it and retuning rescales its remaining duration. Prediction continues the last confirmed recovery memory; only the host confirms overlap and lifecycle changes.

Reliable item state and nested resume/migration checkpoints contain patch ownership, geometry, absolute deadline, captured pass budget, consumed pass count and current overlap latches. A deadline does not change each tick, so untouched patches do not cause new item publications merely because time passed. State is installed only after complete decoding and authority validation. Large reliable boundaries use the Client [bounded chunk transfer](transport.md#large-reliable-boundaries); patches are never evicted to fit a native packet. Allocation guards derive from the maximum lifetime and eight item uses per fixed step (288,000 patches at 600 seconds), rather than a gameplay deployment cap. This upper bound is a safety limit, not a performance claim for extreme settings.

Each successful enemy pass banks 50 base points times the owner's current K/D through [match scoring](matches.md#oil-trigger-scoring), preserving retained-owner credit. Owner passes and continuous overlap bank zero; a new enemy pass after exit banks again. Recovery never replays historical triggers.

`F1 → Configs → Oil` exposes grip reduction, recovery seconds, vehicle passes before removal and maximum lifetime through the existing host transaction, persistence, replication and category reset. See [Oil tuning](developer-options.md#oil-tuning).

`check-oil.ps1 -GodotPath <exe>` exercises native banked placement, real local UDP peers, late admission, owner/peer handling, recovery, repeated overlap/re-entry, repeat-pass scoring/removal, simultaneous patches and large complete publications/checkpoints. `-Impaired` adds delay/jitter/loss; `-Visual` captures the banked hazard. Reconnect and migration fixtures retain 1,500 detached patches plus a partly consumed pass budget to isolate recovery from separately exercised deployment. These are local checks, not Internet/EOS multi-device evidence.

Unchanged Oil in ordered item updates can reference the preceding publication revision. Standalone checkpoints always contain complete patch/contact state; joins and resume establish a fresh full baseline before references resume. This avoids resending every patch during unrelated projectile and cooldown updates.

## Sustained Nitro resource

Every newly acquired Nitro has 100% charge on its physical `ItemSlot` entry. The first and second slots have independent binary64 percentages. Only item authority drains charge, once per committed 60 Hz step; release preserves the exact remainder and zero clears that physical slot through the normal ownership transaction. Charge is bounded to (0,100] while held, and exactly zero for other/empty items. Switching disengages the current resource and requires a new use for the newly selected item; acquisition never resets another slot's charge.

A reliable use still names the exact life/grant capability. Remote presses additionally identify their originating input sequence so a fast re-press cannot be consumed by a preceding held/release frame. Item authority waits at most fifteen ticks for that input boundary, then uses the host-consumed held state. Duplicate pending requests cannot multiply consumption. Missing input becomes neutral through the ordinary input timeout; suspension immediately discards pending commands and neutral input ends sustained use on the next authority step. Charge is never refilled by notification, release, reconnect or migration. Live consumption, thrust and cap edits apply to subsequent steps without repricing retained percentage.

Defaults are 20 percentage points/second, 18,000 N forward rocket thrust, 1.4× forward speed limit, full airborne thrust, and 3 m/s² overspeed recovery. Runtime controls are `items.nitro_consumption_per_second` (2–6000), `items.nitro_forward_thrust` (0–100,000 N), `items.nitro_speed_multiplier` (1–2), `items.nitro_airborne_thrust_scale` (0–1), and `vehicle.overspeed_deceleration` (positive, subject to normal vehicle bounds). Thrust accelerates along the chassis forward direction at force/mass, independently of throttle, wheel traction and surface drive modifiers. It propels from rest or while coasting, combines with ordinary forward drive, and opposes reverse drivetrain force without reversing itself. In flight the configured fraction scales the same chassis-directed force; zero disables it. Only added forward velocity is bounded by the effective drive cap and the existing absolute physics safety bound; existing momentum is not clamped. Charge drains by activation time even if thrust is zero or the cap prevents further acceleration.

Releasing or exhausting Nitro immediately removes rocket thrust and the boosted cap. If horizontal speed remains above the normal cap, movement retains an inactive recovery continuation and removes only excess speed at the configured per-second rate, preserving horizontal direction and vertical motion. Ordinary braking/coasting can also slow the car. Recovery ends at the normal speed range; ordinary external impulses without a preceding Nitro episode retain their existing behavior. [Match scoring](matches.md#nitro-overspeed-scoring) awards actual authoritative overspeed, including this release interval, rather than held input or active-effect duration.

Complete item publications/checkpoints carry both percentages, the engaged capability and the selected Nitro deployment countdown. Selection/acquisition starts 36 fixed ticks (0.60 seconds); no thrust or fuel consumption occurs while any remain. Holding an accepted use begins thrust at readiness; releasing during deployment cancels engagement without spending fuel. Deployment continues while selected, so already-deployed reuse is immediate. Switching away cancels the gate and switching back starts a fresh deployment. Recovery preserves the remaining ticks rather than restarting or bypassing them. Client reconstructs the same bounded mechanical sequence, including when inventory and movement arrive on separate channels. The existing 127-byte movement payload carries an upper bound on predicted remaining boost ticks plus force, cap multiplier and airborne fraction; it does not own charge. Release clears predicted boost immediately. A canonical zero-tick, zero-force/airborne-fraction and unit-speed-multiplier state denotes inactive momentum recovery; all-zero denotes no episode. Authority refreshes the budget from current charge while sustained. Version-seven movement, version-twenty-one items and version-forty-one configuration reject old schemas without a parallel protocol. Exact game-version admission remains required.

Death/new-life reset and Finished clear the vehicle effect; inventory follows the existing death-retention policy. Retained inventory transfers partial charge with its fresh life tokens and no engaged use. Resume and migration install the selected boundary without historical use or score replay, then neutral/rebound input determines whether use can continue. Epoch rollback retains its existing checkpoint semantics.

Both local HUD slots use the shared [dynamic HUD assembly](hud.md) to show registry identity and their independent authoritative charge percentage (rounded upward to a whole percent while nonempty), with EMPTY and no resource presentation at exhaustion. A cyan exhaust underline in the selected Boost cell reflects active movement independently of remaining fuel. The layered [Boost exhaust](boost-exhaust.md) also has a bounded release fade and a distinct confirmed-depletion burst. Stats shows both charge values, activation and recovery. Existing assets/audio hooks and item ownership boundaries remain in place.

`check-nitro.ps1 -GodotPath <exe> [-Impaired] [-Visual]` exercises two native UDP peers, partial release/reuse, zero-charge disposal, second-slot isolation, boosted speed, bounded recovery, and overspeed scoring before/after release. HUD checks render independent percentages at supported resolutions. Reconnect/migration fixtures preserve a partial resource alongside the existing complete-world checks; Core tests additionally replace authority during active use and verify neutralization and reuse.

Rocket force does not cancel on wall contact. Collision response remains with the environment/vehicle adapter: scrape resistance must remain dissipative, with thrust able to overcome it; direct collision resolution still removes normal momentum and feeds the existing damage path. Nitro introduces no wall steering assist or wall-specific force.

The obsolete `items.nitro_acceleration_multiplier` saved key is no longer applied (retained as an unknown key by existing persistence); missing forward-thrust/airborne values use canonical defaults. No conversion from an engine multiplier to a physical force is assumed.

## Magnetic Proxy Mine

Proxy Mine is a Droppable in the ordinary category-balanced pool, sharing Droppable allocation with Oil. Its original project-authored HUD silhouette and [Blender-authored production model](../../assets/items/proxy-mine/README.md) resolve through existing presentation metadata. The 1.3 m-wide armored body uses red/ivory circus panels, a teeth band, recessed magnetic windings and a guarded red beacon. `ProxyMineVisual` instances the same cached GLB for the world and Car rack, sharing body meshes/materials but owning each lens pulse material. `MatchResourceLoader` prepares it with match resources. The existing 0.8-second blink period, 0.22-second bright phase and red light remain unchanged. The attraction radius has no visual mesh. A confirmed detonation creates a Client-owned `ProxyMineExplosion` at the authoritative Mine position: a diffuse, irregular ground-hugging pressure/dust front, a broad earthy dust burst, flying cosmetic clods, and a dust plume whose linear dimensions and rise are three times the earlier landmine-dust version before it dissipates. No fire or spark presentation remains. The pressure front is ray-aligned to static terrain beneath the Mine. A separate `ProxyMineScar` aligns to static terrain beneath the impact and fades over 60 seconds; it never changes terrain geometry, collision or gameplay. The wave is cinematic, not a damage or knockback radius: authoritative knockback affects only the contacted vehicle. `MatchResourceLoader` prepares the retained smoke texture and pressure-wave/scar shaders. Dust bursts free after 3.8 seconds and are capped at the authoritative sixteen-Mine count. Scars are capped at 64 for bounded repeated impacts, replacing the oldest cosmetic mark if necessary. The existing impact audio hook remains tied to committed detonation. Stats reports match mine count, including unfinished placements; Event Log records use, impact and the ordinary attributed health outcome.

The authored origin remains the collider centre: visual radius is at most 0.65 m and the base is -0.25 m; the cosmetic beacon rises to +0.348 m. The imported models contain no collision or simulation components. Accepted position/support-normal updates remain the sole deployed pose source. Existing physics supplies sliding, gradual support tilt and gravity, with no spin state; presentation does not invent rolling, bobbing or attraction telegraphs.

The selected mine sits at full world scale on top of the existing weapon rack. Its Blender-authored folding arm mounts underneath. `ProxyMineRack`, created by the existing `RackItemVisual`, owns two telescopic links, a leveling wrist and opening jaws; `CarRackPresentation` retains ownership of the deck/rack and selection. The arm reaches around the rear edge, grips from above, lifts the mine clear, lowers it onto the road, opens its jaws and folds underneath. Ground release hides the carried visual as `ItemPresentation` creates the world visual. The empty arm returns in 0.23 s before the rack lowers. A subsequent accepted mine uses its preparation phase to finish an interrupted return.

Accepted use consumes the exact item immediately and starts a **60-tick (1 s)** authoritative placement. The first part permits rack preparation; pickup and lowering follow the replicated timer. `PlacementLife` and `PlacementTicks` in `ProxyMineState` reserve capacity in the sixteen-mine bound. Only one unfinished placement per car is permitted; another mine request while busy retains that item. Switching slots does not cancel accepted placement; the rack finishes the accepted arm action before displaying the latest selection, while other items retain their normal authoritative use rules. A carried mine cannot attract, collide, deal damage or detonate. Each tick queries behind the current host-observed car, so driving changes the eventual placement point. Release requires valid support within the arm's 3.5 m reach, measured from the raised shoulder (vehicle-local 0, 1.10, 1.595 m) to the leveling wrist, 0.54 m above the mine centre along its support normal. Missing or unreachable support holds/retracts the carried mine to its raised transfer pose; the final 17 lowering ticks must run with valid support before release. Death, reset or permanent departure cancels unfinished placement without an explosion; temporary disconnect retains the accepted action. After release, existing match-owned hazard rules apply unchanged.

Deployment projects 4.5 m behind the authoritative vehicle onto native static terrain. A centre ray and eight footprint samples fit position and normal on flat, banked and locally uneven ground. The rigid footprint accepts up to 0.22 m variation, with a bounded 0.12 m clearance search for crests between samples. Unsupported cliffs, missing ground, excessive discontinuities, unreachable support and obstructed placements at initial use retain the exact held item. The installed collider centre is 0.25 m above the fitted support plus clearance. Ground release starts the existing 30 fixed seating ticks, then the mine stays dormant at rest until an in-range vehicle supplies magnetic force; vehicle contact can detonate it during seating. Once moving, momentum, gravity and drag continue even when the target leaves the field.

Core selects the nearest living, non-reset vehicle from the complete observation batch, including the owner, with stable vehicle-ID tie breaking. For distance `d`, radius `r`, and `p = clamp(1 - d/r, 0, 1)`, force is `min(1, 10*p)*minimum + (maximum-minimum)*p^falloff`. The outer ten percent ramps the weak minimum field continuously from zero; close-range force approaches the configured maximum. There is no fixed-force switch, homing teleport or attachment. Force divided by the mine's 20 kg mass, gravity and viscous drag advance velocity at 60 Hz, bounded at 40 m/s. The host's native cylinder sweep/slide adapter resolves terrain and vehicle contacts; support orientation follows gradually, using normalized linear interpolation for nearly identical normals to avoid a degenerate rotation axis as alignment converges. Like the network vehicles, motion uses Core integration plus synchronous native queries rather than an independently authoritative native rigid body. No target or solver cache must be restored.

Contact or drive-over produces one detonation, removes the mine, and applies damage plus an outward/upward central impulse only to the contacted vehicle. Defaults are 250 HP (25% of hosted default health) and 18,000 N.s. Damage uses `VehicleEffectRequest` and `DamageContext("proxy-mine", owner, "contact-detonation")`; actual applied HP, life, tick and damage sequence remain owned by vehicle health. Existing kill attribution recognizes lethal mine damage. Actual rival HP loss contributes through the shared authoritative Circus item-damage path; this item has no independent score rule.

Host Configs exposes `items.mine_damage`, `items.mine_attraction_radius` (30 m), `items.mine_minimum_force` (1000 N), `items.mine_maximum_force` (2000 N), `items.mine_falloff` (2.5), and `items.mine_knockback`. Changes affect subsequent steps through normal validation, persistence and complete configuration replication. Registry-generated `spawns.proxy_mine_weight` defaults to one. Minimum force cannot exceed maximum force; zero force/damage/knockback is supported.

The match owns at most 16 mines, including unfinished placements; a full bound retains the held item. Released mines survive deployer death, respawn, disconnect and permanent departure. Complete ID, owner, position, velocity, surface normal, seating timer, placement life and remaining placement ticks travel in item protocol version twenty-two and nested admission/resume/migration checkpoints. Host restore validates IDs against the grant-token high-water mark; a placing mine must reference its living owner's current life. Recovery replaces state without historical impact replay. The Finished commit clears all remaining mines; a new match creates an empty authority. Mine motion and placement retain reliable 60 Hz item publication and may pause under delayed delivery; presentation cannot author damage or attraction.

`check-mine.ps1 -GodotPath <exe> [-Impaired] [-Visual]` exercises real local UDP peers and native flat, banked and uneven geometry, stable seating, outer/mid/close force response, a moving vehicle, drive-over, exact damage/impulse, repeated remote use, replication and fresh late join during arm placement. The harness also deploys sixteen mines through ordinary use, validates shared geometry, independent beacon materials and collision-free visuals, then exercises magnetic motion and distinct committed detonations through the production chase camera, plus two simultaneous car placements while driving. Its final fixture brings two existing authoritative Mines into one vehicle in quick succession and checks distinct impacts, concurrent effects within the Mine cap, peer removal and full effect cleanup. An isolated Client presentation stress sequence feeds 73 sequential impact publications, checks that the pressure front and scar follow native bank support, and checks the sixteen-burst and 64-scar limits, ten seconds of continued transient cleanup and scar expiry at the scheduled minute age without changing authority. `-Visual` saves arm pickup/release, close, multi-mine, close detonation, overlap, smoke ascent/dissipation and chronological chase captures. Native reconnect and migration harnesses seed an isolated moving mine to test recovery separately from deployment. Core tests cover capability/cap guards, atomic failures, configuration, malformed state, continuation and terminal cleanup. These fixtures do not establish Internet/EOS multi-PC performance or competitive balance.

## Arcing Salvo

Salvo is a Weapon in the existing registry (wire identity 6, alongside Proxy Mine identity 5). Each pickup holds five individually fired shots by default. Each separate UseItem press fires one missile and decrements only that physical slot; holding never repeats. A thirty-tick (0.5-second) minimum cooldown rejects early presses without spending ammunition or queuing a future shot. The HUD shows the remaining count, and the slot clears after its final shot. Each shot needs one place within the shared sixteen-projectile cap; insufficient capacity or missing target ground preserves ammunition. There is no manual target packet. At each launch the host projects the vehicle's current horizontal forward direction 65 metres onto static ground. Driving and steering change the aim for remaining rounds; each airborne missile finishes its own committed arc. Shots launch three metres above the current host-observed vehicle when their individual requests are accepted. A parabola twelve metres above the launch-to-target chord provides the arc, with mean travel speed 150 m/s. Complete 60 Hz segment sweeps can hit obstacles/vehicles before the intended ground point; an unobstructed final segment impacts the stored target.

Each impact uses a ten-metre blast radius, 250 maximum HP damage and 3,500 N s impulse. Damage and impulse fade with `(1 - distance / radius)^falloff`, default exponent one, reaching zero at the edge. The existing vehicle damage transaction produces distinct actual-damage events per target/round, with source `salvo`, owner attribution and projectile ID in the radial context. Ordinary kill attribution recognizes Salvo. Each applied rival hit contributes through the shared authoritative Circus item-damage path; this weapon has no independent score rule.

The local arena creates only its own circular marker. Selected Salvo and the owner's ongoing salvo display one guide at the current fixed-range forward aim point. It follows the car throughout the salvo rather than leaving rings at earlier launch targets. No marker identity, geometry or visibility is serialized. Both ring edges sample native static ground, follow height and surface normals, and offset slightly above the surface. Unsupported/discontinuous segments are omitted. The unshaded gold ring is designed for the normal chase camera. Remote inventories never create guides. Slot switching, death, Finished, loss of active session and teardown hide the applicable guide; recovery reconstructs it from local identity and current ownership/flight.

Runtime keys `items.salvo_*` cover count (1–16), interval_ticks (1–60), range (25–250 m), launch_height (1–10 m), arc_height (1–60 m), speed (20–200 m/s), blast_radius (1–30 m), damage (0–1000 HP), falloff (0.25–4), impulse (0–50,000 N s), marker_scale (0.5–2 times blast radius), marker_width (0.1–2 m) and marker_lift (0.02–0.5 m). Ordinary Configs, persistence, host validation and configuration version-forty-one carry all values. Count is captured on acquisition; changing it does not refill held ammunition. Each shot captures its cooldown, current forward aim/range, launch height, speed and arc. A rejected shot retains the item and its remaining count. Flying rounds retain their arc; impact and marker properties use current tuning. Straight Missile behavior is unchanged.

Item protocol version twenty-two carries remaining shots and absolute ready ticks in each physical slot alongside flying arcs and owner life. There are no scheduled automatic rounds. Each accepted shot retires its capability and assigns a fresh token to remaining ammunition, rejecting replayed use requests; this continuation does not emit another pickup sound. Allocation and consumption commit only after the world step succeeds. Resume/migration restore partial ammunition, cooldowns and flying rounds without replaying old events. Owner reset, death or permanent departure removes their rounds; disconnected retained players continue under the existing authority. Finished removes Salvo rounds, and new matches begin empty. Effects reuse existing licensed rocket/particle/audio assets; the HUD SVG is project-original.

`check-salvo.ps1 -GodotPath <exe> [-Visual] [-Impaired]` exercises three UDP peers in isolated native worlds: repeated salvos, five separately pressed launches/impacts per peer, held-input non-repeat and replicated ammunition, driving/steering with a moving guide and per-launch targets, two blast targets, second-slot preservation, chase-camera captures, banked ring geometry and owner-only visibility. Reconnect/migration fixtures seed partial ammunition, cooldowns and slow flying rounds to isolate exact checkpoint installation. These local fixtures do not establish Internet/EOS or independent-device coverage.


## Vehicle rack presentation

`CarRackPresentation` consumes accepted item slots, life/participation and use events for every network body. It adds no independent authority or packet. Proxy Mine consumes the authoritative placement state described above; Shield queues local early use until unfolding finishes, as described below. Other items retain their existing use/fire rules. A selected acquisition opens the split deck, raises the rack above the roof lamps, then reveals its payload. Separate accepted trunk/rack speeds control the ordered motion; defaults take 0.24 s and 0.2933 s respectively, without delaying ordinary authoritative use/fire. A committed mine placement establishes a minimum rack progress during its preparation phase, including reconstruction after admission and unusually slow cosmetic rack tuning. Selecting another physical slot retracts/closes before presenting the latest selection; selection revisions distinguish duplicate items. Rapid changes coalesce to the current selection. Confirmed use gives a short payload motion cue alongside existing world VFX/audio. For rack-mounted items, the rack stays deployed while the confirmed selected slot remains occupied, including cooldowns and trigger release. Authority clears depleted items; after the final use cue, the rack retracts and the trunk closes. Switching to an empty slot closes and stays stowed. Nitro raises the rack-mounted booster on selection, then extends its barrel modestly once the rack is raised. Accepted active Boost alone starts sparks/fumes and then ignites it after 0.30 seconds; release cancels pending ignition or fades the lit flame over .45 seconds while leaving the hardware ready. Confirmed exhaustion produces a distinct .60-second burst. Switching/depletion waits for combustion to finish before nesting the barrel and lowering the rack. Client does not duplicate ammunition rules.

Ordinary payloads scale in/out only above the compartment's clearance height. The booster rides the rack at full size after the deck opens. Death/life change removes the payload immediately. Checkpoint reseeding clears animation memory and reconstructs current selected ownership without replaying historical shots; ordinary cosmetic pre-disconnect animation phase is intentionally not replicated. A carried Proxy Mine reconstructs from its accepted placement timer and ground target instead of replaying pickup.

Missile reuses the registered rocket mesh; Proxy Mine reuses its production Blender model and beacon through the same `ProxyMineVisual` as deployed mines. Wrench, Oil, Salvo and Machine Gun use small colour-coded labelled boxes by explicit art direction. These are temporary representations, not new weapon models. Nitro uses the production [rack-mounted jet](boost-exhaust.md), with no placeholder. `check-car-rack.ps1` exercises the catalog, two physical slots, duplicate types, use/switch transitions, native proximity pickup and lifecycle/restoration presentation through two local UDP peers.

<a id="shield-rear-shield-and-persistent-health"></a>

## Shield rear armor and persistent health

Shield is canonical in the item registry, code, configuration, journal causes,
resources and verification harnesses. `HeldItem.Shield` retains numeric wire identity
8; item protocol 21 and configuration protocol 41 retain their binary layouts and
catalog ordering. Historical host seed/export names are accepted only by the
configuration readers, then become canonical Shield keys. Historical structured
item/developer journal causes are normalized on event decoding. Live edits and new
exports use Shield exclusively. See [configuration compatibility](developer-options.md#host-local-persistence).

An early use press is retained locally while the selected shield's rack/unfold
animation completes. The production arena reports readiness only for the exact
confirmed life, grant token and selection revision, with the rack raised and
mount progress at one. `VehicleNetworkDriver` then sends one ordinary reliable
use request (or invokes the same host use path locally). Repeated taps coalesce;
releasing the button does not discard the queued press. Simultaneous select/use
selects first and waits for that confirmed shield. Switching, discard, destruction,
death/life change and session recovery cancel pending local intent. A blocked
placement consumes that attempt and requires another press, retaining the item.
Protection begins with accepted selection as before. The queue is presentation
input state, not replicated gameplay state or a new server cooldown; Core still
owns placement validation, consumption, HP and the deployed wall. Direct authority
fixtures without an arena presentation continue to use the existing immediate API.
`check-shield-presentation.ps1 -Impaired` exercises this flow over local UDP
with 30 ms delay, 5 ms jitter and 2% loss.

Slot selection atomically exposes the selected Shield as `RearShield` and stows unselected Shields as `Held`. Acquisition into the selected slot exposes it immediately; acquisition into the other slot stores it. No use press is required. Switching away removes rear protection and presents the newly selected item; switching back exposes the same identity with its existing HP and damage/collision watermarks. Both states reserve the physical slot. One normal use press while selected deploys that same pool as a world wall and immediately clears its exact physical slot. Switching away before the use commits stows it and cancels deployment; failed native placement retains the rear shield and capability for a later press.

`ShieldGeometry` defines three full-height armor boxes: a 3.7 × 2.5 × 0.28 m rear panel centered at local `(0, .25, 3.25)`, plus 0.28 × 2.5 × 1.45 m side wings centered at `(±1.85, .25, 2.525)`. The wings wrap forward around the rear quarters; the interior and forward sides remain open. The panels follow the complete authoritative orientation, including pitch/roll. Core tests segments against their union using the current observation batch; native world/chassis hits still win when closer. Machine Gun rounds stop at the first shield. Missile/Salvo segments can impact it, and each radial target's center path is tested against its own shield before applying that target's unchanged radial damage. Proxy Mine contact paths likewise test the attached shield. An intercepted hit spends shield HP and suppresses that target's vehicle damage/weapon impulse, including the lethal shield hit; subsequent hits reach the now-exposed car. Other blast targets retain their ordinary damage. This is finite spatial cover, not a rear-angle immunity rule, and does not add general wall occlusion to explosions.

Native contact points inside the armor envelope route collision severity and host damage tuning to the shield. Registered vehicle impacts use the same paired severity and momentum damage bias as vehicle health; the interception explicitly suppresses mirrored defender damage while preserving the striker's received impact. A collision reported by the striking car also reaches the stationary defender's shield. Multiple manifold/slide observations collapse to the strongest shield contact for the step, with the existing collision cooldown retained per pool through checkpoints. The attacking car keeps its own normal collision damage. Damage uses the same evaluator as `DamageShield`; candidate changes, destroyed-slot cleanup and the single destruction journal entry commit only after the vehicle step succeeds. Sequential world ticks, capability validation, per-entity ordered damage and collision cooldown prevent replay or manifold duplication. When both slots contain Shields, only the selected pool is exposed and receives intercepted damage; the other remains stored.

Client reconstructs those three native collision shapes and the Blender-authored Carnage Circus armor under the interpolated production Car transform. Crimson/ivory stripes, painted grin, welded chevron, brass stars, scraping shoes and caged amber lamps identify the same model mounted and deployed. Its wings wrap forward while mounted and rotate outward to a flat wall. A separate carriage attaches at the production rack's rear socket pair and appears with the compact shield as the cleared rack starts lifting. Core remains the hit authority; the local input path queues early deployment intent until the confirmed selected shield finishes unfolding. Native weapon queries temporarily omit all three reconstructed shield shapes so same-step destruction cannot leave phantom cover. Accepted lifecycle/item state removes them on death, reset, destruction and permanent departure, and reinstalls them after retained recovery. Bevels, lamps and the shallow peaked trim are cosmetic details on conservative panel boxes. [Shield asset notes](../../assets/items/shield/README.md) define editable source, pivots and clearances.

The mounted armor's own sweep ignores contacted driveable support (`landing_terrain`
or legacy `SurfaceBody`), so ordinary slope contact
does not snag the shield or submit armor-ground damage observations. A separate
chassis sweep always retains ground contact. Solid obstacles, other vehicles,
weapon interception and deployed-wall physics retain their existing paths. This
is ground-skimming behavior; the low visual edge may intersect a sharp terrain
transition. Terrain exclusions apply to the contacted body's RID for that sweep;
other faces of the same terrain body remain covered by the chassis query.
The armor filter deliberately ignores sweep-normal direction: concave road seams
can report lateral separating axes even on driveable asphalt. All faces of a
tagged support body are skimmable by the armor; separately authored barriers and
obstacles still block it. Chassis contact classification is unchanged.
`check-shield-presentation.ps1 -ProductionTrack -GodotPath <exe> [-Visual]`
exercises ordinary input across the actual west-bank track join with two peers.
Both presentation modes also compare mounted/unmounted native responses against
sampled production track triangles, including pitched poses that exposed seam snagging.

`check-rear-shield.ps1 -GodotPath <exe> [-Impaired] [-Visual]` runs two production arenas over real local UDP, with independent shields, repeated ordinary selection/stow input from both peers without use, stationary/moving fire, inner/outer rear-quarter edge shots, exposed side/front shots, shield destruction while driving, and a native vehicle collision into the rear plate. Temporary fixture poses isolate shot geometry; moving scenarios advance native physics. Rendered captures use a fixed observation camera. Impairment adds 30 ms outbound delay, 5 ms jitter and 2% loss. Core tests additionally cover rejected-batch atomicity, exact slot retention, collision cooldown restoration, rotated geometry and missile interception. `check-shield-presentation.ps1` also runs native ground/obstacle comparisons and ordinary driving over a twenty-degree climb, crest and descent on two UDP peers.

Shield is registered as a Droppable (wire identity 8) in the existing two-slot inventory. Acquisition creates one match-unique live entity with **1000 HP**, independent of vehicle HP or vehicle-health tuning. Its normal default within-category spawn weight is **1**, using the existing registry-generated configuration and authoritative weighted pickup flow. The shared [Item HUD](hud.md) presents each held/rear-mounted pool with its own shield icon, HP readout and armor plates; released walls no longer belong to a HUD slot.

`ItemAuthority` owns the complete live Shield set, bounded at sixteen including stored/exposed items and released walls. Each pool retains its original entity ID, HP and ordered host-damage watermark. Immutable `ShieldState` records carry attachment life/capability or the installed wall pose. Attached `Held <-> RearShield` state follows accepted slot selection; publications reject exposure that disagrees with ownership/selection. `TransitionShield` remains a host-only recovery fixture seam. Ordinary selected use stages `RearShield -> WorldWall` inside the same transaction as vehicle simulation, slot clearing and the use event. Rejected world steps commit none of those changes.

`DamageShield` accepts a positive finite host-observed amount and a monotonic sequence scoped to that persistent entity, using the shared `VehicleHealth` clamping and destruction semantics on an independent pool. It returns the actual `DamageEvent` without changing vehicle health, kill attribution or player scoring. Host adapters must allocate ordered damage-observation sequences; neither HP nor damage/transition outcomes are accepted in client packets. Duplicate/older sequences are ignored, including after recovery. At zero HP the entity leaves the complete live set, its attached slot clears and one committed Shield destruction event is recorded. Absence is the existing authoritative item-destruction representation; no dead pool is retained or resurrected. Further damage/transitions cannot recreate it. A later acquisition is a new entity with a fresh ID and pool.

Complete item protocol version twenty-two publishes health, stage, entity ID, attachment capability, installed pose, damage watermark and last damaging collision tick through the existing reliable item envelope and nested admission/resume/migration checkpoints. Publications validate each attached pool against exactly one matching inventory slot and reject malformed health, stage, pose, ownership, count and cross-entity IDs. Authority restoration checks the existing token high-water bound, emits no historical destruction and retains damage replay memory. Selection changes, checkpoint installation and lifecycle transitions cannot refill a pool.

Attached Shields follow existing inventory policy: ordinary death/reset/permanent departure removes them, temporary disconnect retains them, and optional retained respawn changes life/capability while preserving ID, HP, stage and watermark. Released walls survive owner death/departure. Finished clears all live Shields and attached Shield slots; new matches start empty.

`check-shield.ps1 -GodotPath <exe> [-Impaired]` runs production drivers over three real local UDP peers, exercising four independent pools, state/slot transitions, repeated damage/destruction, late admission and full authority restoration. Impairment is 30 ms outbound delay, 5 ms jitter and 2% native packet loss. The production reconnect fixture retains a damaged rear shield and world wall; migration retains two damaged world walls. These preserve exact ownership/pose and replay memory. The separate rear-shield harness exercises native blocking and temporary presentation; these checks do not establish remote EOS, independent-device performance or competitive balance.

<a id="movable-shield-world-walls"></a>

## Movable Shield world walls

Normal use asks the host native adapter for rear ground and full expanded-box clearance. The
adapter projects behind the current heading, samples the center and four footprint ends,
and fits the standing wall to their support plane without changing its horizontal heading.
It searches up to one metre upward for a clear box. Its clearance query synchronously
places the deploying car's native proxy at the current observed pose and excludes only its
consumed mounted armor panels, then restores both pose and panels. This
avoids rejecting fast forward deployment against the car's preceding-frame collider while
still checking the actual chassis and other obstructions. A missing/steep support or blocked volume rejects that use
without consuming the shield. Core also rejects overlapping wall candidates within the same batch, before either native body exists. Deployment retains the entity ID, exact HP, damage sequence and
collision cooldown. It captures horizontal vehicle velocity, wall dimensions and mass; it does
not retain an attachment or dependency on the former owner's vehicle life. The Blender visual
releases from the displayed mounted pose over 0.18 seconds while its full-height hinged wings
open from 90 degrees to a flat wall over 0.42 seconds. The full collision envelope remains active
immediately. Captured dimensions scale the installed visual; configured sizes preserve the
same silhouette rather than authoring a separate model for each tuning combination.

Selected presentation reveals the compact folded stack as soon as the deck clears,
as the rack begins lifting. It rides the rack out of the bay. Each center half and
wing has a top and bottom section, forming eight sections in two rows. The subsequent
1.15-second carriage motion carries the stack aft, pitches it upright, opens the two
center halves around a visible central hinge, unfolds the side wings, then opens
all four horizontal hinges before pulling the shield into rear position. Center
tops fold backward and wing tops forward to nest the rows. Deselection reverses
this sequence and holds the rack open until the shield is nested. All panel
folding keeps full constant size. Inventory packing stays at 0.42 scale during rack
rise and initial aft travel, reaching full size behind the Car before the center
hinge opens; it does not represent exact mechanical packing. Accepted protection still
starts/stops immediately with selection. Early player use waits for the completed
mount animation, then releases through existing authority. The wall still inherits
the displayed pose/folds for continuity; the empty carriage returns independently. Rapid switching,
duplicate identities and remote publications share the same reconstructable path.

`ShieldVisual` owns per-instance accepted-HP weathering, impact scars, a brief hit flash
and bounded sparks. Switching stored identities reconstructs their current damage without
replaying an old impact. Late admission and explicit reseeding install a fully expanded wall,
without replaying release. Ordinary world-wall removal detaches a non-colliding visual for an
0.8-second panel breakup; this also represents expiry. At most 32 such world effects coexist.
Reseeding clears those effects. Mounted destruction is shown only when the identity disappears
while its owner remains alive in the same life; switching, deployment and death are not shield
destruction. These effects add no authoritative state, particles with collision, or network fields.

`check-shield-presentation.ps1 -GodotPath <exe> [-Visual]` drives two production arenas over
local UDP and checks mounted/rack state, duplicate inventory, selection, health persistence,
intermediate expansion, remote deployment, simultaneous independent damage, destruction,
repeated deployment, reacquisition, chase-camera driving and landing. Conservative imported
mesh bounds check clearance from articulated rear tires and trunk lids throughout the captured
commands. The opt-in `shield_playtest.tscn` also accepts
bounded JSON input/camera/damage fixture commands in `.godot/ts-219/playtest/input.json`; captures
and traces identify each command. Run only one instance per workspace. This supplements the
rear-shield, world-wall, vehicle/articulation and recovery checks; it does not establish physical
controller ergonomics, real WAN behavior or final competitive balance.

Host Configs exposes `items.shield_width` (6.6 m), `height` (2.5 m), `depth` (0.6 m),
`mass` (250 kg), and `clearance` (1 m behind the chassis envelope). Bounds are respectively
3–12 m, 2–6 m, 0.3–2 m, 50–2000 kg, and 0.5–5 m. Dimensions and mass are captured at
deployment, so later tuning cannot resize an installed collider. Configuration protocol 41
carries these values through the existing validation, publication and recovery path. These
are operational defaults verified on flat/banked native fixtures, not final balance.

`ShieldWorld` reconstructs one rigid body per accepted wall. Only the active host runs its
native solver; Core commits observed pose, linear/angular velocity and independent HP.
Frozen replicas receive the complete accepted state. Vehicle collision queries include wall
layer 32 and retain normal vehicle collision damage. For wall contacts only, Core shares
contact-normal momentum using the car and captured wall masses plus the wall box inertia, so a car pushes the
wall while slowing instead of retaining the generic sweep's stationary-obstacle stop.
Distinct manifold points form one contact centroid per wall; the contact offset produces
angular momentum as well as translation. Duplicate points cannot add duplicate torque.
Unrelated blocking contacts keep their resolved motion. Shield contacts bypass the
native adapter's additional fixed-prop pitch/roll kick: applying it before restoring shared
horizontal momentum could drive the chassis into the floor and launch it through suspension.
Native Shield side-contact normals use the same horizontal plane as that Core response,
so a banked side face cannot turn a sustained push into vertical lift. Top contacts and
terrain support retain their existing normals.
General car handling, suspension, tuning and other collision responses are unchanged. Writing an unchanged host
observation back to the solver is skipped to preserve pending contact impulses.
Standing walls use friction 0.6 and linear damping 2 so they settle after an unpowered push rather
than coasting across the arena. Their captured mass and contact momentum sharing remain
unchanged, allowing a moving car to keep pushing them. Before a strong vehicle impact,
native pitch/roll torque is constrained;
terrain support controls their slope-relative orientation and height as they slide. Horizontal
heading is preserved through ground alignment, while contact torque may rotate yaw freely.
Unsupported or airborne walls retain gravity. A rigid base cannot bend around a crest: it
rests above the highest sampled support, and placement requires all five supported samples.
Walls collide with
terrain/vehicles/other walls and survive the deployer's death, respawn or departure until
expiry/destruction. Inactive authority freezes the bodies.

When a vehicle contact's impulse divided by wall mass reaches `items.shield_tip_speed`
(default 20 m/s, range 1–100), Core permanently marks that wall as tipping. Its native
friction/damping return to 0.08/0.2 to preserve the fall and ground-contact response. Its
pitch/roll locks and ground stabilization release, and the contact offset supplies physical
angular momentum. A tipping wall leaves vehicle collision/query layers on every peer so its
rotating face cannot become a ramp under the car or its wheel rays. It retains native terrain
and standing-wall contact, and Core weapon intersections remain active until destruction.
Core destroys the remaining pool once native walkable-ground contact
coincides with the wall up-axis falling below a 0.2 dot product with that contact normal
(about 78.5 degrees from upright). Airborne tilt and ordinary slope alignment cannot alone
break it. The normal destruction path removes its collider and records the terminal event.

`items.shield_lifetime` defaults to 120 seconds (range 1–600). Successful deployment
captures an absolute host simulation-tick deadline; retuning, owner death/departure and
recovery cannot restart it. Expiry atomically removes the independent wall and records an
Expired event without touching the former owner's new inventory. Held/rear shields have
no deployment timer.

Core oriented-box intersections stop Machine Gun, Missile and Salvo segments at the closest
wall, comparing against native terrain/chassis hits. Native weapon queries omit layer 32 so
same-step destruction cannot leave phantom cover. Bullets damage the struck pool. Explosions
damage/impulse each wall once independently of the number of vehicles in the blast; the
existing non-occluded vehicle radial explosion rule is preserved. Vehicle contacts name the
struck wall; strongest manifold severity damages it once, with the shared default collision
cooldown on the independent pool. Lethal damage journals one destruction and removes the
body through complete-state absence, without touching the former owner's new inventory.

Item protocol 21 and nested checkpoints retain dimensions, mass, pose, both velocities,
identity, HP, expiry deadline, tipping state and replay watermarks. Live motion uses the existing reliable item channel,
including its delayed-delivery limitations. Resume/migration rebuild native bodies from the
selected boundary without replaying use or destruction. Native solver contact caches and
cosmetic expansion progress are not serialized; recovered walls appear fully expanded.

`check-world-wall.ps1 -GodotPath <exe> [-Impaired] [-Visual]` exercises two production UDP
arenas, stationary/60 m/s forward/reverse/banked deployment of damaged shields, blocked-use
retry with the same capability, retired-use rejection,
drive-away independence, retained release facing, bank alignment/elevation following,
native vehicle push with measured slowdown and damage, bounded three-second unpowered coast
after an 8 m/s push, fresh third-peer admission, sustained
Machine Gun destruction, native Missile/Salvo impacts and exact publication-boundary comparisons. It also deploys sixteen
walls through ordinary use and sustains their native state for 600 frames. Rendered runs
capture expanded, pushed, destroyed and stress states. Every live wall is checked for upright
orientation before tipping throughout the run. An off-centre vehicle strike must rotate
the wall without artificial torque. A hard native vehicle strike must tip it and break it
on ground contact. Ordinary banked contact and five hard strikes (host/remote, centred/offset,
40/60 m/s setup speeds, reverse-facing and banked) measure support-relative car rise and
upward speed; hard strikes continue for three seconds without resetting the car. A short
configured lifetime verifies exact expiry and peer cleanup. Missile/Salvo target fixtures reposition the surviving
wall onto level support after the motion test; they do not establish moving-target accuracy.
Add `-ProductionMap` to exercise sixteen normal-use deployments at
all eight oval grid spawns and the seven authored infield pickup areas, including impaired
UDP convergence. That fixture suppresses pickup acquisition to isolate exact-slot assertions.
`check-reconnect.ps1 -Shield` and
`check-migration.ps1` compare complete retained wall state against the exact selected host
boundary; deterministic tests additionally exercise Missile/Salvo wall damage and impulse.
These fixtures do not establish Internet/EOS multi-PC performance, final art or balance.

`check-missile-terrain.ps1 -GodotPath <exe> [-Visual]` exercises native static terrain
sampling and sweeps, existing projectile presentation, asphalt-to-dirt transitions,
ramps, banks, undulations, cliffs, repeated reacquisition, upward/downward shots,
walls, steep faces, live configuration contracts, serialized continuation and
sustained expiry. It also checks repeated remote launches and curved motion on
three real UDP peers with 30 ms delay, 5 ms jitter and 2% packet loss. Artifacts go
to `.godot/missile-checks`; these controlled fixtures do not establish physical
mouse/controller feel or multi-machine Internet behavior.

The missile harness also drives the actual production vehicle onto the tabletop and bank approach, then fires in both tabletop directions at 60/120/180 m/s and at offset lanes. It records native collider/material samples, launch poses, clearances and impact locations. `-ProductionOnly [-Case bank]` isolates these scenarios; `-Diagnose` records failures without accepting them as a regression pass.
