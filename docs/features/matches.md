# Match Scoring and First-to-Target

## Authority and Lifecycle

Multiplayer `HostVehicleSession` enables `MatchConfiguration` on its existing `Simulation`. `SimulationState.Match` is the sole match boundary: phase, target, scores, winner, score deltas and consumed-life watermarks. Local practice and isolated movement fixtures omit match rules. No separate combat or health system is introduced.

The [reusable Game Loop](game-loop.md) owns the shared engine-independent phase transition rules. `MatchAuthority` evaluates those rules inside its existing atomic simulation candidate, while `FirstToTargetMode` reports the kill-target completion outcome. `MatchState.Lifecycle` exposes a read-only generic phase/outcome projection; it creates no additional mutable authority and requires no new wire fields. The development adapter's Waiting/roster policy remains distinct from the reusable post-sync initialization handoff.

The match progresses through Waiting, Countdown, Active and Finished. Defaults are two participants, a 180-tick countdown (three seconds at 60 Hz), and `KillTarget = 5`. Countdown cancels back to Waiting if the authoritative vehicle roster drops below the minimum before activation. Countdown and scoring use Core ticks, never wall-clock or packet-arrival time. Combat remains available outside Active, but those deaths cannot change scores. An active match continues if a participant leaves; there is no automatic forfeit or restart rule. A new arena session starts a fresh match.

Arena disconnect and intentional Leave retire input ownership while retaining the authoritative participant, vehicle, score totals and consumed-life watermark. Remaining participants continue simulating and scoring. Offline participants remain eligible for the same Core ranking; resume changes the connection binding without resetting scores. Finished freezes the complete result and retains all participant reservations throughout results. Only Return out of the arena/results lifecycle releases disconnected reservations; the next arena generation creates fresh scores and ranking. No fixed-time reservation expiry or reset occurs at Finished entry.

## Attribution, Duplicate Protection and Winner

Scoring consumes the existing authoritative lethal `DamageEvent` and vehicle `LifeId` in the same atomic world commit. A positive lethal missile or vehicle-collision event credits its instigator only when that identity is a different player still in the authoritative roster. Collision attribution follows the existing receiving-vehicle rule: the other vehicle is the source of a damaging contact. Mutual lethal contacts can therefore produce mutual credit before the target is reached.

Self-destruction, world/prop collisions, missing players, unsupported damage categories and unattributed deaths award no kill. Each scored victim gets one death. Attribution uses the actual lethal event at the current fixed tick; it does not fall back to an older attacker for a later environmental death. Existing damage context is sufficient, so there is no additional history buffer or expiry timer and no assist credit.

Each player retains the highest destroyed life consumed. This watermark advances even when a death occurs before Active, preventing an old wreck from scoring when the countdown ends. Duplicate observations and restored dead states cannot double-score. Respawning creates a new life and clears vehicle damage memory while preserving match totals. Complete simulation restoration requires the match boundary and registered match target; restoring vehicles alone into a scoring world is rejected.

Deaths in a batch are evaluated in ascending victim identity order. The first credited kill reaching the target records one winner, increments that player's per-match wins from zero to one, and enters Finished. Later deaths in the same batch and later ticks cannot change kills, deaths, wins or winner. Their ordinary damage/death/respawn behavior still proceeds. This deterministic tie rule makes results independent of request enumeration or network arrival order.

Departed score rows remain for the lifetime of the match, including after explicit reservation abandonment. Releasing an admission slot removes its suspended vehicle/inventory and resume authorization, not scores, consumed-life watermarks, rank or the recorded winner; the session retains its display identity for standings. Normal session admission rejects new players during Finished. The lower-level standalone vehicle harness can still expose the frozen result without adding a score row. Match state is bounded to 256 lifetime participants, with at most eight connected vehicles; new standalone admissions are rejected at the lifetime bound. Session resume retains these totals and watermarks. Fresh active admission previews a zero score row in its complete checkpoint and commits that row only on bootstrap acknowledgement, preserving all existing totals, countdown deadlines and consumed-life watermarks. Cross-match win persistence remains unsupported.

## Reliable Publication and Presentation

The version-one `TM` match codec carries arena generation, increasing revision, mutation tick, phase/countdown deadline, target, winner, complete kills/deaths/wins and life watermarks, plus up to eight scored-death deltas. Immutable copies and decode validation reject inconsistent winners, duplicate identities, invalid totals, oversized collections, truncated messages and trailing bytes. The complete bounded payload is below 8 KiB even at the lifetime participant limit.

`VehicleNetworkDriver` sends match changes reliably through the existing ordered transport and sends the current boundary on admission. Complete totals initialize joining peers; consumers should use the first publication as initialization rather than replaying its historical delta into those totals. Clients accept only newer revisions from their assigned host in the current arena over reliable delivery. Match ordering is independent of movement snapshot ticks, so a delayed reliable score update can arrive after newer movement without being lost. A finished client result is terminal. Clients cannot request score mutation or choose winners.

`MatchReceived` exposes each accepted revision once for leaderboard, result UI and [arena audio](audio.md), including its score deltas. Arena audio uses phase transitions for countdown/start/end feedback; its battle playlist runs from arena entry to exit independently of match phase. The arena displays the authoritative waiting/countdown/active/finished state in a small panel beneath the combat HUD timer. The [match standings and results board](standings.md) presents the complete current roster, kills/deaths and winner and supplies the same Core rank to the HUD. Its countdown display uses the host tick and never changes match phase locally. The gameplay protocol generation and EOS compatibility bucket require matching builds so older clients cannot silently join without scoring support.

## Verification and Limits

Core tests exercise real simulation damage/death boundaries, valid and invalid attribution, pre-active consumption, duplicate/restored deaths, configurable targets, stable simultaneous winners, frozen results and codec validation. Driver tests reject client-authored, unreliable, wrong-session, duplicate and post-finish publications and cover reliable state arriving after newer movement.

`check-match.ps1 -GodotPath <exe>` runs eight native UDP peers through alternating remote missile and ram kills to five, followed by a sixth combat death and respawn after Finished. It checks consistent scores and winner, one finished notification per peer, native lifecycle behavior and unchanged final state. `-Impaired` adds 30 ms delay, 5 ms jitter and 2% loss; `-Visual` renders a peer and saves death/respawn and final-result evidence. Scenario setup positions vehicles and lowers victim HP; the real projectile/collision and authoritative damage paths cause each death. This is repeatable single-machine integration evidence, not a separate-PC EOS session or a balance/soak test.

See [reconnection and session resume](reconnection.md) for authenticated match-long retention, rebind and checkpoint semantics.

Authority restoration, epoch fencing, checkpoint cadence and migration limits are described in [host migration](host-migration.md).

[Developer Options](developer-options.md) uses the existing match authority for live KillTarget, CountdownTicks and MinimumPlayers. A changed target must exceed existing scores and cannot change a Finished result. Countdown edits restart its deadline from the current tick; minimum-player rules still apply at the next Waiting/Countdown boundary. Host Force Start is a one-shot minimum-player override through the normal countdown, with no persisted rule change or separate subsystem activation. Audio retains its arena-lifetime policy.

[Feature index](README.md)

The [Event Log](event-log.md) records phase changes and winner plus the existing committed scored-death deltas. It adds no alternative kill-attribution rules.
