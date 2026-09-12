## Jira, Branching, and Pull Request Workflow

Trackstorm uses a strict Jira-to-Git workflow. Follow it for every change unless the current Jira issue explicitly states otherwise.

### Branch hierarchy

```text
main
└── story/TS-X-short-name
    ├── task/TS-Y-short-name
    ├── task/TS-Z-short-name
    └── ...
```

### Story branches

- One Jira Story corresponds to exactly one Story branch.
- Create the Story branch from `main`.
- Before starting or resuming work on a Story, update the Story branch from the latest `main`.
- Do not develop Story implementation directly on `main`.
- All Tasks/Subtasks belonging to the Story must branch from the Story branch.
- When the Story branch changes, active child Task branches must be updated from the Story branch as appropriate.
- After every required Task is merged, the Story must be verified as a whole before its PR is merged into `main`.
- The Story PR targets `main`.

### Task branches

- One Jira Task/Subtask corresponds to exactly one Task branch and exactly one pull request.
- Create the Task branch from its parent Story branch.
- Never create a Task branch directly from `main`.
- Before starting or resuming Task work, synchronize with the current parent Story branch.
- If the Story branch changes while the Task is in progress, update the Task branch from the Story branch before final verification.
- Do not merge or rebase `main` directly into a Task branch. Changes flow through:

```text
main
  ↓
Story branch
  ↓
Task branch
```

- A Task PR always targets its parent Story branch.
- Do not combine multiple Jira Tasks into one Task branch or PR.
- Do not include unrelated cleanup, refactors, features, assets, or fixes in the Task PR.

### Scope discipline

The current Jira Task defines the implementation scope.

Implement only what is required to satisfy that Task's deliverables, acceptance criteria, tests, and integration checks.

Do not:

- Implement adjacent Tasks early.
- Implement future Story requirements speculatively.
- Perform unrelated refactors because they appear beneficial.
- Rename or reorganize unrelated code.
- Introduce abstractions solely for hypothetical future use.
- Add dependencies, plugins, or assets not required by the current Task.
- Change authoritative architecture rules for convenience.

If work outside the Task appears necessary, report it as a dependency, risk, or recommended follow-up rather than silently expanding the PR.

## Core and Client Data Flow

Trackstorm uses `Trackstorm.Core` as the authoritative game/domain layer and `Trackstorm.Client` as the Godot integration and presentation layer.

The intended flow is:

```text
Godot / device / runtime input
        ↓
Trackstorm.Client
        ↓
Core-owned logical command or input data
        ↓
Trackstorm.Core
        ↓
Authoritative validation and state mutation
        ↓
Core state/results
        ↓
Trackstorm.Client
        ↓
Rendering / UI / audio / effects
```

Client may request actions from Core and present Core state.

Client must not independently decide authoritative gameplay outcomes such as:

- Damage
- Healing
- Health
- Inventory ownership
- Item consumption
- Kill attribution
- Score changes
- Death
- Respawn
- Spawn availability
- Match state
- Match winner
- Authoritative configuration

Core-owned public contracts must remain engine-independent. Do not expose Godot runtime types through Core APIs. Convert Godot-specific types at the Client/Core boundary.

## Testing Requirements

Tests required by a Jira Task are part of that Task and must be included in the same PR.

Authoritative Core behavior should have deterministic NUnit coverage when practical.

Tests should cover relevant:

- Normal behavior
- Failure behavior
- Boundary values
- Validation
- State transitions
- Invariants
- Serialization or network-facing data where introduced
- Deterministic behavior where required

Do not defer required tests to a later cleanup Task unless the Jira issue explicitly instructs this.

Tests must not depend on uncontrolled:

- Wall-clock time
- Randomness
- Network access
- Shared mutable state
- Godot runtime state when testing pure Core behavior

## Dependencies and Third-Party Assets

Do not introduce a new package, plugin, library, asset pack, model, texture, material, audio source, font, or other third-party dependency unless it is required by the current Jira Task.

When a Task requires a third-party dependency or asset:

- Use the source specified in Jira when one is provided.
- Record the source URL.
- Record the version when applicable.
- Record the license.
- Preserve required attribution or source metadata.
- Do not silently substitute another dependency.
- Prefer existing project dependencies/assets when they already satisfy the requirement.

Core must not gain a Godot or presentation dependency through a convenience package.

## Verification Before Completion

Before declaring a Task complete:

1. Re-read the Jira Task and verify every required deliverable and acceptance criterion.
2. Confirm the branch contains only work belonging to the current Task.
3. Run the repository verification script from the repository root:

```powershell
./check.ps1
```

4. Run any additional Task-specific integration checks required by Jira.
5. Inspect the complete Git diff.
6. Verify no unrelated files or generated artifacts were added.
7. Verify the Core/Client dependency direction remains valid.
8. Report any assumptions, known limitations, unresolved risks, or follow-up work.

Do not consider a Task complete if required checks fail.

## Diff Hygiene

Before submitting a PR, inspect all changed files.

Do not commit accidental or unrelated files such as:

- Godot-generated cache/import data that belongs in `.gitignore`
- IDE-specific files
- Temporary files
- Build output
- Debug dumps
- Local configuration
- Unrelated formatting changes
- Unrequested assets
- Experimental code
- Commented-out abandoned implementations

Every changed file should be explainable by the current Jira Task.

## Pull Request Completion Rule

A Task is complete only when its implementation, required tests, documentation changes, dependency metadata, and Jira acceptance criteria are satisfied in its single Task PR.

The expected lifecycle is:

```text
main
  ↓
Story branch
  ↓
Task branch
  ↓
Task PR
  ↓
Story branch
  ↓
Story verification
  ↓
Story PR
  ↓
main
```

Preserve this hierarchy throughout development.
