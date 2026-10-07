# Player Settings and Local Preferences

## Behavior and Defaults

When EOS identity permits sign-out, personal Settings includes **EOS logout (leave online session)**. It closes the overlay and asks the session owner to return to Main Menu through authoritative leave and online cleanup before invoking identity logout. It is an account action, not a saved preference. This is available to both hosts and clients; online host rename remains separately under **Lobby Settings â†’ Rename lobby**.

The Main Menu and joined Lobby each provide a **Settings** entry. The joined Lobby also has a distinct host-only **Lobby Settings** dialog for existing match mode and kill-target authority; this is not a local-preference page. Lobby Back returns to the same stage and authoritative roster; underlying lobby controls remain blocked while Settings is open. During gameplay, the [ESC Game Menu](game-menu.md) opens Settings. Seven category pages expose audio, video, gameplay, camera, interface, controls and [Developer Options](developer-options.md); F1 directly toggles the same developer page in development builds. Local preferences apply immediately and save automatically after a short idle interval. **Back** returns through the hierarchy; closing flushes pending preference changes. Escape goes Back or cancels an active binding capture. **Save now / retry** explicitly retries a failed local-preference save. The editor reports save status rather than claiming an unsuccessful write was saved. Host gameplay tuning instead uses **Apply Settings** for validation, live application and automatic persistence (including unchanged-value save retries). **Discard Changes** restores active tuning in its editors; **Reset to Defaults** stages production hosted defaults until Apply. Discard and Reset do not change gameplay or persistence. Local preferences remain outside gameplay synchronization and the developer file.

| Preference | Default | Behavior |
| --- | --- | --- |
| Master, Music, SFX | 100% each | Independent linear gains, clamped to 0â€“100%; zero mutes the corresponding bus. |
| Display mode | Windowed | Fullscreen uses the desktop resolution. |
| Window resolution | 1280 Ã— 720 | The editor offers common sizes fitting the current screen; runtime sizing also bounds the window to the usable screen. |
| Speed units | km/h | mph is a presentation conversion; simulation speeds remain metres per second. |
| Camera shake | 100% | Camera page slider scales collision/damage camera feedback from 0â€“100%; zero disables it immediately. Follow, heading and inertia are unchanged. |
| Chase distance | 1.15× | Camera slider 1.15–1.5× gives a 7.36–9.6 m chase view. Older saved values below the minimum clamp to 1.15. Live edits blend smoothly. |
| Camera inertia | 0.5 | Camera slider 0–1 scales restrained positional weight; zero removes it. No horizontal translation lag is added. |
| Aerial pullback | 1× | Camera slider 0–1.5× adds up to 5.4 m during sustained flight. Zero disables the extra distance. |
| Show FPS / Show Ping | Both off | Independent flags update diagnostics visibility immediately. |
| Invert steering | Off | Applies to the existing signed steering axis. |
| Controller Deadzone | 0.15 | Controls slider 0–0.95; stick neutral range before ground or aerial response. Existing valid saved values below 1 still load. |
| Controller Steering Sensitivity | 1.0 | Controls slider 0.1–3; ground controller gain after the precision curve. |
| Controller Aerial Sensitivity | 1.0 | Controls slider 0.1–3; independent linear controller pitch/yaw/roll gain during automatically activated aerial control. |
| Keyboard/Mouse Steering Sensitivity | 1.0 | Controls slider 0.1-3; scales existing keyboard/mouse steering ramp rates while retaining full range. |
| Keyboard/Mouse Aerial Sensitivity | 1.0 | Controls slider 0.1-1; independent keyboard/mouse pitch/yaw/roll rate gain. |
| Mouse aim sensitivity | 1× | Camera page retains the existing armed-only mouse multiplier from 0.25–3×. |
| Stick aim response curve | 2 | Camera page sets the armed radial exponent from 1–3 after the existing dead zone. |
| Horizontal / Vertical Look Sensitivity | 1× each | Camera sliders 0.25–3× independently scale mouse and controller axes in armed and unarmed views. |
| Controller Camera Sensitivity | 1× | Camera slider 0.25–3× uses the existing `stickAimSensitivity` persistence field; applies with axis gains. |
| Invert Y | Off | Camera toggle reverses vertical mouse/controller look, including the direction seen by input friction. |
| Camera Recenter Speed | 1× | Camera slider 0.25–3× scales the existing 6/s neutral-input return. Active input/RMB hold never recenters. |
| FOV | 65° | Camera slider 50–90° sets base FOV before the existing bounded speed/Boost expansion. |
| Camera Height | 1.25 m | Camera slider 0.5–3 m sets lens height above the deployed rack reference on the car; independent of vertical input sensitivity. |
| Input bindings | Input-system defaults | Saved overrides restore physical keys, mouse buttons, gamepad buttons, signed axes, and exact gamepad device IDs. |

