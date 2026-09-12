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
- After every required Task is merged, the Story must be verified and critiqued as a whole, then explicitly accepted by the human reviewer before its PR is created.
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

The current Jira Task/Subtask or Story, as applicable, defines the implementation scope.

Implement only what is required to satisfy that issue's deliverables, acceptance criteria, tests, and integration checks.

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

## Feature Documentation

`docs/features.md` is mandatory living documentation for implemented Trackstorm features. It preserves durable system knowledge so future developers and agents can understand what a feature does, how it works at a useful system level, and why important decisions were made.

Whenever a Jira Task/Subtask, Story, or other authorized change affects a feature, review the relevant `docs/features.md` section and update it in the same change. Apply this lifecycle:

```text
Feature added
    ↓
Add documentation

Feature modified
    ↓
Update documentation

Feature removed
    ↓
Remove or revise documentation
```

Feature documentation should capture the durable context relevant to the feature:

- Purpose.
- Current behavior.
- System-level implementation.
- Design reasoning.
- Core/Client responsibility ownership.
- Important invariants.
- Important assumptions.
- Relevant configuration or tunable behavior.
- Interactions with other features or systems.
- Intentional limitations or tradeoffs.

Document the resulting feature, not the implementation process. Preserve system intent and context rather than cataloging every class, method, or low-level detail; source code remains the source of truth for low-level implementation details.

Do not use `docs/features.md` for:

- Jira acceptance criteria.
- Task status.
- PR history.
- Commit history.
- Critique scores.
- Temporary TODOs.
- Development diary entries.

Do not leave stale feature documentation. Feature work is not complete if its corresponding documentation is missing, stale, or contradicts the implementation.

Before feature-related work is considered complete, compare the resulting implementation with the relevant `docs/features.md` section and confirm that behavior, design reasoning, ownership, invariants, configuration, interactions, assumptions, and intentional limitations remain synchronized.

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

Every changed file should be explainable by the current Jira issue.

## Task Completion and Pull Request

One Jira Task/Subtask equals one Task branch and exactly one Pull Request. The Task branch must come from its parent Story branch, must never update directly from `main`, and its PR must target the parent Story branch.

The mandatory Task lifecycle is:

```text
Story branch updated
        ↓
Task branch created/updated
        ↓
Implement
        ↓
Build/Test/Inspect
        ↓
Mandatory Task Critique
        ↓
Score + Recommendation(s) if FAIL
        ↓
STOP
        ↓
Human decision
        ↓
Optional human-authorized improvement rounds
        ↓
Human accepts Task
        ↓
Final verification
        ↓
Push Task branch
        ↓
Create Task PR → Story branch
```

Follow these rules:

1. Before implementation and again before the mandatory Task critique, confirm the Task branch is synchronized with its parent Story branch. Changes flow only from `main` into Story and then from Story into Task.
2. Implement only the current Task/Subtask and include its required tests and documentation.
3. Run `./check.ps1`, all Jira-required integration checks, and inspect the complete diff.
4. Perform the mandatory Task critique in `docs/critique.md`. If the score is below 6.0, present at least one meaningful recommendation; if the score is 6.0 or higher, report PASS and no recommendation is required. Then stop for the human decision.
5. Do not make critique-driven changes or begin another critique round without explicit human authorization. Each authorized round repeats implementation, verification, critique, scoring, any recommendations required for a failing score, and the mandatory stop, subject to the three-round limit.
6. Do not create the final Task PR until the critique process is complete and the human reviewer explicitly accepts the Task for PR creation.
7. After acceptance, perform final verification without making additional implementation changes, commit all intended changes, and push the Task branch. If synchronization or implementation changes become necessary, repeat the applicable verification and critique process and obtain renewed human acceptance before PR creation.
8. Create exactly one GitHub Pull Request from the Task branch to its parent Story branch. Never target `main` from a Task branch.
9. Include the Jira key and Task summary in the PR title. Include the Jira key, implementation summary, tests and checks, assumptions, limitations, and unresolved risks in the PR description.
10. After the PR is created successfully, report its number and URL.

Example:

```text
main
└── story/TS-7-arcade-vehicle
    └── task/TS-8-fixed-step-movement
          │
          └── PR → story/TS-7-arcade-vehicle
```

## Story Completion and Pull Request

Story completion begins only after all required Task PRs have been merged into the Story branch. The Story critique evaluates the integrated Story, and the final Story PR targets `main`.

The mandatory Story lifecycle is:

```text
All required Task PRs merged
        ↓
Story branch updated from main
        ↓
Full Story verification
        ↓
Mandatory Story Critique
        ↓
Score + Recommendation(s) if FAIL
        ↓
STOP
        ↓
Human decision
        ↓
Optional corrective Task PRs
        ↓
Optional human-authorized Story critique rounds
        ↓
Human accepts Story
        ↓
Final Story verification
        ↓
Create Story PR → main
```

Follow these rules:

1. Confirm every required Task PR is merged, update the Story branch from `main`, and perform full Story verification against the integrated acceptance criteria.
2. Perform the mandatory Story critique in `docs/critique.md`. If the score is below 6.0, present at least one meaningful recommendation; if the score is 6.0 or higher, report PASS and no recommendation is required. Then stop for the human decision.
3. Do not apply Story critique fixes directly to the Story branch. Every approved implementation fix requires an appropriate Jira Task/Subtask, a Task branch created from the current Story branch, a Task critique, and exactly one Task PR back into the Story branch.
4. After corrective Task PRs are merged, perform another Story critique round only when the human explicitly authorizes it, subject to the three-round limit.
5. Do not create the final Story PR until the critique process is complete and the human reviewer explicitly accepts the Story for PR creation.
6. After acceptance, perform final Story verification without making additional implementation changes. If corrective implementation changes become necessary, route them through the Task workflow and obtain renewed Story acceptance before PR creation.
7. Create the GitHub Pull Request from the Story branch to `main`. Include the Jira key, integrated Story summary, verification performed, assumptions, limitations, and unresolved risks.
8. After the PR is created successfully, report its number and URL.
