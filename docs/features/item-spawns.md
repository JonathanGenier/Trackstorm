# Item Spawn and Distribution

## Acquisition and Authoritative State

Each network arena registers the eight actual `ItemSpawns` markers extracted by `CombatArena.ValidateScene()`. Core `ItemSpawnAuthority`, owned by `HostVehicleSession`, is registered once before the first world step. Marker IDs and positions come from that validated contract; no additional pickup positions are synthesized. All eight start available. The host's Client adapter detects proximity using native vehicle proxies and scene markers, then Core rechecks the committed vehicle position, living roster membership, available spawn and empty current-life slot. Remote clients cannot submit a claimed position, recipient or item choice.

An accepted contact selects one item, grants it through the existing `ItemAuthority` single-slot API, and records the claimant, grant token, awarded item and next activation tick before publication. Rejected contacts do not advance the selector or consume availability. Contacts in a host boundary are deduplicated and ordered by ordinal marker ID then vehicle ID, giving simultaneous contestants exactly one winner. This stable tie-break favors the lower vehicle ID for an exact same-tick tie. Occupied slots never replace their items or consume the pickup. A player remaining in range can acquire again after consuming an item, including within that same committed boundary.

Core advances cooldowns from committed 60 Hz world ticks, after successful vehicle/item simulation. A claim at tick `t` reactivates at `t + CooldownTicks`; clients never activate from a local timer. Claims survive claimant departure or item consumption until the deadline. Lobby return discards the match-owned authority; the next arena starts fresh. Resume checkpoints include this exact state and deadlines; the authority is never restarted on rebind.

## Configuration and Replication

`ItemSpawnConfiguration` defaults to 600 ticks (10 seconds), a three-metre three-dimensional center-to-marker radius, equal Wrench/Missile weights and seed 1. Both weights must be positive, their sum must fit an integer, cooldown is bounded to 1-216000 ticks, and radius must be finite in (0, 10]. The composition root supplies immutable tuning before scene entry through `NetworkVehicleArena.SpawnConfiguration`. No runtime settings UI is added. A seeded selector owns its own stream per arena; an injected selector supports exact deterministic tests. The default seed repeats the item sequence across fresh matches; this is reproducible prototype behavior, not a competitive randomness guarantee.

Spawn changes and assigned slots travel together in the existing reliable `ItemPublication`, including absolute activation deadlines and last claim/token/item. Full state is sent when inventory, projectiles, spawn availability or admission changes. Deadlines need no per-tick countdown traffic. The bounded version-two codec validates IDs, counts, claim fields and deadline/availability consistency before exposing detached state. The established host, reliable delivery, current arena generation and increasing publication revision remain mandatory. There is no predicted pickup award; latency can delay visible confirmation, and the local view does not reactivate ahead of the host.

## Presentation, Assets and Verification

`ItemSpawnPresentation` builds one floating, rotating, non-colliding pickup per marker from the already acquired CC0 Kenney Weapon Pack rocket geometry. The existing rusty emissive pickup material, local light and native `GpuParticles3D` make it stand out from arena props. The rocket is a generic pickup marker: the item is selected on claim, and the confirmed held model/HUD identifies the result. Active models and particles hide together on claim; a `RECHARGING` label remains until confirmed reactivation. A use followed by reacquisition updates the held model even when there was no intervening empty-slot publication.

Provenance and license remain in `assets/items/sources.json` and the acquired Kenney license. The [arena audio system](audio.md) uses confirmed grant tokens for distinct weapon and Wrench pickup cues; its assets and licenses have a separate manifest.

Deterministic NUnit coverage verifies actual registration IDs, single registration, alive/empty/in-range validation, same-tick contention, retry rejection, exact cooldown boundaries, invalid selectors and weights, seeded distribution and complete publication corruption handling. Driver coverage applies the host/trust/delivery/revision guards to spawn-bearing publications.

`check-item-spawns.ps1 -GodotPath <Godot .NET executable>` runs eight actual UDP peers in isolated Godot worlds. Two remote vehicles contest one pickup; every peer verifies one matching claimant/token/item and cooldown. It exercises reactivation, all eight pickup positions, normal weighted distribution of both items, and occupied vehicles remaining in range without duplicate grants. `-Impaired` adds 30 ms outbound delay, 5 ms jitter and 2% loss; `-Visual` captures the rendered client. Existing item, lobby and network vehicle checks cover surrounding combat and scene lifecycle. These are local native integration checks, not multi-machine/NAT or long-session performance evidence.

See [reconnection and session resume](reconnection.md) for authenticated grace, rebind and checkpoint semantics.

[Feature index](README.md)
