# Authoritative Game Loop

## Ownership and entry

`Core.Matches.GameLoop` owns the reusable lifecycle **Initialization → Countdown → Active → Finished**. It begins only when trusted Application Flow supplies a `SynchronizedMatchContext` after completing match loading and synchronization. That immutable handoff contains a nonzero match generation, the authoritative handoff tick and a detached initial participant roster. The context is a completed-handoff contract, not a loader or evidence that Core independently checked native resources or network readiness.

Before the handoff, `State` and `Context` are absent and gameplay is disabled. `Initialize` accepts the handoff once and exposes Initialization before a separate `StartCountdown` command. No scene, map, menu, wall clock, Godot object or transport implementation enters Core. Application Flow remains responsible for deciding when its handoff is ready.

The trusted composition root fixes authority when it constructs `GameLoop`. A non-authoritative instance rejects initialization, countdown, tick advancement and completion; its authority cannot later be toggled. Client packets must never select this constructor argument or call the mode-completion API. Immutable `GameLoopState` values may be inspected or constructed for presentation/validation without mutating a live owner.

## Transitions and timing

`StartCountdown` accepts a positive tick duration only during Initialization. Its start tick is the context's authoritative handoff tick; Initialization does not advance simulation time. Its absolute end tick is exposed as `CountdownAtTick`, with overflow rejected before mutation. `Advance` accepts exactly the next fixed tick. At the deadline it enters Active and clears the deadline. Duplicate, stale, skipped, premature and terminal commands return false without changing state or phase revision.

`Revision` advances once per accepted phase transition. Accepted ticks update the immutable state boundary without incrementing phase revision unless they cross the deadline. `RemainingCountdownTicks` is a saturating read-only projection of the deadline and last observed authoritative tick. A client display reaching zero cannot change phase or enable gameplay.

`AllowsGameplay` is true only in Active. Consumers combine this Core policy with their existing synchronization and vehicle-life eligibility checks. The reusable owner does not implement input transport, prediction or native vehicle controls.

## Game-mode completion

A trusted active game mode reports its validated authoritative result through `ReportCompletion(MatchOutcome)`. The outcome contains a bounded diagnostic reason and an optional winning player; a draw or non-player objective can finish without kills or a winner. The mode owns result validity, attribution and score data. The lifecycle owns whether that result can transition Active to Finished. There is no client completion intent or navigation side effect.

Finished retains the exact final state and outcome. Repeated or competing reports, further ticks, countdown commands and reinitialization are rejected. `ReportFinalResults` accepts a mode's immutable `FinalMatchResults` only at the current Active tick and commits it with Finished. The outcome-only `ReportCompletion` remains available for modes without player statistics; its payload has no standings. The lifecycle does not derive mode rankings from kill counts.

## Final results and Application Flow handoff

`FinalMatchResults` contains the authoritative completion tick, reason, optional winner and detached, ordered `FinalMatchStanding` rows (PlayerId, rank, banked Circus score, kills, deaths and per-match wins). It validates bounded unique identities and complete one-based positions, copies the collection and exposes only read-only values. These are the currently implemented statistics; it adds no new completion policy or cross-match totals.

The combat adapter creates `MatchState.FinalResults` only for Finished, using all retained score identities and the existing Core `MatchRanking` rule. The same immutable result remains available while physics and respawns continue. Accepted post-finish scoring-tuning edits retain the exact match object, completion tick and final results. Existing match and checkpoint codecs retain mode and all source data, so decoding reconstructs identical Core results without a new wire stream or UI calculation. Recovery still follows the existing coherent-checkpoint/rollback contract.

`VehicleNetworkDriver.FinalResults` exposes the accepted match result. `DevelopmentSession.FinalResults` is Application Flow's read-only handoff once entry/recovery synchronization is complete. Its scope is the current lobby session and arena generation; consumers can retain the immutable value before disposing the arena. Late consumers can read the property without replaying an event or historical score deltas. Finished standings use these frozen rows directly. Names remain authoritative session display metadata, while connectivity and ping remain live presentation metadata; none affects final order or totals.

No result API loads scenes, issues Return/Leave, changes reservations or chooses navigation. [Post-match Application Flow](post-match.md) captures the handoff, presents Podium and owns explicit rematch, Return and Leave destinations. It retains the Finished arena for checkpoint/reconnect support until navigation ends that match lifetime.

## Reset and disposal contract

Reset means **dispose and reconstruct**, never rewind a live simulation or reuse a Finished owner. Application Flow ends the current arena through the existing session lifecycle, disposes its driver/native arena, and starts a new generation through Game Load / Sync. `GameLoop.Dispose` clears its context, phase/tick/deadline/outcome, results and revision, and permanently rejects reinitialization. A fresh owner is required even after an interrupted Initialization or Countdown.

