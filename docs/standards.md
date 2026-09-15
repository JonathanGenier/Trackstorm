# Trackstorm Development Standards

Apply only the sections relevant to a change. Keep work scoped and do not introduce speculative architecture.

## Core

- Core is authoritative and engine-independent. It must not reference Client, Godot, scenes, UI, rendering, camera, audio, or local input.
- The production gameplay dependency direction is strictly `Client -> Core`; do not introduce a Shared layer. Shared gameplay contracts belong in Core; native adapters and the official EOS binding remain Client dependencies.
- Prefer plain C# types, deterministic behavior, explicit ownership, and controllable time, randomness, and external dependencies.
- Keep types internal unless they intentionally form the Core API. Seal classes unless inheritance is required.
- Keep validation and state mutation in Core. Client requests actions and presents results.
- Add deterministic NUnit tests for new or changed authoritative behavior.

## Client

- Keep Godot lifecycle, local input capture, rendering, UI, audio, camera, animation, effects, and interpolation in Client.
- Treat Client objects as reconstructable representations of authoritative Core state.
- Do not duplicate Core rules or mutate authoritative state outside intentional Core APIs.
- Keep lifecycle and cleanup aligned with actual ownership.

## Tests

- Core tests use NUnit without requiring Godot or a running scene tree.
- Test classes are internal and sealed; test methods are public.
- Test observable behavior, domain invariants, boundaries, failures, and deterministic results rather than private implementation details.
- Keep fixtures isolated and avoid uncontrolled time, randomness, network access, and shared mutable state.
- Pure Client tests may run without Godot/native runtime loading. Explicitly selected native/integration harnesses exercise real external dependencies and must identify that evidence separately from deterministic unit tests.

## Structure and naming

- Namespaces mirror project folders and use the project's root namespace.
- Use `PascalCase` for files, types, public members, constants, and enum members; `IPascalCase` for interfaces; `_camelCase` for private fields; and `camelCase` for parameters and locals.
- Use one primary type per file and the narrowest practical visibility.
- Organize members consistently: fields, constructors/initialization, events, properties, then methods when those categories are present.
- Public fields are not permitted. XML summaries identify intentional APIs and non-obvious types or members.
- Enable nullable reference types and implicit usings. `Directory.Build.props` supplies StyleCop; `.editorconfig` owns formatting and analyzer severity preferences.
- Gameplay failures should use explicit results where appropriate; exceptions represent programmer errors or broken invariants.

## Verification routing

[Workflow verification](workflow.md#verification) owns required completion commands, runtime checks and diff inspection. [Critique](critique.md) owns the subsequent quality review.
