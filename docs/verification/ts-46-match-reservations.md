# TS-46 match-long player reservation correction

## Former-host restart with stale EOS routing — 2026-09-18

This correction continues reviewed head `6af049f1ed39025159301956fb15b472489ccb9f` on the existing `ts-46-jg` / PR #27. No new branch, PR, merge or Story critique round. Fetch confirmed `origin/main` remains `b409017aa43cf5670762aeca20ce21fa7ac92297`, already integrated. TS-46 comments 10063/10064 and TS-51 comment 10067 remain the approved requirements.

**Physical evidence supplied by the user:** Tests 1–5 and 7–13 PASS. Test 6 migrates immediately after active-host hard-kill and the replacement becomes authoritative, but former-host restart remains on `Waiting for replacement host routing...`. This supersedes older pending/failure summaries below; no new separate-PC result is claimed.

**Root cause verified by code and deterministic reproduction:** `MigrationCompleted()` records the Core-committed epoch/host locally, while `CoordinateMigration()` publishes the route only when that coordinator is also the EOS owner. EOS ownership/attributes can remain at old host / epoch 1 after trusted gameplay authority reaches epoch 2. Restart had only the saved EOS locator and metadata. Its correct self-route guard then waited indefinitely. The earlier `FormerHostResumesThroughProviderAndCoreWithoutPassword` test explicitly replaced fake-service ownership/route with epoch 2 before asserting successful return, so it covered eventual publication rather than permanently stale metadata. Both new Public/Locked cases first failed against the uncorrected production code with the exact waiting status.

**Correction:** normal lease responses include an opaque read-only `routingId` for the same Durable Object. Authenticated `/lease/route` reads only that object's existing holder/epoch/remaining duration; it exposes neither the private session key nor the token and creates no second routing record/store. The provider-neutral HTTP adapter resolves this address from a saved arena locator after EOS membership recovery. Only a timely, live, non-self route consistent with the saved epoch/host fence may select the connection target. Core Resume still validates authenticated subject/session/PlayerId/generation and returns the former host as CLIENT. Lease writes, election, checkpoint agreement, AuthorityEpoch, token fencing and healthy-client request cadence are unchanged. No EOS promotion is required.

**Regression coverage:** production coordinator, binding, EOS packet framing, lease lifecycle and vehicle drivers over deterministic provider boundaries now exercise abrupt host disappearance, automatic fenced epoch-2 takeover, permanently stale EOS owner/route, new-process locator load, trusted routing, explicit Resume (never Join), same PlayerId/session, generation +1, retained vehicle/life/HP/held Missile, complete arena checkpoint, exactly two players/vehicles, and rejection of former-host Return privilege. Both Public and Locked cases pass without access-code re-entry on restart. Ten additional cases cover unavailable service, self route, older epoch, conflicting same epoch, wrong locator, expired lease, malformed holder, invalid duration, delayed response and cancellation. Existing normal EOS-publication coverage remains. Locator tests retain legacy compatibility and reject malformed routing IDs. Actual local Worker HTTP/SQLite tests verify authentication, route shape/token secrecy, stable address across takeover, unchanged fences after read, and inability to renew using the read-only address.

| Current check | Result |
| --- | --- |
| New Public/Locked crash/restart regression before fix | Both FAIL with exact stuck routing status (expected reproduction) |
| Focused online/lease/locator tests | 85 PASS, including all 78 `OnlineLobbyTests` |
| `check.ps1` | PASS: formatting; Debug/Release builds with zero warnings/errors; 331 Core + 269 non-native Client/transport tests per configuration |
| Worker dry-run build and `node --test test/lease-ledger.test.js test/worker.test.js` | PASS: 12 tests, including actual local HTTP/SQLite routing assertions; no deployment performed by these commands |
| `check-migration.ps1 -Players 2` and `-Players 3` | PASS: native UDP, retained state and sequential migration |
| `check-reconnect.ps1` | PASS: three arena resyncs, native body/state continuity, match-long retention and Return cleanup |
| `check-transport.ps1` | PASS: 280 tests and three native Godot lifecycle cycles |
| `check-migration-processes.ps1 -Players 2` and `-Players 3` | PASS: actual old-host process termination; independent survivors resume epoch 2; clean runtime gates |
| `check-online-lobby.ps1`, minimal headless bootstrap | PASS; fake-provider UI is separate from live EOS evidence |
| `check-eos.ps1 -Authenticate` | PASS: three processes, each with three real login/logout cycles and stable identity |
| `check-eos.ps1 -P2p` | FAIL: generic EOS integration failure. Same failure/exit 1 reproduced after building all affected production files from reviewed HEAD; authentication-only checks pass. No new regression established, but this smoke gate remains unresolved and is not called a pass. |
| Restored-source verification after baseline comparison | Every production source restored byte-for-byte; forced Debug rebuild has zero warnings/errors; 269 non-native tests PASS. The first incremental build reused baseline assemblies because copy restored old timestamps; forced rebuild corrected that local build artifact. |
| Deployed service probe before delivery | `/health` HTTP 200; unauthenticated `/lease/route` HTTP 404, so the new endpoint was not deployed at that check |
| Diff/architecture | Scoped review and `git diff --check` PASS; no new dependency, Shared layer, gameplay authority source or modified fencing rule |

