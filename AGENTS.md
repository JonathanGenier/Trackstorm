# Trackstorm Agent Instructions

Follow:

- `docs/standards.md` for architecture, coding, testing, and review standards.
- `docs/workflow.md` for Jira, branch, pull request, synchronization, and completion workflow.
- `docs/critique.md` for the mandatory self-critique, scoring, recommendation, and human-gated improvement process.
- `docs/features.md` as the living reference for implemented Trackstorm features, including their behavior, architecture, design reasoning, invariants, interactions, and intentional limitations.

The Jira Story is the primary implementation and Git delivery unit. The canonical workflow is:

```text
1 Story = the complete Story scope and all required child Tasks/Subtasks = 1 Story branch = 1 Pull Request
```

When assigned a Story, retrieve and read the complete Story and every child Task/Subtask before implementation begins. The Story assignment authorizes all required child work. Implement and verify each child issue on the Story branch, record its completion, and continue to the next required child issue without a mandatory human stop or Git PR between children. The Story is complete only when its own acceptance criteria and every required child acceptance criterion are satisfied.

Tasks/Subtasks are implementation and specification checkpoints, not Git delivery units. They never receive their own branches or Pull Requests. If the human explicitly assigns only one Task/Subtask, identify its parent Story, work only on that child issue on the parent Story branch, and do not automatically implement its siblings.

Keep every change scoped to the assigned Story or, when explicitly assigned alone, the specified Task/Subtask.

The solution has two production areas:

- `Trackstorm.Core` owns authoritative, engine-independent logic.
- `Trackstorm.Client` owns Godot presentation and integration.

The dependency direction is strictly:

```text
Client -> Core
```

Core must never reference Client or Godot. Do not introduce a Shared layer.

Core tests live in `Trackstorm.Core.Tests` and must run without Godot. Add deterministic tests when authoritative behavior changes.

Whenever work adds, changes, or removes a feature, update `docs/features.md` in the same change: add new feature documentation, update existing documentation, or remove/revise obsolete documentation. Also update affected architecture, design reasoning, invariants, configuration, ownership, interactions, and intentional limitations. Do not leave stale feature documentation.

`docs/features.md` documents the current system and why it works that way. It is not a Jira backlog, task history, PR log, critique log, or commit history.

Before completing an assigned Story, or an explicitly assigned individual Task/Subtask:

- Satisfy the Jira acceptance criteria.
- Follow the branch/PR rules in `docs/workflow.md`.
- Run `./check.ps1`.
- Run all integration checks required by the assigned scope.
- For feature-related work, compare the resulting implementation with the relevant `docs/features.md` section and confirm they agree.
- Inspect the complete diff.
- Report assumptions, limitations, or unresolved risks.
- Execute the applicable mandatory critique process in `docs/critique.md`. During a full Story assignment, the human-gated critique occurs after all child work is integrated, not between normal child issues.
- Do not continue into another critique/improvement round without explicit human authorization.
- Do not create the Story Pull Request until the integrated Story critique is complete and the human reviewer has explicitly accepted the Story for PR creation. Only the Story receives a final Pull Request, targeting `main`.
