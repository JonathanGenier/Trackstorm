# Lobby and match entry

Application navigation is **Main Menu → Lobby Browser → Joining / Creating → Lobby → Match Loader / Sync → Game Loop**. DevelopmentSession.Stage projects these states from existing session owners; match phase rules remain in Core.

## Frontend and admission

[Main Menu's Play action](main-menu.md) hoists its rig clear before the [Play Menu](play-menu.md) drops and settles over the same MenuShell video/music instances. Back performs the complementary transition. Existing EOS discovery, Public/Locked access, exact version compatibility and retained-match decisions remain authoritative. Asynchronous membership and transport admission display progress; EOS membership alone does not enter the joined Lobby. Only a validated authoritative roster does. Failed admission keeps the browser usable with a diagnostic.

The initial saved-session lookup runs without replacing the Main Menu with browser chrome. Explicit retained-session validation and authority-confirmed decisions retain their existing browser flow. Direct-IP Back clears the fallback selection so it returns to the Main Menu consistently.

Joined Lobby suspends MenuShell media and displays a dedicated static 3D staging scene. `JoinedLobby` owns an isolated presentation viewport, fixed orthographic camera, eight predefined showcases, red/cream theatrical backdrop, industrial floor, chains and warm lights. It reuses the existing vehicle meshes, licensed arena materials and approved Main Menu crest/plate artwork. No physics, gameplay vehicle authority, damage, weapons, AI or match resources are instantiated by the stage.

The connected authoritative roster supplies every displayed identity, name, HOST and non-host READY/NOT READY marker. Presentation nodes are reconciled by stable player ID; departing/disconnected identities are removed, and reconnect uses the existing fresh-join policy. Empty pads contain no sample cars. The scene releases all showcases when leaving Lobby. Names are associated with selectable nameplates; long names ellipsize with a full-name tooltip. Uniform fixed-camera composition fits the viewport; compact controls use larger design-space fonts.

Host controls are Start, Settings, Quit to Main Menu and Kick Player after selecting another player's nameplate. Non-hosts receive Ready/Not Ready, Settings and Quit. Existing host-only map selection remains available. Start submits host readiness and then Start through the existing authoritative driver; the all-ready and transport-roster checks remain unchanged. Kick is a local-host-only driver operation guarded by lobby phase, current authority and lifecycle, using Core's existing removal policy and reliable rejection/disconnect cleanup. It does not create a ban or change EOS membership/admission authority. Other peers receive the normal roster publication.

Settings uses the existing owner and blocks underlying stage actions, then returns to the retained stage. Quit uses the cleanup-gated Main Menu return. Logical remapped navigation and native pointer targets share existing menu navigation. The stage owns neither networking lifetime nor membership/ready state.

`LobbyShowcase.Replace` and `JoinedLobby.RefreshVehicle` replace a single visual under its existing identity/position. The caller-supplied visual factory defaults to the existing Wasteland vehicle because the current lobby contract has no authoritative vehicle-selection field. A later vehicle-selection projection can supply updated art without changing the roster or stage; no selection UI or parallel selected-vehicle state exists.

## Authoritative map selection

LobbyAuthority.SelectMap accepts only local host authority during Lobby. Exactly two MatchMap values exist: **Old Map** (industrial combat arena) and **New Map** (banked oval). New Map is the default. Snapshots, admission, reconnect, Return and migration retain the selection. A replica rejects a map change within an active generation. The reliable TL schema is version eight and the EOS compatibility bucket is trackstorm-lobby-15.

Clients render the authoritative selection and cannot edit it. Each native arena uses the selected map's eight spawn transforms before constructing vehicle authority. Old Map retains its props and pickups; New Map retains its empty item/prop layout.

## Loading and synchronization contract

MatchResourceLoader prepares match audio, damage shader and the selected packed scene incrementally. Already-cached resources are acquired and retained on the main thread, avoiding a redundant worker-thread reference-count/managed-handle handoff during rematch. Cache misses still use Godot threaded loading and status polling; no unfinished load is synchronously awaited. There is no new global asset cache or retained gameplay state. These resources are absent from startup's reusable resource list. A separate full-screen Match Loader covers gameplay during resource loading and synchronization.

For a new match, the host constructs authority at tick zero and does not step it. After local resources and scene setup complete, each client sends reliable Loaded. The host publishes a complete existing TR checkpoint, including assignment, configuration, world, items, match and optional props. The client installs it atomically and acknowledges synchronization. Only after every still-connected admitted peer acknowledges does the host accept SynchronizedMatchContext through Simulation.InitializeMatch and GameLoop.Initialize, then send reliable release. The existing atomic match adapter remains the sole ongoing phase owner.

Client input, prediction advancement and presentation remain gated until checkpoint installation and host release. After release, driving and item use additionally require the accepted authoritative Game Loop Active phase; neutral prediction and state presentation continue through Countdown and Finished. Controls are fenced by the existing session, authority epoch and connection-generation envelope plus an exact match generation in the TE handshake. Unknown peers, wrong delivery, stale generations and unauthorized release cannot satisfy the barrier.

Fresh active joins announce resource readiness before the host prepares their provisional checkpoint. The existing Activate acknowledgement and committed roster confirmation remain required. Resume uses the same complete checkpoint and presentation gate; running match state is restored rather than reinitialized. Application clients acknowledge every installed checkpoint through TE Synchronized; the host rejects gameplay on a resumed binding until that acknowledgement arrives. Migration retains its fencing and checkpoint rules and reconstructs the same phase participation policy. Isolated legacy driver fixtures may omit the application entry gate.

Resource loading and synchronization each have a thirty-second local deadline; existing admission/reconnect deadlines may fail earlier. Failure drains Leave, removes partial native state and returns a diagnostic to the frontend. The [Game Loop](game-loop.md) owns phase participation after entry. [Post-match Application Flow](post-match.md) consumes its immutable final-results handoff in Podium while retaining the synchronized arena for reconnect. Return/Leave and rematch dispose match drivers and native arenas; a rematch generation repeats this complete loading/sync barrier with fresh lifecycle, mode, vehicle, item and prediction state.

Match resource requests run asynchronously on one worker, with dependency subthreads disabled. This keeps nested scene dependencies within the request's lifetime: Godot 4.7.2's distributed loading path leaves zero-reference internal load tokens behind when loading the oval's external resources. `LoadThreadedGet` still consumes the completed request before the loader retains its result; cache reuse, progress polling and the synchronization barrier are unchanged. Strict native shutdown checks cover this resource lifetime as well as gameplay behavior.

## Verification

Core tests cover both maps through selection, encoding, start, resume and return, plus malformed/stale loading messages. Pure Client tests delay checkpoint and host release, prove tick-zero and empty-input gating, and exercise bounded failure. The eight-player native lobby harness selects both maps across repeated matches, then exercises interrupted/successful active admission and retained capacity. Startup checks verify Main Menu/browser/back with identical running MenuShell media. Browser, reconnect and migration harnesses retain their distinct fake-provider/local-UDP evidence.

Separate-PC EOS, residential network behavior, audible continuity and subjective frontend polish require rendered/manual validation.

[Feature index](README.md) · [Sessions](sessions.md) · [Game Loop](game-loop.md)