Runtime evidence is under ignored `.godot/former-host-routing-checks/`; process-kill evidence is under `.godot/migration-process-checks/454ab93d94494769b65d85f5a175e48d` and `c002a994bd86426783a3942b1662effe`. Native checks use Godot 4.7.2. Successful native logs contain no ERROR/WARNING or shutdown leak diagnostics. Initial sandbox/tool-version restrictions were resolved with the installed SDK and bundled Node runtime; they were not gameplay failures.

**Remaining acceptance:** deploy the updated Worker to the existing namespace, use the corrected game, and create a fresh match before rerunning physical Test 6 (Public and Locked). Legacy locators lack `RoutingId` and still need EOS metadata to catch up; no private lease key is persisted or recoverable from those files. Authenticated live `/lease/route` and full separate-PC restart are unverified here. The baseline-reproduced EOS/P2P smoke failure remains a separate unresolved gate. Keep PR #27 open and unmerged; do not mark TS-46/TS-51 accepted from local evidence alone.

Files changed for this correction:

- `code/Client/Online/AuthorityLeaseClient.cs`, `HttpLeaseTransport.cs`, `ILeaseTransport.cs`, new `LeaseRoute.cs`
- `code/Client/Online/OnlineLobbyCoordinator.cs`, `OnlineSessionBinding.cs`, `ResumeLocator.cs`, `ResumeLocatorStore.cs`
- `code/Core/Sessions/AuthorityLease.cs` (optional provider-neutral read-only locator only)
- `code/TransportTests/LeaseStore.cs`, `LeaseTransport.cs`, `OnlineLobbyTests.Leases.cs`, `ResumeLocatorTests.cs`
- `services/authority-lease/src/worker.js`, `src/lease-ledger.js`, `test/worker.test.js`
- `docs/authority-lease-service.md`, `docs/features/authority-leases.md`, `eos-lobbies.md`, `host-migration.md`, `reconnection.md`, and this evidence file

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
| `check-migration-processes.ps1 -Players 3` | PASS twice after the harness cleanup correction below: both independent survivors restored epoch 2, retained all three vehicles and exited cleanly with no ERROR/WARNING, leaked AudioStream/AudioStreamPlayback or music-resource diagnostics |
| `check-statistics.ps1` | PASS, 42 assertions, including retained disconnected selection/state and Return cleanup |
| `check-developer-options.ps1` | **PASS after the shutdown correction below**, twice: 238 write + 7 read assertions per run, no ERROR/WARNING/ObjectDB/Image/dummy-texture diagnostics. Original reservation-correction runs failed this gate. |
| `check-lobby.ps1` | PASS: eight players, fresh lobby rejoin, arena retention and Return removing disconnected records/native arenas |
| `check-event-log.ps1` | PASS: production activity feed disconnect wording, filtering, bounded history, replicated events and F3 |
| `check-online-lobby.ps1` | PASS, fake-provider native UI lifecycle |
| Minimal Godot startup (`--headless --quit-after 120`) | PASS, exit 0 and no runtime warnings/errors |
| Full diff against current main | Inspected scope, reconnect/migration/restore integrations, protocol compatibility, dependency direction, current docs, assets/export and generated-file boundaries; `git diff --check` PASS; no added Shared layer, tracked build output, machine paths, conflict markers or Sonniss assets |

Deterministic coverage explicitly includes old 1,800-tick / 7,200-tick boundaries and much longer retention, Public and Locked authorization, repeated resume, disconnected ordinary players through sequential authority restores, generation/identity rejection, Return subject/capacity cleanup, saved ordinary-client membership recovery after 181 seconds, manual resume after safe migration failure, and locator removal after observed Return. Existing two-player/3+ fencing, partition, stale checkpoint, configuration, RNG and duplicate-outcome checks remain enabled.

