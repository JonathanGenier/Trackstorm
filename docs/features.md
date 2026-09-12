# Trackstorm Features

This document describes the current behavior, architecture, and design reasoning of implemented Trackstorm features.

It exists so future developers and agents can understand not only how a feature works, but why important decisions were made.

This document describes the current system. It is not a backlog, task log, PR log, critique log, commit history, or development diary. Source code remains the source of truth for low-level implementation details; this document preserves system intent and durable context.

## Implemented Features

## Player Input System

### Purpose and Behavior

Player input converts keyboard and one assigned gamepad into engine-independent, per-tick input suitable for vehicle gameplay and recorded replay. Logical controls include accelerate, brake/reverse, left/right steering, drift, use item, leaderboard, menu navigation, accept, cancel, and pause. The vehicle decides whether a brake request means braking or reversing; input does not implement vehicle rules.

The current main scene owns one `PlayerInput` node. It publishes `FrameCaptured(InputFrame)` and updates `LatestFrame` once per Godot physics callback, starting at tick 1. Event callbacks observe digital transitions between captures so a quick item or leaderboard tap is retained. Menu input continues while the tree is paused. Application focus loss suppresses axes and releases held controls; focus restoration samples current device state.

### Architecture and Data Flow

`Keyboard/gamepad -> Client physical binding resolution -> Core axis conditioning -> Core integer InputFrame -> logical consumer`

Core owns logical action identifiers, integer frames, versioned serialization, normalization/clamping/dead-zone/inversion helpers, and accumulation of logical button edges. It neither polls devices nor references Godot or Client. Its capture helper accepts aggregate logical held masks, never physical keys or events.

Client owns Godot InputMap registration, native keyboard/gamepad polling, runtime remapping, focus and node lifecycle, and the fixed-callback adapter. It samples each binding individually and takes the strongest binding for an action. This preserves a held action when another binding for that action is released. Opposing steering actions subtract and cancel at equal strength. Throttle and brake remain independent. The same signed-axis normalization is applied to each analog binding before combining it with digital bindings; a full keyboard press therefore retains full strength even when a gamepad stick rests in its dead zone.

Gameplay consumers receive only `InputFrame`. No vehicle controller or authoritative simulation loop is present in this checkout. When a simulation owns fixed scheduling, it must call `PlayerInputAdapter.Capture(simulationTick)` exactly once before each step and replace the current node's automatic capture, rather than running both capture paths. Tick ownership stays with the caller; Core reads no clock or frame delta.

### InputFrame and Determinism

The immutable value frame contains a 64-bit unsigned tick, signed steering in `[-32767,32767]`, independent unsigned throttle and brake in `[0,65535]`, and three 16-bit digital masks: held, pressed, and released. Digital bits cover drift, item, leaderboard, four menu directions, accept, cancel, and pause. Steering direction actions become the signed axis rather than duplicating direction bits in the frame.

Pressed/released mean at least one transition since the previous capture. Both can be set for a tap entirely between ticks; held reflects the final state. Pending edges are consumed once, so repeated fixed updates do not repeat an item press. Multiple complete taps inside one tick coalesce. These masks intentionally record edges rather than requiring a replay consumer to infer them from successive held states, which would lose short taps.

Version-one serialization is exactly 21 bytes: version `1`, tick (8), steering (2), throttle (2), brake (2), held (2), pressed (2), released (2). All multibyte values are little-endian. Readers reject wrong lengths, unknown versions, reserved button bits, and steering `-32768`. The format does not depend on CLR struct layout, native endianness, floating-point serialization, or runtime hash codes. A default frame is neutral at tick zero.

Analog conditioning clamps finite samples to `[-1,1]`, maps magnitudes at or below the configured dead zone to zero, and linearly rescales the remainder to full strength. Non-finite samples become neutral; invalid dead zones throw. Quantization rounds midpoints away from zero. Floating-point processing occurs before recording; deterministic replay uses the recorded integer frames, not reprocessing raw device samples. Equivalent recorded frames therefore carry exactly equivalent logical axes and transitions regardless of device bindings or current settings. This establishes an input contract, not a guarantee about future vehicle simulation determinism.

### Defaults and Runtime Configuration

