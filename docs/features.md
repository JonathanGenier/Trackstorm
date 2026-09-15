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

The Godot main scene uses a Client-owned bootstrap node and a child `PlayerInput` node. `PlayerInput` converts current device state to one logical frame during each Godot physics callback and publishes it. The bootstrap routes frames to the development session, which pumps lobby traffic without advancing gameplay until an authoritative arena transition. In the arena it advances the existing network vehicle driver. With `--local-practice`, it instead calls `VehicleArena.Advance`: capture all eight native vehicle observations, invoke one Core simulation step, apply the complete accepted command batch, then publish the resulting state. The bootstrap configures Godot's physics tick rate from the validated Core configuration. Godot timing and raw input never cross the Core boundary.

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

The simulation owns tick/input and the vehicle gameplay aggregates described below. Client supplies native collision observations and the local settings editor. Weapons, inventory, match rules, scoring, prediction and reconciliation are not implemented. Godot's fixed callback is the current scheduler; Core has no wall-clock catch-up policy or rollback history. The transport described below can establish development peer sessions, but the simulation is not connected to a gameplay replication protocol.

## Player Input System

### Purpose and Behavior

Player input converts keyboard and one assigned gamepad into engine-independent, per-tick input suitable for vehicle gameplay and recorded replay. Logical controls include accelerate, brake/reverse, left/right steering, handbrake, use item, leaderboard, menu navigation, accept, cancel, and pause. The vehicle decides whether a brake request means braking or reversing; input does not implement vehicle rules.

The current main scene owns one `PlayerInput` node. It publishes `FrameCaptured(InputFrame)` and updates `LatestFrame` once per Godot physics callback, starting at tick 1. The simulation bootstrap consumes that event synchronously. Active arenas use the fixed callback for simulation/prediction; the lobby consumes networking only, leaving the gameplay tick at zero. Event callbacks observe digital transitions between captures so a quick item or leaderboard tap is retained. Menu input continues while the tree is paused. Application focus loss suppresses axes and releases held controls; focus restoration samples current device state.

### Architecture and Data Flow

`Keyboard/gamepad -> Client physical binding resolution -> Core axis conditioning -> Core integer InputFrame -> logical consumer`

Core owns logical action identifiers, integer frames, versioned serialization, normalization/clamping/dead-zone/inversion helpers, and accumulation of logical button edges. It neither polls devices nor references Godot or Client. Its capture helper accepts aggregate logical held masks, never physical keys or events.

Client owns Godot InputMap registration, native keyboard/gamepad polling, runtime remapping, focus and node lifecycle, and the fixed-callback adapter. It samples each binding individually and takes the strongest binding for an action. This preserves a held action when another binding for that action is released. Opposing steering actions subtract and cancel at equal strength. Throttle and brake remain independent. The same signed-axis normalization is applied to each analog binding before combining it with digital bindings; a full keyboard press therefore retains full strength even when a gamepad stick rests in its dead zone.

Gameplay consumers receive only `InputFrame`. The Client bootstrap consumes the node's automatic fixed-tick capture and invokes the simulation foundation exactly once. It also forwards logical controls to the vehicle adapter, whose native fixed integration callback invokes Core movement using collision-resolved body observations. The frame producer owns sequential capture ticks; each vehicle life owns sequential movement ticks so an explicit arena reset does not rewind the global input clock. Core reads no clock or render delta.

### InputFrame and Determinism

The immutable value frame contains a 64-bit unsigned tick, signed steering in `[-32767,32767]`, independent unsigned throttle and brake in `[0,65535]`, and three 16-bit digital masks: held, pressed, and released. Digital bits cover handbrake (the stable `Drift` bit), item, leaderboard, four menu directions, accept, cancel, and pause. Steering direction actions become the signed axis rather than duplicating direction bits in the frame.

Pressed/released mean at least one transition since the previous capture. Both can be set for a tap entirely between ticks; held reflects the final state. Pending edges are consumed once, so repeated fixed updates do not repeat an item press. Multiple complete taps inside one tick coalesce. These masks intentionally record edges rather than requiring a replay consumer to infer them from successive held states, which would lose short taps.

Version-one serialization is exactly 21 bytes: version `1`, tick (8), steering (2), throttle (2), brake (2), held (2), pressed (2), released (2). All multibyte values are little-endian. Readers reject wrong lengths, unknown versions, reserved button bits, and steering `-32768`. The format does not depend on CLR struct layout, native endianness, floating-point serialization, or runtime hash codes. A default frame is neutral at tick zero.

Analog conditioning clamps finite samples to `[-1,1]`, maps magnitudes at or below the configured dead zone to zero, and linearly rescales the remainder to full strength. Non-finite samples become neutral; invalid dead zones throw. Quantization rounds midpoints away from zero. Digital axes use `DrivingInputShaping` before recording: throttle rise/release 10/14 per second, brake rise/release 18 per second, steering rise/return/reversal 20/24/30 per second. This gives full keyboard throttle in about 100 ms and full steering intent in 50 ms, with prompt countersteering. The physical wheel transition still bounds chassis response. The fixed capture interval matches native physics. Analog bindings bypass this shaping; each action combines its shaped digital and normalized analog values by strength. Focus loss or gameplay suppression clears shaping immediately. Item use requires release after suppression before another press can activate it. This is recorded player intent, so host simulation and reconciliation consume the exact same integer frames without needing a second device history. Floating-point processing occurs before recording; deterministic replay uses the recorded integer frames, not reprocessing raw device samples. Equivalent recorded frames therefore carry exactly equivalent logical axes and transitions regardless of device bindings or current settings. This establishes an input contract, not a guarantee about future vehicle simulation determinism.

### Defaults and Runtime Configuration

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
| Pause | P | Start |
| Camera intent | Mouse motion remains available | Right stick X/Y |

The default gamepad ID is zero; construction accepts another nonnegative Godot device ID. Bindings specify an exact gamepad ID, preventing another controller from affecting this player. Keyboard bindings use physical key positions, keeping the WASD layout consistent across keyboard layouts. RMB has no default action and is reserved. The `Drift` action/bit name remains stable for saved remaps and recorded frames, but now means physical handbrake. New defaults do not overwrite saved custom bindings; Restore default bindings installs the new layout. Right-stick camera directions are remappable local presentation intent and do not enter `InputFrame`; camera movement/tuning is separate.

`PlayerInputBindings.Replace(action, events)` validates and copies the complete replacement binding list before changing it. Supported entries are unmodified physical keys, mouse buttons, gamepad buttons, and gamepad axes with direction `-1` or `+1`. Empty lists unbind an action; multiple bindings are permitted. Old bindings stop contributing immediately, and aggregate state is reconciled on the next event/capture. Invalid replacements leave the existing mapping intact. `FindConflicts` exposes existing assignments. Shared bindings are allowed intentionally, including the default gamepad A for item use and menu accept, and B for handbrake and menu cancel; the eventual gameplay/menu consumer owns contextual routing.

Client registers namespaced `trackstorm_*` InputMap actions, leaving built-in `ui_*` actions intact. Make runtime changes through the bindings API: its owned physical binding list is the polling source, and it mirrors changes into InputMap. Direct InputMap edits after construction are not a supported configuration path. The owning node releases mappings and native binding resources on exit. Only one owner of these global names is supported.

Legacy saves did not distinguish untouched defaults from custom overrides. At binding-default revision zero, the exact old pair (E/X item use and Space/A handbrake on device zero) migrates together to LMB/A and Space/B. Other remaps and explicit unbinds remain intact. Capturing preferences writes binding-default revision one, so choosing the old controls deliberately after migration survives restart. This local preference marker does not change network or input-frame contracts.

Analog dead zone defaults to `0.15` and accepts finite values in `[0,1)`. Digital actions bound to analog axes activate above `0.5` after conditioning. `InvertSteering` negates resolved steering before quantization and publication; positive becomes negative, negative becomes positive, and neutral remains neutral. Pedals are nonnegative magnitudes and are not negated. Signed axis bindings allow choosing the appropriate physical half-axis for pedals or buttons.

### Invariants, Interactions, and Intentional Limitations

- Leaderboard held/pressed/released state is independent of vehicle axes and other button bits. An explicitly conflicting custom binding can intentionally activate both actions.
- Frame consumers must use pressed bits for one-shot requests and held bits for sustained actions. The local settings editor uses native Godot Controls and built-in UI actions; custom logical menu bindings are not automatically forwarded into those Controls. No general menu repeat or gameplay-context routing system is implemented.
- Construction installs defaults; the composed Client then restores locally saved binding overrides, steering inversion, and analog dead zone. The settings editor captures keys, mouse buttons and gamepad controls through the existing binding API. While it is open, `GameplaySuppressed` neutralizes gameplay input independently of application focus, while native Control navigation remains available.
- The vehicle networking system below consumes the same logical input contract for prediction and reconciliation. A replay storage format remains outside this input layer.
- Analog axes use the latest sample at capture. Digital driving intent is progressively shaped before quantization, while digital button edges accumulate. Repeated edges within a tick are coalesced rather than ordered or counted.
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
| Input bindings | Input-system defaults | Saved overrides restore physical keys, mouse buttons, gamepad buttons, signed axes, and exact gamepad device IDs. |

Display changes use a 15-second preview. **Keep** commits the requested mode and window size; **Revert**, timeout, or closing the settings editor restores the last confirmed display preferences. Unconfirmed changes never enter the saved snapshot. Resolution selection is disabled in fullscreen because fullscreen uses the desktop mode. Headless runs retain the preferences but do not call native window APIs.

The control editor replaces keyboard/mouse bindings when a key or mouse button is captured, or gamepad bindings when a gamepad control is captured, preserving the other device type. **Clear** explicitly unbinds an action. **Restore default bindings** restores the input system's keyboard/gamepad mappings, leaving scalar preferences unchanged. Shared assignments are permitted and reported. Escape is reserved for cancelling capture; modified key combinations remain unsupported by the existing input system. Compact binding names include the gamepad number, with native descriptions available as tooltips.

### Ownership and Persistence

Core owns immutable, engine-independent preference data, the stable speed-unit enum, defaults, validation/clamping, and a reusable JSON codec with no filesystem access. Binding tokens are opaque to Core: Core associates copied read-only token lists with logical actions, while the Client input system owns native token encoding, interpretation, and validation. Preferences do not enter authoritative simulation state or replicated messages.

Client owns the settings controller, filesystem store, UI, audio-bus application, display APIs, and input integration. The bootstrap loads preferences after input defaults exist and before the first simulation callback. Runtime consumers subscribe to the controller and read its current snapshot; gameplay and HUD consumers never access the persistence layer. Input changes go through the input owner's binding API, then `CaptureInput`; normal shutdown also captures current input preferences.

The local file is `user://player-settings.json`, under Godot's per-user application data directory. Version-one JSON uses explicit field names and `"km/h"` / `"mph"` enum tokens. Saving flushes a sibling temporary file before replacing the committed file. A failed write preserves the previous committed file and exposes a retryable UI message. Saving is debounced by 0.3 seconds and flushed on close and normal shutdown; a forced process termination during that interval can lose the most recent edit.

Absent, inaccessible, corrupt, oversized, or unsupported-version documents load safe defaults. Missing or incorrectly typed fields default independently, preserving unrelated valid preferences. Finite out-of-range volumes clamp. Unknown/removed action names are ignored; malformed binding overrides retain the complete default binding list for that action. An empty list intentionally means unbound. Documents are bounded to 256 KiB and depth 16, with at most 32 binding tokens per action and 128 characters per token. The supported editor-generated settings fit comfortably within these bounds. This is local persistence only, with no cloud, account, or network synchronization.

