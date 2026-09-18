# Fixed-Step Simulation Foundation

## Purpose

The simulation foundation establishes the authoritative update boundary that future gameplay features can extend without coupling game rules to Godot. A caller supplies engine-independent logical input and explicitly requests one simulation step; Core returns the resulting authoritative state.

## How It Works

`Trackstorm.Core` exposes a simulation with immutable observable state. Each call to its step entry point consumes one Core-owned `InputFrame` and advances the unsigned integer simulation tick exactly once. The initial state starts at tick zero with neutral input. A frame is accepted only when its tick is exactly the next simulation tick, so duplicate, skipped, or out-of-order input cannot mutate state.

The Godot main scene uses a Client-owned bootstrap node and a child `PlayerInput` node. `PlayerInput` converts current device state to one logical frame during each Godot physics callback and publishes it. The bootstrap routes frames to the development session, which pumps lobby traffic without advancing gameplay until an authoritative arena transition. In the arena it advances the existing network vehicle driver. With `--local-practice`, it instead calls `VehicleArena.Advance`: capture all eight native vehicle observations, invoke one Core simulation step, apply the complete accepted command batch, then publish the resulting state. The bootstrap configures Godot's physics tick rate from the validated Core configuration. Godot timing and raw input never cross the Core boundary.

## Design Reasoning

An integer tick makes update ordering explicit and gives future authoritative rules a deterministic time coordinate that does not depend on wall-clock timestamps or floating-point frame deltas. Core advances only when a caller requests a step, which keeps tests and future server or replay drivers in control of scheduling.

Using the versioned integer `InputFrame` as the simulation input avoids a competing demonstration contract and lets the same deterministic data support live input, recorded replay and network delivery. Rejecting non-sequential ticks turns update ordering into a checked invariant instead of an informal caller convention.

## Architecture

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

## Important Invariants

- One Core step advances the tick by exactly one.
- The input frame tick must equal the next authoritative simulation tick.
- Only Core mutates authoritative simulation state.
- Core receives logical input, never Godot or device input objects.
- Simulation scheduling is caller-controlled; Core does not read clocks or frame delta.
- Equivalent initial state, logical inputs and plain external observations produce equivalent Core state.
- A vehicle batch contains exactly one request per registered vehicle at the next global tick; rejected batches cannot partially commit.
- The configured tick rate must be positive.

## Configuration

The Core-owned configuration contains only the fixed rate in ticks per second. Its current default is 60. Client uses that rate to schedule Core steps but does not pass elapsed seconds into the simulation.

## Interactions

The main Godot scene composes the input and simulation foundations at startup. The current `FrameCaptured` callback establishes capture-before-simulation ordering. Future authoritative gameplay systems can be added inside the Core step in an explicit order, while presentation can observe Core state after stepping.

## Intentional Limitations / Tradeoffs

The simulation owns tick/input, vehicle aggregates, lifecycle and optional match scoring. The host composes [held-item authority](items.md) and [pickup distribution](item-spawns.md) around its atomic world step. [Vehicle networking](vehicle-networking.md) connects this authority to host snapshots and client prediction/reconciliation; [match scoring](matches.md) has its own reliable publication. Client supplies native collision observations. Godot's fixed callback is the scheduler; Core has no wall-clock catch-up policy or full-world rollback history.

## Verification

Core NUnit tests exercise simulation configuration, sequential ticks, atomic vehicle batches and restore invariants. `check-gdunit.ps1 -GodotPath <Godot .NET executable>` imports the enabled GdUnit4 plugin, runs the Godot-side Client suite and smoke-tests the main scene. The [input harness](input.md) separately checks native keyboard/gamepad capture and fixed-callback publication.

Authority restoration, epoch fencing, checkpoint cadence and migration limits are described in [host migration](host-migration.md).

[Feature index](README.md)

The [Event Log](event-log.md) consumes committed hit results and lifecycle/match transitions at the same world boundary. Restore produces no historical outcomes; staged item-use events precede their applied damage/healing.