| Logical action | Keyboard physical key | Gamepad (Godot layout) |
| --- | --- | --- |
| Accelerate | W | Right trigger |
| Brake/reverse | S | Left trigger |
| Steer left/right | A / D | Left stick X negative/positive |
| Drift | Space | A / bottom face button |
| Use item | E | X / left face button |
| Leaderboard | Tab | Back |
| Menu navigation | Arrow keys | D-pad |
| Menu accept | Enter | A / bottom face button |
| Menu cancel | Escape | B / right face button |
| Pause | P | Start |

The default gamepad ID is zero; construction accepts another nonnegative Godot device ID. Bindings specify an exact gamepad ID, preventing another controller from affecting this player. Keyboard bindings use physical key positions, keeping the WASD layout consistent across keyboard layouts.

`PlayerInputBindings.Replace(action, events)` validates and copies the complete replacement binding list before changing it. Supported entries are unmodified physical keys, gamepad buttons, and gamepad axes with direction `-1` or `+1`. Empty lists unbind an action; multiple bindings are permitted. Old bindings stop contributing immediately, and aggregate state is reconciled on the next event/capture. Invalid replacements leave the existing mapping intact. `FindConflicts` exposes existing assignments. Shared bindings are allowed intentionally, including the default gamepad A for drift and menu accept; the eventual gameplay/menu consumer owns contextual routing.

Client registers namespaced `trackstorm_*` InputMap actions, leaving built-in `ui_*` actions intact. Make runtime changes through the bindings API: its owned physical binding list is the polling source, and it mirrors changes into InputMap. Direct InputMap edits after construction are not a supported configuration path. The owning node releases mappings and native binding resources on exit. Only one owner of these global names is supported.

Analog dead zone defaults to `0.15` and accepts finite values in `[0,1)`. Digital actions bound to analog axes activate above `0.5` after conditioning. `InvertSteering` negates resolved steering before quantization and publication; positive becomes negative, negative becomes positive, and neutral remains neutral. Pedals are nonnegative magnitudes and are not negated. Signed axis bindings allow choosing the appropriate physical half-axis for pedals or buttons.

### Invariants, Interactions, and Intentional Limitations

- Leaderboard held/pressed/released state is independent of vehicle axes and other button bits. An explicitly conflicting custom binding can intentionally activate both actions.
- Frame consumers must use pressed bits for one-shot requests and held bits for sustained actions. Menus receive logical requests, with no settings UI, repeat policy, or automatic forwarding into Godot Controls implemented.
- No networking, prediction, rollback history, replay storage, vehicle, settings persistence, remapping UI, or gameplay context switch is implemented. Remaps last for the input owner's lifetime; construction restores defaults.
- Axes use the latest sample at capture, while digital edges accumulate. Repeated edges within a tick are coalesced rather than ordered or counted.
- Gamepad disconnect is reflected by native polling on subsequent capture; gamepad reassignment requires replacing bindings. Automatic selection/hotplug assignment and local multiplayer are deferred.
- Core tests run without Godot. The explicit headless Godot verification scene exercises native synthetic keyboard/gamepad events through the production adapter, all defaults, analog conditioning, remapping, short taps, focus suppression, and fixed-callback publication. Synthetic checks do not establish physical controller ergonomics or vehicle feel.

## Authoring Template

The following template is authoring guidance, not an implemented Trackstorm feature. Copy it under **Implemented Features**, remove sections that do not apply, and replace all guidance text with verified details about the current system.

```md
## Feature Name

### Purpose

Explain what the feature exists to accomplish.

### How It Works

Describe the current behavior and system-level implementation.

### Design Reasoning

Explain why important design decisions were made, especially decisions a future developer might otherwise try to simplify or redesign without understanding the original intent.

### Architecture

Describe only the responsibilities relevant to this feature.

`Trackstorm.Core` owns, as applicable:

- Authoritative rules and state.
- Validation.
- Deterministic or synchronization-relevant behavior.

`Trackstorm.Client` owns, as applicable:

- Godot integration and presentation.
- UI, audio, VFX, and camera.
- Local input capture.

### Important Invariants

Document rules that future changes must preserve.

### Important Assumptions

Document assumptions that affect behavior, ownership, integration, or future changes.

### Configuration

Document meaningful tunable or configurable behavior. Omit this section when the feature has no meaningful configuration.

### Interactions

Describe important interactions with other systems or features.

### Intentional Limitations / Tradeoffs

Document constraints, deliberate simplifications, or tradeoffs that matter to future modifications. Omit this section when none are worth preserving.
```
