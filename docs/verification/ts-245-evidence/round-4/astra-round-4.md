# Astra Runtime Critique — Story Round 4

**TS-245 — Overall Score: 7.8 / 10**

**Quality Assessment: FAIL (<8.0)**

**Reviewed:** 2026-10-12, existing `ts-245-pg` branch, after the final implementation verification. This additional round is explicitly authorized by the user's supplied request following Round 3's 7.7/10 and human feedback. That task-specific authorization overrides the older three-round cap; this review changes no policy.

The restored rack position, horizontal tank, connected hoses, larger spring response and mouth-before-retraction sequence make the weapon a more coherent mechanical character. The tank's diamond paint, brass hardware, gauge and altered label are present in the actual renderer. The close rearward burst and retained timer-only coverage work in the exercised scenarios. Fresh native deployment, firing, airborne deposition, active-pour admission and replacement completed successfully.

The central fluid effect remains below the highly polished threshold. It reads predominantly as a broad, smooth, twisted solid strand, with pointed sheet-like ends and sparse peripheral droplets. Landing splashes expose angular radial wedges, while settled trails retain a broad scalloped silhouette and conspicuous mottled highlights. These are visible in both the retained final run and the independent fresh run. The earlier large folded shutdown connection was not reproduced in the inspected sequences, but removing that defect has not yet made the burst convincingly turbulent oily vomit. The unresolved three-peer authority-recovery failure also limits confidence in integrated runtime robustness.

The exact score is an experiential judgment weighted toward the requested weapon presentation and its exercised integration, not an arithmetic mean, an award for effort, or a target rounded up to pass. This critique is not a static engineering review and does not establish human acceptance.

## Category Scores

| Material category | Score | Observed basis and limits |
| --- | ---: | --- |
| Runtime Functionality | 8.6 | Fresh native deployment, sixty-tick/forty-parcel firing, surface impacts and replacement complete. Retained full-run evidence supports repeat contacts, exact timer expiry, visual cleanup and twelve subsequent uses. |
| Physics | 8.2 | Fresh banked turning, airborne emission/landing and vertical first-hit deposition complete. Spring motion stays bounded and returns inside the box. No exhaustive clearance proof or driven ramp crest. |
| Multiplayer / Networking Experience | 7.3 | Fresh active-pour admission and retained impaired four-peer state agreement pass; reconnect and two-peer migration pass in retained logs. Three-peer migration failed twice and remains unresolved. Remote visual smoothness was not observed. |
| Visual Quality | 7.8 | Tank, gauge, hoses and rack arrangement form a coherent circus mechanism; label details are recognizable in close-up. Fluid silhouettes, splash facets and broad deposit highlights remain synthetic. |
| Animation / Motion | 8.1 | Expressive fore/aft and vertical rebound, visible hose following, orderly mouth closure/head return/lid/rack sequence and replacement. No folded V-shaped shutdown connection reproduced. Sparse captures limit fine timing judgment. |
| VFX / Feedback | 7.3 | A forceful rearward burst and substantial persistent coverage are unmistakable. Fine spray is present, but the dominant smooth body and angular landing crowns weaken the intended liquid impression. |
| Game Feel / Juice | 7.8 | The spring and mouth give anticipation and recovery, and the denser brief discharge has visual weight. This evaluates visible action/recovery only; interactive satisfaction and audio are unverified. |
| Runtime Stability | 7.9 | Fresh rendered fixture exits successfully without accepted runtime errors/warnings. Retained repeated use and cleanup pass. Recovery failures prevent an unqualified robustness assessment. |
| Runtime Integration | 8.2 | The original rack, tank, hoses, head, mouth, lid and replacement sequence operate together. Retained gameplay removal precedes bounded visual residue cleanup. Multi-peer recovery remains incomplete. |

Gameplay/fun, physical controls, overall vehicle handling feel, production chase camera and audio are not independently scored. Observed performance and developer UI evidence are reported below with their limits. No code-quality, architecture, test-design, documentation or delivery score is assigned.

## What Was Exercised

### Scope and visual references

