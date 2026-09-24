# Player Input System

## Purpose and Behavior

Player input converts keyboard and one assigned gamepad into engine-independent, per-tick input suitable for vehicle gameplay and recorded replay. Logical controls include accelerate, brake/reverse, left/right steering, handbrake, use item, switch item, leaderboard, menu navigation, accept, cancel, and pause. The vehicle decides whether a brake request means braking or reversing; input does not implement vehicle rules.

The current main scene owns one `PlayerInput` node. It publishes `FrameCaptured(InputFrame)` and updates `LatestFrame` once per Godot physics callback, starting at tick 1. The simulation bootstrap consumes that event synchronously. Active arenas use the fixed callback for simulation/prediction; the lobby consumes networking only, leaving the gameplay tick at zero. Event callbacks observe digital transitions between captures so a quick item or leaderboard tap is retained. Menu input continues while the tree is paused. Application focus loss suppresses axes and releases held controls; focus restoration samples current device state.

## Architecture and Data Flow

`PlayerInput` is the sole owner of Godot mouse mode. The bootstrap supplies arena presence; the existing adapter focus, gameplay-suppression and diagnostic-suppression gates determine whether local gameplay owns the mouse. Focused, unsuppressed gameplay uses `Captured` (hidden and locked); every other state uses `Visible` (free native pointer). Gate changes update the mode synchronously, and frame callbacks reconcile arena transitions. Input-owner teardown releases capture. Focus loss releases the pointer and disables local input; focus regain reapplies the current context, including menus opened while unfocused.

Menus keep the native pointer available even during controller navigation. There is no separate device-mode tracker or software cursor: keyboard/controller focus routing and mouse interaction coexist, so mouse use after controller navigation needs no mode switch. LMB continues through the existing logical binding and item-release guard. Read-only held standings/final results have no pointer controls and retain arena ownership; opening their ESC menu releases capture. The [camera](camera.md) consumes local RMB-gated mouse displacement and right-stick intent while gameplay owns capture; these never enter gameplay frames.

`Keyboard/gamepad -> Client physical binding resolution -> Core axis conditioning -> Core integer InputFrame -> logical consumer`

Core owns logical action identifiers, integer frames, versioned serialization, normalization/clamping/dead-zone/inversion helpers, and accumulation of logical button edges. It neither polls devices nor references Godot or Client. Its capture helper accepts aggregate logical held masks, never physical keys or events.

Client owns Godot InputMap registration, native keyboard/gamepad polling, runtime remapping, focus and node lifecycle, and the fixed-callback adapter. It samples each binding individually and takes the strongest binding for an action. This preserves a held action when another binding for that action is released. Opposing steering actions subtract and cancel at equal strength. Throttle and brake remain independent. The same signed-axis normalization is applied to each analog binding before combining it with digital bindings; a full keyboard press therefore retains full strength even when a gamepad stick rests in its dead zone.

Gameplay consumers receive only `InputFrame`. The Client bootstrap consumes the node's automatic fixed-tick capture and invokes the simulation foundation exactly once. It also forwards logical controls to the vehicle adapter, whose native fixed integration callback invokes Core movement using collision-resolved body observations. The frame producer owns sequential capture ticks; each vehicle life owns sequential movement ticks so an explicit arena reset does not rewind the global input clock. Core reads no clock or render delta.

## InputFrame and Determinism

The immutable value frame contains a 64-bit unsigned tick, signed steering in `[-32767,32767]`, independent unsigned throttle and brake in `[0,65535]`, and three 16-bit digital masks: held, pressed, and released. Digital bits cover handbrake (the stable `Drift` bit), item use, item switch (bit 1024), leaderboard, four menu directions, accept, cancel, and pause. Steering direction actions become the signed axis rather than duplicating direction bits in the frame.

Pressed/released mean at least one transition since the previous capture. Both can be set for a tap entirely between ticks; held reflects the final state. Pending edges are consumed once, so repeated fixed updates do not repeat an item press. Multiple complete taps inside one tick coalesce. These masks intentionally record edges rather than requiring a replay consumer to infer them from successive held states, which would lose short taps.

