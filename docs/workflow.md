# Jira Assignment and Delivery Workflow

This document owns assignment interpretation, Git delivery, feature-document maintenance and completion verification. [Engineering standards](standards.md) own code and test design; [critique](critique.md) owns quality review and its human gates; the [feature index](features/README.md) routes to current system behavior.

## Authority and approved requirement changes

Jira is the persistent source of truth for what assigned work must accomplish: behavior, deliverables, acceptance criteria, constraints, dependencies, sequencing, feature-specific tests and verification, and required assets, sources, versions, licenses and attribution. Repository instructions define how Trackstorm is engineered, tested, documented, reviewed and delivered.

Read the complete assigned description, required children, comments and explicit clarifications before implementation. Never silently omit, simplify, replace or defer a requirement.

An explicit human-approved requirement change during implementation overrides stale Jira content until Jira is updated. Identify the approved change and affected criteria, preserve unaffected requirements, and update Jira to reflect the decision before closing the work. Suggestions, brainstorming and unapproved alternatives do not change active scope.

If Jira or an approved change appears to conflict with an architecture, workflow, security, licensing or engineering invariant, report the conflict and preserve the stricter invariant until the human explicitly resolves it. Do not infer an exception.

## Assignment interpretation

### Complete Story assignment

Read the Story and every required child Task/Subtask in full. Determine dependencies and implementation order, then implement and verify all required children on the Story branch. Each child is a traceable checkpoint; continue after successful verification without a separate human stop. Perform integrated Story verification and critique only after the complete scope is satisfied.

### Individual Task/Subtask assignment

Read the assigned child and enough of its parent Story to understand context and dependencies. Use the parent Story branch, implement only the assigned child, verify it, then perform the individual critique defined in [critique](critique.md). Do not implement siblings automatically.

If the assignment and Jira issue type/parent disagree, identify the discrepancy. An explicit human assignment can establish the intended work unit and branch without changing Jira metadata; do not invent a parent or create child delivery branches. Use the agreed delivery unit for verification and critique.

## Story-only Git delivery

```text
1 Story = complete Story scope + all required child Tasks/Subtasks
        = 1 Story branch = 1 final PR to main
```

- Use the explicitly human-assigned Story branch name. Do not rename it to satisfy a naming pattern. If no branch is assigned, resolve the intended Story branch before implementation.
- Create a new Story branch from `main`; never implement directly on `main`.
- Before starting/resuming implementation, synchronize with the latest `main`.
- Implement and commit all child checkpoints and authorized corrections directly on that branch. Tasks/Subtasks never receive separate branches, PRs or independent Git reviews/merges.
- The only delivery PR is the final integrated Story PR to `main`, after verification, critique and explicit human acceptance.

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

Engineering and test-design requirements are in [standards](standards.md). Feature documents identify relevant harnesses; Jira can require additional feature-specific checks.

### Child checkpoint

Before recording a child complete:

1. Re-read its requirements and verify every deliverable and acceptance criterion.
2. Run required unit, integration, runtime, architecture, documentation and asset/license checks.
3. Inspect changes for scope, regressions, warnings and accidental artifacts.
4. Record evidence and anything inferred or unverified, then continue according to the assignment interpretation above.

### Integrated Story verification

After completing all required children:

1. Re-read the Story, children and approved changes; verify every applicable requirement.
2. Synchronize the branch with latest `main` and resolve integration conflicts.
3. Run `./check.ps1` from the root: restore, formatting verification, Debug/Release builds with warnings as errors, Core tests and non-native Client/transport tests.
4. Run additional applicable or Jira-required gameplay, network, integration, runtime, visual, UI, audio, physics and asset/license checks. Exercise actual runtime behavior when relevant and technically possible; do not substitute inspection for required observation. Run the minimal Godot project when settings, scenes or Client integration change.
5. Confirm engineering standards, including dependency direction and absence of a Shared layer.
6. Verify affected feature documentation against code; check links, index coverage and obsolete references.
7. Inspect the complete Story diff against `main` for correctness, dead paths, stale identifiers, unrelated changes, generated files, build output, local configuration and debug artifacts.
8. Report assumptions, limitations, unresolved risks and unverified behavior.

Historical reports under `docs/verification/` do not replace these checks. Required checks must pass before the work is complete.

## Critique and final PR

After integrated verification, perform the applicable review in [critique](critique.md), which owns scoring, round limits and the mandatory stop for a human decision. Authorized corrections stay on the existing branch and require applicable re-verification.

Only after explicit human acceptance for PR creation, perform final verification without new implementation changes. If changes become necessary, re-verify, repeat the applicable authorized critique process and obtain renewed acceptance.

Create exactly one final PR to `main`. Include the Jira key, integrated summary, verification evidence, assumptions, limitations and unresolved risks, then report its number and URL.
