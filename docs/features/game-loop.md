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

Finished retains the exact final state and outcome. Repeated or competing reports, further ticks, countdown commands and reinitialization are rejected. A new match requires a new owner and completed handoff. Results navigation, reset/rematch orchestration and game-mode-specific scoring are outside this contract.

## Existing combat match integration

`GameLoopState` supplies the transition rules used by both the reusable owner and the existing simulation's atomic `MatchAuthority` evaluation. `FirstToTargetMode` reports the existing kill-target outcome; it does not choose lifecycle transitions. `MatchState.Lifecycle` is an immutable projection of the committed match boundary, not a second mutable authority. Existing `MatchPhase.Waiting` maps to pre-countdown Initialization, and Finished projects the recorded winner as a `kill-target` outcome.

The development match adapter retains its current roster threshold, countdown cancellation, Force Start and scoring semantics. Application sessions enable `HostVehicleSession`'s Active-only participation policy: both local and remote driving are neutralized outside Active, including queued/held commands, and item-use requests are denied. Physics and existing committed combat effects continue; this is input eligibility, not a simulation pause or match reset. Isolated development fixtures that omit application entry retain their pre/post-match controls. Checkpoint fields and wire formats remain unchanged. [Join-in-progress](sessions.md), [reconnect](reconnection.md), [host migration](host-migration.md) and [standings](standings.md) continue using complete `MatchState`; decoding reconstructs the same lifecycle projection without replaying transitions or scoring.

Application sessions complete the [match-entry barrier](match-entry.md) before Simulation.InitializeMatch accepts the SynchronizedMatchContext through GameLoop.Initialize. The completed context is retained by the simulation; the existing atomic match adapter continues as the sole phase owner. No second mutable GameLoop is advanced alongside it. Active-match join/reconnect installs the existing phase checkpoint without restarting initialization or countdown.

`VehicleNetworkDriver.AllowsParticipation` combines completed synchronization, current unfrozen authority/session, and the accepted phase. Reliable `TM` publications carry Countdown, Active and Finished independently of unreliable movement snapshots. Local prediction time or a newer world tick never authorizes Active; delayed phase delivery keeps controls neutral. Non-Active publications also neutralize outstanding prediction/retransmission commands without rewinding sequence acknowledgements. Presentation and authoritative physics continue while controls are suppressed.

Fresh admission requires the existing checkpoint/Activate/committed-roster exchange. Initial and resumed application clients acknowledge installed checkpoints through the existing reliable `TE` Synchronized message before the host accepts gameplay packets on that binding. Recovery retains the final phase, winner and score rows. Fresh admission during Finished remains rejected; retained-player resume remains allowed. Migration reconstructs the same participation policy from trusted application composition and restores phase/deadline/tick from the complete checkpoint. Disconnected reservations retain vehicles and do not restart or block an already-running lifecycle.

## Verification

Core tests cover handoff validation, observable phases, authority rejection, transition ordering, duplicate/skipped ticks, countdown deadline and overflow, read-only timer projection, Active-only participation policy, generic outcomes and stable one-time completion. Existing simulation match tests exercise the real first-to-target mode through damage/scoring, terminal outcomes and codec round trips. Existing session, reconnect, migration and standings suites protect the retained development integration.

[Matches](matches.md) · [Simulation](simulation.md) · [Feature index](README.md)
