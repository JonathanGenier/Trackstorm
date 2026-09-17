# TS-46 match-long player reservation correction

## Scope and implementation

This authorized correction continues `ts-46-jg` / PR #27 from reviewed head `3be549e3bee8da8fd9d95d6b7d4ce12df4bb29d0`. TS-46 comments 10063/10064 and TS-51 comment 10067 govern the lifecycle. No new branch, PR, merge or Story critique round was performed. Current `origin/main` is `b409017aa43cf5670762aeca20ce21fa7ac92297`, already contained in this branch.

- Lobby remains `FreshJoin`: intentional and unexpected loss remove the player immediately; no reservation or locator survives. Return later requires ordinary Public/Locked admission and a fresh PlayerId.
- Arena remains `RetainedResume`, now for every disconnected player through the match lifetime. The disconnected roster record itself is the reservation. `GraceTicks`, the authority deadline dictionary, checkpoint deadlines, local locator expiry/host exception and transport/membership resume-expiry branches are removed. `AdvanceTime` advances only the session/event clock.
- `Remove` (explicit Leave) applies the same arena suspension rule as unexpected loss. Ownership is retired immediately; neutral input and existing simulation continue. HP/lifecycle/held items/score/rank/statistics remain authoritative, can evolve normally while disconnected, and are restored by the existing complete checkpoint. Resume validates subject, session, PlayerId, generation and fresh peer before incrementing generation once; it does not create another player/vehicle/score row.
- Return is the existing match-lifetime cleanup transition. It removes **all** disconnected players, authenticated subjects and retired-peer history, clears connected readiness/former-host flags, and normal session presentation destroys the old arena. The next Start builds new vehicle/item/match state. A Finished score/result screen alone does not destroy the arena; Return/session teardown is the existing lifecycle boundary. Stable ID allocation never reuses a removed identity.
- Every arena player has the same persistent routing hint with no local clock. An offline hint may outlive a completed match until the next attempt; it grants no authority, and Core rejects its removed PlayerId. Observed Return, malformed/account-mismatched data and authoritative rejection clear hints. Arena Leave offers manual resume without auto-rejoining. A bounded failed migration attempt also preserves the manual player-resume route.
- Host loss still starts recovery without waiting for player retention. The previous host loses authority through the existing freeze/retire/release lifecycle. The Cloudflare lease, epoch, exact token, checkpoint freshness/agreement and stale-stream fences are unchanged. Migration's independent 30-second fencing-failure and 20-second agreement deadlines remain fail-closed; they do not expire a player reservation on a healthy authority. Former-host resume retains the same player and CLIENT privileges.
- Wire versions are now TL 5 / TC 4 and EOS compatibility bucket `trackstorm-lobby-11`, so peers cannot mix deadline-based and match-long reservation schemas. TG authority/generation fencing is unchanged.

## Verification

Logs are in ignored `.godot/match-reservation-checks/`. Tests use local sockets, native Godot, authenticated fake EOS datagrams or trusted test identity/retirement seams. None establishes live EOS/Cloudflare or physical-PC acceptance.

| Check | Result |
| --- | --- |
| `./check.ps1` | PASS: formatting, zero-warning Debug/Release builds, 331 Core and 257 non-native Client/transport tests in each configuration |
| Focused Core sessions/migration/event tests | PASS, 45 tests |
| Focused online/P2P/resume/activity/diagnostic tests | PASS, 130 tests |
| `check-transport.ps1` | PASS, 268 tests and three Godot node creation/poll/message/cleanup cycles |
| `check-reconnect.ps1` | PASS: immediate lobby removal/fresh join; three arena resyncs after advancing both session clocks beyond 181 seconds; retained native body, HP/items/spawns/match/standings/generation; Return cleanup |
| `check-migration.ps1 -Players 2` / `-Players 3` | PASS: native local UDP lobby loss, former-host fresh lobby admission, active-match loss, former-host CLIENT resume, complete state and sequential epochs |
| `check-migration-processes.ps1 -Players 2` | PASS: host forcibly terminated, independent survivor restored epoch 2 and exited cleanly |
| `check-migration-processes.ps1 -Players 3` | Both independent survivors restored epoch 2 and recorded pass evidence; **clean-shutdown gate FAIL** on initial corrected run and one repeat, with 28 AudioStream/AudioStreamPlayback objects and one music resource reported at exit |
| `check-statistics.ps1` | PASS, 42 assertions, including retained disconnected selection/state and Return cleanup |
| `check-developer-options.ps1` | 238 write + 7 read functional assertions PASS; **clean-runtime gate FAIL** on initial and repeat runs due to one ObjectDB/Image/dummy texture at shutdown |
| `check-lobby.ps1` | PASS: eight players, fresh lobby rejoin, arena retention and Return removing disconnected records/native arenas |
| `check-event-log.ps1` | PASS: production activity feed disconnect wording, filtering, bounded history, replicated events and F3 |
| `check-online-lobby.ps1` | PASS, fake-provider native UI lifecycle |
| Minimal Godot startup (`--headless --quit-after 120`) | PASS, exit 0 and no runtime warnings/errors |
| Full diff against current main | Inspected scope, reconnect/migration/restore integrations, protocol compatibility, dependency direction, current docs, assets/export and generated-file boundaries; `git diff --check` PASS; no added Shared layer, tracked build output, machine paths, conflict markers or Sonniss assets |