- **VERIFIED — reference inspection:** read the user's full additional-round request, repository routing, workflow verification/review boundary, `docs/critique.md` in full, Round 4 verification/evidence documentation, previous Round 3 critique, relevant Held Items/Oil documentation and the complete saved updated Jira Story snapshot. The snapshot's update is `2026-10-12T21:45:03.575-0400`; it has no comments or required subtasks. Later approvals supersede the historical two-second/contact-quota/rear-carriage wording. TS-247 remains excluded.
- **VERIFIED — concept inspection:** viewed all four supplied concepts: closed, deployed, firing and the new **Laughing oil 2.png** tank reference. The review respects the user-approved box/jester proportions and appearance; it does not demand concept photorealism from the current renderer.

### Fresh independent native execution

Executed with the native graphics, local UDP, certificate and user-cache access required by Godot:

```powershell
./check-oil-jack.ps1 -GodotPath '.godot/godot-ci/4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Visual -VisualOnly
```

- **VERIFIED — fresh execution:** exit code **0**, Godot .NET 4.7.2 Compatibility renderer on NVIDIA GTX 1070; host lifecycle approximately **94.36 seconds**. The [reviewer's log](astra-native-runtime.log) records native acceleration/braking/reacceleration, one-second firing with forty parcels in one hazard, banked steering/throttle/handbrake, airborne activation and landing, active-pour admission of a third local peer, vertical first-hit deposition and Oil-to-Wrench replacement. The 23-degree asphalt ramp check is a supported query, not a driven crest.
- **VERIFIED — fresh measured behavior:** turning retains **331 supported ticks**, with **4.509 m/s** peak lateral slip. Rendered spring per-axis peaks are fore/aft **0.448 m**, lateral **0.429 m**, vertical **0.449 m**; full nesting before replacement passes. All five firing readbacks report approximately **0.007 m** from the actual stream mesh's first world-space vertex to the animated mouth.
- **VERIFIED — fresh sequence observation:** inspected 32 chronologically selected native captures across deployment/inertia, burst/return, airborne motion/landing and replacement. Viewed fresh [straight firing](astra-native-firing.png), [airborne firing](astra-native-airborne.png), [closer firing](astra-native-close-firing.png) and [impact/shutdown](astra-native-impact.png) at full resolution. Images show the hoses following the head and the mechanism returning coherently. The fresh trace records mouth opening, subsequent closure while extension is still full, then reduced extension, followed by rack descent; sampled hose endpoint error is zero. These assertions and sparse images do not prove all mesh-pair clearances throughout motion.
- This was **automated native exercise and direct capture observation**, not hands-on driving or a human playtest. The native window's wall-clock animation was not judged as smooth from simulation-timed recordings.
- `-VisualOnly` stops after presentation/replacement. It does **not** freshly test the exact thirty-second deadline, final residue removal or twelve additional uses. Earlier late-stage files still present in the ignored runtime directory were excluded from fresh-run claims. Primary retained full-run evidence was not overwritten. Reviewer contact sheets and fresh trace remain ignored under `.godot/ts-245-round4/astra-review/` and `.godot/ts-245-round4/runtime/`.

### Retained final evidence

- **VERIFIED — direct image observation:** viewed final ready/tank/jester, straight/turning/airborne/wall/close firing, impact, mouth-closing, all four deposit views and expired-residue images. The two explicitly labeled earlier close-ups were inspected only for the unchanged model/material appearance and lettering. A filename such as `closed.png` is not treated as an exact pose/tick assertion; chronological frames establish the actual closed/deployed transitions.
- **VERIFIED — chronological image observation:** inspected sixteen distributed frames from each of [deployment/straight](deployment-straight.webp), [turning](turning.webp), [airborne](airborne.webp) and [replacement](replacement.webp), 64 retained sequence frames total. The [manifest](recordings.json) supplies source frame numbers and hashes. These show pose/shape progression; their 60 Hz simulation-frame playback timing is not measured wall-clock performance.
- **VERIFIED — evidence inspection; INFERRED — recorded lifecycle outcomes:** the [full native log](runtime.log), [summary](runtime-summary.json) and [trace](trace.json) support four local UDP peers under **30 ms delay, 5 ms jitter and 2% loss**, active/late admission, twelve subsequent uses, and exact gameplay removal at **tick 3617** after activation **1817**. Trace samples show zero gameplay hazards with one visual residue from tick 3620 through 3730, then zero residues at 3740. The [residue image](expired-fading.png) visibly breaks the oil into holes. Together these establish a recorded bounded dissipation transition; its complete real-time aesthetic was not independently replayed in this short run.
- **VERIFIED — evidence inspection; INFERRED — recorded gameplay/recovery outcomes:** [contacts](contacts.log) retains repeated owner/rival entries, continuing single-hazard existence, agreed scoring and four-peer retention of 1,500 exact hazards. [Twenty handling trials](handling-summary.json) cover tire/axle/side contact, speeds, cornering/countersteering, braking/handbrake, landing and continuous-overlap recovery. [Rack](rack.log) records 482 handoff checks; [reconnect](reconnect.log) records three resynchronizations, including 125 simulation seconds offline; [two-peer migration](migration-two-peers.log) completes authority replacement and retained item recovery. These checks were inspected, not rerun by this reviewer.
- **VERIFIED — evidence inspection; INFERRED — developer operation:** [Oil-only developer options](developer-options-oil.log) passes 112 write assertions covering eighteen synchronized controls and thirteen fresh-process read assertions. The unrestricted suite remains failed on its aiming-limit mutation; no overall developer-suite pass is claimed.

## Material Findings and Recommendations

### 1. The dominant discharge still looks like a stretched solid strand

- **Problem:** multiple internal branches largely merge perceptually into one broad smooth black body. Airborne ends form narrow pointed sheets/fins, and peripheral droplets are too sparse to carry the requested rich, turbulent vomit character at vehicle-scale viewing distance.
- **Evidence:** fresh [close firing](astra-native-close-firing.png) and [airborne firing](astra-native-airborne.png), retained [stationary firing](stage-5-firing.png), and chronological straight/airborne frames. The fresh moving stream is connected to the mouth, so this finding does not rely on the earlier readback mismatch.
- **Severity:** Medium.
- **Impact:** the main action reads as extruded material or a hose-like strip more readily than thick flowing oil; the expressly requested firing concept remains only partly realized.
- **Suggested Improvement:** if another round is authorized, make visibly separated, irregular substreams and short-lived liquid breakup carry more of the silhouette. Reduce long smooth shared spans and sharp sheet-like tails, and tune a bounded spray layer for visibility at ordinary gameplay scale while preserving one second, forty authoritative parcels and existing coverage.
- **Scope:** In Scope — TS-245 presentation.
- **Corrective Work Type:** Existing Task — corrections within the existing Story; no new issue is required or created by this critique.

### 2. Splash and settled-oil presentation remains geometric

- **Problem:** impact crowns resemble stiff angular radial wedges; settled trails form broad connected scallops, with mottled bright areas that can resemble a patterned surface rather than restrained wet reflection.
- **Evidence:** fresh [impact](astra-native-impact.png) and [straight firing](astra-native-firing.png); retained [straight deposits](straight-deposits.png), [curved deposits](curved-deposits.png) and [airborne deposits](airborne-deposits.png). This is a rendered appearance finding, not a claim of incorrect tire coverage.
- **Severity:** Medium.
- **Impact:** splashes and persistent hazards are clearly visible but do not consistently convey a fluid striking and wetting the ground, reducing the finish of the central weapon effect.
- **Suggested Improvement:** if authorized, soften and vary splash silhouettes and their collapse, avoid large repeated radial facets, and tune surface highlight scale/contrast from normal vehicle distance. Preserve the shared gameplay edge and supported-surface placement when refining deposits.
- **Scope:** In Scope — TS-245 fluid presentation.
- **Corrective Work Type:** Existing Task — existing Story presentation work.

### 3. Three-peer recovery remains an unresolved operational gap

- **Problem:** the current three-peer migration scenario did not reach successful recovery; both retained attempts failed while establishing replacement-host authority fencing.
- **Evidence:** [first failure](migration-first-failure.log) and [isolated retry](migration.log), stage 61 timeout. The recorded failure occurs before Oil recovery is seeded. The [two-peer pass](migration-two-peers.log) is useful but does not establish the missing three-peer outcome.
- **Severity:** High for the affected recovery scenario; attribution to the Oil changes is unestablished.
- **Impact:** an exercised multi-peer host-loss path ends in a leave/rejoin instruction instead of continuity. Full integrated multiplayer recovery cannot be certified from this evidence.
- **Suggested Improvement:** under future human authorization, reproduce and isolate the authority-fence failure, establish whether it is an Oil regression, and complete the three-peer Oil recovery observation. Do not infer a cause or make an unrelated authority-system change merely from this timeout.
- **Scope:** In Scope for TS-245 integration verification and regression attribution. An independently established unrelated networking redesign would require separate scope approval.
- **Corrective Work Type:** Existing Task — complete investigation/verification within this Story first; no corrective Jira issue or implementation is authorized here.

## Runtime / Operational Limitations

Only the host viewport was rendered. Peer consistency assertions do not establish perceived remote hose, mouth or spring continuity. Cameras use deliberate observation angles/cuts and deposit overhead views, so production chase-camera readability is not fully evaluated. The tank label and decorative gauge are identifiable in close-ups, but their small detail is not reliably legible at the wider inspection distance. The gauge has no observed dynamic capacity behavior and is not scored as an ammunition display.

The new spring is visibly expressive and bounded, but a peak near 0.45 m does not quantify subjective looseness by itself. Hose endpoint agreement and successful nesting do not prove absence of every interior intersection or excessive stretch during all tuning extremes. The box hides much of the final internal fold. Mouth timing is supported by fresh rendered samples and trace; its fine transition is short and difficult to resolve in wide sparse captures.

The historical Round 3 stage-6 timeout was an admission wait, not an established animation failure. Fresh and retained Round 4 welcomes complete, but the old cause remains unproven. No production networking fix or proven pre-existing migration defect is claimed. The earlier developer-options admission failures were fixture roster assumptions; the subsequent unrelated aiming-limit failure remains recorded separately from the passing Oil-only check.

**Observed performance:** retained repeated use reports median **42.064 ms**, p95 **87.409 ms**, maximum **134.925 ms**, with at most six hazards; observed maxima include forty in-flight parcels and 59 additional surface pieces. These intervals are not evidence of 60 FPS. Four scenes, forced draws, capture work and other applications prevent attribution to single-client Oil GPU cost or a controlled performance score. End-state cleanup is supported, but the 1,500-hazard state checks do not establish rendered capacity. The fresh capture run is likewise unsuitable for a wall-clock smoothness benchmark.

**UNVERIFIED:** hands-on keyboard/controller feel, enjoyment/competitive balance, audio listening, all developer-tuning extremes, exhaustive swept-mesh hose clearance, a driven ramp crest, authenticated EOS/WAN/multi-device experience, release-export performance, other hardware, prolonged soak and human acceptance. TS-247 balance/final integration and independent engineering review are outside this critique.

## Recommended Next Round and STOP

If the human explicitly authorizes further work, prioritize resolving the recovery uncertainty and refining the dominant discharge/splash silhouette, then reassess deposited sheen from normal driving distance. Preserve the approved model, original rack placement, loose spring, tank/hoses, mouth sequence, one-second dose and timer-only hazard. Controlled single-client observation would improve performance and remote-motion confidence. These recommendations do not authorize another round or expand TS-245 into unrelated networking work.

**STOP — Story Round 4 is complete at 7.8 / 10, FAIL.** No recommendation was implemented. This reviewer added only the critique and uniquely named native evidence; production code, tests, assets, policy, Jira and PR administration were not changed. Human review and explicit direction are required before any additional polish or critique round. The user's separately requested commit/push handoff remains for the parent agent.
