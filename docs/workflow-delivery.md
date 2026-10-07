# Workflow Delivery

Read the [shared system contract](workflow.md) first. This reference contains only the selected subsystem; preserve its shared ownership and integration requirements.

## Story-only Git delivery

```text
1 Story = complete Story scope + all required child Tasks/Subtasks
        = 1 Story branch = 1 final PR to main
```

- Story branches use `ts-<Jira number>-<initials>` by default, for example `ts-91-jg` or `ts-72-pg`. An explicit human-assigned branch name may override the default; otherwise use the canonical pattern.
- Story branches are normally created and selected locally by the human developer before handing work to an implementation agent. Agents work on the already-selected Story branch unless explicitly asked to perform Git administration; never implement directly on `main`.
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
- During delivery review, mechanical maintenance should be applied directly before the verdict when the correct result is unambiguous and does not change feature behavior. Examples include synchronizing compatible `main` changes, Story version recalculation, `Directory.Build.props` / `export_presets.cfg` synchronization, PR metadata, verification-index maintenance, simple documentation synchronization, analyzer/stale-reference corrections, and simple workflow/config reconciliation. Do not spend a new implementation-agent run on work the independent reviewer can repair safely.
- Being behind `main`, stale version metadata, or a safely resolvable maintenance conflict is not by itself a review defect.
- Merge conflicts and review findings must be classified by substance. Resolve mechanical conflicts and small unambiguous non-behavioral corrections during delivery. Return work to the implementation agent only when correction requires changed feature behavior, architecture decisions, nontrivial production logic, networking/state/serialization work, physics/gameplay changes, substantial multi-file implementation, implementation-related test redesign, or ambiguous semantic conflict resolution. Re-run applicable verification after any correction.
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

## Critique and final PR

After integrated verification, perform the applicable runtime/experiential review in [critique](critique.md), which owns Astra scoring, round limits and the mandatory stop for a human decision. Every completed Story receives Astra runtime/experiential or feature-specific operational critique after final verification. Record missing or unavailable critique as N/A with the limitation; never manufacture a score or silently infer an exemption. Astra evaluates what requires execution or observation and does not duplicate static source-code review.

The independent PR review owns engineering judgment after push: Jira completeness, code correctness and quality, architecture and technical decisions, maintainability, Core/Client ownership, dependency direction, test quality and coverage, documentation, scope discipline, current-`main` integration, version state, CI and delivery readiness. Authorized corrections stay on the existing branch and require applicable re-verification.

Engineering and Astra scores are advisory, with targets of **6.0** and **8.0** respectively. A below-target score requires an explicit warning, not automatic rejection or a polish round. Optional cleanup alone never invalidates acceptable implementation.

The final PR verdict is exactly **PASS**, **BLOCKED**, or **INVALID**. PASS means acceptable implementation and every merge gate satisfied. BLOCKED means merge-readiness is incomplete (for example pending/approval-required CI, baseline/infra failure, missing verification/Astra evidence, or unresolved routine main/version maintenance). INVALID requires a concrete implementation defect: incorrect behavior, acceptance-criteria violation, regression, harmful scope change, unsafe architecture/ownership, or a material coverage gap. CI exposing a real code/test regression is INVALID, not merely BLOCKED. Always inspect current main before the verdict and apply safe synchronization/version corrections rather than only reporting that the branch is behind.

The repository may already have created the Story PR automatically when the `ts-*` branch was pushed. That early PR is only a delivery container and may initially use the branch name as its title with an empty/minimal body. Review and acceptance must still use Jira, approved changes, repository instructions, the current source/diff, current feature documentation, current CI and the Story verification report rather than trusting PR prose.

After explicit human acceptance of a critique, confirm that the previously verified source, build configuration and relevant environment still match. Acceptance alone does not require a duplicate full local suite on an unchanged result. Evidence is not transferable across changed inputs: substantive corrections, relevant main integration changes, or changed build/runtime inputs require applicable re-verification and the required comprehensive final gate. CI remains independent. Delivery-time mechanical maintenance may still be applied when it does not change feature behavior; re-run the affected checks afterward. If substantive implementation changes become necessary, return to implementation on the same Story branch, re-verify, repeat the applicable authorized critique process and obtain renewed acceptance.

Maintain exactly one Story PR to `main`. The delivery reviewer, not a fresh substantive implementation-agent run, should update its title/body before final delivery as useful historical documentation with the Jira key, integrated implementation summary, actual verification evidence, assumptions, limitations and unresolved risks, then report its number and URL. PR prose is history, not review authority. A human performs the final merge; automatic PR creation, passing CI, or a passing review does not merge the Story.

## Automated PR approval

A PR created by the repository's `GITHUB_TOKEN` workflow can have CI awaiting workflow approval. Inspect the actual run status. An authorized human approves required workflows through GitHub; do not create dummy commits, weaken security, or claim that approval-required CI has passed. Mechanical PR/version/index maintenance belongs to delivery review, not another substantive implementation-agent run.