Version-one serialization is exactly 21 bytes: version `1`, tick (8), steering (2), throttle (2), brake (2), held (2), pressed (2), released (2). All multibyte values are little-endian. Readers reject wrong lengths, unknown versions, reserved button bits, and steering `-32768`. The format does not depend on CLR struct layout, native endianness, floating-point serialization, or runtime hash codes. A default frame is neutral at tick zero.

Analog conditioning clamps finite samples to `[-1,1]`, maps magnitudes at or below the configured dead zone to zero, and linearly rescales the remainder to full strength. Non-finite samples become neutral; invalid dead zones throw. Quantization rounds midpoints away from zero. Digital axes use `DrivingInputShaping` before recording: throttle rise/release 10/14 per second, brake rise/release 18 per second, steering rise/return/reversal 20/24/30 per second. This gives full keyboard throttle in about 100 ms and full steering intent in 50 ms, with prompt countersteering. The physical wheel transition still bounds chassis response. The fixed capture interval matches native physics. Analog bindings bypass this shaping; each action combines its shaped digital and normalized analog values by strength. Focus loss or gameplay suppression clears shaping immediately. Item use requires release after suppression before another press can activate it. This is recorded player intent, so host simulation and reconciliation consume the exact same integer frames without needing a second device history. Floating-point processing occurs before recording; deterministic replay uses the recorded integer frames, not reprocessing raw device samples. Equivalent recorded frames therefore carry exactly equivalent logical axes and transitions regardless of device bindings or current settings. This establishes an input contract, not a guarantee about future vehicle simulation determinism.

## Defaults and Runtime Configuration

| Logical action | Keyboard physical key | Gamepad (Godot layout) |
| --- | --- | --- |
| Accelerate | W | Right trigger |
| Brake/reverse | S | Left trigger |
| Steer left/right | A / D | Left stick X negative/positive |
| Handbrake | Space | B / right face button |
| Use item | Left mouse button | A / bottom face button |
| Leaderboard | Tab | Back |
| Menu navigation | Arrow keys | D-pad |
| Menu accept | Enter | A / bottom face button |
| Menu cancel | Escape | B / right face button |
| Switch active item | E | D-pad Right |
| Pause | P | Start |
| Camera intent | Hold RMB and move mouse | Right stick X/Y |

The default gamepad ID is zero; construction accepts another nonnegative Godot device ID. Bindings specify an exact gamepad ID, preventing another controller from affecting this player. Keyboard bindings use physical key positions, keeping the WASD layout consistent across keyboard layouts. RMB is the fixed local free-look hold, with no default gameplay action. The `Drift` action/bit name remains stable for saved remaps and recorded frames, but now means physical handbrake. New defaults do not overwrite saved custom bindings; Restore default bindings installs the new layout. Right-stick camera directions are remappable local presentation intent and do not enter `InputFrame`; camera movement/tuning is separate. Camera analog conditioning enforces a minimum 0.15 dead zone independently of a lower driving dead zone.

`PlayerInputBindings.Replace(action, events)` validates and copies the complete replacement binding list before changing it. Supported entries are unmodified physical keys, mouse buttons, gamepad buttons, and gamepad axes with direction `-1` or `+1`. Empty lists unbind an action; multiple bindings are permitted. Old bindings stop contributing immediately, and aggregate state is reconciled on the next event/capture. Invalid replacements leave the existing mapping intact. `FindConflicts` exposes existing assignments. Shared bindings are allowed intentionally, including the default gamepad A for item use and menu accept, and B for handbrake and menu cancel; the eventual gameplay/menu consumer owns contextual routing.

Client registers namespaced `trackstorm_*` InputMap actions, leaving built-in `ui_*` actions intact. Make runtime changes through the bindings API: its owned physical binding list is the polling source, and it mirrors changes into InputMap. Direct InputMap edits after construction are not a supported configuration path. The owning node releases mappings and native binding resources on exit. Only one owner of these global names is supported.

