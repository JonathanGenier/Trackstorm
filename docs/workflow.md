# Jira Assignment and Delivery Workflow

Read this shared assignment/scope contract first. Implementation also reads [verification](workflow-verification.md); delivery review/admin reads [delivery](workflow-delivery.md). These documents divide stage-specific detail without reducing any completion gate. Implementation agents do not perform PR prose, version or index-only maintenance; delivery review owns those mechanical corrections. [Engineering standards](standards.md) own code and test design; [critique](critique.md) owns quality review and its human gates; the [feature index](features/README.md) routes to current system behavior.

## Authority and approved requirement changes

Jira is the persistent source of truth for what assigned work must accomplish: behavior, deliverables, acceptance criteria, constraints, dependencies, sequencing, feature-specific tests and verification, and required assets, sources, versions, licenses and attribution. Repository instructions define how Trackstorm is engineered, tested, documented, reviewed and delivered.

Read the complete assigned description, required children, comments and explicit clarifications before implementation. Never silently omit, simplify, replace or defer a requirement.

Implementation prompts should stay concise. They should identify the Jira work unit, current Story branch, objective, approved requirement changes, validation expectations and completion handoff, then rely on this repository's routing documents for generic architecture, testing, documentation and delivery rules instead of restating them.

### Implementation-agent efficiency

Read the assigned Jira Story/Bug's `Recommended Model: <Model> — <Reasoning level>` before implementation. Preserve that recommendation; do not independently replace it using an older generic model matrix. The planner chooses the lowest-cost trusted model likely to succeed in one cycle. Light-tier models are not used for substantive work. A materially changed, explicitly approved scope requires an explained reassessment and a Jira update before a different recommendation is issued. If the current model is not identifiable, report that limitation rather than claiming enforcement.

A normal implementation prompt should be close to:

```text
Implement <Jira key> on the current Story branch <branch>.

Inspect the Jira Story/Bug and required children/comments, approved requirement changes,
applicable repository instructions, relevant feature docs, and current implementation.

Jira is the persistent source of truth for Story scope and acceptance criteria.
Explicit user-approved changes made during implementation override stale Jira content
until Jira is updated.

Implement only required scope. Use targeted checks while iterating, then perform the
required final verification and runtime/playtest critique. Update the Story verification
report with actual evidence, commit, push, and report what changed, what was verified,
and anything unverified.
```

Do not repeat generic architecture, Git, test, documentation, critique or delivery rules already owned by this repository. Do not bulk-read the full feature catalog or historical verification archive: follow `AGENTS.md`, the feature index and nested routing to load only context material to the current work. Historical reports are read only when their recorded evidence is actually relevant.

An explicit human-approved requirement change during implementation overrides stale Jira content until Jira is updated. Identify the approved change and affected criteria, preserve unaffected requirements, and update Jira to reflect the decision before closing the work. Suggestions, brainstorming and unapproved alternatives do not change active scope.

If Jira or an approved change appears to conflict with an architecture, workflow, security, licensing or engineering invariant, report the conflict and preserve the stricter invariant until the human explicitly resolves it. Do not infer an exception.

## Assignment interpretation

### Complete Story assignment

Read the Story and every required child Task/Subtask in full. Determine dependencies and implementation order, then implement and verify all required children on the Story branch. Each child is a traceable checkpoint; continue after successful verification without a separate human stop. Perform integrated Story verification and critique only after the complete scope is satisfied.

### Individual Task/Subtask assignment

Read the assigned child and enough of its parent Story to understand context and dependencies. Use the parent Story branch, implement only the assigned child, verify it, then perform the individual critique defined in [critique](critique.md). Do not implement siblings automatically.

If the assignment and Jira issue type/parent disagree, identify the discrepancy. An explicit human assignment can establish the intended work unit and branch without changing Jira metadata; do not invent a parent or create child delivery branches. Use the agreed delivery unit for verification and critique.

## Story-only Git delivery

<a id="delivery-handoff"></a>
<a id="canonical-story-version-procedure"></a>
[Read this section](workflow-delivery.md#story-only-git-delivery).

## Scope discipline

Keep each child's work traceable to its requirements, including documentation, assets and verification. Avoid unrelated cleanup, refactors, renames, dependencies or speculative abstractions. Implement siblings in dependency order. Report work outside the assignment as a dependency, risk or follow-up instead of expanding scope silently.

## Dependencies, assets and licenses

Introduce third-party packages, plugins, libraries, models, textures, materials, audio, fonts or other assets only when required by the assigned scope. Use the specified source without substitution; prefer an existing dependency/asset when it satisfies the requirement.

[THIRD_PARTY.md](../THIRD_PARTY.md) is the canonical registry. For additions or changes, record or link source URL, pinned version/selection, license, required attribution and provenance metadata there. Detailed notices and asset manifests may remain in their existing locations, with the registry identifying their authority. Follow applicable redistribution conditions and preserve required notices. Dependencies must respect [engineering standards](standards.md).

## Feature documentation

Inspect [the feature index](features/README.md) and the relevant system documents before feature work. Keep every materially affected feature document synchronized with the resulting implementation in the same change:

- Added feature: create the appropriate system document and index entry, or extend an existing document when it is part of that system.
- Changed feature: update every materially affected document and integration link.
- Completely removed feature: remove its document and index entry; if a document also covers surviving behavior, remove only the obsolete content.
- Renamed, merged or split feature: reorganize documents and index entries to match the resulting systems, repairing incoming links.

Organize documents by durable implemented systems, not Jira Stories. Each document describes applicable current behavior, architecture/ownership, invariants, assumptions, design rationale, configuration, integrations and intentional limitations. Preserve why non-obvious decisions exist. Use source inspection to correct stale current-system claims; Jira status alone does not prove implementation.

Use a descriptive title and only relevant sections from that list; omit empty headings. Link related systems instead of duplicating their contracts. Generic engineering/process rules belong in their owning policy document, not feature pages. Do not include Jira progress, branch/PR/commit history, critique history or temporary implementation notes. Historical results belong in [verification evidence](verification/README.md).

## Verification

<a id="iteration-efficiency"></a>
<a id="automated-ci-tiers"></a>
<a id="child-checkpoint"></a>
<a id="integrated-story-verification"></a>
[Read this section](workflow-verification.md#verification).

## Critique and final PR

[Read this section](workflow-delivery.md#critique-and-final-pr).

## Targeted-tool operation

[Read this section](workflow-verification.md#targeted-tool-operation).

## Automated PR approval

[Read this section](workflow-delivery.md#automated-pr-approval).
