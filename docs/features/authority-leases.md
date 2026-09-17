# Trusted authority leases

## Ownership and protocol

Cloudflare Workers plus SQLite-backed Durable Objects are the supported hosted fencing implementation. The Worker authenticates callers and maps the private lease session string through `LEASE_SESSIONS.idFromName(session)`. **Each logical Trackstorm lease session has its own Durable Object** and one durable record. There is no global object containing every match.

Only the opaque session ID, verified holder identity, authority epoch, opaque token and expiry are stored. No roster, checkpoint, simulation, vehicle, item, score or match state enters Cloudflare. Trackstorm/Core continues deterministic candidate selection, survivor agreement, `CurrentHostId`/`AuthorityEpoch`, checkpoint restore and gameplay. The game retains the provider-neutral `ILeaseTransport`/`HttpLeaseTransport` contract; Core has no Cloudflare dependency.

The initial host generates a private 256-bit session ID and shares it only through authenticated admitted-peer checkpoints. It is absent from searchable EOS metadata, resume files, URLs and diagnostics. A verified identity and knowledge of this ID scope access. Admitted peers must follow Core election rules; the service cannot independently verify the elected candidate without owning the excluded gameplay roster. This is cooperative-client fencing, not Byzantine consensus or anti-cheat.

Authenticated `POST /lease/{operation}` accepts exactly `{session, epoch, token}` and returns `{session, holder, epoch, token, remainingSeconds}`. The Worker derives holder identity only from the verified JWT subject. Its private Durable Object binding receives that identity; public request bodies cannot override it.

| Operation | Atomic condition and result |
| --- | --- |
| create | Session absent, epoch 0, empty token; creates epoch 1 |
| read | Returns current expiry; never grants local simulation permission |
| renew | Same authenticated holder, exact epoch/token, still live and issued in this object lifetime; rotates the opaque token |
| takeover | Expired lease, different authenticated holder, exact old epoch/token; increments epoch once |
| release | Same authenticated holder and exact epoch/token; persists expired permission after local gameplay freezes |

Each modification uses a Durable Object storage transaction and is acknowledged only after its write succeeds. Stale tokens cannot renew, release or take over newer authority. Competing requests serialize per session, with one winner. Epochs above JavaScript's exact integer range are rejected; takeover at 9,007,199,254,740,991 fails closed instead of wrapping or rounding. Fences are retained after expiry/release and are never silently recreated or garbage-collected.

Conflicts return 409, invalid input 400, unavailable/rejected authentication 401, and configuration/storage/routing failures 503. Requests are bounded to 2048 bytes and responses are not cacheable. No gameplay fallback follows an error. Core agreement completes before the candidate attempts takeover; other survivors independently confirm its holder/epoch before installing the checkpoint.

## Timing, restarts and outages

The service lease is **10 seconds**, host renewal **2 seconds**, and observer polling **1 second**. Local gameplay permission remains **8 seconds from request start on a monotonic clock**. Responses taking three seconds or more are discarded, and a retired local epoch cannot be revived by a delayed successful renewal. Network and storage delay consume permission rather than extending it.

A healthy host continues through P2P-only loss while its lease and independent EOS membership proof remain fresh. P2P loss and EOS ownership promotion cannot grant a client authority. Host process death stops renewal; with a usable checkpoint and reachable electorate/service, promotion follows expiry and agreement without waiting through the former player's reconnect grace. The [host migration document](host-migration.md) owns freshness, rollback, reservations and failure deadlines.

Cloudflare supplies the service clock (`Date.now()`) and strongly consistent storage. A new Durable Object lifetime conservatively extends any existing record's exclusion deadline to at least ten seconds after initialization and rejects its old renewals. This covers object eviction, movement and code deployment without trusting a shorter persisted wall-clock expiry. A fresh takeover after quarantine creates a new renewable epoch. Deployment/restarts can interrupt live authority and reduce availability. Timing assumes normally progressing provider and client clocks; arbitrary clock jumps or malicious clients are not covered by the two-second safety margin.

Storage/routing/authentication failure grants no authority. An existing host stops at its local deadline, and a survivor needs a fresh service observation and successful conditional write. Lost acknowledgements can lengthen exclusion without granting permission. Renewal token changes also provide evidence of continuing old-host progress for checkpoint freshness.

Keep the Durable Object namespace and stored fences across deployments. Never delete/reset an active namespace, restore old storage with point-in-time recovery, or deploy independent namespaces behind interchangeable endpoints for the same sessions. Retire all sessions before changing namespaces. There is no global session cap or storage-pruning API; each retained session consumes a small durable record under Cloudflare's quotas.

## Verified EOS authentication contract

Epic's [Connect reference](https://dev.epicgames.com/docs/epic-online-services/eos-fundamentals/connect-interface/connect-reference#id-tokens) specifies the public signing keys at `https://api.epicgames.dev/auth/v1/oauth/jwks`; these are distinct from Epic Account Services Auth keys. `aud` identifies the EOS client, `sub` the Product User ID, and `pfpid`/`pfsid`/`pfdid` the product/sandbox/deployment. The issuer uses the `https://api.epicgames.dev` base URL. Issue time must not be in the future and expiry must be in the future.

The Worker uses pinned `jose` with RS256 signature verification, required key ID, audience, issue/expiry times, and exact configured product/sandbox/deployment. The issuer URL must have exactly the official HTTPS origin; lookalike host prefixes and credentials are rejected. Paths beneath that documented origin are permitted, rather than inventing one exact issuer path. Keys come only from the hardcoded official Connect JWKS URL, never a token-supplied URL. Fetch timeout is 1.5 seconds; key caching and rotation handling are bounded by the library. Missing/unknown keys and failed verification reject the request.

The game copies the current Connect ID token on its EOS owner thread and sends it only to the configured HTTPS endpoint, with redirects disabled. The Worker needs no EOS client secret, native SDK or trusted-server credential. No tokens, private session IDs, credentials or raw provider failures are logged. Worker observability is disabled in the deployment configuration; do not add request-body/header logging in dashboard integrations.

## Configuration and verification

The public URL in [bundled endpoint configuration](../../config/authority-lease-endpoint.json) is embedded in the Client assembly for both editor and export. Gamers require no environment variables, Cloudflare account or external tooling. `TRACKSTORM_LEASE_URL` is an optional explicit developer/test override; invalid overrides fail instead of silently falling back. A null bundled URL deliberately disables online authority until the release owner configures the deployed endpoint.

[Dashboard deployment](../authority-lease-service.md) uses GitHub-connected Cloudflare Builds. Normal Trackstorm setup and `check.ps1` require no Node/npm/Wrangler. Advanced backend developers may run the isolated service tests locally. The former ASP.NET executable, ledger implementation and NuGet authentication dependencies are removed. A small in-memory C# test model supports deterministic game migration tests and is not deployable infrastructure.

The Worker suite tests protocol timing, contention, stale fences, release, outage, restart, sequential epochs, isolation and signature/claim rejection. It also executes actual Worker HTTP and SQLite Durable Objects through local Miniflare/workerd with synthetic signing keys. Existing C# tests cover delayed responses, P2P partitions, full checkpoint restoration, two/three-player agreement and former-host resume. Neither suite establishes live EOS/Cloudflare or physical-PC acceptance.

[Feature index](README.md)
