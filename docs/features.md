# Trackstorm Features

This document describes the current behavior, architecture, and design reasoning of implemented Trackstorm features.

It exists so future developers and agents can understand not only how a feature works, but why important decisions were made.

This document describes the current system. It is not a backlog, task log, PR log, critique log, commit history, or development diary. Source code remains the source of truth for low-level implementation details; this document preserves system intent and durable context.

## Implemented Features

## Fixed-Step Simulation Foundation

### Purpose

The simulation foundation establishes the authoritative update boundary that future gameplay features can extend without coupling game rules to Godot. A caller supplies engine-independent logical input and explicitly requests one simulation step; Core returns the resulting authoritative state.

### How It Works

`Trackstorm.Core` exposes a simulation with immutable observable state. Each call to its step entry point consumes one Core-owned `InputFrame` and advances the unsigned integer simulation tick exactly once. The initial state starts at tick zero with neutral input. A frame is accepted only when its tick is exactly the next simulation tick, so duplicate, skipped, or out-of-order input cannot mutate state.

The Godot main scene uses a Client-owned bootstrap node and a child `PlayerInput` node. `PlayerInput` converts current device state to one logical frame during each Godot physics callback and publishes it. The bootstrap immediately passes that frame to one Core step, making the order explicit: capture logical input, invoke authoritative simulation, then allow observers to present the resulting state. The bootstrap configures Godot's physics tick rate from the validated Core configuration. Godot timing and raw input never cross the Core boundary.

### Design Reasoning

An integer tick makes update ordering explicit and gives future authoritative rules a deterministic time coordinate that does not depend on wall-clock timestamps or floating-point frame deltas. Core advances only when a caller requests a step, which keeps tests and future server or replay drivers in control of scheduling.

Using the versioned integer `InputFrame` as the simulation input avoids a competing demonstration contract and lets the same deterministic data support live input, recorded replay, and future network delivery. Rejecting non-sequential ticks turns update ordering into a checked invariant instead of an informal caller convention.

### Architecture

`Trackstorm.Core` owns:

- Fixed-step configuration and validation.
- The authoritative tick and most recently consumed logical input.
- The deterministic state transition performed by one step.

`Trackstorm.Client` owns:

- Godot lifecycle and fixed-callback scheduling.
- Raw Godot input polling and binding resolution.
- Conversion from native input to Core logical input.
- Ordered composition, invocation, and observation of the Core simulation.

The allowed dependency and data flow is `Godot -> Trackstorm.Client -> Trackstorm.Core`. Core never references Client, Godot, scene lifecycle, raw device input, rendering, or wall-clock time.

### Important Invariants

- One Core step advances the tick by exactly one.
- The input frame tick must equal the next authoritative simulation tick.
- Only Core mutates authoritative simulation state.
- Core receives logical input, never Godot or device input objects.
- Simulation scheduling is caller-controlled; Core does not read clocks or frame delta.
- Equivalent initial state and logical input sequences produce equivalent Core state.
- The configured tick rate must be positive.

### Configuration

The Core-owned configuration contains only the fixed rate in ticks per second. Its current default is 60. Client uses that rate to schedule Core steps but does not pass elapsed seconds into the simulation.

### Interactions

The main Godot scene composes the input and simulation foundations at startup. The current `FrameCaptured` callback establishes capture-before-simulation ordering. Future authoritative gameplay systems can be added inside the Core step in an explicit order, while presentation can observe Core state after stepping.

### Intentional Limitations / Tradeoffs

The foundation does not implement gameplay, vehicle movement, authoritative physics, damage, weapons, items, inventory, spawning, matches, scoring, prediction, reconciliation, HUD, camera, audio, or VFX. Godot's fixed callback is the current scheduler, so there is no catch-up limit, interpolation, pause policy, or rollback history. The transport and replication contracts described below are boundaries only; they do not create a live multiplayer session.

## Player Input System

### Purpose and Behavior

Player input converts keyboard and one assigned gamepad into engine-independent, per-tick input suitable for vehicle gameplay and recorded replay. Logical controls include accelerate, brake/reverse, left/right steering, drift, use item, leaderboard, menu navigation, accept, cancel, and pause. The vehicle decides whether a brake request means braking or reversing; input does not implement vehicle rules.

The current main scene owns one `PlayerInput` node. It publishes `FrameCaptured(InputFrame)` and updates `LatestFrame` once per Godot physics callback, starting at tick 1. The simulation bootstrap consumes that event synchronously, so exactly one Core step follows each capture. Event callbacks observe digital transitions between captures so a quick item or leaderboard tap is retained. Menu input continues while the tree is paused. Application focus loss suppresses axes and releases held controls; focus restoration samples current device state.

### Architecture and Data Flow

`Keyboard/gamepad -> Client physical binding resolution -> Core axis conditioning -> Core integer InputFrame -> logical consumer`

Core owns logical action identifiers, integer frames, versioned serialization, normalization/clamping/dead-zone/inversion helpers, and accumulation of logical button edges. It neither polls devices nor references Godot or Client. Its capture helper accepts aggregate logical held masks, never physical keys or events.