### Diagnostics and Integration Limits

The diagnostics display consumes vehicle speed in metres per second and optional ping milliseconds. It renders speed in the preferred units and reads Godot's FPS counter. The bootstrap supplies `VehicleSnapshot.Speed`, the horizontal magnitude of the latest solved velocity accepted by Core. Forward, reverse and sideways external motion contribute positively; vertical jump/gravity velocity does not. A stationary supported car therefore displays 0.0 in either unit even while Core commands downward gravity. Ping remains unavailable offline. Unit changes never modify supplied telemetry or authoritative state. Music and SFX buses feed Master. Vehicle impact/blast playback routes to SFX; the settings feature itself introduces no sound assets. Opening the editor suppresses driving input while the physical arena continues running.

`check-settings.ps1 -GodotPath <Godot .NET executable>` checks isolated local storage, save failure/retry, and restart persistence across two separate Godot processes. It exercises restored bindings/inversion, native bus values, malformed overrides, HUD conversion, and independent diagnostics flags. Adding `-Visual` launches rendered Godot, exercises UI signals and native input events, checks display preview/confirmation/reversion and window sizing, and saves UI screenshots under an isolated `.godot/settings-checks` directory. Core NUnit tests cover defaults, stable JSON round trips, invalid/missing values, volume boundaries, unknown actions, and immutable binding snapshots. Synthetic input and bus-state checks do not establish physical-controller ergonomics, audible output, or behavior on every display backend.

## Grounded Arcade Vehicles and Damage

### Behavior and Local Arena

The local practice arena (`-- --local-practice`) contains a drivable cyan vehicle, an orange physical target vehicle, a movable crate, a ramp, static obstacles and boundary walls. Default controls are W to accelerate, S to brake/reverse, A/D to steer and Space for the physical handbrake; saved bindings, steering inversion and dead zone continue to apply. The chase camera follows the player. The combat HUD shows current/max HP and preferred-unit speed; Arena tools exposes movement status, target HP and demonstration controls. Its instructions hide in short viewports. **Reset vehicles** explicitly starts fresh vehicle lives at their initial poses. **Detonate nearby** exercises the generic explosion path beside the player; it is an arena demonstration control, not an inventory/item implementation.

Movement uses a grounded arcade model: player intent drives forces through two tire axles and four independent suspension supports. Acceleration, braking, lateral motion and yaw retain momentum. The handbrake applies rear braking and reduces rear lateral traction; it cannot select a drift angle or create a release boost. Sliding is diagnostic feedback derived from lateral speed and rear saturation, never a simulation mode.

`VehicleConfiguration` owns tuning. Engine/brake forces use a 900 kg reference mass, so increasing mass makes acceleration, stopping and steering response slower. Default forward engine demand is 11 m/s² at reference mass before tire saturation; forward/reverse caps are 28/11 m/s. Drive opposing current motion first brakes it to rest; a 0.05 m/s stopping tolerance avoids floating-point residue preventing reversal. Coasting uses 0.12/s rolling resistance. External impacts may exceed drive caps; absolute command bounds remain 65 m/s and 8 rad/s.

Wheel angle approaches the request at 6 rad/s and is limited to `0.6 / (1 + (speed / 22)^2)` radians. Front/rear lateral contact velocities include yaw and front steering angle. Lateral demand and drive/brake demand share a smooth friction limit (`tanh` saturation, default friction coefficient 1.35 and gravity 9.81 m/s²). Tire forces change both velocity and yaw through the 2.3 m wheelbase; steering never directly sets body heading or target yaw. High entry speed therefore runs wider, and engine/brake demand while turning can consume lateral grip. Rear handbrake application rises at 12/s; full application retains 60% rear lateral grip and adds rear braking. Rear engine drive fades while the handbrake is held so throttle cannot cancel a locked rear axle. Releasing the input immediately ends mechanical rear braking and drive suppression, while the saved handbrake state restores lateral traction at the existing 5/s rate. Front tire capacity is unaffected by the handbrake; retained rear friction prevents prolonged ice-like lateral travel. The lateral force response is 12/s. The higher tire budget and faster wheel transition improve initial response while mass, acceleration limits and tire saturation preserve momentum. Bounded yaw damping (0.65/s, configurable down to zero) dissipates energy without imposing a drift angle or overriding countersteering.

Longitudinal propulsion and lateral traction have distinct demands within the same rear tire budget. With the handbrake released, propulsion can use up to a configured 55% traction reserve when lateral demand would otherwise consume almost the entire budget. This allocation never exceeds pedal demand or pure longitudinal tire capacity; lateral force is bounded by the remaining friction circle before the recovering rear grip fraction applies. It preserves normal straight-line drive and leaves braking/coasting allocation unchanged. Throttle therefore pulls along the vehicle's facing direction while sideways momentum and yaw continue, without waiting for the diagnostic sliding flag to clear or injecting a special drift velocity. The reserve can be tuned down to zero; surface grip, wheel support, mass scaling and speed caps still constrain the force.

Four wheel rays supply compression observations to Core. Default full extension is 0.8 m, vertical spring stiffness is 150/s², and damping is 18/s. Core applies bounded individual spring forces and lever-arm torques. Supported wheel count limits total tire capacity; axle compression and longitudinal load transfer distribute front/rear grip. Pitch/roll spring targets also respond to the actual longitudinal/lateral tire forces (compliance 0.012, tilt bound 0.16 radians). Bumps and landings therefore affect both physical chassis motion and traction. Wheels display the accepted compression and front steering angle. This is a compact sprung-body/axle approximation, without a transmission, differential, or individual wheel angular dynamics.

### Ownership and Fixed-Step Physics

`Core.Simulation` owns one private `VehicleAuthority` per registered stable vehicle ID. Each owns a complete immutable `VehicleSnapshot`: identity and life generation, movement commands, support, steering, handbrake, slip, load and suspension state, observed numeric physics, health, attribution and collision cooldown. `Simulation.State.Vehicles` and `GetVehicle` expose the committed aggregates; Client has no independent movement/health lifecycle or gameplay clock. The pure `VehicleMovement` and `VehicleHealth` helpers operate privately within the authority's candidate evaluation and remain directly testable without Godot.

`Simulation.Step(input, requests)` requires exactly one `VehicleStepRequest` per registered vehicle at the next global tick. It evaluates complete candidates in vehicle-ID order before committing any state. Effects, strongest-contact damage, repair and movement are evaluated in that order; destruction immediately disables driving. If any candidate is invalid, neither vehicle nor the global tick changes. `Simulation.Restore` likewise validates the complete vehicle set and configured memory bounds before publishing. Preparing candidates uses small temporary objects to guarantee this atomicity; the eight-car prototype is not a large-scale performance benchmark.

Client `VehicleBody` is a Godot `RigidBody3D` observation/command adapter with native gravity/force damping disabled and native collision solving/continuous collision detection retained. During the input physics callback, `VehicleArena.Advance` captures all practice bodies through `PhysicsServer3D.BodyGetDirectState`, calls Core once, applies the complete batch, then publishes. Observations contain solved pose/velocities, a unit support normal or zero, and copied raw contacts with numeric velocities, normal, impulse and stable other-vehicle ID. A short downward support ray bridges tiny solver separation gaps without preserving grounding during a real upward launch. Core decides contact severity and damage. Native objects, queries, shapes, inertia, transforms and impulse application remain in Client. Camera smoothing, flashes and blast visuals use presentation time only.

The command boundary is explicitly **before native solving**. `VehicleSnapshot.ObservedPhysics` preserves the latest solved pose and velocities received by Core. `Movement` preserves that pose plus the new commanded velocities and deterministic handling state; `Effects` preserves the accepted one-shot native impulses. Client writes the movement commands, then applies those impulses exactly once; their resolved motion is observed on the following tick. `Movement.CommandSpeed` includes vertical commanded velocity and is only a physics diagnostic. Player-facing `VehicleSnapshot.Speed` uses observed horizontal motion, so jumps and downward gravity commands do not inflate road speed.

Reset is a queued new-life intent. Core increments `LifeId`, clears health/events/collision gates and handling memory, and advances the same global tick as every other vehicle. Client applies the accepted reset transform/velocity and clears presentation effects. Event sequence numbers restart within the new life; all movement and damage ticks remain global. Reset never rewinds ordered input.

### Concrete and Mud Surfaces

Core defines stable `SurfaceType` identifiers (Concrete = 0, Mud = 1). `VehicleConfiguration` holds immutable `SurfaceModifiers` for each surface and resolves them without engine access. Concrete defaults to identity grip/drag/acceleration multipliers (1, 1, 1), preserving baseline driving. Mud defaults to (0.55, 3, 0.6): weaker lateral grip, greater rolling resistance and slower acceleration. Multipliers must be finite and within 0-100; invalid tuning is rejected before simulation. Zero deliberately permits disabling a contribution, and bounded tuning keeps composed handling finite and nonnegative.

Client `SurfaceBody` exposes a scene-authored surface ID, with unmarked colliders treated as Concrete. `VehicleBody.Capture` determines support in the existing fixed callback. A short center-down support ray identifies the supporting collider and surface; when that ray misses but native support contacts exist, the closest horizontal contact supplies the surface (collider identity breaks exact distance ties). Side-wall contacts are excluded. The existing upward-launch guard prevents ray-only grounding during takeoff. No render callback changes surface state. The production arena includes two brown Mud regions described below; the focused movement fixture retains its original rectangle at x = -36 to -28, z = -8 to 8. All use coplanar, non-overlapping Concrete tiles around them. The HUD reads the committed current surface.

Core receives surface and wheel observations through `VehicleObservation` on the fixed boundary. Grip scales the combined tire budget, acceleration scales forward/reverse engine demand, and drag scales rolling resistance. Under power, additional resistance is `CoastDrag * max(0, drag - 1)`; coasting uses `CoastDrag * drag`. Surface transitions change force rates without resetting momentum, steering or handbrake recovery. All paths use `VehicleMovement.Step` with identical configuration.

`VehicleState.CurrentSurface` retains the last supported surface in flight for feedback, while airborne tires exert no driving or cornering forces. Wheel rays provide physical spring response upon reacquiring terrain. A center ray selects the surface when available, with a deterministic supported-wheel fallback. Per-wheel surface blending remains intentionally excluded; individual wheel compression and support still affect suspension and the tire budget.

### Health, Collision Damage and Combat Hooks

The simulation's vehicle authority is the sole HP owner, using `VehicleHealth` to evaluate changes. Release 0.0.1 production practice and network arenas configure MaxHP as 1000; the generic damage helper and focused legacy fixtures retain their 100 HP default. Damage carries a finite amount, source category, stable instigator identity, bounded context and an ordered global tick. Negative/zero requests cannot heal or overwrite attribution. Actual damage clamps HP to zero; repair clamps living vehicles to MaxHP. Reaching zero emits exactly one destroyed outcome and enters the authoritative Dead lifecycle. Further damage cannot duplicate it, and repair cannot revive a wreck. Production arenas automatically respawn after the configured tick delay; explicit development resets remain available. Inactive bodies are hidden and non-colliding, and Core suppresses driving, items and damage participation. The lifecycle and reset policy are described below.

Collision severity is the larger of relative normal closing speed and contact impulse divided by the receiving vehicle's mass. This accommodates native observations made after velocity resolution. Each vehicle authority evaluates its own received contacts, selects the strongest and assigns the other vehicle's stable ID for attribution; walls/props use world identity zero. Core computes zero damage at or below the default 4 m/s threshold, then 3 HP per excess m/s, capped at 100 HP. A Core-owned 12-tick cooldown prevents manifold duplicates and sustained contact storms. This cooldown is per victim, so separate strong impacts within that window intentionally coalesce. Low-speed brushes and ordinary support impulses stay harmless.