Deterministic coverage explicitly includes old 1,800-tick / 7,200-tick boundaries and much longer retention, Public and Locked authorization, repeated resume, disconnected ordinary players through sequential authority restores, generation/identity rejection, Return subject/capacity cleanup, saved ordinary-client membership recovery after 181 seconds, manual resume after safe migration failure, and locator removal after observed Return. Existing two-player/3+ fencing, partition, stale checkpoint, configuration, RNG and duplicate-outcome checks remain enabled.

Intermediate failures were corrected rather than hidden: old arena-removal assertions in Statistics/lobby/Event Log were revised to verify retention followed by Return; the separate-process harness's obsolete positional grace argument was removed after it was interpreted as an expected epoch. Initial failed assertions/build attempts are not final passes. The three-player and Developer Options shutdown diagnostics remain unresolved and their gates have not been waived. Similar shutdown diagnostics were recorded in [earlier lifecycle evidence](ts-46-lease-lifecycle.md), including its Developer Options baseline comparison; this run does not claim a new leak fix or a fresh baseline comparison.

## Remaining acceptance

Implementation is delivered for review, but overall repository acceptance is not complete while the two native clean-shutdown gates above fail. TS-46 and TS-51 remain In Progress; PR #27 remains open and unmerged.

Still required on physical PCs with real EOS and the bundled deployed Worker: Public/Locked ordinary-client loss and resume after more than thirty seconds and more than two minutes within the same match; retained HP/items/lifecycle/score/rank/statistics and one vehicle; disconnected Return cleanup and fresh lobby join; lobby and active-match host Leave/kill; former-host same-player CLIENT return; P2P-only partition, Worker outage, changed network/restart, sequential migration and 3+ agreement. Local tests do not supersede the previous physical-PC evidence.

## Exact correction files
- `code/Client/Development/DeveloperDiagnostics.cs`
- `code/Client/Hud/ActivityFeedView.cs`
- `code/Client/Networking/LobbyNetworkDriver.cs`
- `code/Client/Networking/SessionMigration.cs`
- `code/Client/Networking/VehicleNetworkDriver.cs`
- `code/Client/Online/OnlineLobby.cs`
- `code/Client/Online/OnlineLobbyCoordinator.cs`
- `code/Client/Online/ResumeLocator.cs`
- `code/Client/Online/ResumeLocatorStore.cs`
- `code/Client/Statistics/RuntimeStatistics.cs`
- `code/Client/Verification/EventLogIntegrationChecks.cs`
- `code/Client/Verification/LobbyIntegrationChecks.cs`
- `code/Client/Verification/MigrationIntegrationChecks.cs`
- `code/Client/Verification/MigrationProcessChecks.cs`
- `code/Client/Verification/ReconnectIntegrationChecks.cs`
- `code/Client/Verification/StatisticIntegrationChecks.cs`
- `code/Core/Sessions/LobbyAuthority.cs`
- `code/Core/Sessions/LobbyCodec.cs`
- `code/Core/Sessions/LobbyRestoreState.cs`
- `code/Core/Sessions/LobbySnapshot.cs`
- `code/Core/Sessions/MigrationCheckpointCodec.cs`
- `code/Core/Sessions/SessionPlayer.cs`
- `code/Core/Sessions/SessionReconnectPolicy.cs`
- `code/Tests/EventStreamTests.cs`
- `code/Tests/Networking/MigrationTests.cs`
- `code/Tests/Sessions/LobbyTests.cs`
- `code/Tests/Sessions/ReconnectTests.cs`
- `code/TransportTests/ActivityFeedTests.cs`
- `code/TransportTests/EosP2pTransportTests.cs`
- `code/TransportTests/OnlineLobbyTests.cs`
- `code/TransportTests/ResumeLocatorTests.cs`
- `docs/eos-development.md`
- `docs/features/README.md`
- `docs/features/activity-feed.md`
- `docs/features/developer-options.md`
- `docs/features/eos-lobbies.md`
- `docs/features/eos-p2p.md`
- `docs/features/host-migration.md`
- `docs/features/matches.md`
- `docs/features/reconnection.md`
- `docs/features/sessions.md`
- `docs/features/statistics.md`
- `docs/features/vehicle-networking.md`
- `docs/verification/ts-46-match-reservations.md`
- `docs/verification/README.md`