Display changes use a 15-second preview. **Keep** commits the requested mode and window size; **Revert**, timeout, or closing the settings editor restores the last confirmed display preferences. Unconfirmed changes never enter the saved snapshot. Resolution selection is disabled in fullscreen because fullscreen uses the desktop mode. Headless runs retain the preferences but do not call native window APIs.

The control editor replaces keyboard/mouse bindings when a key or mouse button is captured, or gamepad bindings when a gamepad control is captured, preserving the other device type. **Clear** explicitly unbinds an action. **Restore default bindings** restores the input system's keyboard/gamepad mappings, leaving scalar preferences unchanged. Shared assignments are permitted and reported. Escape is reserved for cancelling capture; modified key combinations remain unsupported by the existing input system. Compact binding names include the gamepad number, with native descriptions available as tooltips.

## Ownership and Persistence

The dedicated **Camera** page contains all camera controls. Gameplay retains speed units; Controls retains driving/air gains, deadzone and bindings. Existing distance, inertia, pullback, shake, armed mouse sensitivity and armed stick curve retain their saved keys. Controller Camera Sensitivity replaces the old stick-aim label without another preference or persistence path. New optional version-one fields are `horizontalLookSensitivity`, `verticalLookSensitivity`, `invertY`, `cameraRecenterSpeed`, `cameraFov` and `cameraHeight`. Missing, invalid or nonfinite fields default independently; finite numeric values clamp to the supported ranges above. Old files retain their preferences and new fields preserve accepted default behavior. The legacy armed-only mouse multiplier/curve remain available on Camera; axis gains and controller camera sensitivity apply to both views.