Intermediate failures were corrected rather than hidden: old arena-removal assertions in Statistics/lobby/Event Log were revised to verify retention followed by Return; the separate-process harness's obsolete positional grace argument was removed after it was interpreted as an expected epoch. Initial failed assertions/build attempts are not final passes. The Developer Options and migration-process shutdown corrections below supersede the original shutdown failures and earlier baseline interpretation. No gate has been waived.

## Developer Options shutdown correction

This narrow follow-up starts from reviewed head `1d63ebea86c7457799d3610abb44a5950332f310` on the same branch/PR. The user confirmed clean current main on Godot 4.7.2 (231 write / 7 read assertions, no diagnostics). That baseline is accepted: this is a branch shutdown regression, not a claimed existing failure on main. Approved TS-46 comments 10063/10064 and TS-51 comment 10067 are unchanged.

**Verified cause and correction:** the exact unmodified branch check reproduced 238 successful write and 7 read assertions followed by one dummy-texture RID and one Image leak. A verbose diagnostic probe saved that Image and identified it as the 2172×724 `assets/hud/Health.png` used by the HUD shader and menu steel treatment. Restoring main's panel logic or diagnostic formatter independently did not resolve it. Freeing Controls/materials or disposing the cached texture alone also did not resolve it; those temporary probes were reverted.

