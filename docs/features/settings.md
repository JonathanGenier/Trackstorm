# Player Settings and Local Preferences

## Behavior and Defaults

The main scene provides a **Settings** button and a scrollable editor for audio, display, HUD diagnostics, and local controls. Preferences apply immediately and save automatically after a short idle interval. **Done** closes the editor and flushes pending changes; Escape closes it or cancels an active binding capture. **Save now / retry** explicitly retries a failed save. The editor reports save status rather than claiming an unsuccessful write was saved.

| Preference | Default | Behavior |
| --- | --- | --- |
| Master, Music, SFX | 100% each | Independent linear gains, clamped to 0–100%; zero mutes the corresponding bus. |
| Display mode | Windowed | Fullscreen uses the desktop resolution. |
| Window resolution | 1280 × 720 | The editor offers common sizes fitting the current screen; runtime sizing also bounds the window to the usable screen. |
| Speed units | km/h | mph is a presentation conversion; simulation speeds remain metres per second. |
| Show FPS / Show Ping | Both off | Independent flags update diagnostics visibility immediately. |
| Invert steering | Off | Applies to the existing signed steering axis. |
| Analog dead zone | 0.15 | Uses the existing input adapter; invalid values restore the default. |
| Input bindings | Input-system defaults | Saved overrides restore physical keys, mouse buttons, gamepad buttons, signed axes, and exact gamepad device IDs. |

Display changes use a 15-second preview. **Keep** commits the requested mode and window size; **Revert**, timeout, or closing the settings editor restores the last confirmed display preferences. Unconfirmed changes never enter the saved snapshot. Resolution selection is disabled in fullscreen because fullscreen uses the desktop mode. Headless runs retain the preferences but do not call native window APIs.

The control editor replaces keyboard/mouse bindings when a key or mouse button is captured, or gamepad bindings when a gamepad control is captured, preserving the other device type. **Clear** explicitly unbinds an action. **Restore default bindings** restores the input system's keyboard/gamepad mappings, leaving scalar preferences unchanged. Shared assignments are permitted and reported. Escape is reserved for cancelling capture; modified key combinations remain unsupported by the existing input system. Compact binding names include the gamepad number, with native descriptions available as tooltips.

## Ownership and Persistence

Core owns immutable, engine-independent preference data, the stable speed-unit enum, defaults, validation/clamping, and a reusable JSON codec with no filesystem access. Binding tokens are opaque to Core: Core associates copied read-only token lists with logical actions, while the Client input system owns native token encoding, interpretation, and validation. Preferences do not enter authoritative simulation state or replicated messages.

Client owns the settings controller, filesystem store, UI, audio-bus application, display APIs, and input integration. The bootstrap loads preferences after input defaults exist and before the first simulation callback. Runtime consumers subscribe to the controller and read its current snapshot; gameplay and HUD consumers never access the persistence layer. Input changes go through the input owner's binding API, then `CaptureInput`; normal shutdown also captures current input preferences.

The local file is `user://player-settings.json`, under Godot's per-user application data directory. Version-one JSON uses explicit field names and `"km/h"` / `"mph"` enum tokens. Saving flushes a sibling temporary file before replacing the committed file. A failed write preserves the previous committed file and exposes a retryable UI message. Saving is debounced by 0.3 seconds and flushed on close and normal shutdown; a forced process termination during that interval can lose the most recent edit.

Absent, inaccessible, corrupt, oversized, or unsupported-version documents load safe defaults. Missing or incorrectly typed fields default independently, preserving unrelated valid preferences. Finite out-of-range volumes clamp. Unknown/removed action names are ignored; malformed binding overrides retain the complete default binding list for that action. An empty list intentionally means unbound. Documents are bounded to 256 KiB and depth 16, with at most 32 binding tokens per action and 128 characters per token. The supported editor-generated settings fit comfortably within these bounds. This is local persistence only, with no cloud, account, or network synchronization.

## Diagnostics and Integration Limits

The diagnostics display consumes vehicle speed in metres per second and a fresh provider-neutral connection projection. It renders speed in the preferred units. A pure Client sampler divides rendered frames by their total elapsed time over half-second windows, holding the completed result until the next window. Invalid frame times are ignored and startup shows unavailable until the first complete window; text does not update every frame. The bootstrap supplies `VehicleSnapshot.Speed`, the horizontal magnitude of the latest solved velocity accepted by Core. Forward, reverse and sideways external motion contribute positively; vertical jump/gravity velocity does not. A stationary supported car therefore displays 0.0 in either unit even while Core commands downward gravity. FPS and Ping share one compact 14-pixel text row in the upper-right corner, separately controlled by the existing immediate settings events. This row stays above the standings board, including at 640×360. Ping uses the local player entry from the same host-published `PlayerLatency` store and `PingFormatter` as the leaderboard. Both display identical milliseconds or `--` for missing/expired samples and the host with no network hop. The HUD additionally describes connecting/reconnecting/disconnected states. It never substitutes an independently sampled client RTT when the publication is missing. Both share roster/session/generation invalidation and three-second publication expiry. See [transport](transport.md) for provider sampling and freshness. Unit changes never modify supplied telemetry or authoritative state. Music and SFX buses feed Master. [Arena audio](audio.md) routes Vehicle, Weapons and UI child buses through SFX, with ambience on SFX and the arena playlist on Music. Hierarchy creation preserves the existing saved gains. Opening the editor suppresses driving input while the physical arena continues running.

`check-settings.ps1 -GodotPath <Godot .NET executable>` checks isolated local storage, save failure/retry, and restart persistence across two separate Godot processes. It exercises restored bindings/inversion, native bus values, malformed overrides, HUD conversion, and independent diagnostics flags. Adding `-Visual` launches rendered Godot, exercises UI signals and native input events, checks display preview/confirmation/reversion and window sizing, and saves UI screenshots under an isolated `.godot/settings-checks` directory. Core NUnit tests cover defaults, stable JSON round trips, invalid/missing values, volume boundaries, unknown actions, and immutable binding snapshots. Synthetic input and bus-state checks do not establish physical-controller ergonomics, audible output, or behavior on every display backend.

[Feature index](README.md)
