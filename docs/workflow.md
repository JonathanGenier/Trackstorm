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

- Story branches use `ts-<Jira number>-<initials>` by default, for example `ts-91-jg` or `ts-72-pg`. An explicit human-assigned branch name may override the default; otherwise use the canonical pattern.
- Create a new Story branch from `main`; never implement directly on `main`.
- Before implementing, resuming or finalizing a Story, synchronize its branch with current `main` and follow the version procedure below.
- Implement and commit all child checkpoints and authorized corrections directly on that branch. Tasks/Subtasks never receive separate branches, PRs or independent Git reviews/merges.
- A push to a valid `ts-*` Story branch may mechanically create the Story's single PR to `main` immediately only when that exact branch has no prior PR history targeting `main`. If an open, closed, or merged PR already exists for the branch, automatic creation must not create another one. Automatic PR creation is only delivery plumbing: it does not imply verification, critique, acceptance, merge readiness or Story completion. Tasks/Subtasks still never receive separate PRs.

### Delivery handoff

The normal Story handoff is:

```text
implement + verify locally
        ↓
commit + push Story branch
        ↓
GitHub creates/reuses the single PR
        ↓
delivery maintenance + review
        ↓
human manually merges
```

- Automated workflows and review agents never merge a Story PR unless a human explicitly requests that action.
- During delivery review, mechanical maintenance may be applied directly before the verdict when the correct result is unambiguous and does not change feature behavior. Examples include synchronizing compatible `main` changes, Story version recalculation, export metadata, documentation synchronization, PR metadata, and simple workflow/config reconciliation.
- Being behind `main`, stale version metadata, or a safely resolvable maintenance conflict is not by itself a review defect.
- Merge conflicts must be classified by substance. Resolve mechanical conflicts during delivery when the intended result is clear. If resolution requires substantive production-code changes, feature-behavior decisions, architecture changes, nontrivial logic, or implementation-related test changes, return the work to implementation on the same Story branch and re-run applicable verification before final review.
- Review-time maintenance must preserve Jira scope, approved requirement changes, current repository invariants, and newer compatible behavior already present on `main`.

### Canonical Story version procedure

