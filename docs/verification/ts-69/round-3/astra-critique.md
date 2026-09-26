# Astra Runtime Critique — Story Round 3

TS-69, `ts-69-jg`, 2026-09-26. Independent review of the integrated working tree after the authorized startup-stall correction and completed Round 3 verification. This evaluates runtime and operational quality; it is not the independent engineering review.

**Overall Score: 7.9 / 10**

**Quality Assessment: FAIL (<8.0)**

The corrected startup behavior is demonstrably better: an independently executed rendered production-entry case recovered from a measured 750.5783 ms client stall with a 0.886377215385437 m maximum correction, no hard snap and no steady prediction hold. Ordinary driving, loading, authoritative gameplay and local recovery have substantial positive evidence. However, the completed implementation still has a failed eight-player ordinary-scheduler sample with a 1.354087471961975 m steady correction during damage, without a frame stall or lifecycle transition. My passing eight-player trial does not resolve that failure. This remaining in-scope multiplayer quality problem, together with unestablished authenticated online operation and limited continuous experiential observation, prevents a highly polished assessment.

The score is an exact overall judgment, not an average of category scores, a test pass percentage, or a rounded value chosen to cross the gate. Improvement over an earlier round does not itself establish acceptance.

## Category scores

| Material runtime category | Score / 10 | Basis |
| --- | ---: | --- |
| Runtime Functionality | 8.4 | Both independently launched scenarios completed with roster and acknowledgement progress; retained native evidence covers broad session/gameplay recovery. Authenticated EOS operation is not established. |
| Controls / Responsiveness | 8.3 | Immediate predicted driving, drift and handbrake exercised; no steady hold in my two-player stall case, five held ticks across seven clients in my impaired eight-player trial. Scripted input does not establish human-perceived feel. |
| Multiplayer / Networking Experience | 7.7 | Strong ordinary two-player measurements and improved stalled entry; an unresolved greater-than-one-metre steady correction remains in the required eight-player scenario. |
| Physics / Authoritative Interaction | 7.8 | Native contact and damage remain functional and replicated, but contact-associated reconciliation still has substantial tails. A dedicated contact gate allowing its 1.2049 m result does not satisfy the ordinary-driving gate. |
| Observed Performance | 8.0 | My eight-player clients had frame maxima of 39.202–53.322 ms; rendered frame p99 was 24.169 ms. The deliberate stall recovered successfully. The historical unexplained 751.334 ms frame remains an unresolved performance limitation. |
| Runtime Integration | 8.4 | Production entry, Countdown/GO, native presentation and scripted camera checks worked directly. Retained verification supports reconnect, host migration, combat/OOB, settings and post-match integration. Real-service recovery remains unverified. |

No separate fun, audio, art, or continuous vehicle-feel score is assigned: this review did not meaningfully observe those categories.

## What was exercised

### VERIFIED — independent execution and observation in this critique

Executed the following sequentially with the completed Debug build and Godot .NET 4.7.2, without changing code, tests, thresholds or configuration:

```powershell
./check-network-vehicles.ps1 -GodotPath <Godot-4.7.2-console> -NoBuild -Visual -Latency 30 -ApplicationEntry -StallBoundary driving -StallMilliseconds 750
./check-network-vehicles.ps1 -GodotPath <Godot-4.7.2-console> -NoBuild -Players 8 -Latency 30 -Jitter 10 -Loss 2
```

The exact executable was `C:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`. Both strict harness invocations passed. All process JSON, logs and rendered captures were copied into new directories; no earlier trial was overwritten and neither case was retried to seek a pass.

| Independent scenario | Startup maximum (m) | Steady p99 (m) | Steady maximum (m) | Hard snaps | Steady held ticks | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Rendered production-entry driving stall | 0.886377215385437 | 0.016858354210853577 | 0.025416724383831024 | 0 | 0 | PASS |
| Eight players, 30 ms delay / 10 ms jitter / 2% loss | 0.2180991917848587 | 0.20290444791316986 | 0.5969013571739197 | 0 | 5 | PASS |

Eight-player maxima and p99 are the worst client; held ticks are summed across all seven clients. Per-client measurements are in [metrics.csv](astra-eight-impaired/metrics.csv), with the complete [eight-player run log](astra-eight-impaired/run.log) and [process artifacts](astra-eight-impaired/897303b723c44136af34eff5e2b53c8b/).

