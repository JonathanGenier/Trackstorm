# TS-46 trusted lease correction — 2026-09-17

## Scope and delivery

The user approved a minimal trusted fencing service, then selected **prepare self-hosted service; hosting details will follow**. Work stays on `ts-46-jg` / PR #27. The PR remains unmerged and real EOS acceptance remains open. No additional Story branch or PR was created. Audio and existing export cleanup are unchanged by this correction.

Current main `a625203` was integrated after it introduced TS-59 Event Log during implementation. Conflict resolution preserves the event journal and adds migration epoch fencing, fresh-epoch journal initialization without gameplay replay, shared restored simulation/journal ownership, and correct elected-host IDs in developer events. This is integration with the Story's authority transition, not a second logging system.

## Code-level cause and correction

The old abrupt-loss path combined the 30-second player grace, an explicit EOS host-departure callback, another ten-second wait, and checkpoint receipt freshness measured against that potentially delayed callback. Missing retirement evidence or a late timestamp could therefore prevent promotion and reject an otherwise recent crash checkpoint. The private EOS member-attribute proof was observable only to its writer; it was not an atomic remote fencing lease. These are directly inspected blocking paths. The exact physical-PC callback timeline was not captured, so it is not claimed as observed.

The separate [investigation](ts-46-authority-fencing-analysis.md) records the original findings. The approved implementation now:

- Uses a trusted ten-second lease, renewed every two seconds, with atomic expected-token takeover and an incremented fencing epoch. Clients stop local authority after eight seconds from request start; responses taking three seconds or more cannot grant permission. A delayed response cannot revive a retired epoch.
- Starts agreement as soon as an authoritative service read confirms expiry. After deterministic survivor agreement, the candidate must acquire the lease before Core commits. Other survivors independently confirm the holder/epoch. P2P loss and EOS ownership never independently grant authority.
- Separates the player's reconnect reservation from authority fencing. Former-host identity, vehicle, lifecycle, HP, items and scores remain represented through the current match. Lobby migration also permits Ready/Start while that former host is absent. Return/session teardown cleans disconnected retained state; a returned former host resumes as CLIENT under normal subject/generation checks.
- Measures checkpoint receipt freshness at first locally detected gameplay loss, extended by any later observed renewal token from the old host. It preserves the independent 240-observed-tick bound and the four-second receipt window, while excluding fencing wait itself. A healthy host continuing through a long partition invalidates the isolated client's stale checkpoint.
- Preserves Public/Locked retained resume authorization, stale epoch/generation/nonce protection, complete checkpoint restore, deterministic three-or-more-player agreement, sequential migration and Developer Options authority.

## Implementation map

| Area | Files |
| --- | --- |
| Coordination contract | `code/Core/Sessions/AuthorityLease.cs`, `LeaseRequest.cs` |
| Standalone service | `code/LeaseService/Program.cs`, `LeaseStore.cs`, `JwksRetriever.cs`, project file |
| Production lease lifecycle | `code/Client/Online/AuthorityLeaseClient.cs`, `HttpLeaseTransport.cs`, `ILeaseTransport.cs`, `EosIdentityService.cs`, `EosSdkPlatform.cs`, `OnlineSessionBinding.cs` |
| Migration/restore | `SessionMigration.cs`, `LobbyNetworkDriver.cs`, `VehicleNetworkDriver.cs`, Core migration checkpoint/codecs and `HostVehicleSession.cs` |
| Player retention | Core `LobbyAuthority.cs`, `SessionPlayer.cs`, `LobbySnapshot.cs`, `LobbyCodec.cs`; Client resume locator/store/coordinator |
| Main integration | `EventStream.cs`, Event Log envelope/restore wiring, migrated developer actor IDs |
| Delivery | Solution/test wiring, `check.ps1`, `publish-lease.ps1`, architecture/feature/deployment docs and third-party notices |

Production compatibility is `trackstorm-lobby-10`, TL v4 and TC v3; TR/TG remain v2. Both physical PCs require this compatible build and the same configured HTTPS lease endpoint.

## Automated coverage

