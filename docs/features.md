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

The Godot main scene uses a Client-owned bootstrap node and a child `PlayerInput` node. `PlayerInput` converts current device state to one logical frame during each Godot physics callback and publishes it. The bootstrap passes that frame to `VehicleArena.Advance`: capture both native vehicle observations, invoke one Core simulation step, apply both accepted command sets, then publish the resulting state. The bootstrap configures Godot's physics tick rate from the validated Core configuration. Godot timing and raw input never cross the Core boundary.

### Design Reasoning

An integer tick makes update ordering explicit and gives future authoritative rules a deterministic time coordinate that does not depend on wall-clock timestamps or floating-point frame deltas. Core advances only when a caller requests a step, which keeps tests and future server or replay drivers in control of scheduling.

Using the versioned integer `InputFrame` as the simulation input avoids a competing demonstration contract and lets the same deterministic data support live input, recorded replay, and future network delivery. Rejecting non-sequential ticks turns update ordering into a checked invariant instead of an informal caller convention.

### Architecture

`Trackstorm.Core` owns:

- Fixed-step configuration and validation.
- The authoritative tick and most recently consumed logical input.
- Registered vehicle aggregates: identity/life, movement commands and memory, observed physics, health and damage memory.
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
- Equivalent initial state, logical inputs and plain external observations produce equivalent Core state.
- A vehicle batch contains exactly one request per registered vehicle at the next global tick; rejected batches cannot partially commit.
- The configured tick rate must be positive.

### Configuration

The Core-owned configuration contains only the fixed rate in ticks per second. Its current default is 60. Client uses that rate to schedule Core steps but does not pass elapsed seconds into the simulation.

### Interactions

The main Godot scene composes the input and simulation foundations at startup. The current `FrameCaptured` callback establishes capture-before-simulation ordering. Future authoritative gameplay systems can be added inside the Core step in an explicit order, while presentation can observe Core state after stepping.

### Intentional Limitations / Tradeoffs

The simulation owns tick/input and the vehicle gameplay aggregates described below. Client supplies native collision observations and the local settings editor. Weapons, inventory, match rules, scoring, prediction and reconciliation are not implemented. Godot's fixed callback is the current scheduler; Core has no wall-clock catch-up policy or rollback history. The transport and replication contracts described below are boundaries only; they do not create a live multiplayer session.

## Player Input System

### Purpose and Behavior

Player input converts keyboard and one assigned gamepad into engine-independent, per-tick input suitable for vehicle gameplay and recorded replay. Logical controls include accelerate, brake/reverse, left/right steering, drift, use item, leaderboard, menu navigation, accept, cancel, and pause. The vehicle decides whether a brake request means braking or reversing; input does not implement vehicle rules.

The current main scene owns one `PlayerInput` node. It publishes `FrameCaptured(InputFrame)` and updates `LatestFrame` once per Godot physics callback, starting at tick 1. The simulation bootstrap consumes that event synchronously, so exactly one Core step follows each capture. Event callbacks observe digital transitions between captures so a quick item or leaderboard tap is retained. Menu input continues while the tree is paused. Application focus loss suppresses axes and releases held controls; focus restoration samples current device state.

### Architecture and Data Flow

`Keyboard/gamepad -> Client physical binding resolution -> Core axis conditioning -> Core integer InputFrame -> logical consumer`

Core owns logical action identifiers, integer frames, versioned serialization, normalization/clamping/dead-zone/inversion helpers, and accumulation of logical button edges. It neither polls devices nor references Godot or Client. Its capture helper accepts aggregate logical held masks, never physical keys or events.

Client owns Godot InputMap registration, native keyboard/gamepad polling, runtime remapping, focus and node lifecycle, and the fixed-callback adapter. It samples each binding individually and takes the strongest binding for an action. This preserves a held action when another binding for that action is released. Opposing steering actions subtract and cancel at equal strength. Throttle and brake remain independent. The same signed-axis normalization is applied to each analog binding before combining it with digital bindings; a full keyboard press therefore retains full strength even when a gamepad stick rests in its dead zone.

