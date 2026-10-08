# TS-283 independent engineering review — Round 2

Verdict: **PASS**

Engineering score: **7.8 / 10**, against the exact **6.0 PASS threshold**.

Reviewed pushed correction `ab69199b4e849bc2a0704f6009c68df5c5944be0` on `ts-283-pg`, [PR #330](https://github.com/JonathanGenier/Trackstorm/pull/330), against correction baseline `869f82d268226a94763eac0d73ed077c2f60ac82` and the previously reviewed complete Story context. Current main is `73c6811b56cc8ae3b05229d66b7ffdfcb0ab2911` (TS-286 / PR #328).

This is independent post-push engineering review, not another runtime critique. No new actionable engineering findings were identified. The original Round 1 P2 is resolved in the reviewed correction.

## Original finding disposition

`VehicleBody.CaptureBatch` now forwards complete captured observations into the internal Client `VehicleContactBatch` helper. The helper identifies every observed vehicle pair, marks world-contact participants, and follows connections in both directions until the entire mixed group is covered. One-sided reports and chains therefore cannot cause only one coupled participant to be rewound. A contact report at capacity conservatively retains the entire batch because an omitted world contact or connecting pair cannot be ruled out.

For a mixed group, the helper preserves each complete captured physics state. It does not re-add accepted effects, recompute world friction/torque, or reduce the world response to normal-only projection. The unchanged Core pair solver subsequently resolves remaining closing motion once per pair. A native pair already separated or no longer closing receives no additional impulse. These changes directly remove the cause of the original lost-tumble/tangential-slowdown finding.

Pure vehicle groups unconnected to world contacts still reconstruct the established incoming command plus accepted-effect boundary, retaining the full shared PIT response. Environment-only observations remain on their existing capture path. The distinction is a bounded adapter policy: constrained world collisions can limit PIT disruption, and native/swept trajectories are not promised identical. The current feature documents describe this behavior and the native impulse-report timing reason for avoiding subtraction.

Relevant locations: `code/Client/Vehicles/VehicleBody.cs:221`; `code/Client/Vehicles/VehicleContactBatch.cs:17` (capacity fallback), `:24` (connected-group expansion), `:39` (mixed-group preservation), and `:55` (one shared solve).

## Correctness, authority and scope

- Production correction is confined to Client observation staging. Core collision, world response, authority, network prediction and aerial production code are unchanged in this round. Dependency direction remains Client to Core with no Shared layer.
- Existing accepted effects are already represented by mixed observations and are not replayed there. The pure-pair reconstruction retains its prior single application. Newly queued effects, reset/repair intents, Oil/Nitro fields and input are forwarded to the existing authoritative step. Raw damage contacts remain unchanged.
- Pair solving retains stable identity order, equal/opposite linear momentum, bounded yaw compliance and the prior duplicate-contact handling. No new damage owner, environment solver, contact cooldown or extra PIT impulse was introduced.
- The helper is small and engine-independent for testing, with responsibilities and conservative fallback documented at the relevant boundary. No unrelated gameplay or architecture expansion was found.
- The rock verification-only cleanup extends the existing managed-wrapper drain after each completed fixture. It leaves gameplay assertions and contact/performance budgets unchanged. Earlier GCHandle and performance failures are retained in evidence rather than replaced by an unqualified clean-run claim.

## Verification and test quality

Independently executed during this engineering review:

`dotnet test code/TransportTests/Trackstorm.Transport.Tests.csproj --no-build --no-restore -c Release --filter FullyQualifiedName~VehicleContactBatchTests`

**PASS: all eight tests.** They cover preservation of terrain/barrier torque and tangential loss, accepted versus queued effects, zero/low/qualifying residual closing, pair momentum and representative energy bounds, one-sided connected chains, full-report fallback, environment-only identity and an independent pure PIT pair. Their observable boundary assertions address the defect rather than only checking helper internals.

Inspected the native `MixedContactChecks` source and recorded mutation evidence. Its raw `Capture` and production `CaptureBatch` read the same native boundary before advancement, compare complete results after the existing residual solve, retain raw contacts, and require actual simultaneous contact, measured torque/tangential loss and low-closing touch. The original implementation failed this native regression (`original-mixed-regression.log`); the restored correction passed. The final four-case native log reports zero batch error. This materially improves the earlier copied numeric probe and separate-system coverage.

The completed implementation evidence records `check.ps1` passing warning-as-error builds, **1,195 Core tests and 453 Client/transport tests**, all **19** routed native/runtime gates ultimately passing, impaired UDP collision/PIT/following/rock/driving checks, and reconnect including 125 seconds offline. Inspected the final gate ledger, build/test output, mixed-contact result and repeated-rock final log. No broad native suite was rerun by this engineering reviewer.

Formal independent Astra Story Round 2 is **8.2 / 10 — PASS**. Its fresh mixed-contact, PIT, aerial and UDP observations are runtime evidence from that reviewer, not new observations by this engineering review. The original score/finding history and failed attempts remain explicit.

## Documentation and delivery

Affected feature documents, harness routing, routing regressions and the verification index are synchronized. The correction does not change config/wire layouts, dependencies or redistributed assets. The complete Story retains TC43 and its existing configuration compatibility policy.

`git diff --check` passed across the complete Story. Reviewed HEAD is zero commits behind / three ahead of current origin/main. Canonical version **0.2.34** and Windows **0.2.34.0** correctly follow main 0.2.33. The tracked working tree is clean.

At this review's completion, PR #330 remains **draft**, its head matches `ab69199b4e849bc2a0704f6009c68df5c5944be0`, and the current [CI verify run](https://github.com/JonathanGenier/Trackstorm/actions/runs/37682028585) is **IN PROGRESS**. Automatic PR maintenance passed. A successful CI result is not claimed yet; final delivery must record the eventual result for its final pushed head. Pending CI is delivery state, not a new code finding.

## Explicit limits

Known native/swept damage asymmetry, the earlier intermittent prediction hitch, rock-fixture/performance sensitivity, actual production Van geometry, physical-device/human-driving judgment and remote authenticated EOS remain disclosed. Larger mixed chains, saturated reports and effects corner cases have deterministic coverage; the fresh rendered mixed fixture uses two cars. These limits do not establish an additional material defect in the reviewed correction. Optional polish is not a reason to fail the engineering threshold.

No tracked files, branches, stashes, Jira state, PR comments/reviews or remote metadata were changed by this review. Only this ignored review artifact was written; the focused test reused completed Release binaries. This PASS does not authorize a merge or additional post-critique implementation.
