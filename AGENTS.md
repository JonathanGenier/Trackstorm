# Trackstorm Agent Instructions

Follow:

- `docs/standards.md` for architecture, coding, testing, and review standards.
- `docs/workflow.md` for Jira, branch, pull request, synchronization, and completion workflow.
- `docs/critique.md` for the mandatory self-critique, scoring, recommendation, and human-gated improvement process.

Keep every change scoped to the current Jira Task.

The solution has two production areas:

- `Trackstorm.Core` owns authoritative, engine-independent logic.
- `Trackstorm.Client` owns Godot presentation and integration.

The dependency direction is strictly:

```text
Client -> Core
```

Core must never reference Client or Godot. Do not introduce a Shared layer.

Core tests live in `Trackstorm.Core.Tests` and must run without Godot. Add deterministic tests when authoritative behavior changes.

Before completing any Task or Story:

- Satisfy the Jira acceptance criteria.
- Follow the branch/PR rules in `docs/workflow.md`.
- Run `./check.ps1`.
- Run required Task-specific integration checks.
- Inspect the complete diff.
- Report assumptions, limitations, or unresolved risks.
- Execute the mandatory critique process in `docs/critique.md`.
- Do not continue into another critique/improvement round without explicit human authorization.