| Required behavior | Direct coverage |
| --- | --- |
| Renewal and healthy P2P partition | Real store renewals over forty controlled seconds; production-framing host continues a fifteen-second partition without client promotion |
| Crash / bounded expired takeover | Two-player lobby and arena scenarios acquire epoch 2 in less than thirteen controlled seconds, without waiting thirty-second player grace |
| Simultaneous claims | Sixteen concurrent expected-token requests produce exactly one winner |
| Stale former-host renewal | Old token/epoch and wrong authenticated holder rejected after takeover; subsequent epoch 3 remains fenced |
| Delayed responses / suspension | Held successful renewal delivered after local expiry cannot revive old authority |
| Service outage | Host simulation freezes by its local permission deadline; disconnected client retains epoch 1 and cannot promote |
| Sequential migration | Host 1 dies, player 2 takes epoch 2, player 1 resumes as CLIENT, player 2 dies, player 1 takes epoch 3 through a new election |
| Former-host retention / resume | Same player and held state after thirty-second grace; locator survives more than two minutes; absent lobby host does not block Start; one reserved vehicle; return/end cleanup |
| Three-player agreement | Lobby/arena common-checkpoint restore and required-survivor denial through the real store; stale Event Log epochs rejected after migration |
| Persistence and endpoint | Exclusive writer; restart quarantine and stale-renewal rejection; real loopback HTTP rejects unauthenticated, forged, wrong issuer/audience/deployment and expired tokens |

## Verification

VERIFIED on Windows x64 with .NET 10.0.401 and Godot 4.7.2 .NET:

- `./check.ps1` after current-main integration: restore, formatting, Debug/Release warnings-as-errors builds pass with zero warnings; **328 Core, 219 non-native Client/transport, and 4 lease-service tests pass in each configuration**.
- Native migration with **2 and 3 players**: intentional lobby Leave, former-host rebind, active match loss, sequential epochs, retained bodies, configuration and gameplay resync pass.
- Separate-process migration with **2 and 3 players**: launcher terminates the host and survivors restore epoch 2; both runs pass cleanly. These harnesses use trusted local identity/retirement seams, not EOS or deployed HTTP fencing.
- Event Log native integration and stale-epoch regression pass; normal reconnect, three arena resyncs, fake-provider Public/Locked browser UI, and eight-player lobby functional stages pass.
- The complete native-enabled transport suite passes all **230 tests**, followed by three Godot transport lifecycle cycles. Vehicle replication, items, pickups, death/respawn and match integration pass; the minimal GdUnit bootstrap passes its one runtime test with no orphans.
- One post-merge lobby run passed all functional stages but reported 24 leaked ObjectDB instances and 3 resources at exit. A single repeat passed cleanly. This matches earlier recorded shutdown diagnostics; no leak fix or guarantee of consistently clean shutdown is claimed.
- The standalone service publishes to ignored `Releases/LeaseService`, including its operator guide and dependency notices. No hosting resources were provisioned.

Full-check evidence is in ignored `.godot/ts46-lease-main-final.log`; runtime evidence uses `.godot/ts46-main-*` logs and each harness's existing artifact directory. The complete Story diff, conflict resolutions, affected documentation and dependency direction were inspected. The coordination service contains no gameplay state or decision logic. The existing Story critique has already reached its three-round limit; this report records the authorized correction and its verification, not a fourth critique round.

## Remaining acceptance and operational limits

UNVERIFIED: deployed HTTPS/TLS and exact live EOS JWT/JWKS configuration, physical-PC lobby/arena process kill, Public/Locked former-host return, real service outages/P2P partitions, changed-network resume, and long-running Internet/large-ledger load. The most recent user-reported physical evidence remains **Public Leave PASS; Public lobby process kill FAIL; Public arena process kill FAIL** on the older build. Local success does not supersede those reports.

The service requires one durable local ledger and one writer, cooperative admitted clients and normally progressing monotonic clocks. Never clone/roll back its live ledger into independent active instances. Capacity is 10,000 retained session records; restart quarantines old grants for ten seconds and can reduce availability. The service carries no gameplay checkpoints or election roster.

After hosting details are supplied, deploy using the [operator guide](../authority-lease-service.md). First rerun **Public lobby host process kill**, then **Public active-match host process kill**, recording takeover time, host/epoch, session/match/player IDs and lease status. Then return the former host after more than thirty seconds and again after more than two minutes within the same match; repeat Locked without password re-entry. Finally test P2P-only isolation while both leases/EOS remain reachable, coordination outage, sequential host deaths and three-player agreement using the [physical-PC procedure](../eos-development.md#host-migration-checks).