Gameplay consumers receive only `InputFrame`. The Client bootstrap consumes the node's automatic fixed-tick capture and invokes the simulation foundation exactly once. It also forwards logical controls to the vehicle adapter, whose native fixed integration callback invokes Core movement using collision-resolved body observations. The frame producer owns sequential capture ticks; each vehicle life owns sequential movement ticks so an explicit arena reset does not rewind the global input clock. Core reads no clock or render delta.

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
- Frame consumers must use pressed bits for one-shot requests and held bits for sustained actions. The local settings editor uses native Godot Controls and built-in UI actions; custom logical menu bindings are not automatically forwarded into those Controls. No general menu repeat or gameplay-context routing system is implemented.
- Construction installs defaults; the composed Client then restores locally saved binding overrides, steering inversion, and analog dead zone. The settings editor captures keys and gamepad controls through the existing binding API. While it is open, `GameplaySuppressed` neutralizes gameplay input independently of application focus, while native Control navigation remains available.
- No live networking, prediction, rollback history or replay storage is implemented. The vehicle system below consumes the existing logical input contract.
- Axes use the latest sample at capture, while digital edges accumulate. Repeated edges within a tick are coalesced rather than ordered or counted.
- Gamepad disconnect is reflected by native polling on subsequent capture; gamepad reassignment requires replacing bindings. Automatic selection/hotplug assignment and local multiplayer are deferred.
- Core tests run without Godot. The explicit headless Godot verification scene exercises native synthetic keyboard/gamepad events through the production adapter, all defaults, analog conditioning, remapping, short taps, focus suppression, and fixed-callback publication. A GdUnit4 Client test loads the composed main scene and checks the bootstrap, input node, and configured tick rate. Synthetic checks do not establish physical controller ergonomics or vehicle feel.

## Player Settings and Local Preferences

### Behavior and Defaults

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
| Input bindings | Input-system defaults | Saved overrides restore physical keys, gamepad buttons, signed axes, and exact gamepad device IDs. |

Display changes use a 15-second preview. **Keep** commits the requested mode and window size; **Revert**, timeout, or closing the settings editor restores the last confirmed display preferences. Unconfirmed changes never enter the saved snapshot. Resolution selection is disabled in fullscreen because fullscreen uses the desktop mode. Headless runs retain the preferences but do not call native window APIs.

The control editor replaces keyboard bindings when a key is captured, or gamepad bindings when a gamepad control is captured, preserving the other device type. **Clear** explicitly unbinds an action. **Restore default bindings** restores the input system's keyboard/gamepad mappings, leaving scalar preferences unchanged. Shared assignments are permitted and reported. Escape is reserved for cancelling capture; modified key combinations remain unsupported by the existing input system. Compact binding names include the gamepad number, with native descriptions available as tooltips.

### Ownership and Persistence

Core owns immutable, engine-independent preference data, the stable speed-unit enum, defaults, validation/clamping, and a reusable JSON codec with no filesystem access. Binding tokens are opaque to Core: Core associates copied read-only token lists with logical actions, while the Client input system owns native token encoding, interpretation, and validation. Preferences do not enter authoritative simulation state or replicated messages.

Client owns the settings controller, filesystem store, UI, audio-bus application, display APIs, and input integration. The bootstrap loads preferences after input defaults exist and before the first simulation callback. Runtime consumers subscribe to the controller and read its current snapshot; gameplay and HUD consumers never access the persistence layer. Input changes go through the input owner's binding API, then `CaptureInput`; normal shutdown also captures current input preferences.

The local file is `user://player-settings.json`, under Godot's per-user application data directory. Version-one JSON uses explicit field names and `"km/h"` / `"mph"` enum tokens. Saving flushes a sibling temporary file before replacing the committed file. A failed write preserves the previous committed file and exposes a retryable UI message. Saving is debounced by 0.3 seconds and flushed on close and normal shutdown; a forced process termination during that interval can lose the most recent edit.

Absent, inaccessible, corrupt, oversized, or unsupported-version documents load safe defaults. Missing or incorrectly typed fields default independently, preserving unrelated valid preferences. Finite out-of-range volumes clamp. Unknown/removed action names are ignored; malformed binding overrides retain the complete default binding list for that action. An empty list intentionally means unbound. Documents are bounded to 256 KiB and depth 16, with at most 32 binding tokens per action and 128 characters per token. The supported editor-generated settings fit comfortably within these bounds. This is local persistence only, with no cloud, account, or network synchronization.