`DamageEffect` carries nonnegative requested damage, a world-space impulse and a world-space application offset. It is independent of missiles, inventory and item classes. The pure explosion helper linearly fades damage and impulse to zero at the radius, biases the outward direction upward, and uses up as the defined direction exactly at the center. Client queues effect intents for the next coordinated fixed step. Core evaluates damage and returns accepted impulses/outcomes; Client applies the impulses at their supplied offsets, allowing native inertia to create rotational response. The arena demonstration uses an 8-metre radius, 55 HP center damage and 15,000 N·s center impulse. Landing or secondary impacts can cause additional collision damage.

Damage feedback is Client-only: chassis flashes, a dark wreck color, a visible expanding blast, HP/destruction text and short original synthesized PCM impact/blast cues. These cues use no external assets and route to the existing SFX bus (Master fallback in isolated scenes without settings). Camera, feedback and UI cannot change HP or damage math.

### Serialization, Replay and Limits

`VehicleStateCodec` uses a 107-byte version-three little-endian layout: version/tick; thirteen physics floats; support/sliding flags; steering angle and handbrake floats; surface ID; front/rear slip, longitudinal/lateral acceleration and landing intensity floats; and four wheel-compression floats. All values round-trip exactly. Readers reject older versions, reserved flags, invalid surfaces, nonfinite values and out-of-range handling state. Restoration also validates the wheel angle against configured tuning.

`VehicleSnapshotCodec` is the complete portable vehicle boundary. Its version-two envelope is a version byte followed by bounded UTF-8 JSON (64 KiB total maximum). Named fields carry vehicle/life identity, lifecycle and optional respawn deadline, the version-three movement payload, an observed-physics payload using the same 107-byte encoding with neutral handling state and Concrete as its neutral surface, validated damage/event/attribution state, and accepted effect requests. JSON represents the two binary payloads as base64. Decode validates nested payloads and cross-field identity/tick/pose/life invariants. Restore additionally checks health capacity and configured handling limits. No Godot objects are serialized.

To restore Core, register the same vehicle IDs/tuning and call `Simulation.Restore` with the global tick/input and all decoded vehicle aggregates. Damage is already committed; restoring does not replay its events or accepted damage. A native reconstruction driver must restore the matching environment and body pose, write the snapshot's movement commands, and apply its retained one-shot impulses once before the matching solve. Fresh observations supply support/contact data on each subsequent step; input alone cannot reconstruct external collisions. There is no second Client gameplay timer to recover.

Equivalent initial pure state, input frames and external observations produce equivalent Core results. Native full-body replay is checked within a numeric tolerance on the current Godot backend; cross-platform bit-identical native physics is not promised. The network vehicle loop below uses these restore APIs for live prediction and reconciliation. A replay file format remains separate future work. Network held-item combat is described below; the local practice damage demonstration remains independent. The primitive vehicle visuals and chase camera serve local gameplay verification; the camera has no obstruction avoidance and the vehicle uses one sprung collision body with independently observed wheel support and a compact axle traction model.

### Verification

Core NUnit coverage checks digital shaping, analog conditioning, mass/drive/braking response, progressive speed-sensitive wheel steering, combined tire demand, analog propulsion during slip, immediate handbrake-release drive with gradual lateral recovery, bounded supported tire forces, handbrake speed loss/recovery, individual spring forces, impulse retention, surfaces, malformed snapshots and deterministic replay/restoration. Existing damage, item, authority and networking tests remain applicable.

`check-input.ps1` checks native default mappings, analog precision, digital ramps, mouse preference round trips, camera intent, remapping, reserved RMB, focus and frame publication. `check-vehicle.ps1` exercises native acceleration, braking/reverse, low/fast corner entry, lane changes, slide recovery, prolonged handbrake use, short/long low/fast power-out slides with throttle before/on/after release, acceleration after braking and collision, suspension/ramp/landing, concrete/mud transitions, collisions, damage, explosions, HUD and settings suppression. It compares traces at 30 and 144 render FPS; `-Visual` saves rendered evidence. `check-network-vehicles.ps1` exercises separate host/client prediction and reconciliation. Synthetic driving establishes repeatable behavior, not physical-controller ergonomics or human judgement of the GTA V/Wreckfest feel target.

## Prototype Combat Arena

### Layout and Playability

Practice and network sessions share `scenes/arena/prototype_arena.tscn` / Client `CombatArena`, a flat 120 × 100 metre industrial yard. Eight vehicles have widely separated facing spawn slots in two rows, with a broad perimeter lane and open central approaches. Two container obstacles and two concrete road barriers break sightlines without closing the driving routes. A low salvage ramp sits near the eastern edge. Three movable barrels provide a bounded physics workload. They are inert obstacles; their source texture does not add explosive-barrel gameplay.

Ground tiles form a flush, non-overlapping partition. Concrete is the hard handling identifier, including the asphalt-textured yard. Mud occupies x = -36 to -24 and x = 24 to 36, both z = -12 to 12, with a distinct brown material. Both native practice support detection and synchronous network collision/support queries report the existing Core `SurfaceType`. A center support ray takes precedence over edge contacts, so crossing a seam changes handling without creating a physical step. HUD surface labels expose the committed result.

Outer boundaries use continuous four-metre-thick box collision extending from y = -2 to 30, with visible four-metre retaining walls. The extra collision height contains normal blast launches without requiring complicated fence meshes. Open space above the visible wall is intentionally blocked at the perimeter. These bounds cover ordinary driving and the existing explosion-force envelope, not arbitrary teleports or unbounded external forces. The ramp uses one convex hull; other colliders are boxes independent of imported visual geometry. Decoration outside the walls has no gameplay collision.

### Configuration and Stable Markers

Core `PrototypeArena.Configuration` defines the shared bounds and ordered spawn contract. `ArenaConfiguration` copies inputs and rejects counts other than eight per category, absent/whitespace/duplicate IDs across both categories, nonfinite poses, out-of-bounds positions, player centres less than five metres apart horizontally, invalid bounds and unknown or absent surface types. `ArenaSpawn` carries a stable string ID, numeric world position and yaw. Player IDs `player-01` through `player-08` are authored slots, independent of session player identities. Item IDs `item-01` through `item-08` define the live network-arena pickup locations. The network host registers the actual validated scene markers with Core spawn authority; local rigid-body practice retains marker-only presentation.

Client builds real `Marker3D` nodes under `PlayerSpawns` and `ItemSpawns`, draws low ground markers/labels and extracts their actual poses plus collision-surface identifiers back through the Core validator at startup. The native arena check compares that extracted contract with the host's configuration. Host admission and rejoining use the same ordered slots; departed slots may be reused, while session identities remain monotonic. The configuration validator checks data invariants, not scene collision accessibility; native vehicle-footprint checks cover that separate concern.

Practice constructs eight native `VehicleBody` adapters around the existing single Core simulation. The first accepts local controls; the other seven receive neutral frames and remain physical collision targets. **Reset arena** requests new vehicle lives at their stable slots and restores all three props. **Detonate nearby** exercises the existing vehicle damage/impulse helper and applies its falloff to props. The focused two-vehicle ramp fixture remains available only to the older movement regression harness through `LegacyTestLayout`.

### Movable Props and Networking

The host alone simulates the three native `RigidBody3D` props (`prop-01` through `prop-03`). Each uses 180 kg mass, CCD, box collision, zero restitution, linear damping 1.2 and angular damping 2. Network vehicle sweep contacts apply one bounded central impulse per contacted prop per observation; client prediction/reconciliation never applies those impulses. Practice uses native rigid-body contact solving. Prop blast impulses reuse Core explosion falloff with an 8 metre radius and 1,800 N·s maximum.

Core `ArenaPropSnapshot` validates and copies complete numeric observations in stable prop order. The existing vehicle network protocol carries a new full prop publication (kind 4), 176 bytes at 20 Hz, with session generation, host tick and three poses/velocities. Unknown versions, malformed/trailing bytes, invalid quaternions, nonfinite/excessive state and wrong prop counts are rejected. Client driver accepts only the established host, active generation, unreliable delivery and increasing ticks. Full updates recover from dropped packets without a delta baseline. Clients reconstruct frozen collision replicas; they do not independently integrate props. Native physics remains in Client, while validation and serialization stay engine-independent in Core.

Prop replicas currently update directly at 20 Hz. Vehicle prediction uses the latest received prop collision poses; there is no prop interpolation, full-world rollback, lag compensation or cross-platform deterministic native physics. These are explicit prototype limits. Returning to the lobby and starting again reconstructs the complete arena and resets prop state.

### Materials, Assets and Verification

Kenney Industrial/Factory/Racing GLB shapes and Poly Haven barrier/barrel glTF models provide the requested industrial forms. Kenney meshes receive dirty concrete, rust or rusted-sheet `StandardMaterial3D` overrides; selected Poly Haven 1K PBR maps provide the material treatment. The source manifests, versions, CC0 provenance, imported subsets and checksum metadata live in `THIRD_PARTY.md` and `assets/arena/sources.json`. Rendering does not generate collision shapes.

`check-arena.ps1 -GodotPath <Godot .NET executable>` exercises all eight practice spawns, wall impacts at 55 m/s, normal blast launches, both Mud regions, vehicle/prop and explosion/prop response, fixed obstacles and vehicle-sized approaches to every item marker. It also exercises production network sweep collision and surface observations. `-Visual` captures an overview and ground-level material view. Core NUnit checks cover authored-data invariants, host slot reuse and prop codec rejection/round trips. Client driver tests cover prop authority, generation, ordering and snapshot cadence. Separate-process `check-network-vehicles.ps1` also launches a host prop and checks its replicated motion and client collision reconstruction; `check-lobby.ps1` covers repeated eight-player arena sessions. These local checks do not establish long-session competitive balance, physical-controller ergonomics, WAN/NAT operation or subjective eight-human combat feel.

## Transport and Replicated-State Foundation

### Purpose

The networking foundation gives authoritative Core code a transport-facing seam without importing a native networking library. It also distinguishes opaque delivery from game-specific replication so transport choices cannot become gameplay-state ownership.

### How It Works

`Trackstorm.Core.Networking.Transport` defines `ITransportGateway`, opaque `TransportMessage` payloads with reliable/unreliable delivery, lifecycle events with stable reasons, nullable statistics and validated `NetworkSimulation` settings. The gateway accepts a validated `TransportEndpoint`: `DirectIp` carries an adapter-validated development address, while `PeerSession` carries opaque peer identity and session context. Core imports no provider types. The gateway supports provider-selected listen/connect, disconnect, poll, send/receive, statistics, simulation configuration and reusable stop. Core `TransportConnections` owns admission and valid transitions: a host reserves one of eight player slots, leaving at most seven remote peers connecting or connected. Pending peers reserve slots before native acceptance. Disconnect releases a slot; identifiers increase within a gateway lifetime and closed IDs never revive. These local peer IDs carry no gameplay/player identity or authority.

Client `GameNetworkingSocketsTransport` implements the boundary using the pinned standalone GameNetworkingSockets library and GnsSharp binding. `NetworkTransportNode` polls on Godot's main thread and disposes on scene exit. Gateways share one process-global native runtime on the same thread. Managed events run after native callbacks return, so consumer exceptions cannot unwind across native code. Aggregate connection state reports peer connectivity; `IsListening` separately reports host availability. Invalid addresses and failed listen/connect creation throw synchronously; asynchronous failures produce lifecycle events with stable reasons and diagnostic text. `Stop` closes peers/listener, drains callbacks and clears received payloads; `Dispose` also releases the shared runtime. The native module stays mapped for the process lifetime because P/Invoke caches function addresses; the last gateway releases native networking threads and sockets.

