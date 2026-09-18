# TS-46 phase-specific disconnect policy verification

## Scope

This correction implements the approved 2026-09-17 disconnect/host-transfer policy on the existing `ts-46-jg` Story branch and PR #27. It does not change EOS P2P gameplay, the Cloudflare Worker/Durable Object lease contract, `CurrentHostId`, `AuthorityEpoch`, checkpoint selection, stale-epoch rejection, or old-authority fencing.

Core now publishes an explicit `SessionReconnectPolicy` derived from the current phase:

- `Lobby` uses `FreshJoin`: intentional and unexpected departure remove the player, authenticated subject and roster slot immediately. No deadline, retained-host flag, Core resume, or local resume locator survives. A later return is ordinary Public/Locked admission with a newly allocated PlayerId.
- `Arena` uses `RetainedResume`: unexpected ordinary-player loss retains the existing grace behavior. Host loss begins migration without waiting for that player grace. The agreed checkpoint retains the former host's PlayerId, vehicle/lifecycle, HP/items, score/rank/statistics and connection generation; authenticated return increments generation and remains CLIENT-only.

Lobby migration still preserves the logical session. The restored authority omits the former host, clears Ready, and installs every continuing survivor's already authenticated migration transport directly rather than constructing lobby reconnect reservations. Transfer still requires the existing checkpoint agreement and trusted lease acquire/confirm boundary.

## Changed files

- Core policy/state: `code/Core/Sessions/SessionReconnectPolicy.cs`, `LobbySnapshot.cs`, `LobbyAuthority.cs`.
- Client policy integration: `code/Client/Networking/LobbyNetworkDriver.cs`, `SessionMigration.cs`, `code/Client/Online/OnlineLobbyCoordinator.cs`.
- Native verification flows: `code/Client/Verification/MigrationIntegrationChecks.cs`, `ReconnectIntegrationChecks.cs`.
- Deterministic regression coverage: `code/Tests/EventStreamTests.cs`, `code/Tests/Networking/MigrationTests.cs`, `code/Tests/Sessions/LobbyTests.cs`, `ReconnectTests.cs`, `code/TransportTests/ActivityFeedTests.cs`, `EosP2pTransportTests.cs`, `OnlineLobbyTests.cs`, `OnlineLobbyTests.Leases.cs`.
- Current-system documentation: `docs/eos-development.md`, `docs/features/eos-lobbies.md`, `eos-p2p.md`, `host-migration.md`, `reconnection.md`, `sessions.md`.
- A pre-existing local EOS client-secret rotation in `code/Client/Online/EosClientConfiguration.cs` was preserved; `code/TransportTests/EosIdentityTests.cs` now expects its non-secret SHA-256 fingerprint so whole-branch verification is coherent.

## Automated verification

VERIFIED on 2026-09-17:

- Focused Core lobby/migration: 31 passed.
- Focused online/lease/resume: 69 passed.
- `./check.ps1`: formatting; zero-warning Debug and Release builds; 329 Core and 253 required non-native Client/transport tests in each configuration.
- `check-transport.ps1`: 264 tests plus three Godot node creation/poll/message/cleanup cycles passed.
- `check-migration.ps1 -Players 2` and `-Players 3`: intentional lobby host Leave removes the former host, fresh return receives a new PlayerId, active-match loss advances authority safely, sequential migration reaches epoch 3, and retained vehicles/configuration/items/RNG remain coherent.
- `check-migration-processes.ps1 -Players 2` and `-Players 3`: forcibly terminated active-match host; independent survivors restored epoch 2 and exited cleanly.
- `check-reconnect.ps1`: lobby disconnect removes immediately and rejoins fresh; three active-match resyncs preserve one native vehicle, HP/item/spawn/match state, standings/ping and generation fences; arena grace expiry removes once.
- `check-lobby.ps1`: all functional stages passed, including immediate lobby removal and fresh identity. The first run then emitted the previously documented Godot ObjectDB/resource shutdown diagnostic; an immediate `-NoBuild` repeat passed the functional and clean-runtime gates. No leak fix or weakened assertion is claimed.
- `check-statistics.ps1`: 39 assertions passed.
- `check-developer-options.ps1`: 238 write assertions and 7 read assertions passed with a clean runtime.

The deterministic suites cover intentional lobby Leave, targeted abrupt lobby membership loss in Public and Locked lobbies, fresh lobby admission, two-player and 3+ lobby migration, trusted-fence delay, active-match host Leave/crash, ordinary active-match client grace, former-host CLIENT resume, sequential epochs, stale generations/epochs, duplicate prevention and retained gameplay/score projections.

## Limitations and remaining acceptance

UNVERIFIED: no real EOS or physical-PC acceptance was performed. Automated local UDP, fake-provider and local lease tests do not prove residential P2P behavior, live EOS Connect token acceptance through the deployed Worker, or physical machine timing.

Keep TS-46 and TS-51 In Progress and PR #27 open/unmerged. Required physical follow-up is Public and Locked lobby intentional/abrupt client removal and fresh return; Public two-PC lobby host Leave/kill; Public active-match host Leave/kill; former-host same-PlayerId CLIENT return with HP/items/score/rank/statistics; ordinary client arena resume; P2P-only partition, Worker outage, sequential migration and 3+ agreement.
