# Lobby and match entry

Application navigation is **Main Menu → Lobby Browser → Joining / Creating → Lobby → Match Loader / Sync → Game Loop**. DevelopmentSession.Stage projects these states from existing session owners; match phase rules remain in Core.

## Frontend and admission

Main Menu's Browse online lobbies action fades browser controls over the same MenuShell video/music instances. Back returns to Main Menu. Existing EOS discovery, Public/Locked access, exact version compatibility and retained-match decisions remain authoritative. Asynchronous membership and transport admission display progress; EOS membership alone does not enter the joined Lobby. Only a validated authoritative roster does. Failed admission keeps the browser usable with a diagnostic.

Joined Lobby is a separate runtime presentation/state. It suspends MenuShell media and presents the admitted roster, Ready, host Start, map selection and Leave, retaining online membership management. No new standalone Godot scene file owns networking lifetime.

## Authoritative map selection

LobbyAuthority.SelectMap accepts only local host authority during Lobby. Exactly two MatchMap values exist: **Old Map** (industrial combat arena) and **New Map** (banked oval). New Map is the default. Snapshots, admission, reconnect, Return and migration retain the selection. A replica rejects a map change within an active generation. The reliable TL schema is version eight and the EOS compatibility bucket is trackstorm-lobby-15.

Clients render the authoritative selection and cannot edit it. Each native arena uses the selected map's eight spawn transforms before constructing vehicle authority. Old Map retains its props and pickups; New Map retains its empty item/prop layout.

## Loading and synchronization contract

MatchResourceLoader prepares match audio, damage shader and the selected packed scene incrementally. Already-cached resources are acquired and retained on the main thread, avoiding a redundant worker-thread reference-count/managed-handle handoff during rematch. Cache misses still use Godot threaded loading and status polling; no unfinished load is synchronously awaited. There is no new global asset cache or retained gameplay state. These resources are absent from startup's reusable resource list. A separate full-screen Match Loader covers gameplay during resource loading and synchronization.

For a new match, the host constructs authority at tick zero and does not step it. After local resources and scene setup complete, each client sends reliable Loaded. The host publishes a complete existing TR checkpoint, including assignment, configuration, world, items, match and optional props. The client installs it atomically and acknowledges synchronization. Only after every still-connected admitted peer acknowledges does the host accept SynchronizedMatchContext through Simulation.InitializeMatch and GameLoop.Initialize, then send reliable release. The existing atomic match adapter remains the sole ongoing phase owner.

Client input, prediction advancement and presentation remain gated until checkpoint installation and host release. After release, driving and item use additionally require the accepted authoritative Game Loop Active phase; neutral prediction and state presentation continue through Countdown and Finished. Controls are fenced by the existing session, authority epoch and connection-generation envelope plus an exact match generation in the TE handshake. Unknown peers, wrong delivery, stale generations and unauthorized release cannot satisfy the barrier.

Fresh active joins announce resource readiness before the host prepares their provisional checkpoint. The existing Activate acknowledgement and committed roster confirmation remain required. Resume uses the same complete checkpoint and presentation gate; running match state is restored rather than reinitialized. Application clients acknowledge every installed checkpoint through TE Synchronized; the host rejects gameplay on a resumed binding until that acknowledgement arrives. Migration retains its fencing and checkpoint rules and reconstructs the same phase participation policy. Isolated legacy driver fixtures may omit the application entry gate.

Resource loading and synchronization each have a thirty-second local deadline; existing admission/reconnect deadlines may fail earlier. Failure drains Leave, removes partial native state and returns a diagnostic to the frontend. The [Game Loop](game-loop.md) owns phase participation after entry. [Post-match Application Flow](post-match.md) consumes its immutable final-results handoff in Podium while retaining the synchronized arena for reconnect. Return/Leave and rematch dispose match drivers and native arenas; a rematch generation repeats this complete loading/sync barrier with fresh lifecycle, mode, vehicle, item and prediction state.

## Verification

Core tests cover both maps through selection, encoding, start, resume and return, plus malformed/stale loading messages. Pure Client tests delay checkpoint and host release, prove tick-zero and empty-input gating, and exercise bounded failure. The eight-player native lobby harness selects both maps across repeated matches, then exercises interrupted/successful active admission and retained capacity. Startup checks verify Main Menu/browser/back with identical running MenuShell media. Browser, reconnect and migration harnesses retain their distinct fake-provider/local-UDP evidence.

Separate-PC EOS, residential network behavior, audible continuity and subjective frontend polish require rendered/manual validation.

[Feature index](README.md) · [Sessions](sessions.md) · [Game Loop](game-loop.md)