### Diagnostics and Integration Limits

The diagnostics display consumes vehicle speed in metres per second and optional ping milliseconds. It renders speed in the preferred units and reads Godot's FPS counter. The bootstrap supplies `VehicleSnapshot.Speed`, the horizontal magnitude of the latest solved velocity accepted by Core. Forward, reverse and sideways external motion contribute positively; vertical jump/gravity velocity does not. A stationary supported car therefore displays 0.0 in either unit even while Core commands downward gravity. Ping remains unavailable offline. Unit changes never modify supplied telemetry or authoritative state. Music and SFX buses feed Master. Vehicle impact/blast playback routes to SFX; the settings feature itself introduces no sound assets. Opening the editor suppresses driving input while the physical arena continues running.

`check-settings.ps1 -GodotPath <Godot .NET executable>` checks isolated local storage, save failure/retry, and restart persistence across two separate Godot processes. It exercises restored bindings/inversion, native bus values, malformed overrides, HUD conversion, and independent diagnostics flags. Adding `-Visual` launches rendered Godot, exercises UI signals and native input events, checks display preview/confirmation/reversion and window sizing, and saves UI screenshots under an isolated `.godot/settings-checks` directory. Core NUnit tests cover defaults, stable JSON round trips, invalid/missing values, volume boundaries, unknown actions, and immutable binding snapshots. Synthetic input and bus-state checks do not establish physical-controller ergonomics, audible output, or behavior on every display backend.

## Arcade Vehicles and Damage

### Behavior and Local Arena

The main scene contains a drivable cyan vehicle, an orange physical target vehicle, a movable crate, a ramp, static obstacles and boundary walls. Default controls are W to accelerate, S to brake/reverse, A/D to steer and Space to drift; saved bindings, steering inversion and dead zone continue to apply. The chase camera and HUD show movement status, current/max HP and the target's HP. The HUD hides its title/instructions in short viewports to preserve the driving view. Speed uses the existing preferred-unit diagnostics. **Reset vehicles** explicitly starts fresh vehicle lives at their initial poses. **Detonate nearby** exercises the generic explosion path beside the player; it is an arena demonstration control, not an inventory/item implementation.

Movement is intentionally arcade: forward/reverse acceleration, braking before direction reversal, speed-sensitive steering, strong lateral grip, reduced drift grip, limited air control, gravity and upright recovery torque. A drift requires grounded forward speed of at least 7 m/s and at least 20% steering. Holding a valid drift for 0.65 seconds charges it; releasing while the turn remains valid grants a 0.9-second boost. Breaking speed, support or turning conditions clears charge. A boost temporarily permits 12 m/s above the normal 28 m/s forward drive limit. Reverse drive is limited to 11 m/s. External impulses can exceed drive limits, but total linear/angular commands are bounded to 65 m/s and 8 rad/s.

### Ownership and Fixed-Step Physics

`Core.Simulation` owns one private `VehicleAuthority` per registered stable vehicle ID. Each owns a complete immutable `VehicleSnapshot`: identity and life generation, movement commands, support/air/drift/boost memory, observed numeric physics, health, attribution and collision cooldown. `Simulation.State.Vehicles` and `GetVehicle` expose the committed aggregates; Client has no independent movement/health lifecycle or gameplay clock. The pure `VehicleMovement` and `VehicleHealth` helpers operate privately within the authority's candidate evaluation and remain directly testable without Godot.

`Simulation.Step(input, requests)` requires exactly one `VehicleStepRequest` per registered vehicle at the next global tick. It evaluates complete candidates in vehicle-ID order before committing any state. Effects, strongest-contact damage, repair and movement are evaluated in that order; destruction immediately disables driving. If any candidate is invalid, neither vehicle nor the global tick changes. `Simulation.Restore` likewise validates the complete vehicle set and configured memory bounds before publishing. Preparing candidates uses small temporary objects to guarantee this atomicity; the two-car arena is not a large-scale performance benchmark.

