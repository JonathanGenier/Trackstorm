# Match Scoring, Circus Combat and First-to-Target

## Authority and Lifecycle

Multiplayer `HostVehicleSession` enables `MatchConfiguration` on its existing `Simulation`. `SimulationState.Match` is the sole match boundary: phase, target, scores, winner, score deltas and consumed-life watermarks. Local practice and isolated movement fixtures omit match rules. No separate combat or health system is introduced.

The [reusable Game Loop](game-loop.md) owns the shared engine-independent phase transition rules. `MatchAuthority` evaluates those rules inside its existing atomic simulation candidate, while `FirstToTargetMode` reports the kill-target completion outcome. `MatchState.Lifecycle` exposes a read-only generic phase/outcome projection; it creates no additional mutable authority and requires no new wire fields. The development adapter's Waiting/roster policy remains distinct from the reusable post-sync initialization handoff.

The match progresses through Waiting, Countdown, Active and Finished. Defaults are two participants, a 180-tick countdown (three seconds at 60 Hz), and `KillTarget = 5`. Countdown cancels back to Waiting if the authoritative vehicle roster drops below the minimum before activation. Countdown and scoring use Core ticks, never wall-clock or packet-arrival time. In application sessions, driving and item use require Active; existing physics and committed combat effects continue outside Active, but those deaths cannot change scores. Isolated development fixtures retain their original controls. An active match continues if a participant leaves; there is no automatic forfeit or restart rule. A new arena session starts a fresh match.

Arena disconnect and intentional Leave retire input ownership while retaining the authoritative participant, vehicle, score totals and consumed-life watermark. Remaining participants continue simulating and scoring. Offline participants remain eligible for the same Core ranking; resume changes the connection binding without resetting scores. Finished freezes the complete result and retains all participant reservations throughout results. Only Return out of the arena/results lifecycle releases disconnected reservations; the next arena generation creates fresh scores and ranking. No fixed-time reservation expiry or reset occurs at Finished entry.

## Attribution, Duplicate Protection and Winner

Scoring consumes the existing authoritative lethal `DamageEvent` and vehicle `LifeId` in the same atomic world commit. A positive lethal missile or vehicle-collision event credits its instigator only when that identity is a different player still in the authoritative roster. Collision attribution follows the existing receiving-vehicle rule: the other vehicle is the source of a damaging contact. Mutual lethal contacts can therefore produce mutual credit before the target is reached.

Self-destruction, world/prop collisions, missing players, unsupported damage categories and unattributed deaths award no kill. Each scored victim gets one death. Attribution uses the actual lethal event at the current fixed tick; it does not fall back to an older attacker for a later environmental death. Existing damage context is sufficient, so there is no additional history buffer or expiry timer and no assist credit.

Each player retains the highest destroyed life consumed. This watermark advances even when a death occurs before Active, preventing an old wreck from scoring when the countdown ends. Duplicate observations and restored dead states cannot double-score. Respawning creates a new life and clears vehicle damage memory while preserving match totals. Complete simulation restoration requires the match boundary and registered match target; restoring vehicles alone into a scoring world is rejected.

Deaths in a batch are evaluated in ascending victim identity order. The first credited kill reaching the target records one winner, increments that player's per-match wins from zero to one, and enters Finished. Later deaths in the same batch and later ticks cannot change kills, deaths, wins or winner. Their ordinary damage/death/respawn behavior still proceeds. This deterministic tie rule makes results independent of request enumeration or network arrival order.

Departed score rows remain for the lifetime of the match, including after explicit reservation abandonment. Releasing an admission slot removes its suspended vehicle/inventory and resume authorization, not scores, consumed-life watermarks, rank or the recorded winner; the session retains its display identity for standings. Normal session admission rejects new players during Finished. The lower-level standalone vehicle harness can still expose the frozen result without adding a score row. Match state is bounded to 256 lifetime participants, with at most eight connected vehicles; new standalone admissions are rejected at the lifetime bound. Session resume retains these totals and watermarks. Fresh active admission previews a zero score row in its complete checkpoint and commits that row only on bootstrap acknowledgement, preserving all existing totals, countdown deadlines and consumed-life watermarks. Cross-match win persistence remains unsupported.

## Circus combat score

`PlayerScore` carries permanent `CircusScore`, consecutive `KillStreak`, and the shared Core `KdMultiplier = max(1, Kills / (Deaths + 1))` using fractional division. `CircusScoring.Bank` applies this one multiplier to a source's base points. UI consumes authority; it does not award points. Death resets the streak without removing banked points.

Every valid attributed kill increments kills and streak first, then banks `(BaseKillPoints + (KillStreak - 1) * KillStreakBonusStep) * KdMultiplier`. The current defaults are 100 base points and a linear 25-point step after the first consecutive kill. These are bounded host tuning, not presentation rules or a balance guarantee.

Collision awards consume **all applied `VehicleStepResult.DamageEvents`** inside the same atomic simulation candidate as health. Nonlethal hits score too; raw contacts, requested damage, harmless brushes, cooldown-suppressed contacts, world/self/missing-player attribution and other nonlethal damage sources do not. Base points are actual HP loss times `CollisionPointsPerDamage` (default 1). A lethal collision awards its applied damage before its kill, so the collision uses the pre-kill multiplier and the kill uses the updated multiplier.

