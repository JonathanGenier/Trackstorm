# Workflow Verification

Read the [shared system contract](workflow.md) first. This reference contains only the selected subsystem; preserve its shared ownership and integration requirements.

## Verification

Engineering and test-design requirements are in [standards](standards.md). Feature documents identify relevant harnesses; Jira can require additional feature-specific checks.

### Iteration efficiency

During implementation, use the narrowest deterministic checks that exercise the systems changed. Prefer `./tools/check-fast.ps1` over repeatedly running the entire repository gate. It combines the committed Story difference with staged, unstaged and non-ignored untracked paths (including both sides of renames), then routes changes to Core, transport, authority-lease service, version/media and existing feature-specific harnesses, deduplicates overlapping routes, and keeps feature-doc-only edits from triggering production runtime checks.

- Run `./tools/check-fast.ps1` for automatic deterministic routing.
- Pass `-GodotPath <path>` (or set `GODOT_PATH`) to execute the routed Godot/runtime harnesses automatically.
- Use `-RequireRuntime` when an iteration must fail rather than merely report pending runtime checks if Godot is unavailable.
- Use `-IncludeExtended` only when the routed extended/native checks are appropriate; expensive multi-process/EOS/GdUnit-regression checks are identified but not silently run by default.
- The router records required playtest/manual scenarios such as multi-car driving, camera inspection, UI navigation, audio listening or latency-sensitive multiplayer when those cannot be fully represented by an automated harness.
- Re-run the affected checks after each meaningful correction.

The routing table lives in `tools/fast-check-routes.ps1` and is regression-tested by `tools/test-workflow-tools.ps1`. Add or change routes when a system gains a durable verification harness; do not duplicate feature behavior or acceptance criteria in the routing table.

Targeted iteration does not reduce completion coverage. Before Story handoff, perform the comprehensive integrated verification below exactly once on the completed result, plus any additional Jira- or feature-specific runtime/native checks. CI remains an independent final machine gate.

Runtime observation is part of implementation verification, not merely critique. When runtime execution is relevant and technically possible, actively exercise the feature and correct clear in-scope defects before the formal critique. Multi-car, multi-entity, multiplayer, reconnect, state-transition, persistence, repeated-use and sustained-runtime scenarios must be exercised when material to the Story.

Use `./tools/new-verification-report.ps1 TS-<number>` to scaffold a missing Story verification report and index entry. The scaffold contains placeholders only; replace them with actual evidence and never convert an unexecuted placeholder into a claimed result.

### Automated CI tiers

The normal PR/main CI is the fast gate. It verifies materialized frontend media, version-rule regressions, Story version ancestry where applicable, Debug compilation of the production dependency graph and Release compilation of the full solution with warnings as errors, Core tests, non-native transport tests and a headless main-scene startup smoke using the pinned Godot .NET editor. The deterministic test suites run once in Release; duplicate Debug test-assembly compilation/execution is intentionally avoided. Superseded runs for the same PR/branch are canceled, non-Story checkouts are shallow, and Git LFS objects plus NuGet packages are cached. EOS and Godot remain pinned and integrity-checked but are downloaded directly because their small downloads are faster than restoring an additional Actions cache.

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

## Targeted-tool operation

Use `tools/check-fast.ps1 -Plan -Explain` to inspect routes without executing checks; add `-Json` for machine-readable output. Plan-only success is never test evidence. `tools/check-agent-environment.ps1` performs read-only prerequisite inspection; it neither installs tools nor disables package auditing. Missing native/media/device capabilities remain explicit.

Execution writes complete per-command stdout/stderr logs and `results.json` below `.godot/fast-checks/<run>/`. It records actual commands, parameters, exit codes, timing, source HEAD/dirty fingerprint, pending manual/runtime/extended work and checks not executed after failure. `-DetailedOutput` restores verbose console output. Store custom evidence outside the repository or in a Git-ignored directory. Do not include credentials in command arguments or committed evidence. An empty plan is not a pass; a completed targeted plan is not final Story verification.

For a runtime batch the router builds the current Debug solution once, then passes each supported build-skip switch. The mixed Debug/Release network-soak harness retains its own setup; GdUnit regression also retains its independent import behavior. Standalone harnesses still build by default. Source changes during execution invalidate the batch; do not edit the checkout or change SDK/native configuration while checks run. No build or verification success is reused from a previous invocation.

Use the structured records to summarize actual execution in the Story report. Human observations, authenticated/remote multiplayer, physical devices, experiential judgments and Astra results remain separately attributed; no script infers them from a passing build.
