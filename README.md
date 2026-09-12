# Trackstorm

Trackstorm is a Godot 4.7.1 C# project targeting .NET 10. This repository currently contains the project foundation only; gameplay is intentionally deferred to later work.

## Solution layout

- `Trackstorm.Client.csproj` is the root Godot project and compiles presentation and engine-integration code from `code/Client`.
- `code/Core/Trackstorm.Core.csproj` is a plain .NET class library for authoritative, engine-independent logic.
- `code/Tests/Trackstorm.Core.Tests.csproj` is the NUnit project for Core behavior and architectural invariants.
- `Trackstorm.sln` groups the Client, Core, and Core test projects.

The only production project reference is `Trackstorm.Client -> Trackstorm.Core`. Core has no reference to Client or Godot, and no Shared layer is used. Types needed by both production layers belong in Core.

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

`check.ps1` also verifies formatting and both Debug and Release configurations. The Godot project starts at `scenes/main.tscn`, a deliberately empty scene with no autoloads or gameplay dependencies.