Legacy saves did not distinguish untouched defaults from custom overrides. At binding-default revision zero, the exact old pair (E/X item use and Space/A handbrake on device zero) migrates together to LMB/A and Space/B. Other remaps and explicit unbinds remain intact. Capturing preferences writes binding-default revision one, so choosing the old controls deliberately after migration survives restart. This local preference marker does not change network or input-frame contracts.

Analog dead zone defaults to `0.15` and accepts finite values in `[0,1)`. Digital actions bound to analog axes activate above `0.5` after conditioning. `InvertSteering` negates resolved steering before quantization and publication; positive becomes negative, negative becomes positive, and neutral remains neutral. Pedals are nonnegative magnitudes and are not negated. Signed axis bindings allow choosing the appropriate physical half-axis for pedals or buttons.

## Invariants, Interactions, and Intentional Limitations

SwitchItem uses the same aggregate press-edge capture as UseItem. A short E/D-pad Right tap survives between fixed ticks and holding does not repeat. Gameplay, diagnostics and focus suppression require releasing either action before another press can act, preventing a held menu-navigation D-pad input from switching inventory on close. Saved bindings remain intact; a missing SwitchItem override inherits the new defaults. Switch and use in the same frame select first, then submit the selected capability through the existing reliable item path.

Network application sessions apply the [Game Loop participation gate](game-loop.md) after capture: synchronization and authoritative Active are both required for driving or item use. Countdown and Finished retain neutral command sequencing/prediction; the host independently enforces the same phase rule and clears pending controls. Logical menu/standings capture remains available. Local loading completion or a displayed countdown reaching zero cannot enable participation.

- Leaderboard held/pressed/released state is independent of vehicle axes and other button bits. An explicitly conflicting custom binding can intentionally activate both actions.
- Frame consumers must use pressed bits for one-shot requests and held bits for sustained actions. The [Game Menu](game-menu.md) routes existing logical menu bindings to native Controls, including directional repeat, options, sliders and confirmation. Gameplay suppression does not suppress this separate local navigation sampling. Native ui_* input is consumed while the overlay is open to avoid double activation. Escape remains a fixed access/cancel key even after remapping.
- Construction installs defaults; the composed Client then restores locally saved binding overrides, steering inversion, and analog dead zone. The settings editor captures keys, mouse buttons and gamepad controls through the existing binding API. While it is open, `GameplaySuppressed` neutralizes gameplay input independently of application focus, while logical menu navigation remains available.
- The [vehicle networking system](vehicle-networking.md) consumes the same logical input contract for prediction and reconciliation. A replay storage format remains outside this input layer.
- Analog axes use the latest sample at capture. Digital driving intent is progressively shaped before quantization, while digital button edges accumulate. Repeated edges within a tick are coalesced rather than ordered or counted.
- Gamepad disconnect is reflected by native polling on subsequent capture; gamepad reassignment requires replacing bindings. Automatic selection/hotplug assignment and local multiplayer are deferred.
- Core tests run without Godot. The explicit headless Godot verification scene exercises native synthetic keyboard/gamepad events through the production adapter, all defaults, analog conditioning, remapping, short taps, focus suppression, and fixed-callback publication. A GdUnit4 Client test loads the composed main scene and checks the bootstrap, input node, and configured tick rate. Synthetic checks do not establish physical controller ergonomics or vehicle feel.

[Feature index](README.md)

The [Event Log](event-log.md) reserves F3 and owns a separate local diagnostic suppression flag. Closing it does not clear Settings suppression; the existing item-release guard prevents click-through use.

Nitro uses the same UseItem action with sustained held state following a capability-bound press. Release/suppression stops charge consumption and boosted drive on the next authoritative step; other items retain one-press use. Remote Nitro presses are associated with the originating input sequence across reliable item and sequenced driving delivery. Switching slots disengages Nitro without discarding charge.
