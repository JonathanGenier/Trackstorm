# Trusted authority leases

## Ownership and protocol

`Trackstorm.LeaseService` is a small, self-hosted ASP.NET service. It owns only an opaque session identifier, authenticated lease holder, authority epoch/fencing token, and expiry. It stores no roster, checkpoint, simulation, vehicle, item, score or match data. Core still chooses the successor, agrees on the checkpoint and owns `CurrentHostId`, `AuthorityEpoch`, restore and gameplay. Production dependencies remain Client → Core; the service references only the Core lease contracts and has no EOS SDK or Godot dependency.

The initial host creates a cryptographically random 256-bit session key, shared only in authenticated, admitted-peer migration checkpoints. Together with a valid identity token this key scopes access to the coordination session. It is never placed in searchable EOS metadata, resume files or diagnostics. All participants are trusted to follow Core election rules; this does not add Byzantine consensus or anti-cheat protection against an admitted malicious peer.

Authenticated `POST /lease/{operation}` accepts `{session, epoch, token}`. The response is `{session, holder, epoch, token, remainingSeconds}`. Identity comes exclusively from the verified bearer token's `sub`, never a request-body holder. `create` requires an absent session and epoch zero. `read` returns a snapshot; it grants no local simulation permission. `renew` requires the current holder, exact epoch/token and an unexpired lease. Each renewal rotates the opaque token so other peers can observe continuing host progress. `takeover` requires an expired lease, a different holder and the exact previous epoch/token; it increments the epoch once. `release` conditionally expires the holder's lease after local gameplay has frozen for intentional Leave. Conflicts return 409, durable-write failures return 503, and responses are not cacheable.

Core's deterministic agreement completes **before** the candidate attempts takeover. Only a successful grant permits Core's next epoch to commit. Other survivors independently confirm the committed holder/epoch with the service before installing the agreed checkpoint. The service never selects candidates or votes. Previous tokens cannot renew, release or take over a newer grant. A former host returns through ordinary authenticated player resume and does not recreate authority from its saved locator.

## Timing and failure

The service lease lasts **10 seconds**. Hosts renew every **2 seconds**; other peers poll every **1 second**. Client simulation permission lasts at most **8 seconds from request start**, using monotonic time, and responses taking **3 seconds or longer** are discarded. The two-second margin separates local retirement from service expiry; network/processing delay consumes local permission rather than extending it. These bounds assume cooperative clients and normally progressing monotonic clocks. Suspension expires permission before the next simulation step. Expired local authority is irrevocable for that epoch, even if an earlier renewal later completes.

A healthy host retains authority and continues through a P2P partition while its service lease and EOS membership proof remain fresh. A client freezes/reconnects; transport loss, EOS ownership promotion or a cached member omission cannot authorize takeover. A process crash stops renewal. With a current checkpoint, reachable service and available electorate, takeover follows the remaining ten-second lease plus polling/agreement/transport delay, independently of the former player's 30-second reconnect grace. The deterministic two-player tests bound normal crash recovery to less than 13 seconds; this is not a network latency guarantee.

A coordination outage grants no new authority. Existing hosts stop at their conservative local deadline; clients cannot promote from cached expiry. Lost acknowledgements may extend the service's exclusion window without granting the requester permission, which sacrifices availability safely. Host renewal tokens also let checkpoint selection reject a pre-partition copy after later continuing host progress. The [migration document](host-migration.md) owns rollback limits and recovery deadlines.

## Durable single-writer storage

The minimal implementation serializes conditional operations under one lock and holds an exclusive file lock for the entire process lifetime. It flushes a replacement ledger before acknowledging a grant. Epoch/token records are retained; expired sessions are not recreated or silently evicted. The 10,000-session capacity fails closed when full. Each write replaces the small ledger; this is intended for development-scale self-hosting, not a large multi-tenant fleet.

After restart, all persisted grants are quarantined for a full ten seconds on the new process's monotonic clock. Old renewals are rejected, and takeover becomes possible only after quarantine. This avoids trusting a persisted UTC expiry or a reset process clock. A restart can reduce availability; it cannot shorten an old holder's possible permission.

Run **one instance with one durable local ledger**. Independent replicas, ledger deletion, restoring an old backup while sessions may be live, unreliable/shared filesystem locking, or loss of durable storage violate the fencing guarantee. Keep ledger and lock files private and outside the publish directory. Do not run two copies behind a load balancer. Replacing this store with a provider's linearizable conditional transaction is possible behind the same protocol; no such adapter is implemented.

## Authentication and operation

Every lease request requires a signed RS256 identity JWT from the operator-configured HTTPS JWKS source, exact issuer and audience, a valid expiration with zero clock skew, a subject and the configured `pfdid` deployment. The game copies the current EOS Connect ID token on its owner thread and transmits it only to `TRACKSTORM_LEASE_URL` over HTTPS, with redirects disabled. Tokens are never persisted or logged. JWKS refresh uses the standard IdentityModel configuration manager. Unknown keys or unavailable validation data fail authentication.

The API/store are independent of the cloud hosting provider. The current authentication policy uses EOS-compatible deployment claims; other identity providers must issue the same scoped contract or supply an explicit authentication adapter. Configuration is operator-controlled, never inferred from an unverified incoming token. TLS may terminate at Kestrel or a reverse proxy on the same machine; plaintext ingress is permitted only from loopback. Configure request limits/rate limits at that proxy and do not log request bodies or authorization headers.

See [self-hosting setup](../authority-lease-service.md), [EOS development](../eos-development.md), and [reconnection](reconnection.md). `LeaseServiceTests` exercises actual HTTP authentication with locally signed test keys plus deterministic renewal, atomic contention, stale tokens, crash expiry and restart quarantine. Client tests exercise delayed responses, outages, P2P partitions, two/three-player agreement, sequential migration and former-host return. Local tests do not establish deployed TLS, real EOS token configuration or separate-PC behavior.

[Feature index](README.md)