Client `VehicleBody` is a Godot `RigidBody3D` observation/command adapter with native gravity/force damping disabled and native collision solving/continuous collision detection retained. During the input physics callback, `VehicleArena.Advance` captures both bodies through `PhysicsServer3D.BodyGetDirectState`, calls Core once, applies both results, then publishes. Observations contain solved pose/velocities, a unit support normal or zero, and copied raw contacts with numeric velocities, normal, impulse and stable other-vehicle ID. A short downward support ray bridges tiny solver separation gaps without preserving grounding during a real upward launch. Core decides contact severity and damage. Native objects, queries, shapes, inertia, transforms and impulse application remain in Client. Camera smoothing, flashes and blast visuals use presentation time only.

The command boundary is explicitly **before native solving**. `VehicleSnapshot.ObservedPhysics` preserves the latest solved pose and velocities received by Core. `Movement` preserves that pose plus the new commanded velocities and deterministic timers; `Effects` preserves the accepted one-shot native impulses. Client writes the movement commands, then applies those impulses exactly once; their resolved motion is observed on the following tick. `Movement.CommandSpeed` includes vertical commanded velocity and is only a physics diagnostic. Player-facing `VehicleSnapshot.Speed` uses observed horizontal motion, so jumps and downward gravity commands do not inflate road speed.

Reset is a queued new-life intent. Core increments `LifeId`, clears health/events/collision gates and drift/boost memory, and advances the same global tick as every other vehicle. Client applies the accepted reset transform/velocity and clears presentation effects. Event sequence numbers restart within the new life; all movement and damage ticks remain global. Reset never rewinds ordered input.

### Health, Collision Damage and Combat Hooks

The simulation's vehicle authority is the sole HP owner, using `VehicleHealth` to evaluate changes. Default MaxHP is 100. Damage carries a finite amount, source category, stable instigator identity, bounded context and an ordered global tick. Negative/zero requests cannot heal or overwrite attribution. Actual damage clamps HP to zero; repair clamps living vehicles to MaxHP. Reaching zero emits exactly one destroyed/death-candidate outcome. Further damage cannot duplicate it, and repair cannot revive a wreck: an explicit reset starts a new life and clears its event/cooldown memory. Destroyed bodies remain physical but Core disables acceleration, steering, drift and boost.

Collision severity is the larger of relative normal closing speed and contact impulse divided by the receiving vehicle's mass. This accommodates native observations made after velocity resolution. Each vehicle authority evaluates its own received contacts, selects the strongest and assigns the other vehicle's stable ID for attribution; walls/props use world identity zero. Core computes zero damage at or below the default 4 m/s threshold, then 3 HP per excess m/s, capped at 100 HP. A Core-owned 12-tick cooldown prevents manifold duplicates and sustained contact storms. This cooldown is per victim, so separate strong impacts within that window intentionally coalesce. Low-speed brushes and ordinary support impulses stay harmless.

`DamageEffect` carries nonnegative requested damage, a world-space impulse and a world-space application offset. It is independent of missiles, inventory and item classes. The pure explosion helper linearly fades damage and impulse to zero at the radius, biases the outward direction upward, and uses up as the defined direction exactly at the center. Client queues effect intents for the next coordinated fixed step. Core evaluates damage and returns accepted impulses/outcomes; Client applies the impulses at their supplied offsets, allowing native inertia to create rotational response. The arena demonstration uses an 8-metre radius, 55 HP center damage and 15,000 N·s center impulse. Landing or secondary impacts can cause additional collision damage.

Damage feedback is Client-only: chassis flashes, a dark wreck color, a visible expanding blast, HP/destruction text and short original synthesized PCM impact/blast cues. These cues use no external assets and route to the existing SFX bus (Master fallback in isolated scenes without settings). Camera, feedback and UI cannot change HP or damage math.

### Serialization, Replay and Limits

`VehicleStateCodec` retains its explicit 70-byte version-one little-endian layout: version, tick, thirteen IEEE floats for position/orientation/linear/angular velocity, support/drift flags, and two integer timers. It validates length/version/reserved flags, finite physics, unit orientation and state invariants. It is a movement payload, not a complete vehicle aggregate.

