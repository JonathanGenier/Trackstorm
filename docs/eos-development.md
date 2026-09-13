# EOS development setup

## Developer Portal (product owner)

1. Sign in to the [Epic Developer Portal](https://dev.epicgames.com/portal) with the product owner's Epic account, create/select the Trackstorm organization and product, and review/accept the applicable EOS Game Services agreement. Only the product administrator needs portal access.
2. In **Product Settings → Sandboxes**, create/select a sandbox reserved for development and a deployment named for development testing. Keep future production sandbox/deployment/client configuration separate. Record the Product ID, Sandbox ID and Deployment ID from this product; all three must belong together.
3. In **Product Settings → Clients**, create a dedicated **untrusted game client** and a custom client policy with **User required** checked. Leave optional feature actions disabled for this identity-only foundation. The current [client-policy reference](https://dev.epicgames.com/docs/epic-online-services/eos-fundamentals/client-and-client-policy/client-policy-reference#connect) lists privileged account-query actions under Connect; these require a trusted server and are not needed for local Device ID login. It does not list a `createUser` policy action. Do not grant trusted-server permissions to make a login work. Follow the [client-policy setup guide](https://dev.epicgames.com/docs/epic-online-services/eos-fundamentals/client-and-client-policy/client-policy-guide), apply the policy to the client, and copy its client ID and client secret. Do not enable EAS, EAC, social, storage, commerce, lobbies or P2P for this foundation. The identity-only policy must still be validated by the real login check below against your product.
4. Create `eos.development.local.json` at the repository root (ignored by Git), or outside the repository and set `TRACKSTORM_EOS_CONFIG` to its absolute path. The file is deliberately not auto-packaged. Use this schema with your actual portal values; empty values fail validation:

   ```json
   {
     "Environment": "development",
     "DeploymentName": "dev-testers",
     "ProductId": "",
     "SandboxId": "",
     "DeploymentId": "",
     "ClientId": "",
     "ClientSecret": ""
   }
   ```

5. Run `./setup-eos.ps1`, then `./check.ps1`. To use an already downloaded, exact official release, pass `-ArchivePath <zip>`. Setup validates SHA-256 and only extracts necessary files; it installs no services. Launch Godot with `-- --eos`, or the exported game with `--eos`. EOS login starts automatically; the development panel has login and logout buttons for repeated checks. Launch without `--eos` for ordinary Direct-IP development.

## Configuration security

Product, sandbox, deployment and client IDs are client configuration, not authentication tokens. Environment/deployment labels can be shown in diagnostics. The **untrusted game-client** client secret is also necessarily recoverable by anyone running that client; Epic supports distributing it with a correctly restricted game-client policy. It cannot secure a trusted backend. Trackstorm nevertheless keeps all real configuration out of Git and logs. Supply a reviewed configuration file separately with the private tester build, next to its executable. Never use trusted-server credentials in a game build.

Portal/personal credentials, private keys, trusted-server client secrets, access/refresh/continuance tokens and the locally stored Device ID credential must remain private. Never paste them into Jira, source, launch arguments, screenshots or logs. No tokens are accepted through command-line arguments here. The SDK handles Device ID credential persistence in the local OS user keychain. Trackstorm does not copy, export or delete it on logout. Raw SDK logging is not enabled; diagnostics show operation/result codes and a 12-hex SHA-256 PUID fingerprint, never the full PUID. Treat even fingerprints as pseudonymous tester information when sharing reports.

Configuration validation catches missing, malformed and unknown fields; it cannot prove that portal IDs match or that the client policy is correct. EOS reports those errors at platform creation/login. Login/logout have a 60-second local deadline, followed by platform release and an actionable retry message. Only `Environment: development` is supported; production requires its own reviewed configuration/loading path.

## Testers: no Epic account or developer tooling

The selected flow is official [EOS Connect Device ID](https://dev.epicgames.com/docs/epic-online-services/eos-fundamentals/connect-interface/connect-reference#device-ids), supported on PC desktop. It provides a persistent pseudo-account for the local OS user. The SDK creates/reuses the credential; Connect login uses `DeviceidAccessToken` with a **null token**, plus display-name metadata. If Connect returns `InvalidUser` and a continuance token, the Device ID flow creates the product user and accepts the resulting PUID. Display name does not select identity.

Give each tester the complete Windows x64 export and reviewed development configuration. They need Internet access and any missing Microsoft VC++ x64 runtime. They do **not** need source code, Godot, the Developer Portal, an Epic account, Epic Games Launcher, or Developer Authentication Tool. Start the executable with `--eos`. Each tester runs under their own local Windows profile on their own PC. Do not clone the OS credential store between testers. Multiple game instances under one Windows profile are not a way to manufacture distinct identities. Five separate PCs/profiles use the same product configuration but separate SDK-managed credentials; the same flow supports eight. No custom anonymous service, shared developer identity, IP-based identity, or tester-count limit is introduced by this adapter.

Device ID is development convenience, not a recoverable storefront account. Loss of the local profile/keychain can permanently lose the pseudo-account. It cannot be used with EOS Anti-Cheat. The official Dev Auth Tool is an alternative for EAS-based testing and requires Epic accounts; it is unnecessary for the selected Connect flow.

## Runtime ownership and identity boundary

`EosIdentityService` owns one `IEosPlatform` on the Godot thread. It exposes stopped/ready/logging-in/logged-in/logging-out/failed/disposed state. Cancel, failure and disposal clear identity immediately and invalidate the generation. Native callbacks only enqueue work; managed state changes execute after `Platform.Tick` returns. Logout uses official Connect Logout, then releases the platform; this discards local auth state but does not revoke already issued tokens or destroy the Device ID. Login during an in-progress login is ignored. Logout during login cancels by platform release. A new login creates a fresh platform. Notifications invalidate expired/lost identity and require explicit login, without implementing automatic reconnection.

EOS's SDK documentation explicitly says no SDK calls are allowed after `Shutdown`. Therefore **process initialization happens once**, repeated platform startup/login/logout/release happens beneath that process owner, and final SDK shutdown occurs at application-root exit. Post-shutdown initialization is rejected. Full process initialization/shutdown repetition is tested with separate application processes, never by calling EOS again after shutdown.

`IEosPlatform` and `OnlineProductUserId` form the minimal Client-only boundary for this Device ID implementation. The service does not expose EOS SDK types or accept other credential formats. Any later authentication provider must define its acquisition, account-linking and identity-transfer behavior in a separate reviewed story. `OnlineProductUserId` is a separate reference type with no implicit string/integer/player-ID conversion. Existing Core `LobbyAuthority` still allocates session player IDs; the PUID is not serialized into the Direct-IP protocol.

## Verification procedure

- `./check.ps1`: both configurations, Core tests and Client fake lifecycle/configuration/identity tests.
- `./check-transport.ps1 -GodotPath <Godot .NET exe>` and `./check-lobby.ps1 -GodotPath <exe>`: existing Direct-IP regressions.
- `./check-eos.ps1 -GodotPath <exe>`: native SDK initialization/version check and terminal shutdown in three distinct processes, plus missing-configuration runtime check. This does not establish a working portal login.
- `./check-eos.ps1 -GodotPath <Godot .NET executable> -Authenticate`: runs `res://scenes/verification/eos_checks.tscn` in three fresh Godot processes. It requires valid development configuration, exercises real repeated platform/login/logout and checks a PUID is present. No fake product configuration is used.
- Export using the Windows Desktop preset to an ignored directory. Verify `Epic.OnlineServices.dll`, `EOSSDK-Win64-Shipping.dll`, `licenses/eos/README.md` and the SDK `ThirdPartySoftwareNotice.txt` are present in the exported managed data directory. Copy reviewed configuration next to the executable separately. Run the exported executable with `--eos`, then repeat login/logout and application restart. `--eos-check` runs the same native smoke verification from an export; add `--eos-authenticate` for real authentication.
- On a **second PC without Godot**, repeat the exported check. With **at least two actual testers**, compare successful PUID fingerprints to confirm distinct identities; extend to five and eight concurrent testers, including two behind the same router. Confirm repeated runs on the same Windows profile retain identity. Do not infer these outcomes from multiple local processes.

Portal setup, real login, exported-build execution, second-machine execution and multi-PC identity checks remain manual until the corresponding configuration and machines are available. Record each item separately: portal client policy, authenticated PUID, logout/relogin, same-profile persistence after restart, distinct fingerprints across at least two testers, five-test concurrency, eight-test concurrency and two testers behind one router. A native SDK init success without portal configuration is not a successful Game Services login.