The victim's `(ProcessedDamageLife, ProcessedDamageSequence)` watermark consumes each applied outcome at most once, including outside Active. Within a batch, victim identity then damage sequence determine evaluation order; each victim's damage precedes that victim's death. Mutual deaths therefore reset streaks in the existing deterministic victim order. The existing first-to-target terminal rule still freezes further scoring in that batch and subsequent ticks. All score, streak and watermarks are restored with the complete match boundary. Restoring does not emit or score historical damage. New lives can score from sequence one; new matches start from zero.

The three tuning values use the existing Developer Options catalog, validation, host persistence, reliable configuration publication and checkpoint/migration continuation. Existing schema-two settings files default missing Circus keys; the configuration wire codec is version two. Scores use finite nonnegative binary64 points, retaining fractional awards without integer truncation; deterministic replay uses identical applied float HP outcomes, tuning and ordering. Native cross-platform physics determinism is not promised.

Circus combat totals accompany the current match without changing its kill-target lifecycle, kill-based ranking or final-result presentation. Stunts, pending points, Circus HUD/ranking and score-attack completion are separate integrations.

## Final result contract

Finished constructs one immutable `FinalMatchResults` on the committed `MatchState`, with completion tick/outcome and ordered rank, kills, deaths and per-match wins for every retained score identity. The Core ranking rule is shared with Active standings. Continued physics, respawns, disconnect, resume or reservation abandonment cannot change that value. Match/checkpoint decoding reconstructs the same rows from the existing authoritative fields; no new protocol is needed. Application Flow consumes the [Game Loop results and disposal contract](game-loop.md#final-results-and-application-flow-handoff). A new match reconstructs its entire simulation/mode state through Game Load / Sync rather than rewinding score or life-watermark fields in place.

## Reliable Publication and Presentation

The version-two `TM` match codec carries arena generation, increasing revision, mutation tick, phase/countdown deadline, target, winner, complete kills/deaths/wins, Circus points/streaks and life/damage watermarks, plus up to eight scored-death deltas. Immutable copies and decode validation reject inconsistent winners, duplicate identities, invalid totals, oversized collections, truncated messages and trailing bytes. The complete bounded payload is below 16 KiB even at the lifetime participant limit.

`VehicleNetworkDriver` sends match changes reliably through the existing ordered transport and sends the current boundary on admission. Complete totals initialize joining peers; consumers should use the first publication as initialization rather than replaying its historical delta into those totals. Clients accept only newer revisions from their assigned host in the current arena over reliable delivery. Match ordering is independent of movement snapshot ticks, so a delayed reliable score update can arrive after newer movement without being lost. A finished client result is terminal. Clients cannot request score mutation or choose winners.

`MatchReceived` exposes each accepted revision once for leaderboard, result UI and [arena audio](audio.md), including its score deltas. Arena audio uses phase transitions for countdown/start/end feedback; its battle playlist runs from arena entry to exit independently of match phase. The arena displays the authoritative waiting/countdown/active/finished state in a small panel beneath the combat HUD timer. The [match standings and results board](standings.md) presents the complete current roster, kills/deaths and winner and supplies the same Core rank to the HUD. Its countdown display uses the host tick and never changes match phase locally. The gameplay protocol generation and EOS compatibility bucket require matching builds so older clients cannot silently join without scoring support.

## Verification and Limits

Core tests exercise real simulation damage/death boundaries, valid and invalid attribution, pre-active consumption, duplicate/restored deaths, configurable targets, stable simultaneous winners, frozen results and codec validation. Driver tests reject client-authored, unreliable, wrong-session, duplicate and post-finish publications and cover reliable state arriving after newer movement.

`check-match.ps1 -GodotPath <exe>` runs eight native UDP peers through a nonlethal ram with applied-HP Circus scoring, then alternating remote missile and ram kills to five, followed by a sixth combat death and respawn after Finished. It checks consistent Circus points, streaks, damage watermarks, kill totals and winner, one finished notification per peer, native lifecycle behavior and unchanged final state. `-Impaired` adds 30 ms delay, 5 ms jitter and 2% loss; `-Visual` renders a peer and saves death/respawn and final-result evidence. Scenario setup positions vehicles and lowers victim HP; the real projectile/collision and authoritative damage paths cause each death. This is repeatable single-machine integration evidence, not a separate-PC EOS session or a balance/soak test.

See [reconnection and session resume](reconnection.md) for authenticated match-long retention, rebind and checkpoint semantics.

Authority restoration, epoch fencing, checkpoint cadence and migration limits are described in [host migration](host-migration.md).

[Developer Options](developer-options.md) uses the existing match authority for live KillTarget, CountdownTicks and MinimumPlayers. A changed target must exceed existing scores and cannot change a Finished result. Countdown edits restart its deadline from the current tick; minimum-player rules still apply at the next Waiting/Countdown boundary. Host Force Start is a one-shot minimum-player override through the normal countdown, with no persisted rule change or separate subsystem activation. Audio retains its arena-lifetime policy.

[Feature index](README.md)

The [Event Log](event-log.md) records phase changes and winner plus the existing committed scored-death deltas. It adds no alternative kill-attribution rules.