Client owns Godot InputMap registration, native keyboard/gamepad polling, runtime remapping, focus and node lifecycle, and the fixed-callback adapter. It samples each binding individually and takes the strongest binding for an action. This preserves a held action when another binding for that action is released. Opposing steering actions subtract and cancel at equal strength. Throttle and brake remain independent. The same signed-axis normalization is applied to each analog binding before combining it with digital bindings; a full keyboard press therefore retains full strength even when a gamepad stick rests in its dead zone.

Gameplay consumers receive only `InputFrame`. The Client bootstrap consumes the node's automatic fixed-tick capture and invokes the authoritative simulation exactly once. The frame producer owns sequential tick assignment, while Core validates that sequence and reads no clock or frame delta. No vehicle controller is present.

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
- No live networking, prediction, rollback history, replay storage, vehicle, settings persistence, remapping UI, or gameplay context switch is implemented. Remaps last for the input owner's lifetime; construction restores defaults.
- Axes use the latest sample at capture, while digital edges accumulate. Repeated edges within a tick are coalesced rather than ordered or counted.
- Gamepad disconnect is reflected by native polling on subsequent capture; gamepad reassignment requires replacing bindings. Automatic selection/hotplug assignment and local multiplayer are deferred.
- Core tests run without Godot. The explicit headless Godot verification scene exercises native synthetic keyboard/gamepad events through the production adapter, all defaults, analog conditioning, remapping, short taps, focus suppression, and fixed-callback publication. A GdUnit4 Client test loads the composed main scene and checks the bootstrap, input node, and configured tick rate. Synthetic checks do not establish physical controller ergonomics or vehicle feel.

## Transport and Replicated-State Foundation

### Purpose

The networking foundation gives authoritative Core code a transport-facing seam without importing a native networking library. It also distinguishes opaque delivery from game-specific replication so transport choices cannot become gameplay-state ownership.

### How It Works

`Trackstorm.Core.Networking.Transport` defines `ITransportGateway`, `TransportMessage`, and `TransportConnectionState`. A gateway reports a minimal connection state and sends or receives opaque payloads associated with a remote peer identifier. Core does not know how a socket is created, which native library carries a payload, or how peers are discovered.

`Trackstorm.Core.Networking.Replication` separately defines `SimulationStateMessage`. Its version-one wire representation contains an envelope version followed by one versioned `InputFrame`, for 22 bytes total. Reading the message reconstructs Core-owned `SimulationState`; it does not open a connection or call a transport. The split allows a native adapter to carry the bytes without interpreting authoritative gameplay state.

### Design Reasoning

Native transports are runtime and platform concerns, while message meaning, validation, and synchronization-relevant state are authoritative game concerns. Keeping an opaque byte seam between them prevents Core from acquiring Godot or native APIs and lets deterministic serialization be tested without a socket. A concrete transport can change without moving replication rules out of Core.

### Architecture and Module Ownership

`Trackstorm.Core` owns:

- Vehicle simulation, logical input, gameplay-affecting settings, items, spawning, and match state whenever those authoritative modules are introduced.
- The transport-facing plain-C# interface and data contracts.
- Game-specific replicated-state contracts, validation, and versioned serialization.

`Trackstorm.Client` or another outer runtime layer owns:

- Godot nodes, scenes, bootstrap, UI, audio, and Dev Mode presentation.
- Native transport objects, sockets, callbacks, buffer adaptation, and gateway implementation.
- Translation between transport-delivered bytes and the appropriate Core replication message.

The dependency direction remains `Client -> Core`. Core cannot reference Client, Godot, GameNetworkingSockets, or another native transport API. Networking transport and game-specific replication are distinct Core namespaces and responsibilities; no Shared layer is used.

### Important Invariants

- Transport payloads are opaque to the transport boundary; game-specific code owns their interpretation.
- A published payload buffer is treated as immutable for the duration of its use.
- Peer identifiers are transport-assigned plain unsigned integers and carry no gameplay authority by themselves.
- Replication message versions and lengths are validated before state is constructed.
- Replicated simulation and input ticks must match.
- Serializable and synchronization-relevant gameplay state remains Core-owned.

### Serialization / Network Assumptions

The current replication envelope is little-endian through its nested `InputFrame` contract and uses explicit version bytes rather than CLR memory layout. Message delivery reliability, ordering, duplication, maximum payload size, peer identity authentication, and buffer lifetime beyond publication are responsibilities of the future transport adapter and protocol. The Core simulation independently rejects duplicate, skipped, or out-of-order frame ticks.

### Interactions

A future replication driver can encode `SimulationStateMessage`, place the bytes in a `TransportMessage`, and call an outer-layer `ITransportGateway` implementation. The receiving path performs the reverse conversion before passing validated Core state to authoritative replication logic. The current simulation and Client bootstrap do not send these messages.

### Intentional Limitations / Tradeoffs

There is no GameNetworkingSockets package, native adapter, socket, lobby, matchmaking, host/client session, connection retry policy, prediction, reconciliation, interpolation, rollback, or full replication loop. The contracts establish only the foundation architecture boundary and the smallest serializable state message needed to verify it.

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