Reliable sends use ordered retransmission; unreliable sends use the independent no-retransmission path without reliable head-of-line delivery blocking. Both share network bandwidth. Each payload is bounded at 64 KiB and native send rejection is explicit. Receive polling copies and releases each native message, reads up to 32 messages per peer per poll and bounds the managed queue at 256 messages. Capacity overflow disconnects the peer with an explicit reason. Callers must consume messages regularly. Copied payloads remain valid after native memory is released; protocol consumers must account for peers closing after reception.

Statistics expose nullable round-trip ping and local/remote on-time delivery quality fractions. Loss is one minus quality and includes late packets; unknown samples stay null instead of reporting perfect connectivity. Network simulation acts on outbound native packets **process-wide**, including all local test gateways. Latency clamps to 0–5000 ms, average jitter to 0–1000 ms (native maximum twice the average), percentages to 0–100, and reorder delay to 0–5000 ms. Non-finite percentages are rejected. Simulation acts below retransmission, so reliable loss is recovered by the native library. Reset with `ConfigureSimulation(new NetworkSimulation())`; stopping one gateway does not change other gateways' process-wide test settings.

Development launches pass `-- --transport-host=0.0.0.0:27020` or `-- --transport-connect=127.0.0.1:27020` to Godot. IPv6 uses brackets, for example `[::1]:27020`. The options are mutually exclusive. The diagnostics HUD displays the first available sampled ping among connected peers when enabled, or unavailable when none has a sample. These options open the development lobby in host or join mode; `--player-name=Name` supplies the requested display name. Without either option the main scene opens the EOS multiplayer browser; Direct-IP Host/Join is an explicit developer fallback, initializing transport only when requested. `-- --local-practice` selects the local rigid-body arena. Lobby gameplay starts only after the host validates a Start Match intent.

`Trackstorm.Core.Networking.Replication` separately defines `SimulationStateMessage`. Its legacy version-one wire representation contains an envelope version followed by one versioned `InputFrame`, for 22 bytes total. It supports only states without registered vehicles and rejects a populated aggregate rather than silently discarding gameplay state. Complete archival vehicle serialization uses `VehicleSnapshotCodec`; live vehicle replication uses the compact binary `VehicleNetworkCodec` described below. Reading the legacy message reconstructs a tick/input-only Core state; it does not open a connection or call a transport. The split allows a native adapter to carry bytes without interpreting authoritative gameplay state.

### Design Reasoning

Native transports are runtime and platform concerns, while message meaning, validation, and synchronization-relevant state are authoritative game concerns. Keeping an opaque byte seam between them prevents Core from acquiring Godot or native APIs and lets deterministic serialization be tested without a socket. A concrete transport can change without moving replication rules out of Core.

### Native Listener Reuse

