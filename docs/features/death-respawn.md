# Player Death and Respawn

## Authoritative Lifecycle

The existing `VehicleSnapshot` owns `Lifecycle` (Alive, Dead, Respawning), `LifeId` and nullable `RespawnAtTick` alongside HP and movement. `CanInteract` is the shared participation gate. There is no parallel player-health or Client lifecycle authority. `VehicleAuthority` enters Dead on the lethal tick, using the unique `DamageEvent.DestroyedTransition` for attribution. On the following tick it enters Respawning until the deadline; with a one-tick delay the next boundary is already Alive. Additional damage, repair, input and impulses cannot revive or move an inactive vehicle.

`RespawnConfiguration.DelayTicks` defaults to 180 (three seconds at the production 60 Hz rate) and must be positive. Death records the absolute global deadline once. Simulation steps determine elapsed time; frame rate, wall-clock time and packet receipt never advance the timer. The host session and production local practice enable this policy. Isolated movement/replay fixtures can omit it and retain explicit reset control. Checked tick/life arithmetic fails before world publication rather than wrapping into a different life.

## Spawn Selection and Reset Policy

Selection uses the eight validated `ArenaConfiguration.Players` markers extracted from the [active oval](oval-map.md), including the common 0.2 m vehicle spawn lift and after host migration. Old prototype spawn placement is confined to explicit fixtures. Vehicle identity and the next life generation choose a deterministic cyclic starting slot; all eight markers are reachable without random or mutable selector state. The selector skips markers within 5.6 horizontal metres (the scaled vehicle's conservative circumscribed footprint) of another living vehicle. Simultaneous respawns reserve candidates in stable vehicle-ID order within the atomic batch. If every marker is occupied, the vehicle remains Respawning and retries on subsequent authoritative ticks; the configured deadline is the earliest eligible tick. This checks vehicle clearance, not line-of-sight safety, spawn protection, or dynamic prop occupancy. Static marker accessibility is validated by the native arena harness.

At the deadline, when a marker is available, Core increments `LifeId`, restores that marker's position/yaw and full configured MaxHP, zeros both observed and commanded linear/angular velocities, and clears drift, boost, grounding, surface memory (Concrete), damage attribution, collision cooldown and accepted impulses. The respawn boundary itself consumes no driving, repair, incoming damage or queued impulse; normal driving and damage evaluation resume on the next step. Global simulation/input time never rewinds. Native adapters reconstruct the accepted transform and velocities and reset interpolation/correction and damage-flash memory.

## Combat and Inventory Integration

Inactive vehicles have no native collision layer/mask and are hidden. Core independently rejects their movement and item grant/use requests and filters effects/contacts attributed to registered inactive vehicles. This prevents native presentation from becoming a combat rule. Batches evaluate participation at their starting boundary, so simultaneous living attackers can trade lethal hits deterministically. Existing projectiles owned by a player are removed in that player's lethal batch and cannot survive into another life. The live `ItemSpawnAuthority` pickup path uses the same participation gate before choosing an item or consuming marker availability. Death and waiting leave unclaimed pickups available for living players. Per-player category balancing history belongs to the match and survives every life change, independently of held-slot clearing or retention.

`RespawnConfiguration.ClearHeldItemOnDeath` defaults to true. `ItemAuthority` clears the existing slot in the same committed death batch. With retention explicitly enabled, the held item remains unavailable during death/waiting and transfers to the new life with a fresh grant token on respawn. Pending uses clear each step; an old life/token cannot spend the retained or replacement item. Final departure and explicit development resets discard inventory; authenticated reconnect grace retains current authoritative inventory.

## Replication, Prediction and Hooks

Every lifecycle or life-generation change publishes a complete existing vehicle snapshot reliably through the current ordered transport. Collision-only deaths therefore replicate even without item changes or a periodic movement publication. Joining peers receive a reliable current boundary. Aggregate codec version two and vehicle protocol version six preserve lifecycle/deadline; the EOS discovery compatibility bucket is `trackstorm-lobby-14`. Older aggregate/gameplay versions are rejected.

The existing snapshot history rejects stale poses. A delayed reliable death/wait/respawn publication can still notify presentation observers once, even if a newer unreliable movement snapshot arrived first; it cannot rewind the current vehicle state. `Simulation.LifecycleChanges` exposes immutable committed transitions, including the existing damage attribution, for later scoring or other consumers. `VehicleNetworkDriver.LifecycleReceived` provides ordered reliable full boundaries for Client UI/audio. Consumers distinguish transitions by vehicle/life/state; match scoring consumes the committed Core death boundary; [arena audio](audio.md) consumes those boundaries for distinct destruction, local death and respawn feedback.

Prediction advances movement only and keeps host HP/lifecycle unchanged, including when its local tick passes the respawn deadline. Lifecycle corrections neutralize retained controls while preserving sequence acknowledgements; input envelopes identify the observed life, and the host acknowledges delayed old-life inputs as neutral. Buffered held controls also clear on host lifecycle boundaries. Remote rendering excludes earlier-life poses after a respawn; local correction offsets reset and the chase camera snaps on the new-life boundary instead of blending a wreck into its spawn.

## Presentation and Verification

Client `VehicleDestructionEffects` consumes confirmed deaths once per life and uses the already-acquired Kenney Particle Pack CC0 fire, smoke and spark textures for a short destruction flash, smoke and debris-like sparks. Existing provenance and license records remain in `assets/items/sources.json`. Bursts have no gameplay collision or state access for mutation, expire after 1.6 presentation seconds, and are capped at 24 during delivery catch-up. Arena teardown owns all particle nodes. There is no dedicated death-animation system or dissolve shader. [Arena audio](audio.md) owns destruction/death/respawn sounds separately from particles.

Core tests cover lethal crossing, duplicate damage, inactive interactions including live pickup claims, exact thresholds, full reset, optional inventory retention, repeated cycles, custom markers/all-eight coverage, occupied markers, simultaneous reservations, stale-life input, prediction authority and both state codecs. Driver tests cover collision-only reliable publication, stale/duplicate/forged delivery and reliable events arriving after newer movement.

`check-death-respawn.ps1 -GodotPath <exe>` runs eight UDP peers in isolated Godot worlds through four alternating remote missile/collision deaths and respawns on the active oval. The lethal wall-impact target is added by the test fixture outside the map scene; it is absent from normal gameplay. It asserts matching lifecycle/deadline/spawn/HP across peers, native collision/visibility changes, movement/item rejection, reset physics/transients and bounded once-per-life VFX cleanup. `-Impaired` adds 30 ms outbound delay, 5 ms jitter and 2% loss; `-Visual` renders a client and saves death/respawn images. This is single-machine native evidence, not separate-PC, authenticated EOS peer gameplay, or an indefinite multiplayer soak.

See [reconnection and session resume](reconnection.md) for authenticated grace, rebind and checkpoint semantics.

Host [Developer Options](developer-options.md) changes the existing `RespawnConfiguration`: new deaths use the current DelayTicks, while already committed deadlines remain stable. ClearHeldItemOnDeath is consumed by the existing inventory lifecycle policy. Live Max HP preserves health percentage and cannot revive an inactive life. Accepted configuration and revision accompany resume checkpoints.

Authority restoration and epoch fencing are described in [host migration](host-migration.md).

[Feature index](README.md)

The [Event Log](event-log.md) consumes committed lifecycle transitions once, independently of unreliable pose publications. It preserves the affected life and lethal cause, while scored kill credit remains owned by match authority.

Fresh active admission preserves existing HP, life generations and absolute respawn deadlines through the shared checkpoint. Client reseeding suppresses historical destruction/respawn feedback. The new vehicle uses the existing marker-clearance selector at admission and again at activation, without moving or respawning existing vehicles. See [session activation](sessions.md#fresh-admission-during-an-arena).

[Circus pending stunts](matches.md#circus-stunt-detection-and-banking) are cancelled in the lethal candidate before landing/threshold completion can bank. Reset/respawn life changes also cancel pending events; all previously banked Circus points remain permanent for the match.