The [rendered client JSON](astra-entry-stall/cb95ed8c0bf74cdcad17d05a650cd27e/player-1.json) confirms entry ready/released/participating at the stall, a frozen predicted tick of 315, and unchanged local ack 303 during the sleep. Host trace samples in the blocked interval advance ticks 311–357 and the remote vehicle's ack progresses 305–309 before remaining at 309. Its ending forward velocity is 1.2434322 m/s. The first large correction installs authority tick 354 / ack 309 with zero pending inputs, life 1, Alive, full HP and no damage/effect. This directly verifies continued authority/coasting and bounded recovery under the requested startup condition. The frame maximum is 768.142 ms, including the intentional sleep; it is not evidence of a new spontaneous stall.

The rendered trial records mean RTT 60.9385416666667 ms, mean local buffer delay 44.52137935446494 ms, and mean received-timeline delay 63.28990695982977 ms. These presentation measurements exclude unknown one-way transit. The eight-player RTT means range from 80.47083333333333 to 88.346875 ms. All seven report roster eight and final acknowledgements 947–951. Client 3 has a 0.43463265895843506 m steady maximum, followed by recorded vehicle-collision damage attributed to vehicle 2; client 8's 0.5969013571739197 m event occurs with full HP and no recorded damage. Passing results therefore still contain measurable corrections; they are not uniformly zero-error motion.

I inspected the generated [chase-camera PNG](astra-entry-stall/cb95ed8c0bf74cdcad17d05a650cd27e/player-1.camera-chase.png): both native vehicles, the host tag/health bar, terrain and chase view render coherently at the captured endpoint. Scripted mouse/controller camera checks passed. The PNG was captured after measured driving and does not show the correction's continuous visual appearance or prove smooth motion during the stall.

### Retained runtime evidence — reviewed, not independently rerun in this critique

