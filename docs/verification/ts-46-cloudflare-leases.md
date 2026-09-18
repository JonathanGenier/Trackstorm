# TS-46 / TS-51 Cloudflare lease implementation evidence

Recorded 2026-09-17 on the existing `ts-46-jg` branch and [PR #27](https://github.com/JonathanGenier/Trackstorm/pull/27). This records the approved replacement of the earlier self-hosted ASP.NET backend. Main `15f8e51` was integrated in `3c0063b`; the Statistics changes from that integration are preserved. The PR remains unmerged. [TS-46](https://jonathangenier.atlassian.net/browse/TS-46) and [TS-51](https://jonathangenier.atlassian.net/browse/TS-51) retain the approved scope and outstanding acceptance criteria.

## Implemented boundary

The Worker authenticates EOS Connect JWTs and routes each private lease session ID to its own SQLite-backed Durable Object. Each object stores only session, verified holder, epoch, opaque token and expiry. Conditional transactions implement create/read/renew/release/expired takeover. There is no global match object, roster, gameplay, checkpoint or match state in Cloudflare. Core election, SessionMigration, checkpoint agreement/restore and the provider-neutral HTTP transport are unchanged by this architecture correction.

Ten-second service leases retain the existing two-second renewal and eight-second client permission deadline from request start. P2P-only loss cannot replace a renewing host; crash fencing does not wait for player reconnect grace. Token rotation rejects stale renew/release/takeover requests. Competing takeovers have one winner and one epoch increment. Client retirement and delayed-response checks remain in the existing game-side lease client. Former-host identity/state remains reserved through the match and resumes as CLIENT without automatically reclaiming authority.

The supported hosted backend is now Cloudflare only. The ASP.NET executable, disk ledger, authentication packages, dedicated service test project, publishing script and obsolete notices were removed. A small in-memory C# lease reference model remains exclusively in TransportTests to preserve deterministic migration tests without requiring backend tooling for game development. It is not a deployable server.

The normal endpoint comes from `config/authority-lease-endpoint.json`, embedded in the Client assembly for editor/export. The deployed `https://trackstorm-authority-leases.crypt-jo-g.workers.dev` URL is now bundled, so gamers need no endpoint configuration. `TRACKSTORM_LEASE_URL` remains an optional developer/test override.

Primary deployment is GitHub-connected Cloudflare Builds/dashboard, documented in the [deployment guide](../authority-lease-service.md). Node/npm/Wrangler run there; normal Godot/.NET development and `check.ps1` do not require them. Optional backend tools are isolated under `services/authority-lease`, pinned by the lockfile and excluded from Godot scanning/export. The bundled jose MIT notice remains present after compilation and is served at `/licenses`.

## Authentication and operational limits

**VERIFIED contract:** Epic's live [Connect reference](https://dev.epicgames.com/docs/epic-online-services/eos-fundamentals/connect-interface/connect-reference#id-tokens) was inspected. The Connect JWKS URL is `https://api.epicgames.dev/auth/v1/oauth/jwks`; audience is the EOS client, subject the PUID, and product/sandbox/deployment are `pfpid`/`pfsid`/`pfdid`. The issuer uses the official HTTPS origin. The implementation checks signed RS256 tokens using pinned jose, required key ID, audience, issue/expiry times, origin and exact configured product scope. Identity is never taken from the request body. No raw token/session/provider-error logging was added. Live EOS token acceptance against the deployed Worker remains unverified.

**INFERRED safety assumptions:** Durable Object serialization/transaction guarantees and normally progressing provider/client clocks underpin the fence. Restart/eviction/deployment quarantines an existing record for at least ten seconds and rejects old-lifetime renewals; this favors safety over uninterrupted availability. Persisted fences must never be deleted, rolled back or split across independent interchangeable namespaces. There is no garbage-collection API. The service verifies identity and private-session knowledge, while cooperative Trackstorm peers enforce deterministic election; it does not independently know the gameplay roster or provide Byzantine consensus. See [authority leases](../features/authority-leases.md) for the complete contract.

## Local verification

Environment: Windows, .NET SDK 10.0.401, Godot Mono 4.7.2, backend Node 24.19.0. Backend runtime library jose 6.2.12; optional tooling Wrangler 4.134.0 and its selected Miniflare 5.20260917.0-alpha. Dependencies were installed cleanly from the committed lockfile.

| Check | Final result | Local evidence |
| --- | --- | --- |
| `./check.ps1` | PASS: restore, formatting, Debug/Release builds with zero warnings; 328 Core and 229 non-native Client/transport tests in each configuration | `.godot/ts46-cloudflare-full.log` |
| Backend `npm ci`, then `npm test` | PASS: dry-run Worker build and 12/12 tests; no deployment | `.godot/ts46-cloudflare-npm-ci.log`, `.godot/ts46-cloudflare-worker.log` |
| Native-enabled transport suite | PASS on repeat: 240/240 tests plus three Godot transport lifecycle cycles | `.godot/ts46-cloudflare-transport-repeat.log` |
| Two- and three-player native migration | PASS | `.godot/ts46-cloudflare-migration-2.log`, `.godot/ts46-cloudflare-migration-3.log` |
| Separate-process two-player kill | PASS | `.godot/migration-process-checks/6b3cd9b801404413bc1a8e6bbc300a4a` |
| Separate-process three-player kill | Clean PASS on repeat | `.godot/migration-process-checks/bea3d0aa3af74cd4aa3a9f7f96212239` |
| Reconnect and fake-provider online lobby | PASS | `.godot/ts46-cloudflare-reconnect.log`, `.godot/ts46-cloudflare-online-lobby.log` |
| Native lobby | Clean PASS | `.godot/lobby-checks/a5b49460da7e4b5e9a7e185949e675a5` |
| Statistics integration from current main | PASS: 39 assertions | `.godot/ts46-cloudflare-statistics.log` |
| Event Log integration | Clean PASS on repeat | `.godot/ts46-cloudflare-event-log-repeat.log` |
| Minimal GdUnit bootstrap | PASS: one test, zero errors/failures/orphans | `.godot/ts46-cloudflare-gdunit.log` |
| Documentation links, license inclusion and diff whitespace | PASS | Relative feature links resolved; compiled Worker contains jose notice; `git diff --check` |

The eight deterministic backend cases exercise renewal through a P2P partition, exact expiry, sixteen competing takeover attempts, sequential epochs, conditional release, stale fences, storage outage, restart quarantine, isolation, malformed requests and incompatible persisted state. Four further tests exercise real RSA/JWT validation and actual local Worker HTTP/SQLite Durable Objects, including forged/mismatched/expired tokens, unauthorized holder injection, session isolation, competing claims and unreachable/unconfigured coordination. Existing C# delayed-response, outage, complete-checkpoint, two/three-player election and former-host resume assertions were preserved; only their reference lease-store setup changed. Seven endpoint cases cover bundled defaults, overrides and rejected addresses.

## Deployment configuration update

On 2026-09-17, Cloudflare deployment completed for `trackstorm-authority-leases`. The deployed `LEASE_SESSIONS` Durable Object binding and four required EOS runtime identifiers were confirmed, backend tests passed during deployment, and `GET /health` returned `{"status":"up"}`. The public HTTPS URL is now bundled in `config/authority-lease-endpoint.json`; `TRACKSTORM_LEASE_URL` remains the optional developer/test override.

After integrating current `main`, the focused endpoint suite passed 8/8 cases in Debug and Release, including loading the actual embedded resource. Final `./check.ps1` passed restore, formatting verification, zero-warning Debug/Release builds, 328 Core tests and 253 non-native Client/transport tests in each configuration. The scoped diff was inspected and contains no EOS Client Secret, Cloudflare token, EOS JWT, private lease session ID or other credential.

This update verifies deployment, reachability and bundled configuration only. Live EOS Connect JWT authentication, lease creation/renewal/takeover, host migration through the deployed Worker and physical-PC acceptance remain unverified.

Initial failures are retained rather than hidden:

- Initial backend HTTP tests used the older Miniflare constructor shape; switching to the installed package's documented conversion adapter fixed that setup failure. All twelve tests then passed, including after the final persisted-state validation change.
- An initial C# style error in test-model member order was corrected before the complete passing Debug/Release run.
- First native transport run passed 239/240: `VehicleLoopConvergesAcrossNetworkConditions(2, 0, 0, 2.0f)` observed no rejected stale packet. Inspection found its single stale injection uses unreliable delivery under packet loss; packet loss is a plausible explanation, not a traced diagnosis. One repeat passed all 240. No test assertion or gameplay implementation was weakened to obtain the pass.
- First three-player process-kill run completed both survivors' functional assertions but reported 28 ObjectDB instances and one resource at shutdown (`c4f1ec61bb7047d0a2d8933111d42e7d`). One repeat was clean. First Event Log run likewise passed functionality but emitted texture/ObjectDB shutdown diagnostics; one repeat was clean. No underlying leak fix is claimed.

## Remaining external acceptance

Cloudflare deployment is complete. The `LEASE_SESSIONS` Durable Object binding deployed, backend tests passed during deployment, the four required EOS runtime identifiers are configured, and `GET /health` returned `{"status":"up"}` at the bundled endpoint. This verifies deployment and reachability only; no live EOS Connect JWT, lease creation/renewal/takeover, migration or physical-PC result is claimed. The latest earlier physical reports remain Public Leave PASS, Public lobby process kill FAIL and Public active-match process kill FAIL on the older build; local evidence and `/health` do not supersede them.

The operator must preserve the deployed Durable Object namespace, binding/migration, runtime identifiers and trusted HTTPS endpoint across later deployments. Keep automatic independent preview deployments disabled. Build both PCs with the bundled endpoint and verify real Connect authentication and lease acquisition/renewal before migration acceptance.

First verify normal **Public lobby host/join**, then run **Public lobby, two PCs, abrupt host-process kill** and **Public active match, two PCs, abrupt host-process kill**. The survivor must stay in the same logical session/match and migrate promptly after fencing. Only after both kill scenarios pass proceed to former-host CLIENT return after more than thirty seconds and more than two minutes within the match, Locked return, coordination outage, P2P-only partition, sequential migration and three-or-more-player agreement. Jira remains In Progress pending this evidence. PR #27 must not be merged as part of this work.

## Exact files in this architecture correction

The list below excludes the separately integrated main changes and previously committed Story work. `A` means added, `M` modified and `D` removed. Generated dependencies, bundles and local test artifacts are ignored and not delivered.

| Change | File |
| --- | --- |
| M | `.gitignore` |
| M | `check.ps1` |
| M | `code/Client/Online/EosIdentityService.cs` |
| A | `code/Client/Online/LeaseEndpointConfiguration.cs` |
| D | `code/LeaseService/JwksRetriever.cs` |
| D | `code/LeaseService/LeaseStore.cs` |
| D | `code/LeaseService/Program.cs` |
| D | `code/LeaseService/Trackstorm.LeaseService.csproj` |
| D | `code/LeaseServiceTests/HttpLeaseTests.cs` |
| D | `code/LeaseServiceTests/LeaseStoreTests.cs` |
| D | `code/LeaseServiceTests/Trackstorm.LeaseService.Tests.csproj` |
| M | `code/TransportTests/AuthorityLeaseClientTests.cs` |
| M | `code/TransportTests/EosP2pTransportTests.cs` |
| A | `code/TransportTests/LeaseEndpointConfigurationTests.cs` |
| A | `code/TransportTests/LeaseStore.cs` |
| M | `code/TransportTests/LeaseTransport.cs` |
| M | `code/TransportTests/Trackstorm.Transport.Tests.csproj` |
| A | `config/authority-lease-endpoint.json` |
| M | `docs/authority-lease-service.md` |
| M | `docs/eos-development.md` |
| M | `docs/features/authority-leases.md` |
| M | `docs/features/eos-lobbies.md` |
| M | `docs/features/eos-p2p.md` |
| M | `docs/features/host-migration.md` |
| M | `docs/features/README.md` |
| M | `docs/features/reconnection.md` |
| D | `docs/licenses/authority-aspnet-notices.txt` |
| A | `docs/licenses/authority-jose.txt` |
| D | `docs/licenses/authority-leases.txt` |
| M | `docs/verification/README.md` |
| A | `docs/verification/ts-46-cloudflare-leases.md` |
| D | `publish-lease.ps1` |
| M | `README.md` |
| A | `services/authority-lease/.gdignore` |
| A | `services/authority-lease/package-lock.json` |
| A | `services/authority-lease/package.json` |
| A | `services/authority-lease/src/eos-identity.js` |
| A | `services/authority-lease/src/jose-license.js` |
| A | `services/authority-lease/src/lease-ledger.js` |
| A | `services/authority-lease/src/worker.js` |
| A | `services/authority-lease/test/lease-ledger.test.js` |
| A | `services/authority-lease/test/worker.test.js` |
| A | `services/authority-lease/wrangler.jsonc` |
| M | `THIRD_PARTY.md` |
| M | `Trackstorm.Client.csproj` |
| M | `Trackstorm.sln` |
