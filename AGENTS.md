# Trackstorm Agent Instructions

## Sources of Truth

- Jira defines what assigned implementation work must accomplish.
- `docs/standards.md` defines how Trackstorm code is engineered and tested.
- `docs/workflow.md` defines how Jira work moves through implementation, Git, verification, and delivery.
- `docs/critique.md` defines quality scoring and the human-gated improvement process.
- `docs/features.md` describes the implemented system and its design rationale. For feature work, locate and read the relevant feature sections; do not treat the entire growing document as required context for unrelated changes.

Read the complete assigned Jira scope before implementation. For a Story, read the Story and every required child Task/Subtask. For an explicitly assigned child, read it and enough of its parent Story to understand context. Never silently omit, simplify, replace, or reinterpret a Jira requirement.

Repository policy governs how work is engineered and delivered. If Jira appears to conflict with an architecture, workflow, security, licensing, or engineering invariant, report the conflict and preserve the stricter invariant until the human resolves it.

## Critical Invariants

```text
1 Story = complete Story scope + all required child Tasks/Subtasks = 1 Story branch = 1 final PR to main
```

Tasks/Subtasks are implementation checkpoints on the parent Story branch. They never receive branches or Pull Requests. During a full Story assignment, verify each child and continue without a mandatory human stop. If only one child is explicitly assigned, implement only that child and not its siblings.

The production dependency direction is strictly `Client -> Core`. Core is authoritative and engine-independent; it must never reference Client or Godot. Do not introduce a Shared layer. Add deterministic Core tests when authoritative behavior changes.

When a feature changes, keep the relevant `docs/features.md` sections synchronized with current behavior and durable design context. Do not record Jira progress or development history there.

## Completion Gate

Before completing assigned work:

- Satisfy every applicable Jira acceptance criterion.
- Perform verification required by `docs/workflow.md`, `docs/standards.md`, and Jira, including `./check.ps1` and relevant runtime/integration checks.
- Inspect the complete diff and report assumptions, limitations, risks, and anything unverified.
- Perform the applicable critique from `docs/critique.md` only after verification.
- Stop after the critique. Do not begin critique-driven changes or another round without explicit human authorization.
- Create only the final Story PR to `main`, and only after the integrated Story critique and explicit human acceptance.
