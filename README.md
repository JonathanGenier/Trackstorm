# Trackstorm

Trackstorm is a Godot 4.7.2 C# project targeting .NET 10. This repository currently contains the project foundation only; gameplay is intentionally deferred to later work.

## Solution layout

- `Trackstorm.Client.csproj` is the root Godot project and compiles presentation and engine-integration code from `code/Client`.
- `code/Core/Trackstorm.Core.csproj` is a plain .NET class library for authoritative, engine-independent logic.
- `code/Tests/Trackstorm.Core.Tests.csproj` is the NUnit project for Core behavior and architectural invariants.
- `Trackstorm.sln` groups the Client, Core, and Core test projects.

The only production project reference is `Trackstorm.Client -> Trackstorm.Core`. Core has no reference to Client or Godot, and no Shared layer is used. Types needed by both production layers belong in Core.

## Module boundaries

The foundation reserves responsibility without creating empty frameworks:

| Area | Owner and boundary |
| --- | --- |
| Vehicle simulation | Core owns future authoritative vehicle rules and state; Client owns Godot physics/runtime adaptation and presentation. |
| Input | Core owns logical, serialized per-tick input; Client owns device polling, bindings, and Godot InputMap integration. |
| Settings | Core owns validated gameplay-affecting configuration; Client owns settings UI and platform persistence adapters. |
| Networking transport | Core exposes only the plain-C# transport gateway and opaque messages; native APIs and implementations stay outside Core. |
| Replicated gameplay state | Core owns game-specific state messages and serialization separately from transport delivery. |
| Items, spawning, and match state | Core will own their authoritative state, validation, and deterministic rules when introduced. |
| UI and audio | Client owns presentation and feedback derived from Core state/results. |
| Dev Mode | Client owns developer controls and overlays; any authoritative data or mutations still pass through Core contracts. |

## Ownership conventions

Core owns authoritative state, rules, validation, simulation, and deterministic external seams. Core code should use plain C# wherever practical and must not depend on rendering, UI, camera, audio, local input, Godot nodes, or scene-tree lifecycle.

Client owns Godot integration and local presentation concerns such as nodes, scenes, rendering, input capture, UI, camera, audio, animation, effects, and interpolation. Client may observe Core state and invoke intentional Core APIs; it must not duplicate or arbitrarily mutate authoritative rules.

## Code standards

- Nullable reference types and implicit usings are enabled in every project.
- StyleCop analyzers are supplied centrally by `Directory.Build.props`; CI treats warnings as errors.
- `.editorconfig` owns formatting and analyzer severity preferences.
- Namespaces start with `Trackstorm.Core`, `Trackstorm.Client`, or `Trackstorm.Core.Tests` and mirror folders below their project root.
- Files, types, properties, methods, constants, and enum members use `PascalCase`; interfaces use an `I` prefix; private fields use `_camelCase`; parameters and locals use `camelCase`.
- One primary type is kept per file, classes are sealed unless inheritance is intentional, and visibility is as restrictive as practical.
- Public Core APIs are intentional boundaries. Gameplay failures should use explicit results where appropriate; exceptions represent programmer errors or broken invariants.

More detailed review guidance is in `docs/standards.md`.

## Verification

From the repository root, run:

```powershell
dotnet restore Trackstorm.sln
dotnet build Trackstorm.sln -c Release -warnaserror
dotnet test code/Tests/Trackstorm.Core.Tests.csproj -c Release --no-build
```

`check.ps1` also verifies formatting and both Debug and Release configurations. The Godot project starts at `scenes/main.tscn`; its Client-owned bootstrap captures one logical input frame and invokes one engine-independent Core simulation step per fixed tick. See `docs/features.md` for the fixed-step, input, transport, and replication contracts.

Run the GdUnit4 Client test and focused native-input integration checks with the installed Godot 4.7.2 .NET executable:

```powershell
./check-gdunit.ps1 -GodotPath "C:/path/to/Godot_console.exe"
./check-input.ps1 -GodotPath "C:/path/to/Godot_console.exe"
```

These checks import the enabled GdUnit4 plugin, run the Godot-side Client suite, inject keyboard/gamepad events into a dedicated verification scene, and smoke-test the main scene. Core tests remain independent of Godot.
