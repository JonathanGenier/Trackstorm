# TS-46 authority-fencing investigation

## Scope and status

Investigation of `ts-46-jg`, PR #27, head `25fee16c99c4657c93b8dec9e911fd0d551f0cbb`, following the reported two-PC lobby and arena process-kill failures. This is design evidence, not implemented behavior or real-EOS verification.

The latest user clarification requires a healthy host to continue through a P2P partition while its EOS coordination proof is fresh. A survivor may promote only after safe fencing, with a short timeout independent of player reconnect grace. Former-host state must survive through the current match. The previous transport-only interpretation is not authorized.

## Verified implementation findings

- `SessionMigration.Advance` waits at least the full advertised reconnect grace before checking retirement. The default is 30 seconds.
- `OnlineLobbyCoordinator.HostRetired` requires a host departure callback, a further ten-second wait, fresh local membership, and an exact survivor cohort.
- `SessionMigration.Recoverable` measures external checkpoint receipt freshness against the delayed service departure event. A checkpoint can become ineligible before fencing succeeds, despite being recent when gameplay transport was lost.
- `EosLobbyProvider.ConfirmMembership` writes a random `coordination` member attribute with `LobbyAttributeVisibility.Private`. The pinned SDK documents Private as visible only to its writer, not to other lobby members or search results.
- The ten-second lease is a local `TimeProvider` deadline following successful writes. EOS does not manage that deadline, and the survivor cannot observe its renewal or expiry through the existing provider interface.
- The inspected SDK lobby update options provide attribute writes, not an expected-version conditional write or an expiring lease acquisition operation. Search provides snapshots; the inspected documentation does not establish the consistency guarantee needed to infer exclusive authority from an unchanged value.
- Restored former hosts receive ordinary reconnect deadlines. The saved resume locator also expires after two minutes. Both conflict with arbitrary same-match former-host return.

These findings explain concrete blocking paths. They do not establish the precise callback timing on the physical PCs without their logs.

## Why a public heartbeat alone is insufficient

Making the member attribute public would expose positive liveness evidence. It would not make missing notifications, an unchanged cached copy, a failed query, or an undocumented-staleness search result proof that the host has stopped renewing. A host can continue receiving successful renewals while the survivor sees stale information. A local timeout in that situation would create two authorities.

An epoch on gameplay messages protects peers that have adopted that epoch. It does not stop a disconnected old host from executing its own simulation. That host must independently lose permission to simulate before the successor may commit.

## Concrete safe coordination contract

One possible implementation uses a small trusted coordination service, separate from gameplay simulation. Before implementing it, its introduction and deployment must be explicitly authorized; the current repository has no such service.

The service must authenticate membership and provide atomic operations scoped to the logical session:

1. Renew a lease only for the currently recorded holder and fencing generation, before expiry. Renewal cannot resurrect an expired holder.
2. Return authoritative lease state, including the holder, generation and expiration, with documented consistency.
3. Acquire the successor lease only after the old lease has expired and only for the expected prior generation and agreed candidate. Concurrent acquisitions cannot both succeed.

For example, renew every two seconds with a ten-second lease. The host uses a conservative monotonic deadline measured from request start, checks it before every simulation step, and irreversibly retires on expiry. A delayed success cannot revive it. The service's expiry and the host's conservative deadline must be specified together; wall-clock synchronization between PCs is not assumed.

Trackstorm still derives the candidate from the checkpoint, obtains the required survivor agreement, restores Core state, and commits exactly one next `AuthorityEpoch`. The service supplies exclusive permission, not player IDs, simulation, checkpoints, or an independent gameplay election. EOS continues to provide identity, membership, discovery and P2P.

Alternatively, an EOS-supported operation with equivalent documented fencing semantics could satisfy this contract. No such operation was established in this investigation. Service departure remains the existing safe fallback, but does not promise a short crash-detection bound.

## Implementation after the coordination contract is available

- Separate transport loss, authority fencing, survivor agreement and player reservation timers. Never wait for player reconnect grace before checking a completed fence.
- Retain the first monotonic local gameplay-loss boundary and the existing 240-tick rollback check. Validate checkpoint receipt age independently of subsequent fencing delay. If coordination proves that the old host continued running after the original interruption, do not indefinitely preserve the original boundary as justification for restoring a now-stale checkpoint.
- Require valid fencing at selection and commit. Failed or stale coordination reads cannot authorize migration.
- Preserve authenticated former-host identity and complete vehicle/lifecycle/item/score continuation through the current match, including subsequent migrations. Disconnected input remains neutral. Clean retained state up at match/session teardown.
- Let restart routing outlive the current two-minute hint while keeping Core session, identity and connection-generation validation authoritative. Retained Locked resume remains passwordless; fresh admission remains gated.
- Test healthy-host P2P partitions, real stopped renewals, lost/delayed/duplicated renewal responses, process suspension, stale reads, concurrent claims, exact expiry boundaries, sequential migrations and all requested state/resume cases.

## Verification boundary

Prior baseline checks on the unchanged head passed eight Core migration tests and six focused transport tests. The transport test build initially collided with a simultaneous Core build; its sequential rerun passed. These tests encode the existing policy and do not verify the requested replacement protocol. No new implementation, full verification run, physical-PC test, commit, push or PR merge is claimed by this report.