`CombatHud.Component` implicitly converted the shared `Texture2D` into a disposable Godot `Variant` for `SetShaderParameter("steel", ...)`, leaving its independent native reference to managed finalization. The correction explicitly scopes that temporary with `using var steelParameter = Variant.From(steel)`. The shader copies its own parameter reference; the caller's temporary is disposed as the component setup returns. Shared textures, material ownership, rendering and diagnostics remain intact. No shared cached texture is forcibly disposed. Godot's [Variant implementation](https://github.com/godotengine/godot/blob/master/modules/mono/glue/GodotSharp/GodotSharp/Core/Variant.cs) documents the owning disposable representation used by this conversion.

The implicit handoff also exists in main; main's clean result does not establish deterministic disposal. **Inference:** the branch's changed runtime/allocation pattern exposes the finalization-dependent lifetime that main's tested run does not. No individual networking change or exact garbage-collector schedule is claimed as proven. The specific native resource and successful disposal correction were directly verified. Neither `GC.Collect`, warning suppression, log filtering nor weakened runtime assertions is used. The existing `QueueFree` plus four SceneTree frames in the Developer Options harness remains sufficient and unchanged.

Exact correction files:

- `code/Client/Hud/CombatHud.cs`: scope the temporary resource Variant.
- `code/Client/Verification/MenuIntegrationChecks.cs`: the affected runtime check still expected immediate removal on arena Leave. Assert reliable local departure plus the same disconnected retained PlayerId in the two-player roster, matching the already-approved policy. No gameplay/networking implementation changed.
- `docs/features/hud.md`: document the native parameter lifetime.
- `docs/verification/ts-46-match-reservations.md`: this evidence and current acceptance status.

Validation uses `Godot_v4.7.2-stable_mono_win64_console.exe`:

| Check | Result / ignored local evidence |
| --- | --- |
| Exact `./check-developer-options.ps1 -GodotPath <4.7.2>` run 1 | PASS, 238 write + 7 read; no ERROR/WARNING; `.godot/developer-options-checks/4d89f2365ee547bb970f30fd3c6b87a4` |
| Exact check, independent run 2 | PASS, 238 write + 7 read; no ERROR/WARNING; `.godot/developer-options-checks/6e62c44108d44e139401d87b63bff64c` |
| Verbose read-phase reproduction after fix | PASS, no ObjectDB/Image/dummy-texture leak; `.godot/developer-variant-release-probe.log` |
| `./check-hud.ps1 -NoBuild -GodotPath <4.7.2>` | PASS, rendered state/material changes, units, items/placeholders and nine resolutions; clean exit; `.godot/hud-checks/2c72c7cde6544d46af018c194d201697` |
| `./check-menu.ps1 -GodotPath <4.7.2>` | PASS, 68 assertions and clean production Quit after correcting the stale arena-leave assertion; `.godot/menu-checks/2b4fea2bc7c24b7f9605c7667e42cb3d` |
| Focused CombatHud / DeveloperOptions / DeveloperDiagnostics / MenuNavigation test filter | PASS, 14 tests; `.godot/developer-shutdown-focused.log` |
| `./check.ps1` | PASS, formatting, zero-warning Debug/Release builds, 331 Core + 257 non-native transport/Client tests per configuration; `.godot/developer-shutdown-check-final.log` |
| `./check-migration-processes.ps1 -NoBuild -Players 3 -GodotPath <4.7.2>` | Both survivors passed epoch 2; shutdown gate still FAIL with 28 audio stream/playback instances and one music resource; `.godot/migration-process-checks/fc497a5246ad4da3a9f31e8bdcf3c1f0` |

The independent-process migration harness constructs `NetworkVehicleArena` directly and never constructs `CombatHud`; the fixed texture handoff is not on that path. No common cause with its audio diagnostic was established, and no audio cleanup was added at that checkpoint. Physical-PC/live-EOS acceptance remains unresolved. No new Story critique round, branch, PR or merge was performed.

## Migration-process shutdown correction

This narrow follow-up starts from reviewed head `a6b9b10046fda6177a1bb907897a2f967000a309` on the same `ts-46-jg` branch and PR #27. TS-46 comments 10063/10064 and TS-51 comment 10067 remain unchanged. No gameplay migration, checkpoint, election, lease, epoch, reconnect, retention or production audio behavior changed.

**Verified cause and correction:** the unmodified three-player harness reproduced after one clean timing-dependent baseline. Both survivors wrote pass evidence at epoch 2, but role 1 then reported 28 leaked ObjectDB instances, including 14 `AudioStreamWAV`/`AudioStreamPlaybackWAV` instances and one music resource still in use. `MigrationProcessChecks` reused `_resumed` for both the 60-frame migrated-gameplay stability gate and shutdown delay. A survivor that had already waited for its peer's pass file could therefore queue the arena and quit on the next physics frame, before deferred deletion ran `ArenaAudio._ExitTree()` and `VehicleAudio._ExitTree()`.

`MigrationProcessChecks` now has a separate cleanup lifecycle and frame counter. Once both survivors have passed, it disposes transport once, marks the check finished and defers `Complete()`. `Complete()` resets the cleanup counter, queues the arena, awaits six actual `SceneTree.ProcessFrame` signals and only then quits, matching the established `NetworkVehicleChecks.Complete()` ownership pattern. The clean-shutdown gate remains strict; no warning suppression, log filtering, `GC.Collect()` or production audio change was added.

Validation uses `Godot_v4.7.2-stable_mono_win64_console.exe`:

| Check | Result / ignored local evidence |
| --- | --- |
| Unmodified `check-migration-processes.ps1 -Players 3` reproduction | Functional migration PASS, shutdown gate FAIL on role 1 with 28 ObjectDB instances, 14 audio stream/playback objects and one music resource; `.godot/migration-process-checks/ce137dc0975e43d6bd51a5fd38935013` |
| Corrected three-player run 1 | PASS: roles 1/2 reached epoch 2 with host 2 at ticks 169/171; no shutdown diagnostics; `.godot/migration-process-checks/5bad811812554de18e95d608659262d8` |
| Corrected three-player run 2 | PASS: roles 1/2 reached epoch 2 with host 2 at ticks 169/171; no shutdown diagnostics; `.godot/migration-process-checks/3e342cd176974daab3ff424f5525048c` |
| Corrected two-player regression | PASS: survivor reached epoch 2 with host 2 at tick 170; no shutdown diagnostics; `.godot/migration-process-checks/49508e9805e14be18ef416cca01aa41b` |
| `check-migration.ps1 -Players 3 -NoBuild` | PASS: native local UDP lobby and active-match migration, sequential epochs, retained vehicles and complete restore |
| `check-network-vehicles.ps1 -Players 2 -NoBuild` | PASS: host/client production arena replication and deferred audio cleanup; `.godot/network-vehicle-checks/3ef0273e2638402298f6028f56a4610f` |
| `check-audio.ps1 -NoBuild` | PASS, 53 native audio assertions and clean exit |
| `./check.ps1` | PASS: formatting, zero-warning Debug/Release builds, 331 Core + 257 non-native transport/Client tests per configuration |

Exact correction files:

- `code/Client/Verification/MigrationProcessChecks.cs`: independent deferred cleanup lifecycle and six-frame shutdown wait.
- `docs/verification/ts-46-match-reservations.md`: reproduction, correction and current verification evidence.

## Remaining acceptance

All local automated gates, including the separate three-player audio clean-shutdown gate, now pass. TS-46 and TS-51 remain In Progress pending the physical-PC/live-EOS acceptance below; PR #27 remains open and unmerged.

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
