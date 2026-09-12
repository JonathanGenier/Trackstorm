# Jira Assignment and Delivery Workflow

This document owns Jira assignment interpretation, scope, Git, verification, and delivery. `docs/standards.md` owns engineering and testing; `docs/critique.md` owns quality review; `docs/features.md` owns implemented feature behavior.

## Jira Is the Source of Truth for Assigned Work

For implementation work, Jira defines all specified behavior, deliverables, acceptance criteria, feature constraints, dependencies, sequencing, tests, runtime or other verification, and required assets, sources, versions, licenses, and attribution.

Read and respect the complete assigned Jira scope. Do not silently omit, simplify, replace, reinterpret, or defer a requirement. Repository documentation defines how Trackstorm is engineered, reviewed, and delivered; Jira defines what the assigned work must accomplish.

If Jira appears to conflict with a repository architecture, workflow, security, licensing, or engineering invariant, report the conflict clearly. Preserve the stricter invariant and wait for the human to resolve the conflict rather than silently choosing an interpretation.

## Assignment Interpretation

### Complete Story assignment

When assigned a Story:

1. Read the complete Story and retrieve every required child Task/Subtask.
2. Read all child requirements and acceptance criteria before implementation.
3. Treat the Story plus its required children as the complete authorized scope.
4. Determine dependencies and a sensible implementation order.
5. Implement and verify every required child on the Story branch.
6. Continue automatically between normal child issues; no separate human request is required.
7. Complete integrated Story verification and critique only after all required child work is complete.

A Story assignment is not permission to implement only the Story description while ignoring its children.

### Individual Task/Subtask assignment

When the human explicitly assigns only one Task/Subtask:

1. Identify its parent Story and read enough of it to understand context and dependencies.
2. Checkout the parent Story branch.
3. Implement and verify only the assigned child scope.
4. Do not automatically implement sibling issues.
5. Perform the applicable individual critique from `docs/critique.md` and stop for the human decision.

An individual child assignment narrows implementation scope; it does not change the Story-only Git model.

## Story-Only Git Delivery

The canonical rule is:

```text
1 Story
= complete Story scope + all required child Tasks/Subtasks
= 1 Story branch
= 1 final Pull Request to main
```

The delivery path is:

```text
Jira Story and all required children
        ↓
story/TS-X-short-name
        ↓
Integrated verification
        ↓
Story critique
        ↓
Human acceptance
        ↓
ONE Story PR → main
```

- Create the Story branch from `main`; do not implement Story work directly on `main`.
- Before starting or resuming work, synchronize the Story branch with the latest `main`.
- Implement and commit all Story and child work directly on the Story branch.
- Never create `task/TS-X-...` or `subtask/TS-X-...` branches.
- Never create Task/Subtask Pull Requests, intermediate PRs to the Story branch, or independent child reviews and merges.
- The final Story PR is the only GitHub delivery and review unit and targets `main`.

## Scope and Child Progression

Keep each child implementation traceable to its Jira requirements even though all children share one branch. While working on a child:

- Satisfy its deliverables, acceptance criteria, tests, documentation, and asset/license requirements.
- Avoid unrelated cleanup, refactors, renames, reorganizations, dependencies, assets, or speculative abstractions.
- Avoid implementing sibling requirements early unless required by dependency order.
- Report work outside the assigned scope as a dependency, risk, or follow-up instead of silently expanding the Story.

During a full Story assignment, completing a child is an internal checkpoint: implement it on the Story branch, verify and record its requirements, then continue. Do not insert a mandatory human stop, final Git review, branch, or PR between normal children; the Story assignment already authorizes all required child work.

## Dependencies, Assets, and Licenses

Introduce a third-party package, plugin, library, model, texture, material, audio source, font, or other asset only when required by the assigned scope.

When third-party material is required:

- Use the Jira-specified source when one is provided; do not silently substitute another.
- Prefer an existing project dependency or asset when it already satisfies the requirement.
- Record the source URL, version when applicable, license, and required attribution or provenance metadata.
- Keep convenience dependencies from violating the architecture in `docs/standards.md`.

## Feature Documentation

When work adds, changes, or removes a feature, locate and update the relevant sections of `docs/features.md` in the same change. Ensure the resulting behavior and durable design context agree with the implementation. Do not add Jira progress, child completion logs, branch names, PR or commit history, critique history, or temporary TODOs.

## Verification

Apply the engineering and testing requirements in `docs/standards.md`. Jira may require additional feature-specific checks; those checks are part of the assigned scope.

### Child checkpoint

Before recording a child issue complete:

1. Re-read it and verify every deliverable and acceptance criterion.
2. Run its required unit, integration, runtime, architecture, documentation, and asset/license checks.
3. Inspect the relevant changes for scope, regressions, warnings, and accidental artifacts.
4. Record what was verified and anything inferred or unverified.

During a complete Story assignment, a successful child checkpoint leads to the next required child without a human gate.

### Integrated Story verification

After all required children are complete:

1. Re-read the Story and every required child; verify all acceptance criteria.
2. Synchronize the Story branch with the latest `main` and resolve integration conflicts correctly.
3. Run `./check.ps1` from the repository root.
4. Run every additional Jira-required or technically applicable integration, gameplay, network, runtime, visual, UI, audio, physics, and asset/license check.
5. Exercise and inspect actual runtime behavior when relevant and technically possible; do not substitute code inspection for required observation.
6. Confirm the architecture and testing standards in `docs/standards.md` remain satisfied.
7. For feature changes, compare the implementation with the relevant `docs/features.md` sections and synchronize them.
8. Inspect the complete Story diff against `main` for correctness, unrelated changes, generated files, build output, local configuration, debug artifacts, and stale identifiers.
9. Report assumptions, limitations, unresolved risks, and checks or behavior that could not be verified.

Do not consider the Story complete while required checks fail.

## Critique, Human Gate, and Final PR

After integrated verification, perform the Story quality review in `docs/critique.md` and stop for the human decision.

- Do not make critique-driven changes or begin another critique round without explicit human authorization.
- Apply authorized corrective work directly on the existing Story branch. A Jira child may be useful for traceability, but it receives no branch or PR.
- Re-run applicable verification after corrective work and perform another critique only when authorized, subject to the limit in `docs/critique.md`.
- Do not create the Story PR until the critique process is complete and the human explicitly accepts the Story for PR creation.
- After acceptance, perform final verification without new implementation changes. If changes become necessary, re-verify, repeat the applicable critique process, and obtain renewed acceptance.
- Create exactly one Story PR from the Story branch to `main`. Include the Jira key, integrated Story summary, verification evidence, assumptions, limitations, and unresolved risks, then report the PR number and URL.
