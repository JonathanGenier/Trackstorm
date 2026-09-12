# Trackstorm Agent Instructions

Follow `docs/standards.md` and keep changes scoped to the requested task.

The solution has two production areas: `Trackstorm.Core` owns authoritative, engine-independent logic, and `Trackstorm.Client` owns Godot presentation and integration. The dependency direction is strictly `Client -> Core`. Core must never reference Client or Godot, and no Shared layer should be introduced.

Core tests live in `Trackstorm.Core.Tests` and run without Godot. Add deterministic tests when authoritative behavior changes. Before finishing, format, build, run relevant tests, inspect the diff, and report assumptions or unresolved risks.