The optional version-one `tireEffects` object stores the allowlisted local
[tire-effect Configs controls](terrain-effects.md#local-configs-tuning). Missing
or invalid keys default independently. They use this same settings controller and
file, but are edited through staged Configs actions rather than immediate player
preference sliders. They are available on each device independently and never
enter host gameplay configuration or network recovery state.

Settings and its category pages release gameplay mouse capture through the existing local-input suppression gate. The [input owner](input.md) keeps a normal native pointer available alongside keyboard/controller focus navigation, and restores capture when the player returns to gameplay. Settings controls do not own or restore mouse modes themselves.

Core owns immutable, engine-independent preference data, the stable speed-unit enum, defaults, validation/clamping, and a reusable JSON codec with no filesystem access. Binding tokens are opaque to Core: Core associates copied read-only token lists with logical actions, while the Client input system owns native token encoding, interpretation, and validation. Preferences do not enter authoritative simulation state or replicated messages.

Client owns the settings controller, filesystem store, UI, audio-bus application, display APIs, and input integration. The bootstrap loads preferences after input defaults exist and before the first simulation callback. Runtime consumers subscribe to the controller and read its current snapshot; gameplay and HUD consumers never access the persistence layer. Input changes go through the input owner's binding API, then `CaptureInput`; normal shutdown also captures current input preferences.

The local file is `user://player-settings.json`, under Godot's per-user application data directory. Version-one JSON uses explicit field names and `"km/h"` / `"mph"` enum tokens. Saving flushes a sibling temporary file before replacing the committed file. A failed write preserves the previous committed file and exposes a retryable UI message. Saving is debounced by 0.3 seconds and flushed on close and normal shutdown; a forced process termination during that interval can lose the most recent edit.

Absent, inaccessible, corrupt, oversized, or unsupported-version documents load safe defaults. Missing or incorrectly typed fields default independently, preserving unrelated valid preferences. Finite out-of-range volumes clamp. Unknown/removed action names are ignored; malformed binding overrides retain the complete default binding list for that action. An empty list intentionally means unbound. Documents are bounded to 256 KiB and depth 16, with at most 32 binding tokens per action and 128 characters per token. The supported editor-generated settings fit comfortably within these bounds. This is local persistence only, with no cloud, account, or network synchronization.

## Diagnostics and Integration Limits

`cameraDistance`, `cameraInertia`, and `cameraAerialPullback` are optional version-one numbers, clamped respectively to [1.15,1.5], [0,1], and [0,1.5]. Missing, incorrectly typed or nonfinite values independently default to 1.15, 0.5, and 1. They use the same immediate controller snapshot, debounce, retry, close flush and restart path as other local preferences. The shared [camera](camera.md) supplies smooth transitions; neither host configuration nor gameplay/network state owns these values.

`cameraShakeIntensity` is an optional version-one JSON number in [0,1]. Missing, wrongly typed or nonfinite values default to 1; finite out-of-range values clamp. The shared practice/network camera reads the current local controller snapshot supplied by application composition. It never reads the file, enters DevTools Configs or synchronizes to peers. Setting zero clears pending feedback without resetting inertia or contact cooldown; disabled damage sequences are still consumed, so re-enabling cannot replay them. See [camera](camera.md).

The diagnostics display consumes a fresh provider-neutral connection projection and a pure Client frame-rate sampler. The obsolete diagnostic speed readout and its telemetry path are removed; gameplay speed remains on the [combat HUD](hud.md), using the existing snapshot and preferred units. FPS and Ping share one compact 14-pixel text row in the upper-right corner, separately controlled by the existing immediate settings events. This row stays above the standings board, including at 640Ã—360. Ping uses the local player entry from the same host-published `PlayerLatency` store and `PingFormatter` as the leaderboard. Both display identical milliseconds or `--` for missing/expired samples and the host with no network hop. The HUD additionally describes connecting/reconnecting/disconnected states. It never substitutes an independently sampled client RTT when the publication is missing. Both share roster/session/generation invalidation and three-second publication expiry. See [transport](transport.md) for provider sampling and freshness. Unit changes never modify supplied telemetry or authoritative state. Music and SFX buses feed Master. [Arena audio](audio.md) routes Vehicle, Weapons and UI child buses through SFX, with ambience on SFX and the arena playlist on Music. Hierarchy creation preserves the existing saved gains. Opening Game Menu or Settings suppresses only local gameplay input while the physical arena and network session continue running. Logical menu bindings drive focus, buttons, options, sliders and display confirmation; no pointer is required.

`check-settings.ps1 -GodotPath <Godot .NET executable>` checks isolated local storage, save failure/retry, and restart persistence across two separate Godot processes. It exercises restored bindings/inversion, native bus values, malformed overrides, absence of the retired speed control, and independent diagnostics flags. Adding `-Visual` launches rendered Godot, exercises UI signals and native input events, checks display preview/confirmation/reversion and window sizing, and saves UI screenshots under an isolated `.godot/settings-checks` directory. Core NUnit tests cover defaults, stable JSON round trips, invalid/missing values, volume boundaries, unknown actions, and immutable binding snapshots. Synthetic input and bus-state checks do not establish physical-controller ergonomics, audible output, or behavior on every display backend.

[Feature index](README.md)

Controls separates controller tuning, keyboard/mouse tuning and button/key rebinding. It explains immediate activation without tire support, W/RT nose-up, S/LT nose-down, A/D or left-stick yaw, and Shift/LB horizontal roll modification. After using aerial controls, releasing all axes holds the chosen orientation while travel continues. Non-wheel contact cancels the hold. The retired Air Control action is absent from the binding editor; Air Roll defaults to Shift/LB. Normal remapping, explicit unbinding and persistence remain supported. See [input](input.md).

`steeringSensitivity` and `aerialSensitivity` are optional version-one JSON fields. Missing, wrongly typed or nonfinite values default independently to 1; finite values clamp to 0.1–3. `deadZone` remains the existing field. These and the optional `keyboardSteeringSensitivity` (0.1-3) and `keyboardAerialSensitivity` (0.1-1) fields default independently to 1 when absent/invalid. Existing controller field names retain saved values. All five apply immediately, save through the existing debounce/retry path, and survive restart. Restore default bindings leaves scalar tuning intact.