`VehicleNetworkDriver.Dispose` is idempotent: it disables advancement and participation, releases simulation/mode, input/prediction/history, item/prop/publication and entry references, and detaches its admission and migration callbacks and native presentation delegates. It never closes the caller-owned gateway or mutates lobby reservations. Delayed disposal of an old owner cannot detach a newer owner's callbacks. `DevelopmentSession.RemoveArena` calls disposal before native removal, clears loader time and held standings intent, and releases the arena; native `_ExitTree` also disposes the driver.

| Match-scoped: reconstructed for each arena generation | Session-scoped: owned outside Game Loop |
| --- | --- |
| Context, countdown, phase, completion tick/outcome/results, Circus banked score/K/D/streak/pending stunt state, score totals/wins, consumed-life and damage watermarks, one-shot Force Start | Logical session, connected identities/names, transport bindings, selected map, authority epoch and effective host tuning |
| Simulation tick/input, vehicles/lives/HP/respawn timers and physical memory, both held slots/tokens, active selection, selection watermark, pending uses/projectiles and pickup timers | Session event journal and online membership/coordination |
| Admission/loading acknowledgements, prediction/retransmission/interpolation histories, replication baselines, native arena and effects | Match reservations/departed identities are held by session authority **through Finished**; only explicit abandonment or surrounding session Return/Leave rules clear them |

The next host session constructs fresh simulation, mode, vehicles and items at tick zero with zero scores/watermarks, no winner/deadline/result, initial vehicle lives and empty inventory/projectiles. The loader/sync barrier must complete again. Connected session players and host tuning survive Return; ready flags reset and disconnected reservations/departed history clear under the existing lobby rules. Local disposal itself never grants or revokes remote reconnect rights.

## Existing combat match integration

`GameLoopState` supplies the transition rules used by both the reusable owner and the existing simulation's atomic `MatchAuthority` evaluation. `FirstToTargetMode` reports the existing kill-target outcome; it does not choose lifecycle transitions. `MatchState.Lifecycle` is an immutable projection of the committed match boundary, not a second mutable authority. Existing `MatchPhase.Waiting` maps to pre-countdown Initialization, and Finished projects the recorded winner as a `kill-target` outcome.

The development match adapter retains its current roster threshold, countdown cancellation, Force Start and scoring semantics. Application sessions enable `HostVehicleSession`'s Active-only participation policy: both local and remote driving are neutralized outside Active, including queued/held commands, and item-use requests are denied. Physics and existing committed combat effects continue; this is input eligibility, not a simulation pause or match reset. Isolated development fixtures that omit application entry retain their pre/post-match controls. The existing checkpoint and publication contracts carry match mode with the complete match boundary. [Join-in-progress](sessions.md), [reconnect](reconnection.md), [host migration](host-migration.md) and [standings](standings.md) continue using complete `MatchState`; decoding reconstructs the same lifecycle projection without replaying transitions or scoring.

Application sessions complete the [match-entry barrier](match-entry.md) before Simulation.InitializeMatch accepts the SynchronizedMatchContext through GameLoop.Initialize. The completed context is retained by the simulation; the existing atomic match adapter continues as the sole phase owner. No second mutable GameLoop is advanced alongside it. The match configuration selects Circus or FirstToTarget scoring; both report the existing configured kill-target outcome through this atomic adapter and the shared GameLoopState.Finish transition. Circus adds no timer, phase owner or navigation. Active-match join/reconnect installs the existing phase checkpoint without restarting initialization or countdown.

`VehicleNetworkDriver.AllowsParticipation` combines completed synchronization, current unfrozen authority/session, and the accepted phase. Reliable `TM` publications carry Countdown, Active and Finished independently of unreliable movement snapshots. Local prediction time or a newer world tick never authorizes Active; delayed phase delivery keeps controls neutral. Non-Active publications also neutralize outstanding prediction/retransmission commands without rewinding sequence acknowledgements. Presentation and authoritative physics continue while controls are suppressed.

Fresh admission requires the existing checkpoint/Activate/committed-roster exchange. Initial and resumed application clients acknowledge installed checkpoints through the existing reliable `TE` Synchronized message before the host accepts gameplay packets on that binding. Recovery retains the final phase, winner and score rows. Fresh admission during Finished remains rejected; retained-player resume remains allowed. Migration reconstructs the same participation policy from trusted application composition and restores phase/deadline/tick from the complete checkpoint. Disconnected reservations retain vehicles and do not restart or block an already-running lifecycle.

## Verification

Core tests cover handoff validation, observable phases, authority rejection, transition ordering, duplicate/skipped ticks, countdown deadline and overflow, read-only timer projection, Active-only participation policy, generic outcomes, detached results, codec reconstruction, disposal and repeated lifecycles. Driver tests run three real scoring cycles with fresh load/sync, one Finished publication, retained session tuning/identity and disposal; recovery tests compare final payloads. The native lobby harness uses authoritative Finished fixtures in two eight-player application cycles, verifies the handoff and native teardown, then starts another match. The separate match harness exercises real missile/ram end conditions. Existing session, reconnect, migration and standings suites protect retained behavior.

[Matches](matches.md) · [Simulation](simulation.md) · [Feature index](README.md)
