# TS-46 / TS-51 lease retirement and observer lifecycle corrections

## Scope and delivery

Authorized corrections on existing `ts-46-jg` and PR #27. No new branch or PR; no merge of PR #27. Current main `4ea5f0b` was integrated by merge `d5ecd08` before implementation. Live TS-46/TS-51 descriptions and comments, repository routing, relevant feature contracts, PR head and existing tests were inspected. Jira descriptions now include the approved lifecycle requirements.

EOS P2P gameplay, Cloudflare Worker plus one Durable Object per private session, verified EOS Connect identity, atomic fencing, rotating tokens, session isolation, Core election/checkpoint/restore, dashboard deployment and endpoint configuration are preserved. No backend, dependencies, assets, gameplay schemas or timing constants changed.

## Root cause and corrected lifecycle

Production simulation permission combined `coordinator.CoordinationAvailable` with `lease.Available(epoch)`, but the independently pumped lease client never learned that the coordinator had permanently retired local authority. A host could therefore freeze forever while continuing successful Cloudflare renewals, indefinitely excluding its successor.

The binding now exposes the coordinator's existing permanent-retirement flag to migration. Before pumping lease work, migration evaluates that boundary and calls the existing departure lifecycle exactly once: freeze gameplay, retire the local lease epoch, announce frozen-host departure, then pump any pending completion and conditional release. Retirement clears the grant and bootstrap intent. A response cannot grant a retired epoch; no subsequent renewals are scheduled. Release uses the latest accepted rotated token. A response discarded for excessive delay, stale-token release failure or outage safely leaves expiry as the fallback. Release cannot target a later epoch. Even retirement before a retained checkpoint freezes and retires permission.

This introduces no second retirement timeout. Temporary proof delays within the existing EOS window and ordinary client/P2P disconnects do not retire a healthy host. Intentional Leave keeps freeze-before-release ordering. Suspension, service outages, stale fences and former-host CLIENT-only resume retain their existing protections.

Healthy client reads previously came from the lease pump's unconditional final `read` branch. `SessionMigration.NeedsLeaseObservation` now controls that branch using existing host-loss, checkpoint silence, authority pause and agreement state. Each activation invalidates cached freshness and starts observation promptly; recovery, completed client installation and terminal failure stop periodic reads. Writes still grant authority only after successful create/renew/takeover. Cached expiry never substitutes for observation and atomic takeover.

The existing checkpoint progress rule is preserved: changed tokens, including the first observation without a prior token, conservatively advance the freshness boundary to request time. A new regression covers a client unable to observe while its partitioned host continues renewing, then first observing expiry after that host crashes. It refuses stale restoration. This safety choice can decline recovery after a long service-observation outage.

## Cloudflare request pattern

| State | Approximate lease traffic |
| --- | --- |
| Healthy host | One create at bootstrap, then one renewal every 2 seconds: about 30 requests/minute |
| Healthy connected clients | Zero periodic reads; the production healthy-session test sends no client lease requests at all |
| Suspected host loss / migration | Immediate observation, then approximately one read/second per observing survivor; one elected conditional takeover and independent successor confirmation |
| Recovered client / installed successor client | Observer returns to idle |
| Permanently retired host | No new renewals; drain an already-issued request and attempt one conditional release, otherwise await expiry |

Requests are serialized; delay/outage can reduce the rate. For eight healthy players this removes approximately 420 client reads/minute, leaving about 30 host renewals/minute. EOS membership proof traffic is separate and unchanged. Service/local durations remain 10/8 seconds.

## Verification

**VERIFIED** with deterministic clocks and production online coordinator/binding/migration/EOS framing: explicit retirement and EOS proof expiry; healthy Cloudflare during retirement; in-flight renewal; freeze before release; actual arena tick stopping; one successor/epoch increment; no revived old authority; zero healthy client reads; host renewal; temporary proof delay; client/P2P loss; observation start/stop/restart; continuing-host freshness; first observation after outage; true crash; recovery. Existing suites preserve service-outage failure, suspension, Leave, stale tokens, competing takeover, sequential migrations, former-host resume, checkpoint rules and two-/three-player behavior.

| Check | Result / evidence |
| --- | --- |
| Focused coordinator, lease lifecycle and production framing | PASS, 121 tests after final freshness regression |
| `check.ps1` on final implementation | PASS, formatting, restore, zero-warning Debug/Release builds; 328 Core and 252 non-native Client/transport tests in each configuration; `.godot/ts46-lifecycle-full-final.log` |
| Unchanged Worker dry-run build and tests | PASS, 12 tests including actual local HTTP/SQLite Durable Objects, synthetic signed JWTs, contention, stale fences, isolation, expiry and outage; `.godot/ts46-lifecycle-worker.log`; no deployment |
| Native transport and Godot lifecycle | PASS on one repeat, 262 tests plus three Godot cycles; `.godot/ts46-lifecycle-transport-repeat.log` (before the final additional deterministic outage case; that case passed in final focused/full runs) |
| Native migration, 2 and 3 players | PASS; `.godot/ts46-lifecycle-migration-2.log`, `-3.log` |
| Independent-process host kill, 2 players | PASS; `.godot/migration-process-checks/1194c0449f184b53bf417882c7f38eee` |
| Independent-process host kill, 3 players | PASS on one repeat; `.godot/migration-process-checks/692c4e38ecf643648675958fad514adb` |
| Reconnect, fake-provider online lobby, eight-player native lobby | PASS; `.godot/ts46-lifecycle-reconnect.log`, `-online-lobby.log`, `-lobby.log` |
| Developer Options | Functional assertions PASS (238 write + 7 read); **clean-runtime gate FAIL** on initial and repeat runs due to a shutdown Image/dummy-texture leak. Verbose diagnostic and pre-fix baseline comparison reproduce the same leak; `.godot/ts46-lifecycle-developer-read-diagnostic.log`, `-developer-read-baseline.log` |
| Statistics | PASS, 39 assertions; `.godot/ts46-lifecycle-statistics.log` |
| Event Log / integrated activity feed | PASS on one repeat; `.godot/ts46-lifecycle-event-log-repeat.log` |
| Minimal Godot import/bootstrap | PASS, one case, zero failures/errors/orphans; `.godot/ts46-lifecycle-gdunit.log` |

