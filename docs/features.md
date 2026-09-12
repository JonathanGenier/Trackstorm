# Trackstorm Features

This document describes the current behavior, architecture, and design reasoning of implemented Trackstorm features.

It exists so future developers and agents can understand not only how a feature works, but why important decisions were made.

This document describes the current system. It is not a backlog, task log, PR log, critique log, commit history, or development diary. Source code remains the source of truth for low-level implementation details; this document preserves system intent and durable context.

## Implemented Features

## Fixed-Step Simulation Foundation

### Purpose

The simulation foundation establishes the authoritative update boundary that future gameplay features can extend without coupling game rules to Godot. A caller supplies engine-independent logical input and explicitly requests one simulation step; Core returns the resulting authoritative state.

### How It Works

`Trackstorm.Core` exposes a simulation with immutable observable state. Each call to its step entry point consumes one logical input value and advances the integer simulation tick exactly once. The initial state starts at tick zero.

The Godot main scene uses a Client-owned bootstrap node. On each rendered update, the bootstrap captures the built-in `ui_accept` action as a minimal demonstration input, converts it to the Core logical input contract, accumulates Godot frame delta locally, and invokes as many fixed Core steps as the configured rate requires. Godot timing and raw input never cross the Core boundary.

### Design Reasoning

An integer tick makes update ordering explicit and gives future authoritative rules a deterministic time coordinate that does not depend on wall-clock timestamps or floating-point frame deltas. Core advances only when a caller requests a step, which keeps tests and future server or replay drivers in control of scheduling.

The logical input currently contains only a generic active flag. Its deliberately small shape proves the conversion boundary without predicting future vehicle, combat, or menu controls. Future features should add only the logical inputs they need.

### Architecture

`Trackstorm.Core` owns:

- Fixed-step configuration and validation.
- The authoritative tick and most recently consumed logical input.
- The deterministic state transition performed by one step.

`Trackstorm.Client` owns:

- Godot lifecycle and frame-delta accumulation.
- Raw Godot `Input` polling.
- Conversion from a Godot action to Core logical input.
- Composition, invocation, and observation of the Core simulation.

The allowed dependency and data flow is `Godot -> Trackstorm.Client -> Trackstorm.Core`. Core never references Client, Godot, scene lifecycle, raw device input, rendering, or wall-clock time.

### Important Invariants

- One Core step advances the tick by exactly one.
- Only Core mutates authoritative simulation state.
- Core receives logical input, never Godot or device input objects.
- Simulation scheduling is caller-controlled; Core does not read clocks or frame delta.
- Equivalent initial state and logical input sequences produce equivalent Core state.
- The configured tick rate must be positive.

### Configuration

The Core-owned configuration contains only the fixed rate in ticks per second. Its current default is 60. Client uses that rate to schedule Core steps but does not pass elapsed seconds into the simulation.

### Interactions

The main Godot scene composes the foundation at startup. Future authoritative gameplay systems can be added to the Core step in explicit order, while presentation can observe Core state after stepping.

### Intentional Limitations / Tradeoffs

The foundation does not implement gameplay, vehicle movement, physics, damage, weapons, items, inventory, spawning, matches, scoring, networking, replication, prediction, reconciliation, HUD, camera, audio, or VFX. The current logical input is demonstrative rather than a permanent gameplay input model. No catch-up limit, interpolation, pause policy, state serialization, transport contract, or rollback history exists yet; the feature that needs each behavior should introduce it.

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