The pinned GameNetworkingSockets 1.6.0 library defers raw UDP socket destruction to its service thread. `CloseListenSocket` invalidates the listener before that thread necessarily releases the OS port; `RunCallbacks` only dispatches notifications and is not a socket-release barrier. See the pinned [raw socket close implementation](https://github.com/ValveSoftware/GameNetworkingSockets/blob/v1.6.0/src/steamnetworkingsockets/clientlib/steamnetworkingsockets_socketthread.cpp).

For immediate reuse of the same endpoint on the same gateway, `Listen` retries only the native bind diagnostic for Windows `WSAEADDRINUSE` (`0x2740`). Each attempt yields for 1 ms outside native calls so the service thread can finish deferred destruction. The recovery window is capped at 100 ms from the successful listener close, not restarted by subsequent calls. This is a short scheduling allowance, not a guaranteed OS release deadline; exhausted recovery still throws. Initial binds, different endpoints, unknown diagnostics, and other native errors fail without retry. No socket-sharing flags or alternate ports are used. Native creation diagnostics are captured synchronously on the calling thread and included in the exception; background output is not logged. The process owner roots the diagnostic delegate across all gateways and native shutdown.

`RepeatedHostJoinAndStopReleasesListenerAndPeers` retains one port across all twelve cycles. A separate native test holds a real exclusive UDP socket, checks the bind diagnostic and absent listener state, then verifies recovery once that owner closes. The test helper's ephemeral-port reservation has a release-to-bind race with other processes; it is not a reservation that production can rely on, and unrelated occupancy is not treated as our deferred close.

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

The current replication envelope is little-endian through its nested `InputFrame` contract and uses explicit version bytes rather than CLR memory layout. Message interpretation and peer identity authentication belong to the game protocol; delivery mode, payload limits and native buffer ownership are enforced by the adapter. The Core simulation independently rejects duplicate, skipped, or out-of-order frame ticks. Transport messages, lifecycle events, configuration and statistics are engine-independent JSON-serializable data. Serializing diagnostics or connection IDs does not restore live sockets or grant gameplay identity.

### Interactions

Client `VehicleNetworkDriver` routes versioned vehicle protocol payloads over `ITransportGateway` at fixed boundaries. Reliable session assignment is separate from unreliable redundant inputs and world snapshots. The legacy `SimulationStateMessage` remains a standalone serialization contract and is not the live vehicle protocol.

### Intentional Limitations / Tradeoffs

The packaged native adapter currently targets Windows x64. [Dependency provenance and licenses](licenses/transport/README.md) record the pinned binary pairing. Native DLLs and notices copy to build/publish output; Godot uses its application base directory to resolve native dependencies. Other platforms need their corresponding bindings and native binaries. There is no matchmaking, automatic connection retry or authenticated Internet player identity. The development lobby below owns session identity and admission over this transport. The vehicle loop supplies prediction and reconciliation, but does not implement full-world rollback or future match events. This is a development IP transport, not an Internet session service.

`check.ps1` verifies Core contracts and transport flag/error conversions in Debug and Release. `check-transport.ps1 -GodotPath <Godot .NET executable>` additionally runs native UDP tests for host plus seven clients, excess admission, bidirectional payloads, reliable ordering during unreliable loss, sampled diagnostics, initial/established timeouts, queue overflow and repeated reconnect/host cycles. It then runs production Godot nodes through three creation/poll/message/removal cycles. `TRACKSTORM_TEST_ADDRESS` can select a local IPv4 LAN interface instead of loopback for native tests. These checks do not establish cross-machine firewall behavior, long-session memory stability, export-template packaging or non-Windows compatibility.

## Host-Authoritative Vehicle Networking

### Ownership and Fixed Steps

`HostVehicleSession` owns the active roster, sender-to-vehicle assignment and a single `Simulation` at 60 Hz. In the development session, the lobby assigns a stable player ID on reliable admission; the vehicle session uses that same ID when the lobby starts an arena. Each arena uses a fresh vehicle protocol generation and fresh simulation/prediction state. The standalone vehicle verification driver also supports immediate reliable vehicle assignment without lobby presentation. Clients never choose the vehicle affected by their input. The host reserves its own slot and admits at most seven clients. Departure removes the vehicle at a fixed boundary. Rejoining receives a new gameplay identity, so delayed data cannot revive a departed player. `Simulation.JoinVehicle` and `LeaveVehicle` change the roster without rewinding the world; the original pre-start `AddVehicle` API retains its existing contract.

The host accepts logical input only. It never accepts client HP, position, velocity or handling-state claims. Host simulation runs the existing `Simulation` → `VehicleAuthority` → `VehicleMovement`/`VehicleHealth` path. Local prediction restores the same aggregate and advances only `VehicleMovement`; HP, death and respawn always remain at the latest accepted host boundary. Core owns sequencing, host validation, bounded prediction history, acknowledgement/replay, immutable snapshot data and ordering. Client owns transport pumping, native collision observations, render-time interpolation, correction offsets, camera and diagnostics. The dependency remains `Client -> Core`.

### Input, Snapshots and Reconciliation

Each client input receives a wrapping uint sequence independent of the host's ulong simulation tick. A packet carries the current command plus up to three recent unacknowledged commands in order, without duplicates. The host rejects stale/out-of-order packets and malformed windows within a 120-command validation bound. Before stepping, a burst larger than six queued commands is trimmed to the newest six (100 ms at 60 Hz); older controls are explicitly retired in the acknowledgement. This prevents a delivery or scheduling stall from creating permanent input latency. It consumes at most one per fixed step, rebasing that input onto the host tick; retired commands do not grant extra simulation ticks or replay button edges. A missing sequence gets three ticks for recovery; an unrecoverable gap is explicitly retired before consuming the next available command. Brief gaps retain held controls without repeating button edges; after 15 silent ticks controls become neutral.

Vehicle publication handles a native close racing the preceding transport poll. A failed send disconnects that peer, and the next host boundary removes its vehicle while other peers continue. A client receives a recoverable rejoin diagnostic. The same rule applies to snapshot, prop, reliable item and assignment sends; network failure does not escape as an unhandled arena exception.

After reliable assignment, each captured client input is sequenced, retained and sent immediately, including while the first authoritative snapshot is still in flight. Prediction does not create vehicle state until that snapshot provides the host-owned spawn boundary. Initialization removes any inputs already acknowledged by the snapshot and replays the remaining commands once in original sequence order. Prediction then continues immediately after local capture without waiting for an acknowledgement. The shared history is bounded to 120 commands (two seconds at 60 Hz); exhaustion before or after prediction initialization stops the session with an explicit reconnect diagnostic rather than silently dropping required replay state. Serial comparisons accept uint wrap and reject equal, stale and half-range-ambiguous values. Core simulation ticks remain checked ulong values and never wrap silently.

World snapshots run at 20 Hz. The version-four `TS` binary protocol separates reliable welcome/assignment, unreliable input windows and world snapshots. Lifecycle boundaries use the same complete snapshot payload with reliable delivery; ordinary movement snapshots remain unreliable. A four-input packet is 121 bytes including its observed life generation; the eight-vehicle test snapshot is 1,333 bytes (bounded below 1,400 bytes). Little-endian fields carry the session generation, global tick, complete active roster, per-owner acknowledgement, identity/life, lifecycle and optional respawn deadline, movement, observed linear/angular velocities, HP, damage attribution, collision cooldown and up to 64 accepted one-shot effects per vehicle. Each effect retains damage metadata, impulse and application offset at the pre-solver command boundary. Older protocol versions are rejected. Payloads remain capped at 16 KiB; decode validates versions, lengths, flags, finite values, tuning, identities and matching ticks before publication. The separate reliable item protocol carries complete vehicle outcomes when items change. Prediction restores already committed HP and reproduces retained impulses during the next observation; it never reapplies their damage.

### Native Collision Replay and Presentation

The network arena uses synchronous Godot [body motion queries](https://docs.godotengine.org/en/stable/classes/class_physicsserver3d.html#class-physicsserver3d-method-body-test-motion) to reconstruct collision observations during ordinary steps and multi-tick replay. Each vehicle's preceding Core velocity command is swept/slid against native shapes in up to four queries, with orientation integrated from commanded angular velocity. Four wheel rays provide the same suspension observations as practice. Side contacts apply bounded angular response from their contact lever arm; accepted explosion impulses retain their application-offset torque. Numeric support/contact observations return to Core for movement and collision damage. All host proxies are positioned at the same previous boundary before the batch is evaluated. Render offsets never move collision proxies or change HP. The offline arena retains its native rigid-body solver and movable-prop demonstrations.

This is a replay-capable collision adapter, not full native rigid-body rollback. Network vehicles use kinematic collision proxies; they do not reproduce the offline arena's rigid-body impulse exchange with movable props. During local replay, other vehicles use their latest authoritative collision proxies, so unpredictable vehicle-to-vehicle contacts can require larger corrections. The static environment is shared by host and clients. Cross-platform bit-identical Godot collision solving is not promised.

Core retains at most 20 ordered world snapshots. Client remote rendering normally runs six host ticks (100 ms) behind received authority. Its cursor advances monotonically and adjusts speed within 90–110% to absorb jitter without restarting on each packet. Positions and velocities interpolate linearly; orientations use normalized spherical interpolation. Before/after available data, rendering holds the nearest endpoint and never extrapolates. New lives are discrete transitions, and departed identities are removed from the active roster.

Local authoritative corrections apply to gameplay immediately. Client preserves a presentation-only position/orientation offset for small corrections and exponentially decays it at rate 15/s. Position errors up to 1 cm need no offset; errors of 3 m or more snap explicitly instead of dragging a ghost through obstacles. The network Arena tools panel exposes prediction error after replay, client snapshot age, actual interpolation delay, last acknowledged input and large-correction count, alongside HP and handbrake/slip/support state. Snapshot age is shown as unavailable on the host because the authority does not receive world snapshots. Existing bindings and settings continue to feed local input and preferred-unit speed/ping telemetry.

### Verification and Boundaries

Core tests cover serial wrap/ambiguity, redundancy, bounded history, acknowledgement removal, host input/loss handling, sender ownership, eight-player admission and departure, compact codec validation, gameplay-state preservation, stale snapshot rejection and reconciliation replay order. Fast Client tests cover correction thresholds/decay, interpolation endpoints/midpoints, explicit holding, life transitions and render-clock ordering without loading Godot or native sockets. These run through `check.ps1` in Debug and Release and through the non-native CI filter.

`NativeVehicleReplicationTests` exercise real UDP at baseline, 30 ms outbound delay (approximately 60 ms RTT), 50 ms outbound delay with 10 ms jitter (approximately 100 ms RTT), 2% packet loss and eight local instances with combined impairment. They inject stale snapshots and measure correction magnitude, acknowledgement progress, immediate prediction and settled convergence using a deterministic flat-ground observation seam.

`check-network-vehicles.ps1 -GodotPath <Godot .NET executable>` starts separate production Godot host/client processes with real collision queries. `-Players 8`, `-Latency`, `-Jitter` and `-Loss` select stress/impairment conditions; delay is per outbound direction. `-Visual` renders one client and captures a viewport image. Reports under `.godot/network-vehicle-checks` include roster size, snapshots, input immediacy, sliding/handbrake frames, correction percentiles, large corrections and final gameplay state. The standalone vehicle harness bypasses the development lobby to isolate vehicle replication. Host migration, automatic reconnect, full-world rollback and Internet identity authentication remain unsupported. Reliable item combat is implemented below.

## Development Lobby and Session Flow

### Behavior and Controls

Selecting **Developer fallback: Direct-IP / LAN** opens **Host Game** and **Join Game by address** with display-name and numeric address entry. Explicit transport launch arguments also select this fallback. The default `127.0.0.1:27020` is for same-machine development. For LAN hosting, enter the host's local interface address or `0.0.0.0:27020`; clients enter the host's reachable LAN address and port. IPv6 endpoints use brackets. The host listener and joining clients use the existing GameNetworkingSockets gateway. Invalid endpoints return a visible error; admission failure, connection timeout or host loss returns to Host/Join with a diagnostic. Settings remain available above session presentation and continue suppressing driving while open.

The authoritative roster contains one host and at most seven clients. Rows display a sanitized name, stable session player ID, host/local markers and ready status. Everyone, including the host, explicitly chooses **Ready**; **Unready** reverses only that player's readiness. **Start Match** is available only to the host. Core requires a lobby phase, one through eight players, every player ready, and an exact match between admitted peers and the transport connection roster. Pending admission or an unremoved departure prevents starting. A single ready host is intentionally valid for development.

One accepted start publishes the shared arena phase and generation over reliable ordered delivery. Each peer constructs the same network arena and resumes the existing authoritative vehicle/prediction loop. Clients cannot start or end an arena, even by submitting those intents directly over the transport. The host's **End session / Return to lobby** is the development match-completion action: it tears down each arena, retains connected session identities, and resets every ready flag. There is no automatic win condition or scoring system in this development flow. A later start constructs a fresh arena without reconnecting. **Leave session** disconnects an individual client; on the host it closes the session for everyone.

### Core Ownership and Wire Invariants

`Core.Sessions.LobbyAuthority` owns the transport-sender mapping, monotonically assigned player IDs, capacity, readiness and lifecycle mutations. Identity one belongs to the host; remote IDs are never reused within the session, including after disconnect/rejoin. Names are not identities and duplicate names are permitted. Names retain Unicode letters/digits, hyphen and underscore, collapse whitespace, discard control/markup/directional characters, and truncate to 24 Unicode scalars without splitting surrogate pairs. Empty results become `Player`. Player IDs represent this development session only, not authenticated accounts or reconnect credentials.

`LobbySnapshot` validates and copies a complete immutable roster. `LobbyReplica` accepts publications only from the established host, for the same session and recipient identity, with increasing revision and nondecreasing arena generation. Client UI renders this state directly. `LobbyCodec` uses `TL` magic, version one, a message-kind byte and bounded UTF-8 JSON, at most 4096 bytes and depth eight. State messages carry session, revision, arena generation, phase, recipient ID and all player records. Client intents carry no target player ID; ownership comes from the transport sender. Session, generation and phase guards reject stale ready/start/return commands. Versions, JSON shapes, roster identities and canonical names are validated before publication.

Client `LobbyNetworkDriver` routes these reliable intents and snapshots through the existing `ITransportGateway`; `DevelopmentSession` owns controls, transport-node lifetime and arena presentation. There is one receive consumer per session: the lobby driver dispatches vehicle payloads to the existing vehicle driver only while the arena is active. Core remains engine-independent and the dependency is strictly `Client -> Core`.

### Vehicle Integration and Disconnects

The lobby starts a new `HostVehicleSession` with its admitted sender-to-player mapping. `JoinPlayer` creates vehicles with the existing stable player IDs and available spawn slots. Remote input still routes by actual sender, never a client-selected vehicle. Lobby phase changes are reliable; existing redundant vehicle inputs and 20 Hz snapshots remain unreliable. Arena generations advance on every start so delayed vehicle packets from a prior match cannot initialize the new prediction/history. Early world snapshots arriving before arena construction may be discarded; subsequent snapshots initialize prediction through the existing bounded input-history path.

Departure removes the entire Core player record, including readiness, and publishes the new complete roster. During gameplay the vehicle driver also removes that peer's vehicle at a fixed boundary. Rejoining is allowed only in the lobby and assigns a fresh ID. Joining an active arena is rejected. Host departure returns every client to Host/Join; there is no host migration. Return to lobby retains sockets but reconstructs vehicle authority, prediction, interpolation and collision proxies on the next start.

### Verification and Intentional Limits

Core NUnit tests cover eight-player capacity, duplicate/invalid/retired IDs, sender-targeted ready/unready, connected-roster and host-only start validation, return/reset/repeat, removal in both phases, name boundaries and Unicode sanitization, malformed state payloads, replica ordering and stale command guards. `check-lobby.ps1 -GodotPath <Godot .NET executable>` runs eight production session UIs over actual UDP in isolated Godot viewport/physics worlds. It exercises UI Host/Join and ready actions, matching names/IDs on every peer, non-host start rejection, one-command synchronized start, stable vehicle identities, two arena cycles, ready-client removal/rejoin, in-arena removal and host-loss cleanup. `-Visual` renders the host and saves lobby/arena screenshots and evidence under `.godot/lobby-checks`.

The lobby harness uses eight real sockets and eight isolated native worlds in one process. Separate-process vehicle checks remain in `check-network-vehicles.ps1`; transport impairment/capacity tests remain in `check-transport.ps1`. These local checks do not establish cross-machine firewall/NAT behavior, cross-platform compatibility, hostile Internet security, long-session soak stability or subjective controller ergonomics. Transitions converge through reliable delivery; peers are not promised to load/render on exactly the same wall-clock frame, and there is no loading barrier or countdown. The prototype arena and host-controlled return are intentional development limitations, not a full competitive match service.

## Held-Item Combat

### Behavior and Acquisition

Network arenas support exactly Wrench and Missile, with one held slot per living player. Use **LMB** or controller **A** through the remappable **UseItem** action, or the **Use held item** button. One press generates one request; holding does not repeat, and a short tap between fixed ticks is retained. Empty-slot requests fail safely. The held-item HUD and floating model show confirmed ownership. The host's development controls give either item to every empty living slot; they cannot overwrite an occupied slot. Normal acquisition happens by driving within range of an available arena pickup, as described below. Local rigid-body practice retains its separate generic blast demonstration.

Wrench repairs 35 HP by default, clamped to the vehicle's existing maximum. A living player can always consume it at full health: zero effective healing still clears the slot and produces a confirmed use outcome. It cannot revive a destroyed vehicle.

Missile launches from the host-observed vehicle center along its forward (-Z) direction at 45 m/s, independent of vehicle velocity and subsequent steering. The collision adapter excludes the owner's body and sweeps the entire 60 Hz segment, including the launch segment. Starting inside the owner's body avoids a muzzle offset skipping a nearby wall. Hits against vehicle proxies, static world, native props and perimeter walls produce one explosion at the hit fraction. There is no homing, gravity, direct-hit bonus or additional direct-hit damage. A projectile that survives 300 ticks expires without exploding. At most 16 missiles can be active; reaching the cap leaves the requested item held.

The explosion linearly fades from 55 HP and 15,000 N·s at the center to zero at/outside an eight-metre radius. `ItemConfiguration` validates configurable healing, speed, lifetime, radius, maximum damage and impulse; the zero edge is the falloff curve's defined minimum. Vehicles and movable objects use the same Core distance/direction helper, including a stable upward direction at the exact center. Every living vehicle in range receives one radial effect, including the owner; walls do not occlude the radial blast. Props receive the host-calculated physical impulse but have no HP or destruction rules. Secondary native collisions can still cause the existing collision damage.

### Authority and Replication

`Core.Items.ItemAuthority`, owned by `HostVehicleSession`, owns slots, match-unique grant tokens, pending uses, projectiles and committed events. Use requests identify the arena generation, vehicle life and exact grant token. The host resolves player identity from the actual connected sender; clients send neither a target player nor damage, healing, launch pose or velocity. Invalid generation, departed/destroyed player, wrong life/token, empty slot and duplicate pending use are rejected. Clearing a slot keeps its token retired; regranting uses a new token, so a delayed request cannot spend the replacement item. Removal and explicit development resets clear stale ownership. Death clears the held item by default in the lethal batch; the optional retention policy transfers it to a fresh life/token on automatic respawn. Inactive players cannot use retained inventory.

Core evaluates item candidates against the complete observation batch, queues repairs/damage through the existing vehicle authority, and commits inventory/projectile changes only after the world step succeeds. Core never references Godot. The Client host supplies segment hit fractions from native ray queries and applies Core-calculated prop impulses. Network vehicle observations apply retained central impulses to velocity before the next sweep using the existing 900 kg tuning and 65 m/s bound. Client prediction can reconstruct those physical effects but cannot originate authoritative item outcomes.

`ItemCodec` uses `TI` magic, version two and bounded binary messages up to 32 KiB. Complete publications contain increasing revision, the current-generation vehicle snapshot, at most eight slots, at most 16 projectiles and at most 24 launch/repair/impact events, and the complete eight-marker spawn state when a layout is registered. Version-one item envelopes are rejected; peers must run the same item protocol version. Clients accept them only reliably from the established host with a newer revision in the current arena. They update item presentation once per publication; vehicle snapshot tick ordering prevents a delayed reliable outcome from rewinding a newer world snapshot. Reliable use requests and complete ownership/outcome publications guarantee delivery through the existing ordered transport. Moving projectiles currently also publish reliably at fixed-step frequency; this deliberately favors simplicity for the bounded prototype over a separate unreliable interpolation channel. Scene reconstruction on lobby return starts a fresh authority and token space under a new arena generation.

### Presentation and Assets

Client `ItemPresentation` reconstructs the Kenney Weapon Pack rocket mesh with a project-created dark metallic orange-emissive `StandardMaterial3D`. A small project-built wrench and the held rocket use the rust-colored, bright pickup material. Kenney Particle Pack fire, smoke and spark textures drive GPU particle launch/impact effects and a world-space `GpuParticles3D` trail. The parameter-controlled `DamageFlash.gdshader` flashes the chassis on confirmed HP loss. Original synthesized impact/blast audio is shared with existing vehicle feedback and uses the SFX bus. These effects have no collision or HP authority. All presentation nodes are owned by the arena and removed on teardown; transient bursts have bounded lifetimes.

`assets/items/sources.json` records source URLs, archive/file hashes, selected files, CC0 licenses and the original author's Weapon Pack mirror. Native materials and the damage shader are project-created; no plugin or optional dissolve shader is introduced.

### Verification and Limits

Core tests cover repair/clamping/full-health consumption, invalid/duplicate/stale requests, a single occupied slot, destroyed/departed players, straight launch velocity, lifetime, monotonic radial damage/impulse, exact-center safety, one impact per projectile, atomic rejected collision queries and reliable codec corruption. Driver tests check trusted host/reliable delivery and reject forged or duplicate publications without predicting consumption.

`check-items.ps1 -GodotPath <Godot .NET executable>` runs eight actual UDP peers in isolated native worlds. It exercises full and partial HP Wrench uses on every peer, repeated requests, the real remote input-use edge, matching launch/impact identities and points on all clients, vehicle distance falloff, native vehicle/prop impulse response, and static-container/perimeter collision. `-Impaired` adds 30 ms outbound delay, 5 ms jitter and 2% loss; `-Visual` renders a client and saves impact images under `.godot/item-checks`. The native prop assertion waits for the next solver steps after confirmed impact. Existing lobby and network-vehicle harnesses cover surrounding session lifecycle and movement regressions.

The native item harness uses one Windows process with eight sockets/worlds; it does not establish multi-machine/NAT compatibility, cross-platform deterministic physics or long-session performance. Projectiles are swept points, the radial blast does not use line-of-sight cover, and there is no combat-specific client prediction. Reliable projectile movement may pause visibly under delayed delivery. Reconnect persistence remains a separate feature.

## Item Spawn and Distribution

### Acquisition and Authoritative State

Each network arena registers the eight actual `ItemSpawns` markers extracted by `CombatArena.ValidateScene()`. Core `ItemSpawnAuthority`, owned by `HostVehicleSession`, is registered once before the first world step. Marker IDs and positions come from that validated contract; no additional pickup positions are synthesized. All eight start available. The host's Client adapter detects proximity using native vehicle proxies and scene markers, then Core rechecks the committed vehicle position, living roster membership, available spawn and empty current-life slot. Remote clients cannot submit a claimed position, recipient or item choice.

An accepted contact selects one item, grants it through the existing `ItemAuthority` single-slot API, and records the claimant, grant token, awarded item and next activation tick before publication. Rejected contacts do not advance the selector or consume availability. Contacts in a host boundary are deduplicated and ordered by ordinal marker ID then vehicle ID, giving simultaneous contestants exactly one winner. This stable tie-break favors the lower vehicle ID for an exact same-tick tie. Occupied slots never replace their items or consume the pickup. A player remaining in range can acquire again after consuming an item, including within that same committed boundary.

Core advances cooldowns from committed 60 Hz world ticks, after successful vehicle/item simulation. A claim at tick `t` reactivates at `t + CooldownTicks`; clients never activate from a local timer. Claims survive claimant departure or item consumption until the deadline. Lobby return discards the match-owned authority; the next arena starts fresh. Restore/reconnect of this state is not implemented here.

### Configuration and Replication

`ItemSpawnConfiguration` defaults to 600 ticks (10 seconds), a three-metre three-dimensional center-to-marker radius, equal Wrench/Missile weights and seed 1. Both weights must be positive, their sum must fit an integer, cooldown is bounded to 1-216000 ticks, and radius must be finite in (0, 10]. The composition root supplies immutable tuning before scene entry through `NetworkVehicleArena.SpawnConfiguration`. No runtime settings UI is added. A seeded selector owns its own stream per arena; an injected selector supports exact deterministic tests. The default seed repeats the item sequence across fresh matches; this is reproducible prototype behavior, not a competitive randomness guarantee.

Spawn changes and assigned slots travel together in the existing reliable `ItemPublication`, including absolute activation deadlines and last claim/token/item. Full state is sent when inventory, projectiles, spawn availability or admission changes. Deadlines need no per-tick countdown traffic. The bounded version-two codec validates IDs, counts, claim fields and deadline/availability consistency before exposing detached state. The established host, reliable delivery, current arena generation and increasing publication revision remain mandatory. There is no predicted pickup award; latency can delay visible confirmation, and the local view does not reactivate ahead of the host.

### Presentation, Assets and Verification

`ItemSpawnPresentation` builds one floating, rotating, non-colliding pickup per marker from the already acquired CC0 Kenney Weapon Pack rocket geometry. The existing rusty emissive pickup material, local light and native `GpuParticles3D` make it stand out from arena props. The rocket is a generic pickup marker: the item is selected on claim, and the confirmed held model/HUD identifies the result. Active models and particles hide together on claim; a `RECHARGING` label remains until confirmed reactivation. A use followed by reacquisition updates the held model even when there was no intervening empty-slot publication.

Provenance and license remain in `assets/items/sources.json` and the acquired Kenney license. No additional pack, plugin, HUD icon or pickup audio is acquired. Pickup sound remains part of the separate audio feature.

Deterministic NUnit coverage verifies actual registration IDs, single registration, alive/empty/in-range validation, same-tick contention, retry rejection, exact cooldown boundaries, invalid selectors and weights, seeded distribution and complete publication corruption handling. Driver coverage applies the host/trust/delivery/revision guards to spawn-bearing publications.

`check-item-spawns.ps1 -GodotPath <Godot .NET executable>` runs eight actual UDP peers in isolated Godot worlds. Two remote vehicles contest one pickup; every peer verifies one matching claimant/token/item and cooldown. It exercises reactivation, all eight pickup positions, normal weighted distribution of both items, and occupied vehicles remaining in range without duplicate grants. `-Impaired` adds 30 ms outbound delay, 5 ms jitter and 2% loss; `-Visual` captures the rendered client. Existing item, lobby and network vehicle checks cover surrounding combat and scene lifecycle. These are local native integration checks, not multi-machine/NAT or long-session performance evidence.

## Player Death and Respawn

### Authoritative Lifecycle

The existing `VehicleSnapshot` owns `Lifecycle` (Alive, Dead, Respawning), `LifeId` and nullable `RespawnAtTick` alongside HP and movement. `CanInteract` is the shared participation gate. There is no parallel player-health or Client lifecycle authority. `VehicleAuthority` enters Dead on the lethal tick, using the unique `DamageEvent.DestroyedTransition` for attribution. On the following tick it enters Respawning until the deadline; with a one-tick delay the next boundary is already Alive. Additional damage, repair, input and impulses cannot revive or move an inactive vehicle.

`RespawnConfiguration.DelayTicks` defaults to 180 (three seconds at the production 60 Hz rate) and must be positive. Death records the absolute global deadline once. Simulation steps determine elapsed time; frame rate, wall-clock time and packet receipt never advance the timer. The host session and production local practice enable this policy. Isolated movement/replay fixtures can omit it and retain explicit reset control. Checked tick/life arithmetic fails before world publication rather than wrapping into a different life.

### Spawn Selection and Reset Policy

Selection uses the eight validated `ArenaConfiguration.Players` markers. Vehicle identity and the next life generation choose a deterministic cyclic starting slot; all eight markers are reachable without random or mutable selector state. The selector skips markers within 4.5 horizontal metres of another living vehicle. Simultaneous respawns reserve candidates in stable vehicle-ID order within the atomic batch. If every marker is occupied, the vehicle remains Respawning and retries on subsequent authoritative ticks; the configured deadline is the earliest eligible tick. This checks vehicle clearance, not line-of-sight safety, spawn protection, or dynamic prop occupancy. Static marker accessibility is validated by the native arena harness.

At the deadline, when a marker is available, Core increments `LifeId`, restores that marker's position/yaw and full configured MaxHP, zeros both observed and commanded linear/angular velocities, and clears drift, boost, grounding, surface memory (Concrete), damage attribution, collision cooldown and accepted impulses. The respawn boundary itself consumes no driving, repair, incoming damage or queued impulse; normal driving and damage evaluation resume on the next step. Global simulation/input time never rewinds. Native adapters reconstruct the accepted transform and velocities and reset interpolation/correction and damage-flash memory.

### Combat and Inventory Integration

Inactive vehicles have no native collision layer/mask and are hidden. Core independently rejects their movement and item grant/use requests and filters effects/contacts attributed to registered inactive vehicles. This prevents native presentation from becoming a combat rule. Batches evaluate participation at their starting boundary, so simultaneous living attackers can trade lethal hits deterministically. Existing projectiles owned by a player are removed in that player's lethal batch and cannot survive into another life. The live `ItemSpawnAuthority` pickup path uses the same participation gate before choosing an item or consuming marker availability. Death and waiting leave unclaimed pickups available for living players.

`RespawnConfiguration.ClearHeldItemOnDeath` defaults to true. `ItemAuthority` clears the existing slot in the same committed death batch. With retention explicitly enabled, the held item remains unavailable during death/waiting and transfers to the new life with a fresh grant token on respawn. Pending uses clear each step; an old life/token cannot spend the retained or replacement item. Disconnect and explicit development resets still discard inventory.

### Replication, Prediction and Hooks

Every lifecycle or life-generation change publishes a complete existing vehicle snapshot reliably through the current ordered transport. Collision-only deaths therefore replicate even without item changes or a periodic movement publication. Joining peers receive a reliable current boundary. Aggregate codec version two and vehicle protocol version three preserve lifecycle/deadline; the EOS discovery compatibility bucket is `trackstorm-lobby-3`. Older aggregate/gameplay versions are rejected.

The existing snapshot history rejects stale poses. A delayed reliable death/wait/respawn publication can still notify presentation observers once, even if a newer unreliable movement snapshot arrived first; it cannot rewind the current vehicle state. `Simulation.LifecycleChanges` exposes immutable committed transitions, including the existing damage attribution, for later scoring or other consumers. `VehicleNetworkDriver.LifecycleReceived` provides ordered reliable full boundaries for future Client UI/audio. Consumers distinguish transitions by vehicle/life/state; no scoring or dedicated death audio is implemented.

Prediction advances movement only and keeps host HP/lifecycle unchanged, including when its local tick passes the respawn deadline. Lifecycle corrections neutralize retained controls while preserving sequence acknowledgements; input envelopes identify the observed life, and the host acknowledges delayed old-life inputs as neutral. Buffered held controls also clear on host lifecycle boundaries. Remote rendering excludes earlier-life poses after a respawn; local correction offsets reset and the chase camera snaps on the new-life boundary instead of blending a wreck into its spawn.

### Presentation and Verification

Client `VehicleDestructionEffects` consumes confirmed deaths once per life and uses the already-acquired Kenney Particle Pack CC0 fire, smoke and spark textures for a short destruction flash, smoke and debris-like sparks. Existing provenance and license records remain in `assets/items/sources.json`. Bursts have no gameplay collision or state access for mutation, expire after 1.6 presentation seconds, and are capped at 24 during delivery catch-up. Arena teardown owns all particle nodes. There is no dedicated death-animation system, dissolve shader, or destruction/respawn audio.

Core tests cover lethal crossing, duplicate damage, inactive interactions including live pickup claims, exact thresholds, full reset, optional inventory retention, repeated cycles, custom markers/all-eight coverage, occupied markers, simultaneous reservations, stale-life input, prediction authority and both state codecs. Driver tests cover collision-only reliable publication, stale/duplicate/forged delivery and reliable events arriving after newer movement.

`check-death-respawn.ps1 -GodotPath <exe>` runs eight UDP peers in isolated Godot worlds through four alternating remote missile/collision deaths and respawns. It asserts matching lifecycle/deadline/spawn/HP across peers, native collision/visibility changes, movement/item rejection, reset physics/transients and bounded once-per-life VFX cleanup. `-Impaired` adds 30 ms outbound delay, 5 ms jitter and 2% loss; `-Visual` renders a client and saves death/respawn images. This is single-machine native evidence, not separate-PC, authenticated EOS peer gameplay, or an indefinite multiplayer soak.

## EOS Runtime and Online Identity

### Behavior and Development Authentication

The multiplayer entry point enables the Client-owned EOS development identity panel automatically. Local practice does not start EOS; the legacy `--eos` option remains accepted. The panel resolves the embedded development configuration or an explicit advanced override, starts Connect login, shows authentication/status such as `EOS: Online`, and offers login/logout. The hashed PUID fingerprint remains available through identity diagnostics, but is not displayed by the multiplayer panel. Invalid configuration or missing native dependencies report actionable errors. EOS availability never gates the current GameNetworkingSockets session menu or gameplay.

The official EOS Connect Device ID flow creates/reuses a credential for the local Windows user and logs in with a null external token. It creates a product user only when Device ID login returns the appropriate new-user continuation. Different PCs/profiles provide independent credentials, supporting the five-tester workflow and the eight-player target without shared developer accounts, Epic accounts, the Developer Authentication Tool or a custom anonymous backend. Repeated processes under one OS profile are not distinct testers. The generic display-name metadata is not identity. Device ID is a development pseudo-account and cannot recover from loss of its OS credential store; no storefront persistence or account-linking flow is implemented.

### Ownership, Lifecycle and Identity Boundaries

`Client.Online.EosIdentityService` owns a replaceable `IEosPlatform`. The official `EosSdkPlatform` initializes a platform, ticks on Godot's main thread, translates Connect results, and releases handles and notifications on stop/disposal. Native callbacks enqueue work, which runs only after native Tick returns. Generation and state guards reject late/duplicate completions after cancellation, replacement or disposal. Login/logout have a monotonic 60-second deadline. Logout clears the published identity immediately and uses Connect Logout before releasing the platform. Cancellation during login releases the platform directly. Pending managed callback registrations are removed only after native platform release. Expired/lost authentication clears identity and requires explicit login.

`EosProcessRuntime` initializes the SDK once and permits one platform owner at a time. Repeated platform/login/logout/release cycles preserve this process initialization. SDK shutdown at application exit is terminal, as required by Epic's API contract; attempts to start EOS after shutdown are rejected. Full initialization/shutdown cycles therefore use separate application processes.

`OnlineProductUserId`, Core's session `PlayerId`, transport peer IDs, names and addresses remain separate. The PUID wrapper has no implicit conversion to a numeric session ID. No EOS types or dependencies enter Core, serialization or existing transport-neutral contracts. The existing lobby authority continues to assign gameplay identities. `IEosPlatform` and `OnlineProductUserId` are the minimal Client boundary around the current Device ID flow. Storefront credential acquisition, account linking and other authentication providers are outside this foundation and require their own reviewed design.

### Configuration, Dependencies and Limits

Only the development environment is supported. `EosClientConfiguration` embeds the default distributable, untrusted game-client settings in Client so editor and exported execution require no JSON file. If `TRACKSTORM_EOS_CONFIG` is explicitly set, its JSON file completely overrides the embedded default and preserves strict validation and actionable failure reporting; no conventional filename is searched automatically. Configuration values are not logged. Diagnostics expose only validated environment labels, safe result codes and a PUID fingerprint. The SDK manages the local Device ID secret; logout does not delete it. A future production environment requires separate configuration and policy review.

Embedded EOS game-client values are recoverable from a distributed build. Compiling them into the game is a deployment convenience, not a security boundary; the restricted EOS client policy controls what the untrusted client may do. Trusted-server credentials, backend secrets, private keys, account credentials and reusable authentication tokens must never enter this Client configuration. Core remains EOS-independent.

The pinned official C# SDK is compiled outside Core; the Windows x64 native DLL and third-party notices copy into build/publish output. See [EOS dependency provenance](licenses/eos/README.md) and [developer/tester setup](eos-development.md) for version, licensing, policy, prerequisites and verification. A fresh checkout runs `setup-eos.ps1` to acquire the verified official archive. EAS is unnecessary because Connect supplies Game Services identity without an Epic-account login. Overlays, RTC and EAC are disabled/unused. EOS lobby coordination is described below. P2P gameplay transport uses this same platform and identity as described below. Reconnect, host migration, ranked matchmaking, storefront SDKs and social services remain separate concerns. No EOS identity is yet bound to an admitted Direct-IP peer.

## EOS Lobby Discovery and Session Coordination

### Browser, Names and Access

Normal multiplayer opens one browser for compatible **Public** and **Locked / Private** lobbies, without address or port entry. Each row has a name, online member count out of eight, access indicator and Join action. Counts describe EOS membership, not an authoritative gameplay roster. Names truncate visually before the separate count/access/action columns. A refresh replaces the previous cache; membership updates replace entries by logical EOS lobby ID. Rows sort by `OrdinalIgnoreCase` name and then ordinal lobby ID. Search is a local ordinal, case-insensitive substring of the lobby name; clearing it restores all compatible cached results. Updated names stop matching their previous spelling. Native search retrieves up to EOS's 200-result limit in the compatibility bucket; this prototype does not implement global pagination beyond that service limit.

Creation and rename share canonical name rules: retain Unicode letters/digits, apostrophe, hyphen and underscore; collapse whitespace; discard other characters including markup and directional controls; trim and truncate to 48 Unicode scalars without splitting surrogate pairs. Empty canonical names are rejected. The host chooses Public or Locked and supplies a 4–64 character access code for Locked lobbies. The credential edit is masked and cleared after submission. Public joins never request a code; Locked joins display a separate masked prompt. Incorrect codes do not join EOS through the normal coordinator and cannot admit a player through the host transport binding.

Only the host may rename an active lobby. The name attribute changes in place: EOS lobby ID, Trackstorm session lifetime, access policy, verifier, authoritative roster and Ready state are preserved. Current members receive notifications; browsers see the new name on refresh. Invalid names and non-host rename requests receive recoverable errors.

The multiplayer panel owns the visible EOS status and login/retry/logout controls. Distinct initializing, authenticating, online, configuration-error, authentication-failure and unavailable states explain disabled hosting next to Host Game. Configuration guidance distinguishes an invalid embedded default from an explicitly invalid `TRACKSTORM_EOS_CONFIG` override; a valid identity and coordinator enable hosting automatically. Pending cleanup also disables hosting with its current reason. Direct-IP fallback stays at the top; the menu scrolls when recovery guidance needs more space. There is no separate floating identity panel.

### Lobbies Decision and Minimal Metadata

EOS **Lobbies** provides the persistent group, owner-controlled attributes, bounded membership and update notifications needed here. Sessions would add another coordination primitive without improving this browser/rename flow. The pinned SDK's Create, Update, Search, Join, Leave and Destroy APIs implement the lifecycle. See the [official lobby interface](https://dev.epicgames.com/docs/epic-online-services/multiplayer/lobbies-and-sessions/lobby-interface).

Both access modes use EOS `Publicadvertised` permission so they appear together. “Private” means a game-level access code, not EOS invite-only visibility. A new lobby starts hidden until its complete metadata is published. Invites, presence, RTC and host migration are disabled. EOS supplies owner, membership count and eight-member capacity. Custom attributes contain only canonical `name`, Trackstorm `session`, `access`, and a salted `verifier` for Locked lobbies. Indexed bucket `trackstorm-lobby-3` identifies compatibility. No Ready, phase, HP, vehicle, score or item state is stored in attributes. When an attached host driver enters an arena, its authority-derived admission availability changes EOS permission to hidden/nonjoinable; returning to lobby advertises it again. Core independently rejects invalid admission if a publication races discovery.

### Lightweight Credential Handling

Each Locked lobby creates a random 128-bit salt and a 256-bit PBKDF2-SHA256 verifier with 100,000 iterations. Raw codes are not retained in provider state, searchable metadata, row view models or normal diagnostic formatting. Verification metadata is public to support browser admission without a backend; short codes remain susceptible to offline guessing. This is intentional lightweight lobby access control, not account authentication or a security boundary. Renaming does not rotate the verifier. Closing or replacing membership invalidates transport admission and identity bindings; a new lobby creates a fresh verifier. Immutable discovery snapshots never grant authority by themselves.

### Authority, Transport and Identity

`EosIdentityService` supplies the existing authenticated platform to `EosLobbyProvider`. `OnlineLobbyCoordinator` owns discovery, one membership lifetime, subscriptions, safe status and cancellation. `OnlineSessionBinding` and `DevelopmentSession.OpenOnline` connect a separately established authenticated gateway to the existing `LobbyNetworkDriver` and arena presentation. Production composition automatically creates `EosP2pTransport` when online membership becomes active. Its authenticated handshake runs before the existing lobby admission, Ready, Start, arena and Return flow. Provider-independent tests exercise the real packet framing and lobby driver over a fake native packet provider.

The transport adapter must obtain the remote PUID from its authenticated connection, never from a player-supplied identity claim. Before normal Join processing, the host binding checks online membership and the Locked verifier. Only then can `LobbyNetworkDriver` call `LobbyAuthority.Join`. Core assigns its normal monotonic numeric PlayerId. Client keeps an explicit online-identity-to-PlayerId map; PUID is not a gameplay ID and no EOS SDK type enters Core. A departed online member disconnects its bound peer; the existing driver removes the Core player on its next pump. Notifications never set Ready or session phase. The explicit Direct-IP developer fallback uses its original development admission path.

### Lifetime and Verification

Native callbacks enqueue managed work for delivery after platform Tick. Disposable subscriptions reject already-queued events; a separate membership generation rejects notifications from an earlier visit even to the same lobby. Operation generations reject stale create/join/rename callbacks and search request generations reject obsolete refreshes. Late successful membership operations are explicitly left/destroyed. Leaving or closing drops the departed membership snapshot from the browser until fresh discovery arrives. Failed close retains cleanup ownership, blocks replacement and exposes Leave for retry. Search and membership operations have a monotonic 60-second deadline. Disposal removes notifications and suppresses consumer delivery. Async search, join-details and modification handles remain platform-owned through completion; teardown releases still-pending caller-owned handles while the platform remains valid, then releases the platform and discards canceled callback registrations before SDK shutdown. Process exit or connectivity loss can still require EOS's service-side departure detection rather than a confirmed asynchronous close acknowledgment.

`OnlineLobbyTests` exercises fake-provider coordination plus the real lobby driver without loading native EOS or Godot: access mapping, Unicode names, search/order/rename, identity separation, capacity, credential failure/expiry, wrong-code roster exclusion, lifecycle cleanup, delayed operations, obsolete subscriptions, timeouts and close retry. `check-online-lobby.ps1 -GodotPath <exe>` exercises production browser controls using a fake provider. `-Visual` saves browser/prompt/renamed-host images under `.godot/online-lobby-checks`. Existing `check-lobby.ps1` exercises eight actual UDP sessions and Ready/Start/Return regression behavior. These checks do not establish authenticated EOS service behavior or separate-PC interoperability. The real-device procedure remains in [EOS development setup](eos-development.md).

## EOS P2P Gameplay Transport

### Ownership and Normal Flow

The normal multiplayer browser composes `EOS identity -> EOS lobby -> EOS P2P -> LobbyNetworkDriver -> Ready/Start -> NetworkVehicleArena`. No IP address, port, router configuration or public-IP exchange is requested. EOS owns connectivity and its supported traversal/relay path. Direct-IP/GNS remains the explicit developer fallback. The adapters are separate; neither wraps the other. Gameplay authority and the vehicle, lobby and item protocols remain above `ITransportGateway`.

`EosSdkPlatform` owns the authenticated platform and releases its packet adapter before releasing that platform. Only the identity owner ticks EOS. `EosP2pSdk` owns notification registration and native peer routing; `EosP2pTransport` owns connection state, framing, bounded receive work and managed lifecycle delivery. `NetworkTransportNode.Factory` provides explicit adapter composition for node-owned transports. The online session owns its gameplay gateway, with platform disposal providing terminal cleanup if authentication is lost.

### Endpoints, Membership and Admission

Core's `TransportEndpoint` represents either a development address or an opaque peer/session pair. Its values are bounded and immutable, with value equality; it introduces no endpoint serialization protocol. Client constructs the EOS target from the current lobby owner PUID and a 32-character alphanumeric socket name derived from SHA-256 of the lobby ID and Trackstorm session lifetime. The discovery compatibility bucket is `trackstorm-lobby-3`. Different lobbies and session replacements use different sockets.

Incoming requests reserve one of seven remote slots only for current compatible lobby members. Clients connect only to the discovered host. Connection establishment includes a fresh host challenge, client response and acknowledgement with independent random connection nonces. Data must carry both nonces, preventing old packets from an earlier connection from becoming new gameplay input. The host passes the native-authenticated sender identity and transient access code to `OnlineSessionBinding`; the existing membership and Locked verifier gate precedes authoritative roster admission. A wire identity claim cannot substitute for the authenticated sender. Credentials are never logged; temporary send-buffer bytes are cleared after the handshake send, and the retained join credential is discarded on transfer, failure or leave.

Online membership never sets Ready or session phase. Trackstorm still assigns PlayerIds, validates Start/Return and owns all vehicle state. Membership loss closes the corresponding transport peer; the existing driver removes its gameplay player. Native callbacks only enqueue bounded work and a subscription generation rejects callbacks from an earlier session. Disconnect clears identity bindings so subsequent joins receive fresh gameplay IDs. The initial handshake deadline is 12 seconds; EOS closure/interruption notifications handle established connection failures. Reconnect/resume and host migration are not implemented.

### Reliability, Packet Bounds and Allocation

Reliable/control traffic uses EOS `ReliableOrdered` on channel 0; high-rate traffic uses `UnreliableUnordered` on channel 1. Both share available bandwidth, but an incomplete unreliable message does not block reliable control. All sends explicitly disable automatic connection acceptance.

The pinned SDK caps native packets at 1,170 bytes. A 29-byte frame carries message type, connection nonces, sequence, total length and fragment index. Payloads up to 64 KiB are fragmented into at most 58 packets. Reliable delivery uses one fixed 64 KiB assembly buffer; replacement of an incomplete reliable message is a protocol failure. Malformed sizes and fragment indices are rejected before copying. No arbitrary-sized reassembly allocation occurs.

Unreliable delivery uses a fixed sixteen-message receive window: the newest observed sequence and its fifteen predecessors, compared with wrap-safe 32-bit serial arithmetic. Independent messages and their fragments can arrive interleaved or out of order and are delivered once in completion order. This permits a prop publication to arrive before a preceding vehicle publication without suppressing the vehicle or its input acknowledgements. The transport does not inspect application protocols; each consumer retains its own freshness rules. Packets sixteen or more sequences behind the newest, including an ambiguous half-range serial, are discarded. A newer serial evicts any incomplete assemblies outside that window. Sixteen slots allow eight pairs of vehicle/prop publications in flight (about 400 ms at 20 publications/s), while bounding memory.

An incomplete unreliable assembly expires on Poll at one second after its first accepted fragment. Further fragments and duplicates do not extend its deadline. Expired and completed sequences remain tombstones until window eviction, preventing late fragments from restarting or duplicating them. Idle polls also expire incomplete work; missing fragments never block other messages or reliable control. Each slot reuses a fixed 64 KiB buffer: sixteen unreliable plus one reliable buffer total 1,114,112 bytes per peer (7,798,784 bytes for seven peers), excluding small metadata, native queues and completed-payload ownership. Expiry scans sixteen slots per peer per poll; advancing the newest sequence scans at most sixteen slots per received packet. Disconnect/Stop/Dispose discard the peer-owned window. The EOS framing, nonces and delivery mapping are independent of gameplay payload versions. The current discovery bucket excludes older vehicle lifecycle protocols; the receiver window itself does not change wire framing.

A poll consumes at most 256 native packets and 256 queued callbacks. At most 256 completed messages wait for consumers. Overflow closes affected connections with a stable reason; native incoming/outgoing queues each have a requested 1 MiB limit. Packet and assembly buffers are reused. One stable byte array is allocated for each completed message because the existing transport contract permits consumers to retain payload memory; official SDK marshalling can also allocate. This is bounded copying, not a zero-allocation claim. No packet logging or blocking network calls occur in the EOS polling/simulation path.

### Diagnostics and Limits

The gateway reports active adapter name, aggregate/peer state, portable closure reasons and optional capabilities. GNS supports sampled ping/quality and process-wide artificial network conditions. EOS reports none of those optional capabilities, returns null ping/quality and rejects simulation configuration explicitly. This SDK P2P interface has no sampled per-peer RTT/loss API; unavailable values never become zero loss or zero latency. No direct-versus-relay route is inferred. EOS failures provide a recoverable leave/rejoin or login/client-policy message.

The EOS adapter records cumulative sent/received datagrams, sent bytes, maximum datagram size and peak gateway polling cost for integration measurements. Poll cost excludes the identity owner's SDK Tick. Native queueing success is not delivery confirmation.

Deterministic tests exercise two- and eight-player vehicle traffic, lobby Ready/Start/Return, packet fragmentation/loss/reordering, capacity, timeouts, stale callbacks and cleanup through the production gateway with a fake native packet provider. These measurements do not establish Internet performance. `check-eos.ps1 -P2p -GodotPath <exe>` adds real authenticated create/listen/notification/stop/close cycles and the solo host Ready/Start/arena/Return flow. Separate-PC Internet gameplay, realistic loss/jitter, NAT topology, long-session behavior and clean-machine export operation require real deployment testing using [the EOS verification procedure](eos-development.md).

## In-Match Combat HUD

### Behavior and Layout

During a production practice or network arena, the HUD shows a bottom-left standing badge and current/max HP, a bottom-right speedometer and held-item slot, and a top-center timer. Standing intentionally displays `--`; timer intentionally displays `--:--`. Neither component creates scoring, ranking, tie rules or a gameplay clock. The HUD disappears when the local vehicle is unavailable or the arena closes.

Health uses the actual `VehicleSnapshot.Damage.CurrentHP / MaxHP`. Production Release 0.0.1 vehicles spawn with 1000 HP; future capacities require no HUD formula changes. The red segmented health bar decreases and increases immediately with the committed health fraction. Speed uses `VehicleSnapshot.Speed`, the existing observed horizontal speed in metres per second, and the current local unit preference. Only the number converts to km/h or mph. The red arc fills at 200 km/h (55.555… m/s) in either setting, and clamps at full above that speed. This is a visual scale, not a new physical speed cap.

Empty, Wrench and Missile have distinct slot presentations. Item identity comes from the existing confirmed slot, matched to the local vehicle and life; dead/respawning vehicles and unavailable or mismatched slots show Empty. There is no predicted pickup or item consumption. Existing remappable item inputs and the Use held item button keep their actions. Existing arena diagnostics and grant/reset controls are grouped under Arena tools to keep the combat composition clear; session buttons are above the lower HUD. The settings diagnostics omit their duplicate speed line while combat speed is visible; independent FPS/ping preferences still apply.

### Presentation Ownership and Assets

`Client.Hud.CombatHudView` is a detached pure Client projection, unit-tested without Godot. `CombatHud` owns the native component Controls, labels and materials. The bootstrap supplies functions reading the current local practice snapshot or network driver's local snapshot, confirmed item slot and preference service. Render refreshes never call gameplay mutation APIs or persistence. Session replacement is naturally reflected by these providers; the HUD holds no vehicle authority or inventory lifecycle.

The four project-supplied component PNGs provide the actual distressed metal frames. `assets/hud/sources.json` records their Jira sources and acquired hashes. The assembled scene reference guides placement and is never used as a fullscreen overlay. A small canvas shader masks the baked example numbers, Q prompt and machine gun using dark steel sampled from the supplied health texture, and drives the authored red gauge cells from normalized fractions. Native labels supply current values. Original project SVG silhouettes represent Wrench and Missile; there is no new external font or icon dependency. Text is cleaner than the baked distressed reference typography, and the slot intentionally replaces the reference's unsupported .50 CAL item.

The components use a 1280×720 design coordinate system, uniform scaling bounded by both viewport dimensions, and independent bottom-left, bottom-right and top-center placement. This preserves aspect ratios and margins on the settings-supported sizes down to 640×360, plus taller and ultrawide viewports. The composition uses a slightly taller speedometer from the supplied standalone component; it never stretches the full screen artwork.

### Capacity Integration and Verification

`HostVehicleSession` accepts explicit health configuration and applies it to host and joining players. The existing network snapshot already carries MaxHP; the decoder validates its bounded value rather than requiring the generic 100 HP default. Prediction reconstructs its health capacity from that authoritative spawn boundary. Capacity remains fixed for that registered vehicle; this change does not add runtime vehicle-type switching. The wire layout and physics/input units are unchanged. Mixed old/new builds should not share a match: older decoders reject the 1000 HP production state.

Core tests exercise configured capacities through join, wire round-trip and prediction. Pure Client tests cover unit conversion, 200 km/h normalization, current/max formatting, future capacities, all item mappings, identity/life gating, placeholders and non-mutating state projection. `check-hud.ps1 -GodotPath <Godot .NET executable>` renders native HUD state transitions, checks labels/materials and rendered fill pixels, verifies unit changes and out-of-match hiding, and saves images for nine viewport sizes under `.godot/hud-checks`. The existing eight-peer item and pickup harnesses also assert that the HUD follows confirmed replicated HP/ownership through use and acquisition. These are local-machine integration checks, not a separate-PC or Internet multiplayer soak.

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