1. Fetch current `main` and synchronize the Story branch with it before implementation and again before final delivery.
2. Run `./tools/sync-story-version.ps1` after synchronization. It reads `TrackstormVersion` from current main's root `Directory.Build.props`, derives the expected normal Story version, conditionally updates the branch's canonical property, and synchronizes the tracked Windows `application/file_version` and `application/product_version` fields in `export_presets.cfg` to `MAJOR.RELEASE.PR.0`.
3. For a normal Story, the operation preserves main's exact `MAJOR.RELEASE` prefix and sets only the third (`PR`) component to `main.PR + 1`. Main `0.0.15` requires canonical `0.0.16` and preset `0.0.16.0`; main `0.1.14` requires canonical `0.1.15` and preset `0.1.15.0`. Canonical versions have exactly three components, with `MAJOR = 0` for the current generation.
4. The operation derives this value from current main, never from branch creation time, commit count, the previous local value or another Story branch. It changes neither file when both already contain the expected values, so repeated agent/developer runs are idempotent rather than additional increments. `Directory.Build.props` remains the sole canonical source; the preset fields are a validated tracked representation.
5. If another Story merges and advances main, synchronize again and recalculate from that new main value. A previously valid Story version can become stale.
6. A release change requires a dedicated Jira release-version Story. Update `.github/version-transition.json` with `kind: release`, that Story's key, exact `from` main version and exact `to` canonical target, then run `./tools/sync-story-version.ps1 -Kind release`. The branch must contain that Jira key. CI requires a newly changed declaration, exactly the next release and `PR = 0`: `0.1.15 -> 0.2.0`, never `0.1.15 -> 0.8.0`. Normal Stories leave the last declaration unchanged. The declaration authorizes a transition only; `Directory.Build.props` remains the sole build-version source.
7. TS-70 alone uses `./tools/sync-story-version.ps1 -Kind migration` to apply its authorization from old main `0.0.1.0` to the explicitly user-approved baseline `0.0.15`. It is not an old-fourth-component increment or an inferred `0.1.0`. Old four-component values are otherwise invalid canonical versions.
8. TS-94 alone uses `./tools/sync-story-version.ps1 -Kind baseline-correction` with its newly changed transition declaration to retain canonical `0.1.0`. The [versioning authorization rules](features/game-versioning.md#explicit-release-authorization) restrict this one-time baseline correction; normal future Stories still increment.
9. Run `./tools/check-version.ps1` after synchronization/version adjustment and before final delivery. CI supplies the PR head branch; local checks use the checked-out branch. It validates main ancestry, the canonical transition, and that each required tracked preset field occurs exactly once and is well formed. File/product versions must equal canonical `MAJOR.RELEASE.PR.0`; company/product identity must equal the fixed defaults defined in [game versioning](features/game-versioning.md). Resolve expected/actual, authorization, preset-drift or ancestry failures before delivery.

[Game versioning](features/game-versioning.md#story-sequencing) describes the existing single CI enforcement path and parser limits. This repository procedure applies to agents and developers alike; it is not a per-Story Jira instruction.

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

### Automated CI tiers

The normal PR/main CI is the fast gate. It verifies materialized frontend media, version-rule regressions, Story version ancestry where applicable, Debug compilation of the production dependency graph and Release compilation of the full solution with warnings as errors, Core tests, non-native transport tests and a headless main-scene startup smoke using the pinned Godot .NET editor. The deterministic test suites run once in Release; duplicate Debug test-assembly compilation/execution is intentionally avoided. Superseded runs for the same PR/branch are canceled, non-Story checkouts are shallow, and Git LFS objects, NuGet packages, and pinned EOS/Godot downloads are cached.

Extended CI runs nightly, on manual dispatch and for version tags. It exercises native transport plus Godot transport integration, the native local lobby harness and the unauthenticated EOS native SDK lifecycle smoke. Real authenticated EOS, exported-build, remote-network and multi-device acceptance remains manual because those checks require deployment credentials/state or independent physical identities and cannot be represented faithfully by a hosted runner.

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
3. Run `./check.ps1` from the root: restore, Debug-build the production dependency graph and Release-build the full solution with warnings as errors, then run Core tests and non-native Client/transport tests once in Release configuration.
4. Run additional applicable or Jira-required gameplay, network, integration, runtime, visual, UI, audio, physics and asset/license checks. Exercise actual runtime behavior when relevant and technically possible; do not substitute inspection for required observation. Run the minimal Godot project when settings, scenes or Client integration change.
5. Confirm engineering standards, including dependency direction and absence of a Shared layer.
6. Verify affected feature documentation against code; check links, index coverage and obsolete references.
7. Inspect the complete Story diff against `main` for correctness, dead paths, stale identifiers, unrelated changes, generated files, build output, local configuration and debug artifacts.
8. Report assumptions, limitations, unresolved risks and unverified behavior.
9. Create or update the Story's historical verification report at `docs/verification/ts-<number>.md` using the Jira Story number in lowercase filename form (for example, `TS-86` → `docs/verification/ts-86.md`). Record only evidence from the current Story: materially implemented behavior/systems, verification and test commands/results actually run, applicable runtime/manual/native evidence, assumptions, limitations, unresolved risks and explicitly unverified areas. Never invent or infer a test result that was not run or observed. Add or update the Story's entry in `docs/verification/README.md`.

The per-Story report is a historical delivery artifact, not a source of current requirements or proof that old evidence still applies. Jira, approved requirement changes, current source inspection, current CI, repository instructions and current feature documentation remain authoritative for review. Historical reports under `docs/verification/` do not replace the checks above; required current checks must pass before the work is complete.

## Critique and final PR

After integrated verification, perform the applicable review in [critique](critique.md), which owns scoring, round limits and the mandatory stop for a human decision. Authorized corrections stay on the existing branch and require applicable re-verification.

The repository may already have created the Story PR automatically when the `ts-*` branch was pushed. That early PR is only a delivery container and may initially use the branch name as its title with an empty/minimal body. Review and acceptance must still use Jira, approved changes, repository instructions, the current source/diff, current feature documentation, current CI and the Story verification report rather than trusting PR prose.

After explicit human acceptance of the critique, perform final verification without new implementation changes. Delivery-time mechanical maintenance may still be applied when it does not change feature behavior; re-run the affected checks afterward. If substantive implementation changes become necessary, return to implementation on the same Story branch, re-verify, repeat the applicable authorized critique process and obtain renewed acceptance.

Maintain exactly one Story PR to `main`. Before final delivery, update its title/body as useful historical documentation with the Jira key, integrated implementation summary, actual verification evidence, assumptions, limitations and unresolved risks, then report its number and URL. PR prose is history, not review authority. A human performs the final merge; automatic PR creation, passing CI, or a passing review does not merge the Story.
