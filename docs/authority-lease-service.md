# Deploying authority fencing with Cloudflare

The supported hosted implementation is a Cloudflare Worker with **one SQLite-backed Durable Object per private Trackstorm session**. It stores only lease coordination data. Gameplay and checkpoints remain on the player PCs. Read the [protocol and failure assumptions](features/authority-leases.md) before deploying.

## Ordinary development and players

Use the existing Godot/.NET/EOS setup and `./check.ps1`. Node.js, npm, Wrangler, a .NET web server, reverse proxy, ledger directory and domain provisioning are not normal Trackstorm development prerequisites.

Players receive the normal game export with its public HTTPS lease URL embedded. They do not configure Cloudflare, environment variables or external software. The optional `TRACKSTORM_LEASE_URL` override is only for developers/tests.

The actual Worker URL remains pending account/dashboard setup. [config/authority-lease-endpoint.json](../config/authority-lease-endpoint.json) currently has `"url": null`, so an unconfigured build fails closed with a build-configuration message. This is deliberate preparation, not a working public deployment. Once the Worker exists, the release owner commits its trusted HTTPS URL there and rebuilds both editor/export. No per-PC configuration file is needed. Do not substitute an invented URL or send EOS tokens to an unverified endpoint.

## GitHub-connected dashboard deployment

Only the service operator needs a Cloudflare account and GitHub repository access. Cloudflare's [Git integration](https://developers.cloudflare.com/workers/ci-cd/builds/git-integration/) and [build configuration](https://developers.cloudflare.com/workers/ci-cd/builds/configuration/) run the backend tooling on Cloudflare's infrastructure.

1. In **Workers & Pages**, create/connect a Worker to the existing Trackstorm GitHub repository. Name it `trackstorm-authority-leases`, matching `wrangler.jsonc`. Select the intended deployment branch explicitly; use the existing Story branch for an authorized pre-merge deployment. Creating this connection does not require merging a PR.
2. Set the root directory to `services/authority-lease`. The committed package lock supplies exact dependencies. Set the build command to `npm test` (its pretest script dry-builds the Worker), and the deploy command to `npm run deploy`. In **Settings → Build → Build Variables and Secrets**, set `NODE_VERSION` to `24.19.0`, the version used for local verification; the backend requires Node 22 or newer. Cloudflare documents this [build-image override](https://developers.cloudflare.com/workers/ci-cd/builds/build-image/#overriding-default-versions). These commands run in Cloudflare Builds, not on ordinary developers' PCs.
3. Keep deployments restricted to that selected branch. Disable automatic non-production branch deployments for this service until a separate test namespace/endpoint is intentionally configured. Never point a game session at interchangeable independent namespaces.
4. The committed binding `LEASE_SESSIONS` and migration `v1` create the SQLite Durable Object class `LeaseSession`. Keep that namespace and migration history on later deployments. Routing uses the private session string as the deterministic object name; no global match object or separate KV database is needed.
5. In the Worker's **Settings → Variables and Secrets**, configure the runtime values below from the same EOS product used by the game. These are public identifiers, not credentials. `keep_vars` preserves dashboard runtime variables across deployment. Build-only variables do not configure the runtime.
6. Deploy and use the HTTPS `workers.dev` URL assigned by Cloudflare, or an intentionally configured custom domain. Cloudflare manages HTTPS. Keep the `/lease/` routes and no-store responses intact. Do not enable request-body/authorization-header logging or expose private session IDs in URLs. Review account quotas/rate controls before opening a public deployment.
7. Confirm HTTPS `GET /health` returns success and unauthenticated lease requests fail. Health alone proves neither valid EOS configuration nor lease acquisition. Bundle the verified endpoint in the game configuration and rebuild before physical acceptance.

| Runtime variable | Value |
| --- | --- |
| `EOS_CLIENT_ID` | Client ID used by Trackstorm's EOS Connect login; checked against `aud` |
| `EOS_PRODUCT_ID` | Product ID, checked against `pfpid` |
| `EOS_SANDBOX_ID` | Sandbox ID, checked against `pfsid` |
| `EOS_DEPLOYMENT_ID` | Deployment ID, checked against `pfdid` |

There are no issuer/JWKS guesses to fill in. The Worker validates Epic's documented issuer origin and uses its official Connect JWKS endpoint. The [authentication contract](features/authority-leases.md#verified-eos-authentication-contract) links the verified reference. Do not use Epic Account Services Auth tokens or add a trusted-server/client secret. Exact live token acceptance is still a deployment check.

A restart/deployment can retire current leases and impose a ten-second conservative quarantine. Schedule updates with that behavior in mind. Preserve the Durable Object namespace; do not reset storage or use point-in-time recovery while old sessions may still address it. Expired session fences remain stored to prevent generation reset.

## Optional backend work

Only someone editing or locally testing the Worker needs Node 22+, npm and the pinned development tools. From `services/authority-lease`:

```powershell
npm ci
npm test
```

The pretest script runs Wrangler's **dry-run** build; tests use controlled clocks plus local Miniflare/workerd SQLite Durable Objects and synthetic RSA keys. No Cloudflare account, live EOS token or cloud deployment is involved. `npm run dev` is optional; local runtime variables may use ignored `.dev.vars`. The production game adapter requires HTTPS, so do not weaken it to point at a plain-HTTP local emulator. Use the test harness or an explicitly provisioned HTTPS test endpoint. `npm run deploy` is an optional operator path, not a normal game setup step.

The backend source and Node dependencies are excluded from Godot scanning with `.gdignore`; they are not shipped with the game. Dependency provenance is recorded in [THIRD_PARTY.md](../THIRD_PARTY.md).

The deployed Worker retains the runtime library's MIT notice and exposes it at `GET /licenses`. This notice is bundled as data because the deployment bundler strips ordinary source comments.

## Required deployment acceptance

First establish real EOS Connect lease acquisition/renewal with the deployed endpoint. Record safe host/epoch/lease status without token/session-key dumps. Then follow [the physical-PC procedure](eos-development.md#host-migration-checks), in this order:

1. **Public lobby, two PCs:** abruptly kill the host process. The survivor must become host in the same logical session instead of timing out to the menu.
2. **Public active match, two PCs:** abruptly kill the host process. The survivor must restore/resume the same match without waiting through player reconnect grace.

Only after both pass, test former-host CLIENT return after more than thirty seconds and more than two minutes within that match, Locked retained return, coordination outage, P2P-only partition while the host still renews, sequential migrations and three-or-more-player agreement. Local mocks, workerd and native UDP harnesses do not establish live EOS/Cloudflare acceptance.