The [Round 3 report](../../ts-69.md#human-authorized-round-3--controlled-startup-stall-correction), [comparison.csv](comparison.csv), [runtime-status.txt](runtime-status.txt) and raw runtime logs supply the broader integrated verification. This is indirect evidence for my judgment, with attribution to the implementation verification run:

- Near-zero delay, approximately 50/107 ms measured RTT, 2% loss, ordinary rendered driving and production-entry impairment passed; the listed ordinary two-player steady corrections are zero. Earlier corrected production-entry stall maxima were 0.8440535068511963, 0.9073919057846069 and 0.9073919057846069 m; standalone was 0.853438675403595 m.
- Final before-ready/after-ready rendered stalls passed at 0.06817766/0.16327107 m. The recorded host stays at tick zero before release and advances through Countdown while the blocked released client stays at tick one. This supports the entry fence independently of the driving-stall behavior.
- Native transport/UDP delay, jitter, loss, reorder, stale rejection and convergence passed. Local lobby, combat/items/Nitro/pickups, eight-peer death/OOB, match scoring, terrain/water/boundary, menu/post-match and shared DevTools configuration/import passed.
- Three reconnects include 125 seconds offline. Two/three-player migration and independent-process host termination passed using the documented trusted local identity/retirement seams. These are local integration successes, not real-service fencing or EOS claims.
- Dedicated impaired scrape/crash/head-on verification passed its contact-specific gate with maximum 1.2049 m and exact host/client HP 907.2932. I reviewed its raw log. This validates contact functionality/settled damage agreement, not consistently sub-metre ordinary reconciliation.
- Final builds and deterministic suites passed (896 Core / 383 non-native; extended native transport 395). Those establish the verification prerequisite and are not used as a substitute for experiential quality.

### Failed and adverse evidence retained in the judgment

- **Current corrected implementation:** [eight-2/player-7.json](eight-2/player-7.json) fails at 1.354087471961975 m steady error. Its event at 10.983333333333459 driving seconds records authority tick 672, damage tick 670, HP 945.5863, ack 650, nine pending inputs and a 16.089 ms frame. There are no recorded frame stalls; life stays 1/Alive. Exact damage attribution was not captured in this earlier sample. It remains FAIL and is neither an expected teleport nor proof of a particular collision counterpart. Forced teardown warnings follow the failure; they are not silently discarded or treated as an independently demonstrated gameplay crash.
- **Pre-correction Round 3 controls:** standalone 1.95685875415802 m and production entry 1.785560965538025 m failed the unchanged startup gate. They support the diagnosed stale-control divergence, but remain failures of the old behavior rather than results of the corrected build.
- **Earlier adverse results:** Round 2's 1.421 m correction following a 751.334 ms frame remains real and unexplained at the OS/GPU level. Its explicit 64-step overload reached 4.930 m and two startup snaps; that non-production scheduler cannot be presented as ordinary runtime behavior. Round 1's convergence snaps, 58 steady held ticks/one steady snap on an eight-player client, 0.838 m rendered steady correction and 204.3 ms frame remain historical evidence in the parent report, not erased by later fixture corrections.
- **Authenticated EOS/P2P:** the current [raw log](runtime-eos-p2p.log) fails at cycle 1 with a generic credential-safe diagnostic. Its exact cause is unknown. Native SDK lifecycle success does not change that outcome. Earlier authentication failures remain recorded.
- Iteration/setup failures (including the enum assertion mismatch, sandbox access failures and previously repaired fixture timing/assertions) remain documented in the parent report. They are not successful runtime observations or evidence that those fixture repairs improved production behavior.

## Runtime / operational limitations

**INFERRED:** shortening stale held controls explains much of the observed low-speed stalled-entry improvement; a greater-than-one-metre live correction can disrupt vehicle/contact presentation. Continuous human observation did not establish the exact perceived severity of the failed event. No guarantee is made for arbitrary speeds, stall lengths or long burst loss, and earlier neutralization can interrupt controls sooner during delivery outages.

**UNVERIFIED:** successful authenticated EOS connectivity; separate-PC/WAN/NAT/relay quality; real lease-service migration/reconnect; exported release play; continuous human-driven feel, physical controller interaction and audio listening; cross-platform collision agreement; extended soak; synchronized end-to-end presentation latency. No second PC was available in the supplied context. No credential/deployment changes were attempted.

All network trials here use real local processes/sockets on one shared Windows PC with probabilistic impairment and adaptive scripted trajectories. They are finite, unseeded trials, not paired identical paths or representative Internet reliability statistics. My eight-player case was headless. Screenshots and synthetic camera inputs cannot establish continuous visual polish. The old baseline lacks actual clamped presentation-age measurement, so no percentage improvement in true end-to-end latency is claimed.

## Meaningful recommendation

**Problem:** eight-player steady synchronization is not yet consistently within its ordinary quality gate around native interaction.

- **Evidence:** the retained 1.354087471961975 m correction occurs during ongoing life/damage, with a normal frame and no lifecycle reset. Dedicated contact verification also records a 1.2049 m tail under its separate gate. My passing independent trial still records 0.4346/0.5969 m tails and cannot negate the failed trial.
- **Severity:** Medium.
- **Impact:** occasional substantial reconciliation can move a player's vehicle away from its locally predicted contact/steering outcome during combat, reducing confidence and polish even when damage ultimately agrees and no hard snap is counted.
- **Suggested Improvement:** if the human authorizes corrective work, reproduce the eight-player interaction with retained authoritative and client collision observations, damage attribution, peer poses and input/ack timing around the event. Use that evidence to correct the bounded native contact/reconciliation divergence and verify both ordinary driving and impaired contacts, preserving all failures, authority, stale rejection, history bounds and the existing thresholds. Do not solve the result by enlarging smoothing tolerance or relabeling the event as lifecycle movement. The added attribution fields aid diagnosis but do not by themselves resolve this failure.
- **Scope:** In Scope.
- **Corrective Work Type:** Existing Task — TS-69. No corrective issue or implementation was created.

Authenticated separate-PC verification remains a material acceptance limitation. When the environment is available, follow the existing EOS acceptance path and retain both peers' logs; local UDP passes cannot supply that evidence. The generic failed authentication diagnostic does not justify guessing a credential or deployment fix.

## Recommended Next Round / human decision

**No fourth Story critique round exists under the current policy.** Round 3 is final. The recommendation above is information for the human's decision about acceptance, deferral or explicitly scoped further work; it is not authorization to implement it or start another Story critique. Any different review process would require an explicit policy decision.

Stop after presenting this critique. [The critique policy](../../../critique.md#mandatory-human-gate) requires: “After presenting any formal critique, STOP and wait for explicit human instruction.” The independent reviewer made no production/test/threshold changes, Jira issues, commits, pushes or merges.
