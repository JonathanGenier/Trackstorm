# TS-121 — Multiplayer impairment and repeated-session validation

## Scope and final branch state

[Jira TS-121](https://jonathangenier.atlassian.net/browse/TS-121) defines this as a validation/integration Story. It assigns discovered correctness or performance defects to the owning Story/system. The 2026-09-26 user-approved Jira update defers Vote Kick/Pause/Resume, paused admission, electorate changes and vote repetition to TS-188; voting was neither implemented nor tested. Jira contains no approval to change the production prediction horizon in TS-121.

Following PR #125 review, `PredictedVehicle.MaximumPredictionSteps` is restored to **18 commands / 300 ms**, as on current `main` (`82b6dcc38d497fb355bbd8a11095bf7e871506cd`). The 24-command driver regression and its sole-purpose Core test/doc changes were removed. The final branch retains the validation work: observed delivered-stale rejection versus controlled loss, repeated native impairment profiles, the ten-minute soak fixture, the EOS Active-state fixture correction, fifty Oil/Nitro/resource-bearing rematches, verification routing, and their evidence. There is no production movement, wire-protocol, queue, or publication change. Story version remains **0.1.74**, with Windows export version **0.1.74.0**.

**Final result: validation is incomplete / FAIL.** The restored production policy fails the existing movement quality gates under eight-player combined impairment. The 8.2/10 PASS recorded on the temporary 24-step policy is historical and does not apply to this final branch.

## Current-branch commands and results

| Command | Actual result |
| --- | --- |
| `./check.ps1` | First sandboxed attempt stopped at package restore; rerun with restore access **PASS**. Debug production and Release solution builds had zero warnings/errors; **896 Core** and **401 non-native transport** tests passed. Version, workflow and media checks passed. |
| `./tools/check-version.ps1` | **PASS**: current-main ancestry and 0.1.74 / 0.1.74.0 metadata. |
| `dotnet test code/TransportTests/Trackstorm.Transport.Tests.csproj -c Release --no-build --filter 'TestCategory=Native' --logger 'console;verbosity=normal'` | **14/15 PASS; one FAIL** in the eight-player 30 ms outbound delay / 10 ms jitter / 2% loss / 10% reordering profile. It recorded one prediction-limited tick outside the stale-probe window (tick 681, pending 19, snapshot age 0.1508 s); the zero-hold assertion expected 0. [Full log](../../ts-121/round-2/scope-correction-native.log). |
| `./check-network-soak.ps1 -GodotPath <Godot 4.7.2 .NET console executable> -NoBuild` | **FAIL overall** because its ten-minute movement case failed; it continued to run the independent 50-cycle post-match case, which passed. [Movement/command log](../../ts-121/round-2/scope-correction-soak.log), [post-match runtime](../../ts-121/round-2/scope-correction-rematch.log), [2,024 assertions](../../ts-121/round-2/scope-correction-rematch-evidence.txt). |

The ordinary native suite directly separated loss from stale rejection. Its eight-player controlled-loss case used 30 ms outbound delay, 10 ms jitter and 10% reordering (up to 25 ms extra delay), with native loss disabled. It deliberately withheld **28 ordinary received snapshots** (four per client), observed **420 delivered stale probes** (60 per client) rejected, and passed without prediction holds. The other native profiles exercised baseline, approximately 50–60 ms and 100 ms RTT with jitter, and 2% loss. A missing unreliable probe was not counted as a delivered rejection.

### Ten-minute eight-player movement soak

The explicit `TenMinuteEightPlayerImpairmentSoak` inside the combined command ran **36,000 ticks / 599.98 sampled seconds** over eight local UDP instances, with 30 ms outbound delay each way, 10 ms jitter, 2% native loss, 10% reordering and up to 25 ms extra delay. It had no additional controlled receive loss.

- **FAIL:** 72 prediction-limited client ticks across 252,000 sampled client ticks: 16 in the stale-probe window plus 0.5-second drain, 56 elsewhere. This is an aggregate tick count, not a measured longest continuous freeze.
- **FAIL:** worst steady correction **0.8620 m** against the unchanged **0.5 m** limit. Startup maximum was 0.2298 m; steady p99 printed 0.0000 m. The test reported both assertion failures.
- **Observed transport evidence:** 80,512 accepted snapshots and **20,163 individually delivered stale probes** rejected without authority replacement. Native 2% loss was configured but individual native drops were not counted as stale rejection. Immediate local prediction, eight-authority-vehicle checks and the in-loop failure/history checks ran; the failed quality gates prevent a PASS claim.
- **Backlog/resource samples:** minute pending-input maxima were 19–23, final maximum 13; snapshot history remained at or below 20. First-minute snapshot-age peak was 0.3853 s (including startup), later peaks 0.1507–0.1994 s. No monotonic pending-input or age backlog was observed. Private bytes sampled 35,569,664–39,751,680; handles 422–427. These process-wide samples include NUnit and correction-sample allocations and do not attribute a networking leak.

This rerun is the final-policy result. An earlier Round-2 **18-step diagnostic** also failed (60 held ticks, 0.6898 m maximum steady correction) in [diagnostic-soak.log](../../ts-121/round-2/diagnostic-soak.log). The temporary **24-step** ten-minute run passed with zero holds and 0.2240 m maximum steady correction in [soak-budget.log](../../ts-121/round-2/soak-budget.log); it is retained only as experiment evidence and cannot certify the reverted production branch.

### Fifty impaired native match generations

The post-match component invoked `./check-post-match.ps1 -GodotPath <Godot 4.7.2 .NET console executable> -NoBuild -Impaired -Cycles 50` and **passed 2,024 assertions**: fifty Finished boundaries, forty-nine immediate rematches, then destination/cleanup checks. Each native generation used 30 ms outbound delay, 10 ms jitter, 2% loss, 10% reordering and up to 25 ms extra delay. The fixture granted Oil and Nitro through item authority, deployed Oil from the remote client, held/released Nitro with partial charge, killed and respawned the Oil owner during Active, checked Oil persistence until Finished, then checked authoritative cleanup and fresh-generation resource reset. Grants bypass map pickups; prior Circus statistics were seeded before the final lethal interval. These are fixture seams, not fifty full-length human games.

All fifty samples had zero orphan nodes, 20 retained snapshots and 7–13 pending inputs. After the first warm-up cycle, native counts stayed at **6,380 nodes / 12,682 objects**. Host/client event journals filled to their **1,024-entry cap**. Handles ranged 460–475; managed samples 7,169,872–8,678,280 bytes; private bytes 520,507,392–572,104,704. A later managed-memory step and process-wide private growth were not attributed to an owner, so these observations support bounded fixture state rather than proving no leak.

## Other Round-2 evidence and its limit

Before scope correction, `./check.ps1`, 15 ordinary native tests, the ten-minute soak, fifty impaired rematches, and [26 runtime commands](../../ts-121/round-2/final-runtime-status.json) passed on the temporary 24-step policy. That matrix included lobby/load/Countdown, Circus and item flows, active join, three resumes including 125 seconds offline, native host migration and process loss, standings, and EOS checks. The [runtime matrix log](../../ts-121/round-2/runtime-matrix.log), [vehicle metrics](../../ts-121/round-2/vehicle-metrics-summary.json), [normal-entry metrics](../../ts-121/round-2/entry-metrics-summary.json), and individual logs remain actual historical evidence. They were **not rerun in full after the 18-step restoration** and are not a final-policy 26/26 PASS. The current-branch commands above are the applicable rerun evidence.

The temporary-policy run also completed nine authenticated EOS login/lobby/P2P/Ready/Start/Active/Return cycles from one local device identity ([log](../../ts-121/round-2/final-eos-authenticated.log)). It did not exchange remote gameplay packets. The EOS fixture correction remains on this branch, but authenticated EOS was not rerun after the prediction rollback.

## Limitations and unverified areas

- **VERIFIED on the corrected branch:** deterministic build/tests, controlled loss versus delivered-stale rejection, the native short-profile failure, the full ten-minute movement failure and its bounded backlog samples, plus fifty impaired resource-bearing rematches and cleanup.
- **UNVERIFIED on the corrected branch:** the full prior 26-command runtime matrix, production-entry rendered profiles, authenticated EOS lifecycle, and continuous human driving after restoring 18 steps. Their earlier 24-step results remain historical.
- **UNVERIFIED externally:** separate physical PCs/networks, distinct authenticated EOS identities, WAN/NAT/relay paths, exported-build play, remote reconnect/migration and multi-PC lease behavior. No separate-PC tester or endpoint was available. A continuous all-feature eight-player EOS game, multi-hour soak, native queue-byte peaks, per-owner heap attribution, physical controls and audio were not measured.
- **DEFERRED BY USER:** gameplay voting and paused-admission/electorate behavior, as recorded in Jira. Host kick and migration controls are not voting evidence.

## Astra Runtime Critique — Story Round 2, corrected final state

**Overall Score: 6.8 / 10. Quality Assessment: FAIL (<8.0).** This supersedes the 8.2/10 PASS for the removed 24-step policy. It judges the exercised runtime/operational outcome, not source quality or test volume.

| Material runtime category | Score | Observed basis |
| --- | ---: | --- |
| Runtime Functionality | 7.8 | Controlled loss, delivered-stale rejection and fifty resource-bearing rematches worked; combined movement quality failed. |
| Multiplayer / Networking Experience | 6.4 | One hold in the short eight-player profile; 72 held ticks and 0.8620 m steady correction in the ten-minute run. |
| Runtime Stability | 7.9 | Native resource and journal counts stayed bounded in fifty generations; sustained driver continued processing snapshots. |
| Observed Performance / Responsiveness | 6.5 | Brief local prediction holds and an above-gate correction remain under representative impairment. |
| Runtime Integration | 7.8 | Local repeated match state cleanup passed; broad matrix and remote EOS were not rerun on the final policy. |

**What Was Exercised:** VERIFIED — real local UDP impairment, controlled snapshot withholding, exact delivered-stale rejection, ten-minute eight-player scripted driving, and fifty native Oil/Nitro/death/respawn/Finished/rematch cycles. INFERRED — bounded sampled histories, journals and native counts support limited-duration cleanup. UNVERIFIED — the final-policy full runtime matrix, separate-PC EOS/WAN, continuous subjective feel, and owner-specific heap/queue measurements.

**Runtime / Operational Limitations:** flat-ground movement observations, authority grants, seeded scoring and trusted local identity/lease seams do not substitute for full remote production play. Process resource totals cannot isolate a networking leak. Voting is deferred, not assumed to work.

**Problem:** representative eight-player impairment still exhausts the 18-step prediction horizon and produces an above-gate correction. **Evidence:** short-profile hold; ten-minute 72 held ticks (56 outside the probe window) and 0.8620 m maximum steady correction. **Severity:** High. **Impact:** intermittent control discontinuity and visible correction may affect multiplayer driving. **Suggested Improvement:** investigate acknowledgement lag and movement correction in the owning prediction/replication Story, make an explicitly approved production correction there, then rerun TS-121 native and entry/soak validation. **Scope:** production repair is an **Out-of-Scope Recommendation** for TS-121; validation/retest is in scope. **Corrective Work Type:** New corrective owning-system work, not created by this review.

**Recommended Next Round:** after the owning-system correction is approved and delivered, repeat the short combined-impairment profile, ten-minute soak, normal-entry rendered profiles and affected runtime checks. Separate-PC EOS remains conditional on physical test availability. This critique does not authorize an additional implementation round.
