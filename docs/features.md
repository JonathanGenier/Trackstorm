# Trackstorm Features

This document describes the current behavior, architecture, and design reasoning of implemented Trackstorm features.

It exists so future developers and agents can understand not only how a feature works, but why important decisions were made.

This document describes the current system. It is not a backlog, task log, PR log, critique log, commit history, or development diary. Source code remains the source of truth for low-level implementation details; this document preserves system intent and durable context.

## Implemented Features

No implemented feature entries are documented yet.

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
