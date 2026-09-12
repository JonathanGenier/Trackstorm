## Jira, Branching, and Pull Request Workflow

Trackstorm uses a strict Jira-to-Git workflow. Follow it for every change unless the assigned Jira issue explicitly states otherwise.

The canonical rule is:

```text
1 Story = complete Story scope + all required child Tasks/Subtasks = 1 Story branch = 1 Pull Request
```

A Story is the primary implementation assignment and Git delivery unit. Tasks/Subtasks decompose the Story into implementation instructions, acceptance criteria, dependencies, sequencing, asset and testing requirements, and progress checkpoints. They are not independent Git delivery units.

The complete model is:

```text
Jira Story TS-X
│
├── Child Task/Subtask A
├── Child Task/Subtask B
├── Child Task/Subtask C
└── Child Task/Subtask ...
        │
        ▼
ALL implemented on:
story/TS-X-short-name
        │
        ▼
Integrated Story verification
        │
        ▼
Story critique
        │
        ▼
Human acceptance
        │
        ▼
ONE PR → main
```

In short:

- Story = implementation assignment.
- Tasks/Subtasks = implementation instructions and checkpoints inside the Story.
- Story branch = workspace for the entire Story.
- Story PR = delivery and GitHub review unit.

## Story Assignment Behavior

When a Story is assigned for implementation, all required child Tasks/Subtasks are part of that assignment and must be implemented before the Story is considered complete. Do not require the human to assign each child separately.

For a full Story assignment:

1. Read the complete Story.
2. Retrieve and read all Tasks/Subtasks belonging to it.
3. Combine the Story and child acceptance criteria, identify dependencies, and choose a sensible implementation order.
4. Create or checkout the Story branch from the appropriate `main` state.
5. Implement every required child issue directly on the Story branch.
6. Verify each child's requirements while progressing and record its completion.
7. Continue automatically to the next required child issue.
8. After all child work is complete, verify the Story as one integrated feature.
9. Update `docs/features.md` wherever implemented feature behavior changed.
10. Run all required build, test, integration, runtime, asset/license, and repository checks.
11. Perform the mandatory integrated Story critique described in `docs/critique.md`, present the score and findings, and stop for the human decision.
12. Create the single Story PR targeting `main` only after explicit human acceptance.

Completing a child Task/Subtask during full Story implementation does not require a human stop or a Git PR. Verify it, record its completion, and continue to the next required child issue. The original Story assignment already authorizes all required child work.

## Branch and Pull Request Rules

The Git hierarchy is:

```text
main
└── story/TS-X-short-name
```

Child issues such as TS-Y and TS-Z are implemented directly on `story/TS-X-short-name`. The child hierarchy exists in Jira, not as additional Git branches.

- One Jira Story corresponds to exactly one Story branch and one final Pull Request.
- Create the Story branch from `main`.
- Before starting or resuming Story work, update the Story branch from the latest `main`.
- Do not develop Story implementation directly on `main`.
- Implement and commit all Story and child work on the Story branch.
- The Story PR targets `main` and is the single GitHub review and merge unit.
- Never create a `task/TS-X-...` or `subtask/TS-X-...` branch.
- Never create a Task/Subtask Pull Request or an intermediate PR targeting the Story branch.
- Never give a child issue an independent GitHub review or merge workflow.

## Working Directly on One Task/Subtask

If the human explicitly assigns only a specific Task/Subtask rather than the complete Story:

1. Identify its parent Story.
2. Read enough of the parent Story to understand branch, dependency, and integration context.
3. Checkout the parent Story branch.
4. Implement only the assigned child issue on that Story branch.
5. Verify and record that child's requirements and completion.
6. Do not implement sibling issues automatically.
7. Do not create a child branch or Pull Request, and do not merge anything independently.

The individual assignment narrows implementation scope but does not change the Story-only Git model.

## Scope Discipline

The assigned Story defines the authorized overall scope. Its Tasks/Subtasks define how that scope is decomposed. If the human explicitly assigns only one child issue, that child defines the authorized implementation scope.

While implementing a child issue:

- Satisfy that child's deliverables and acceptance criteria.
- Preserve clear traceability between the Jira requirements and the implementation.
- Avoid unrelated cleanup, refactors, features, assets, or fixes.
- Avoid implementing sibling requirements early unless dependency order technically requires it.
- Include the child's required tests, integration checks, documentation, and asset/license work.

During a full Story assignment, this child-level discipline does not reduce the overall authorization: continue through every required child issue until the complete Story is implemented.

Do not:

- Implement future Story requirements speculatively.
- Rename or reorganize unrelated code.
- Introduce abstractions solely for hypothetical future use.
- Add dependencies, plugins, or assets not required by the assigned scope.
- Change authoritative architecture rules for convenience.

If work outside the assigned Story or explicitly assigned child appears necessary, report it as a dependency, risk, or recommended follow-up rather than silently expanding the Story branch and PR.

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

- Damage.
- Healing.
- Health.
- Inventory ownership.
- Item consumption.
- Kill attribution.
- Score changes.
- Death.
- Respawn.
- Spawn availability.
- Match state.
- Match winner.
- Authoritative configuration.

Core-owned public contracts must remain engine-independent. Do not expose Godot runtime types through Core APIs. Convert Godot-specific types at the Client/Core boundary.

## Testing Requirements

Tests required by a Jira Task/Subtask are part of that child issue and must be implemented on the Story branch and delivered in the Story PR.

Authoritative Core behavior should have deterministic NUnit coverage when practical.

Tests should cover relevant:

