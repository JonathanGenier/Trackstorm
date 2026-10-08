# TS-283 independent engineering review

Verdict: **CHANGES REQUIRED**

Engineering score: **5.8 / 10**, against the exact **6.0 PASS threshold**.

Reviewed pushed commit `d285a457c34f64c16b8b02ca6228008ee3c74cb4` on `ts-283-pg`, against current main `73c6811b56cc8ae3b05229d66b7ffdfcb0ab2911` (TS-286 / PR #328). PR: https://github.com/JonathanGenier/Trackstorm/pull/330 . This is independent static engineering review, not a new runtime/experiential critique.

## Actionable finding

### [P2] Preserve non-vehicle collision response when reconstructing pair participants

Location: `code/Client/Vehicles/VehicleBody.cs:241-245` (incoming command reconstruction begins at lines 233-239).

`CaptureBatch` selects a body whenever either participant reports a vehicle contact, replaces its observed linear/angular velocities with the previous Core command plus accepted effects, and restores other contacts only by projecting linear velocity out of their normal. It never retains or reconstructs the same-boundary terrain/barrier angular impulse, contact friction, or obstacle tangential dissipation. The subsequent `VehicleCollision.ResolveContacts` only processes vehicle contacts and cannot recover those contributions.

This matters when a car hits terrain nose/roof/side-first or a barrier while another vehicle is still touching it. A zero-closing side contact is sufficient to enter this path: it adds no pair impulse, yet the world-contact tumble is erased. In the prior implementation, `VehicleArena` passed `Capture` directly to Core, so mixed contacts retained the native-solved response. `Capture` deliberately skips its separate `TerrainCollision` / `EnvironmentCollision` reconstruction when a vehicle contact exists (lines 187 and 203); the new batch therefore removes the native fallback without replacing it. The network adapter, by contrast, retains its terrain/obstacle response before invoking the pair solver. This violates preservation of crash-generated rotation and existing collision/slowdown behavior, and creates a material practice/network inconsistency in combined collisions.

Correct the mixed-contact reconstruction so independent terrain, barrier and movable-body contributions coexist with the shared vehicle solve, and add a focused simultaneous vehicle-plus-world contact regression, including a zero-relative-speed vehicle touch. Preserve accepted effect impulses exactly once. This is a behavioral correction requiring the existing implementation/reverification/critique process; it is not delivery metadata maintenance.

Evidence classification:

- **Source-confirmed:** the new path discards every observed angular change for a pair participant, except rotation reconstructed from prior accepted effects and the new vehicle pair solve. Its non-vehicle loop changes linear velocity only.
- **Focused deterministic boundary probe:** `.godot/ts-283/engineering-probe/Program.cs` uses the current Release `code/Core/bin/Release/net10.0/Trackstorm.Core.dll`, the production `TerrainCollision`, `EnvironmentCollision` and `VehicleCollision.ResolveContacts`, and reproduces the new reconstruction statements with an empty effect list and zero-closing pair contact. A qualifying terrain impact's existing Core response was angular `(6.5189614, 0.81499124, 1.2701993)` rad/s; reconstruction plus pair solve returned `(0, 0, 0)`. A barrier response was angular `(0, 0.50683993, 0)` and tangential speed 11.461622 m/s; reconstruction returned zero angular velocity and retained 15 m/s tangential speed. Command: `dotnet run --project .godot/ts-283/engineering-probe/Probe.csproj -c Release`.
- The probe is a numeric boundary demonstration using copied reconstruction statements, **not a fresh Godot runtime reproduction or an exact native trajectory comparison**. The first probe build referenced an older `.godot/mono/temp/bin/Release` Core artifact; that setup error was corrected to the current Core Release output before the successful probe.
- **Coverage gap:** aerial/crash/landing fixtures call `Capture` directly; the new PIT fixture exercises pair contacts on a flat floor, and trophy pair cases are separate from its awkward terrain/rollover cases. The passing separate native gates do not exercise loss of a simultaneous non-wheel world-impact response through `CaptureBatch`.

## Remaining review assessment

Read the complete user request attachment, live Jira TS-283 and parent Epic TS-229, root/nested routing, workflow, engineering standards, feature routing and affected vehicle/network/configuration contracts. Jira TS-283 has no subtasks, comments or issue links. Inspected the full changed production/test/tooling/documentation scope against main, the Story verification report, formal Astra evidence and relevant raw gate output.

- Aerial decay retains the TS-286 chassis-contact cancellation predicate, untouched-flight inertia and portable release latch. No new orientation target, linear momentum suppression or snapshot field was introduced.
- The pair solver uses one equal/opposite mass-weighted impulse, geometrically qualified yaw compliance, bounded rotation and stable pair ownership. Core remains independent of Godot; Client calls Core without introducing a Shared layer or a second damage authority.
- Prior accepted effects are reconstructed once; new effect requests are forwarded to the existing authoritative step. Raw contact arrays remain available to damage handling. The observed native/swept damage asymmetry remains disclosed and is not classified as an introduced regression without baseline evidence.
- Configuration uses the existing catalog/validation/transaction/recovery path, with TC43 explicitly rejecting TC42. Snapshot layout is unchanged. Defaults, configuration round trips and axis-release tests are meaningful. Pair tests cover geometry, strength, mass, ordering, raw evidence and repeated solves; native/UDP following evidence exercises actual sustained contact. Swept rubbing only lasting two observations is correctly disclosed.
- Feature documentation and verification index are updated. No dependency or asset redistribution change was introduced. Version 0.2.34 and Windows 0.2.34.0 are consistent with main 0.2.33. Branch is zero commits behind / one ahead of origin/main; tracked working tree is clean. `git diff --check` passed.
- Implementation evidence confirms `check.ps1` passed 1,195 Core and 445 non-native transport tests with warning-as-error builds; all 17 applicable native gates ultimately passed. The report retains initial failed attempts and the intermittent following correction hitch. Formal independent Astra Round 1 is 8.1 / 10 PASS. These results remain valid evidence for their exercised scenarios, but do not cover the mixed-contact defect above.
- Current PR head matches the reviewed SHA. GitHub `verify` completed **SUCCESS**: https://github.com/JonathanGenier/Trackstorm/actions/runs/37672643241 . `create-pr` also succeeded; `revalidate-open-prs` was skipped. Green CI does not resolve the finding.
- Actual production Van geometry, physical controller ergonomics, remote physical-machine networking and authenticated EOS remain explicitly unverified. No separate blockers were invented from these known scope limitations or from optional polish.

No tracked files, branches, stashes, PR reviews/comments or Jira state were changed by this review. Only ignored review/probe artifacts were written. Do not merge or perform post-critique behavioral correction without the required human direction.