The first native transport run failed the existing single unreliable stale-snapshot injection under 2% loss (261/262). Packet loss is a plausible explanation, not traced evidence. The first three-player process-kill run emitted ObjectDB/resource shutdown diagnostics. Event Log initially emitted an Image/dummy-texture shutdown diagnostic. Their single repeats were clean; no leak fix or weakened assertion is claimed.

Developer Options' clean-shutdown gate remains unresolved. The baseline comparison temporarily restored only the four changed production files from pre-fix merge `d5ecd08`, built the Client, ran the same read phase and reproduced the same Image/texture diagnostic. All four files were then restored byte-for-byte and the Client rebuilt with zero warnings. This is direct evidence that the requested lifecycle corrections did not introduce that diagnostic; it does not fix it or waive the repository's completion gate.

Initial sandbox attempts to run solution restore and Worker tooling were denied access to existing user configuration directories. Authorized runs with the required filesystem access passed. No tooling/dependency policy or game configuration was changed to bypass those restrictions.

## Review and remaining acceptance

The changed production paths, integrated authority/restore boundaries, main merge, affected documentation, dependency direction and complete Story file inventory were reviewed. Core remains provider-neutral; no Shared layer, generated output, private configuration, added dependency or audio change is included. Existing Story critique rounds already reached the repository's three-round limit; this is the authorized correction review and evidence, not a fourth numbered critique. Final integrated clean-runtime acceptance is not claimed because of the Developer Options gate above.

**UNVERIFIED:** deployed Cloudflare and real EOS/physical-PC acceptance. The Worker still requires GitHub-connected dashboard setup, the existing Durable Object binding/migration, the four public EOS runtime identifiers and deployment. The bundled endpoint remains `null`. Once the verified HTTPS URL exists, commit it to `config/authority-lease-endpoint.json` and rebuild. Ordinary developers and players require no Node/npm/Wrangler or per-PC endpoint configuration. See [dashboard deployment](../authority-lease-service.md).

First run Public two-PC lobby abrupt host kill, then Public two-PC active-match abrupt host kill. Then verify permanent EOS retirement while Cloudflare remains reachable, healthy-client zero periodic reads, former-host CLIENT return after 30 seconds and two minutes, Locked return, outage, P2P-only partition, sequential epochs and 3+ agreement. The latest older-build physical results remain lobby/arena process-kill failures until rerun; local tests do not replace them. PR #27 must remain unmerged.

## Exact correction files

- `code/Client/Networking/SessionMigration.cs`
- `code/Client/Online/AuthorityLeaseClient.cs`
- `code/Client/Online/OnlineLobbyCoordinator.cs`
- `code/Client/Online/OnlineSessionBinding.cs`
- `code/TransportTests/AuthorityLeaseClientTests.cs`
- `code/TransportTests/EosP2pTransportTests.cs`
- `code/TransportTests/EosP2pWire.cs` (extracted existing fake datagram implementation for shared composition tests)
- `code/TransportTests/LeaseTransport.cs`
- `code/TransportTests/OnlineLobbyTests.cs`
- `code/TransportTests/OnlineLobbyTests.Leases.cs`
- `docs/features/authority-leases.md`
- `docs/features/eos-lobbies.md`
- `docs/features/host-migration.md`
- `docs/verification/README.md`
- `docs/verification/ts-46-lease-lifecycle.md`

Separately, the required main synchronization imported these existing main changes, without expanding the correction scope: `code/Client/Bootstrap/SimulationBootstrap.cs`, `code/Client/Hud/ActivityFeed.cs`, `code/Client/Hud/ActivityFeedEntry.cs`, `code/Client/Hud/ActivityFeedTone.cs`, `code/Client/Hud/ActivityFeedView.cs`, `code/Client/Verification/EventLogIntegrationChecks.cs`, `code/Core/Events/EventStream.cs`, `code/Core/Simulation/Simulation.cs`, `code/Tests/EventStreamTests.cs`, `code/TransportTests/ActivityFeedTests.cs`, `code/TransportTests/VehicleNetworkDriverTests.cs`, `docs/features/README.md`, `docs/features/activity-feed.md`, `docs/features/event-log.md`, `docs/features/hud.md`, `docs/verification/README.md`, `docs/verification/ts-60.md`.
