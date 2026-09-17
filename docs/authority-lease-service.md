# Self-hosting the authority lease service

This service coordinates fencing only. Its [contract and failure assumptions](features/authority-leases.md) are required deployment context. No hosting account, domain, certificate or cloud resource is created by the repository scripts.

## Publish and configure

Run `./publish-lease.ps1` from the repository with the .NET 10 SDK. It produces a framework-dependent service under ignored `Releases/LeaseService`. Install the ASP.NET Core 10 runtime on the target and copy that directory. Run `dotnet Trackstorm.LeaseService.dll` under a dedicated service account/process supervisor. The game export does not include or launch the service.

Configure these environment variables on the service host:

| Variable | Operator-supplied value |
| --- | --- |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5080` behind a same-machine HTTPS reverse proxy, or a configured Kestrel HTTPS listener |
| `Lease__Issuer` | Exact trusted issuer of the deployment's EOS **Connect** ID tokens |
| `Lease__Audience` | EOS client ID used by this game's Connect login |
| `Lease__Jwks` | Trusted HTTPS Connect signing-key endpoint; obtain from the identity provider's configuration/documentation |
| `Lease__Deployment` | The deployment ID matching the token's `pfdid` claim |
| `Lease__Ledger` | Absolute durable private path, e.g. `/var/lib/trackstorm-leases/ledger.json` or `C:\ProgramData\TrackstormLeases\ledger.json` |

Use Connect identity configuration, not Epic Account Services Auth tokens. Exact issuer/key configuration must be verified for the target EOS deployment before acceptance; the service intentionally ships without a guessed issuer or credentials. Epic's [Connect interface documentation](https://dev.epicgames.com/docs/game-services/connect-interface) is the integration entry point. No EOS client secret is needed by the service: it verifies public-key signatures on the clients' current ID tokens.

Provide a publicly trusted HTTPS certificate at the reachable domain, preserve the `/lease/` routes and `Authorization` header, disable body/header credential logging and caching, and bound request rate/size at the reverse proxy. Keep the HTTP listener loopback-only; do not expose port 5080 to the Internet. The service also limits request bodies to 2048 bytes. `/health` is an unauthenticated process-health endpoint and proves neither identity configuration nor successful lease acquisition. Configure UTC correctly for identity-token lifetime validation; lease deadlines themselves use monotonic clocks.

Set `TRACKSTORM_LEASE_URL=https://<your-service-domain>/` in the environment used to launch **both game PCs**. Use the same endpoint, deployment and compatible build. Missing configuration prevents online transport attachment with an actionable error; unavailable service freezes authority rather than permitting an unfenced game. Direct-IP/native harnesses retain their explicit trusted test seams.

## Storage and restart

Use one service instance and a private, persistent local directory writable by only its operator. Do not place the ledger in a game export, source control, a temporary directory or a synchronizing/network filesystem. Preserve the ledger across binary updates. The exclusive lock rejects a second writer; the process refuses to start with a malformed ledger. Do not reset or roll back storage while clients can still address old sessions. Capacity is intentionally bounded and there is no live-session garbage collector or administration API.

Stop the service cleanly before maintenance. After restart, persisted sessions cannot renew old grants and must wait ten seconds before a different holder can take over. Hosts may fail closed and need normal session recovery. To retire a development ledger, first end all sessions and retire its endpoint from old clients; establish a new endpoint/session namespace before starting with empty storage. Reusing an old endpoint after rollback is unsafe.

## Deployment acceptance

1. Run `./check.ps1` and `./publish-lease.ps1`; the service tests include a real loopback HTTP/JWT middleware check with isolated test keys.
2. Confirm unauthenticated lease calls are rejected and health is reachable over the public HTTPS endpoint. Launch a host and observe a held lease in Developer Options; verify real Connect authentication without printing the token or private session key.
3. Follow the [two-PC Public/Locked migration procedure](eos-development.md#host-migration-checks): lobby and arena host kill, P2P-only partition with healthy lease renewal, outage, delayed network recovery, former-host return after ordinary grace, and sequential migration.
4. Run the three-or-more-PC agreement regression. Record actual failover times and network conditions separately from deterministic/local test evidence.

The local publish output is deployable preparation. A supplied reachable host/domain, certificate and operator access are still needed for deployment and physical-PC verification.