- Normal behavior.
- Failure behavior.
- Boundary values.
- Validation.
- State transitions.
- Invariants.
- Serialization or network-facing data where introduced.
- Deterministic behavior where required.

Do not defer required tests to a later child issue unless the Jira requirements explicitly assign them there.

Tests must not depend on uncontrolled:

- Wall-clock time.
- Randomness.
- Network access.
- Shared mutable state.
- Godot runtime state when testing pure Core behavior.

## Dependencies and Third-Party Assets

Do not introduce a new package, plugin, library, asset pack, model, texture, material, audio source, font, or other third-party dependency unless it is required by the assigned Story or explicitly assigned child issue.

When the assigned scope requires a third-party dependency or asset:

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
- Task/Subtask status or completion logs.
- Branch names.
- PR history.
- Commit history.
- Critique scores or history.
- Temporary TODOs.
- Development diary entries.

Do not leave stale feature documentation. Feature work is not complete if its corresponding documentation is missing, stale, or contradicts the implementation.

Before feature-related work is considered complete, compare the resulting implementation with the relevant `docs/features.md` section and confirm that behavior, design reasoning, ownership, invariants, configuration, interactions, assumptions, and intentional limitations remain synchronized.

## Child Task/Subtask Verification

Before recording a child issue complete on the Story branch:

1. Re-read the child issue and verify every required deliverable and acceptance criterion.
2. Confirm the implementation is on the parent Story branch and traceable to the assigned scope.
3. Run the unit, integration, runtime, asset/license, architecture, and documentation checks required by that child.
4. Inspect the relevant diff and verify no unrelated or generated artifacts were added.
5. Record the child's completion and verification evidence.

During a full Story assignment, successful child verification leads directly to the next required child issue. It does not trigger a mandatory human gate, a final Git review, or a Pull Request.

If only one child issue was explicitly assigned, perform the applicable Task critique described in `docs/critique.md` and stop for the human decision. Keep the work on the Story branch; no child Pull Request follows.

## Story Verification Before Completion

After all required child issues are implemented and verified:

1. Re-read the complete Story and every required child issue.
2. Verify all Story and child deliverables and acceptance criteria.
3. Update the Story branch from `main` and resolve integration conflicts correctly.
4. Run the repository verification script from the repository root:

```powershell
./check.ps1
```

5. Run every additional integration, gameplay, network, runtime, visual, UI, audio, physics, asset/license, or other check required by the Story and its children.
6. Compare feature-related implementation with `docs/features.md` and synchronize it where needed.
7. Inspect the complete Story diff against `main`.
8. Verify no unrelated files or generated artifacts were added.
9. Verify the Core/Client dependency direction remains valid.
10. Report assumptions, known limitations, unresolved risks, and unperformed verification.

Do not consider the Story complete if required checks fail.

## Diff Hygiene

Before critique and again before submitting the Story PR, inspect all changed files.

Do not commit accidental or unrelated files such as:

- Godot-generated cache/import data that belongs in `.gitignore`.
- IDE-specific files.
- Temporary files.
- Build output.
- Debug dumps.
- Local configuration.
- Unrelated formatting changes.
- Unrequested assets.
- Experimental code.
- Commented-out abandoned implementations.

Every changed file should be explainable by the assigned Story or explicitly assigned child issue.

## Child Task/Subtask Completion

A child Task/Subtask is complete when its requirements are implemented and verified on the Story branch. During a full Story assignment, use this lifecycle:

```text
Implement child Task/Subtask on Story branch
        ↓
Verify requirements
        ↓
Record completion
        ↓
Continue Story implementation
```

Do not stop for human authorization merely to move between required children already included in the assigned Story. Do not create or request child-level branches, PRs, reviews, or merges.

## Story Completion and Pull Request

Story completion begins only after all required Tasks/Subtasks have been implemented and verified on the Story branch. The mandatory lifecycle is:

```text
All required Tasks/Subtasks implemented and verified on the Story branch
        ↓
Story branch updated from main
        ↓
Full integrated Story verification
        ↓
Mandatory Story critique
        ↓
Score + Recommendation(s) if FAIL
        ↓
STOP
        ↓
Human decision
        ↓
Optional human-authorized corrective work on the Story branch
        ↓
Optional human-authorized Story critique rounds
        ↓
Human accepts Story
        ↓
Final Story verification
        ↓
Create one Story PR → main
```

Follow these rules:

1. Confirm every required child issue is implemented and verified on the Story branch, then perform full Story verification against the integrated Story and child acceptance criteria.
2. Perform the mandatory Story critique in `docs/critique.md`. If the score is below 6.0, present at least one meaningful recommendation; if the score is 6.0 or higher, report PASS and no recommendation is required. Stop for the human decision.
3. Do not make critique-driven changes or begin another critique round without explicit human authorization.
4. Apply authorized corrective work directly on the existing Story branch. Create or use a Jira Task/Subtask for traceability when useful, but never create a corrective child branch or child PR.
5. After authorized corrective work is implemented and verified, perform another Story critique round only when the human explicitly authorizes it, subject to the three-round limit.
6. Do not create the Story PR until the critique process is complete and the human reviewer explicitly accepts the Story for PR creation.
7. After acceptance, perform final Story verification without making additional implementation changes. If corrective implementation changes become necessary, make only authorized changes on the Story branch, repeat the applicable verification and Story critique process, and obtain renewed Story acceptance before PR creation.
8. Create exactly one GitHub Pull Request from the Story branch to `main`. Include the Jira key, integrated Story summary, verification performed, assumptions, limitations, and unresolved risks.
9. After the PR is created successfully, report its number and URL.