`VehicleSnapshotCodec` is the complete portable vehicle boundary. Its version-one envelope is a version byte followed by bounded UTF-8 JSON (64 KiB total maximum). Named fields carry vehicle/life identity, the unchanged movement payload, an observed-physics payload using the same 70-byte encoding with zero flags/timers, validated damage/event/attribution state, and accepted effect requests. JSON represents the two binary payloads as base64. Decode validates nested payloads and cross-field identity/tick/pose/life invariants. Restore additionally checks health capacity and configured timer limits. No Godot objects are serialized.

To restore Core, register the same vehicle IDs/tuning and call `Simulation.Restore` with the global tick/input and all decoded vehicle aggregates. Damage is already committed; restoring does not replay its events or accepted damage. A native reconstruction driver must restore the matching environment and body pose, write the snapshot's movement commands, and apply its retained one-shot impulses once before the matching solve. Fresh observations supply support/contact data on each subsequent step; input alone cannot reconstruct external collisions. There is no second Client gameplay timer to recover.

Equivalent initial pure state, input frames and external observations produce equivalent Core results. Native full-body replay is checked within a numeric tolerance on the current Godot backend; cross-platform bit-identical native physics is not promised. Restore APIs and portable state make authority/prediction integration possible, but there is no live multiplayer, reconciliation loop, snapshot transport, replay file format or item system. The small arena, primitive visuals and chase camera serve local gameplay verification; the camera has no obstruction avoidance and the vehicle uses a single body rather than simulated suspension/tires.

### Verification

Core NUnit coverage checks drive/brake limits, steering boundaries, drift validity/charge/release/expiry, gravity/support transitions, grip, deterministic replay/restoration, snapshot corruption, HP/repair clamps, source retention, collision threshold/scaling/each victim's inputs, cooldowns, explosion falloff and single destruction transitions. Aggregate tests exercise the real simulation owner, atomic rejected batches/restores, global reset ordering, codec round trips with accepted effects, restored charge/release/collision memory, and solved horizontal speed for rest/forward/reverse/vertical/external motion. These tests require no Godot.

`check-vehicle.ps1 -GodotPath <Godot .NET executable>` builds and runs real native bodies through driving, reverse, turning, drift/boost, ramp launch/landing, wall stops, vehicle/crate interaction, hard impact damage to both vehicles, sustained harmless brushing, off-center explosion recovery, destruction and explicit reset. It also uses the production settings panel/HUD with isolated preferences to check zero speed at rest in both units, forward/reverse conversions, vertical-only and combined launch speed, horizontal external motion, and actual Settings-open input suppression. It compares all 240 movement snapshots in the ramp-driving trace across 30 and 144 fixed render FPS at a 0.02 numeric tolerance. `-Visual` also renders the scenarios and saves screenshots under `.godot/vehicle-checks`. The harness tears down its arena and allows the native audio mixer to release stopped playbacks before quitting its accelerated runs. Feedback submission checks establish native playback requests, not subjective audibility or sound quality. Physical-controller ergonomics, long multiplayer sessions and cross-platform native physics remain outside these automated checks.

## Transport and Replicated-State Foundation

### Purpose

The networking foundation gives authoritative Core code a transport-facing seam without importing a native networking library. It also distinguishes opaque delivery from game-specific replication so transport choices cannot become gameplay-state ownership.

### How It Works

`Trackstorm.Core.Networking.Transport` defines `ITransportGateway`, `TransportMessage`, and `TransportConnectionState`. A gateway reports a minimal connection state and sends or receives opaque payloads associated with a remote peer identifier. Core does not know how a socket is created, which native library carries a payload, or how peers are discovered.

`Trackstorm.Core.Networking.Replication` separately defines `SimulationStateMessage`. Its legacy version-one wire representation contains an envelope version followed by one versioned `InputFrame`, for 22 bytes total. It supports only states without registered vehicles and rejects a populated aggregate rather than silently discarding gameplay state. Complete vehicle serialization uses `VehicleSnapshotCodec`; a future live protocol must explicitly compose the global input and vehicle payloads. Reading the legacy message reconstructs a tick/input-only Core state; it does not open a connection or call a transport. The split allows a native adapter to carry bytes without interpreting authoritative gameplay state.

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
