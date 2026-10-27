# Astra Runtime Critique — Story Round 5

**TS-245 — Overall Score: 7.9 / 10**

**Quality Assessment: FAIL (<8.0)**

Reviewed 2026-10-13 on the existing `ts-245-pg` branch after completed implementation verification. The explicit additional-round authorization supersedes the older three-round cap for this assignment. This is the integrated Story's experiential assessment, not a static engineering review or human acceptance.

The Round 5 corrections work in the independently exercised scenarios. The production grass-to-bank hard drift no longer leaves unsupported deposited pieces in the tested route, including the near-180-degree reversal and airborne case. The sight glass visibly drains and reaches empty, the brass connections are recognizable, and the approved character, loose spring, rack and return sequence remain coherent. These are meaningful improvements to the working weapon.

The central discharge still falls short of the highly polished threshold: its dominant silhouette is a broad smooth twisted strand, with pointed sheet-like ends during airborne firing. The landing splashes remain conspicuously angular, like rigid radial strips. These prior findings are visible again in fresh runtime captures and chronological frames. The score improves from Round 4's 7.8 because the supported deposition, meter integration and recovery evidence are stronger; it does not reach 8.0 merely because the new checks pass. This exact score is a judgment weighted toward the weapon's central fluid action, not an arithmetic average of categories or a score rounded to the requested target.

## Category Scores

| Material category | Score | Observed basis and limits |
| --- | ---: | --- |
| Runtime Functionality | 8.6 | Fresh deployment, forty-parcel one-second firing, valid deposits, meter progression/reset and replacement pass. Retained evidence covers timer expiry, repeat contacts and repeated-use cleanup. |
| Physics | 8.5 | Fresh production grass/bank reversal and airborne cases leave supported footprints; spring remains bounded. These routes do not prove every terrain transition or mesh clearance. |
| Multiplayer / Networking Experience | 8.1 | Fresh impaired UDP observation and accepted-clock meter checks pass; new retained two-/three-peer migration and reconnect pass. Remote viewport motion and reconnect during a burst remain unverified. |
| Visual Quality | 7.8 | Approved circus character, tank, brass hardware and readable close-up meter fit together. Coarse liquid silhouettes remain below the finish of the surrounding mechanism. Hose aging is subtle at the wide inspection distance. |
| Animation / Motion | 8.3 | Expressive spring, attached passive hose, mouth-before-retraction sequencing, bottom-anchored meter drain and orderly replacement are coherent in the inspected samples. |
| VFX / Feedback | 7.3 | Burst direction, persistent coverage and full/empty meter states are clear. Smooth strand dominance and angular impact crowns still weaken the liquid impression. |
| Game Feel / Juice | 7.8 | Visible anticipation, kickback and recovery give the weapon character; the fluid action remains synthetic. Audio and hands-on satisfaction are not established. |
| Runtime Stability | 8.6 | Both fresh native runs exit successfully without accepted ERROR/WARNING output; retained repeated-use and recovery runs pass. No long-soak claim. |
| Runtime Integration | 8.5 | Model, hoses, meter, mouth, rack, supported deposition and accepted state work together in the exercised lifecycle. Retained expiry removes gameplay before residue cleanup. |

Gameplay/fun, physical controls, overall driving feel, production chase camera, audio and isolated performance are not independently scored. No code-quality, architecture, test-design or delivery score is assigned.

## What Was Exercised

### Scope and references

- **VERIFIED — review inputs:** read repository routing, workflow and critique rules; relevant Persistent Oil documentation; the full user request; the complete saved Jira description/clarifications at update `2026-10-13T10:02:13.437-0400`, with no comments/subtasks and the TS-247 dependency; Round 4 critique; and Round 5 verification/evidence. Viewed the closed, deployed, firing and **Laughing oil 2.png** concepts. Later approved one-second/timer-only/original-rack/visual-meter decisions govern conflicting historical wording. The user's grass-entry, almost-180-degree hard-drift clarification governs the new route.
- The approved box, jester, total oil quantity and loose spring are treated as the baseline. This critique does not demand a redesign or photorealistic duplication of concept artwork. TS-247 balance work is excluded.

### Fresh independent native execution and observation

Executed sequentially with the pinned Godot .NET 4.7.2 console executable, native graphics/cache/certificate/local UDP access, and the existing built result:

```powershell
./check-oil-jack.ps1 -GodotPath '.godot/godot-ci/4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Visual -Impaired -VisualOnly
./check-oil-jack.ps1 -GodotPath '.godot/godot-ci/4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -ProductionBank -Visual -Impaired
```

- **VERIFIED — lifecycle run:** exit **0**, Compatibility renderer on NVIDIA GTX 1070, host lifecycle **87.20 seconds**. [Fresh log](astra-native-runtime.log) and [trace](astra-native-trace.json) record native acceleration/braking/reacceleration, banked steering/throttle/handbrake, airborne firing/landing, a third peer admitted during the airborne scenario, vertical first-hit deposition and Oil-to-Wrench replacement. The 23-degree ramp check is a support query, not a driven crest. Impairment is 30 ms delay, 5 ms jitter and 2% loss.
- **VERIFIED — lifecycle observations:** forty parcels in one hazard over sixty accepted ticks; four completed stream readbacks are 0.007 m from the animated mouth. Spring peaks are fore/aft **0.446 m**, lateral **0.428 m**, vertical **0.450 m**, with complete nesting before replacement. The trace shows mouth closure while the head is still fully extended, then retraction and rack descent; sampled hose endpoint error is zero. This is not exhaustive swept-mesh clearance proof.
- **VERIFIED — new meter:** directly viewed fresh [full](astra-native-meter-full.png), [partial](astra-native-meter-half.png) and [empty](astra-native-meter-empty.png) readbacks. The final saved partial readback is **0.268** (its filename says `half`, but it is not a claim of an exact 50% frame). The log records intermediate 0.368 and 0.268 samples, full 1.000 and empty 0.000. Chronological [replacement/meter frames](astra-sequence-replacement.png) show the top edge descending while the base remains anchored. The run verifies new-Oil reset and **89 observer samples** against each observer's accepted clock, with maximum publication lag **18 ticks** and delayed level difference **0.200**. This establishes accepted-state behavior, not visually simultaneous peer levels.
- **VERIFIED — production route:** exit **0**, approximately **49.18 seconds** host lifecycle. [Fresh bank log](astra-bank-runtime.log), [trace](astra-bank-trace.json) and [evidence](astra-bank-evidence.txt) record the production oval grass approach, bank crossing, full-lock drift and three burst timings including airborne firing. Peak speed **32.683 m/s**, lateral slip **15.230 m/s**, heading change **179.72 degrees**, rise **11.039 m**, 113 grass/793 asphalt observations, 1,019 supported and 86 airborne frames. All **172 pieces**, each tested at its center and all 64 rendered perimeter points, retained valid support; **zero unsupported pieces**. Forty parcels emitted per activation, 115 accepted impacts. Native cases reject car proxies, moving/animated/rigid geometry and destructible rock support, and exercise terrain, ledge shrink, water and missing support.
- **VERIFIED — image observation:** inspected 51 selected chronological fresh frames: 12 [deployment/straight](astra-sequence-deployment.png), 12 [airborne](astra-sequence-airborne.png), 12 [replacement/meter](astra-sequence-replacement.png), and 15 [bank route](astra-sequence-bank.png). Also viewed fresh full-resolution ready/tank/jester, straight/turning/airborne firing, impact, curved deposits, all three bank deposit captures and meter images. [Lifecycle source hashes](astra-sequence-sources.json) and [bank source hashes](astra-bank-sequence-sources.json) identify the original frames. Contact sheets only resize/arrange genuine native captures and add filenames; no generated imagery substitutes for runtime evidence.
- The fresh captures show grounded oil where the route leaves it, a coherent returning mechanism and no reproduced folded shutdown connection. The gray angular tire trails visible alongside the black bank deposits are not used as evidence of Oil geometry defects. The actual Oil impact crowns in the straight firing sequence supply the splash finding below.
- This was **automated native exercise plus direct capture observation**, not physical keyboard/controller play or real-time audiovisual observation. Simulation frame spacing and sampled images cannot prove wall-clock smoothness. `-VisualOnly` ends before timer-expiry and twelve-additional-use checks; those are retained evidence, not newly executed claims. Fresh files are retained with `astra-*` names and do not overwrite the primary evidence. The user's editor was not terminated.

### Retained final evidence inspected

- **VERIFIED — evidence inspection; INFERRED — recorded full-run outcomes:** [runtime log](runtime.log), [summary](runtime-summary.json) and evidence describe four impaired UDP peers, twelve subsequent activations, exact removal at **3634** after activation **1834**, followed by two-second visual-only dissipation and final zero hazards/visual owners. Directly viewed the [fading residue](expired-fading.png). The long full run was not rerun by this reviewer.
- **VERIFIED — evidence inspection; INFERRED — recorded interaction/recovery outcomes:** contacts retain owner/rival reentries, 150 agreed rival points and 1,500 retained hazards; handling reports twenty native trials; rack reports 482 handoff checks; reconnect reports three resynchronizations including 125 simulation seconds offline. Both current two-peer and three-peer migration logs end in successful authority replacement and item recovery. The prior three-peer failures **did not recur in this retained run**, but their cause remains unknown and intermittent failure is not ruled out. No authority-system fix is claimed.
- **VERIFIED — evidence inspection; INFERRED — developer operation:** Oil-only options report 112 write assertions and thirteen fresh-process checks. The earlier unrelated unrestricted aiming-control failure is not claimed fixed or rerun.
- The retained before-fix bank evidence distinguishes the first car-proxy defect from the later unsupported streak at the grass/bank transition. The fresh passing route supports the resulting placement correction; it does not establish the exact unknown coordinates/input timing of the human's original playtest.

## Material Findings and Recommendations

### 1. The dominant discharge still reads as a solid strand

- **Problem:** the central burst silhouette remains broad and smooth. Its internal branches merge visually; during airborne firing the trailing end forms pointed sheet-like prongs. Peripheral drops are visible, but do not overcome the dominant extruded-strand impression.
- **Evidence:** fresh [straight firing](astra-native-stage-1-firing.png), [airborne firing](astra-native-stage-3-firing.png) and [airborne chronological frames](astra-sequence-airborne.png), particularly frames 1140 and 1158. This repeats the prior finding using new execution, rather than assuming it persists from unchanged source.
- **Severity:** Medium.
- **Impact:** the weapon's central action communicates direction and force but only partially achieves the rich, turbulent oily-vomit appearance requested by the firing concept.
- **Suggested Improvement:** if separately authorized, reduce long shared smooth spans and sharp terminal sheets; make visibly distinct irregular substreams and short-lived breakup carry more of the outline at vehicle viewing distance. Preserve the accepted burst clock, forty parcels, quantity, footprint and rearward aiming.
- **Scope:** In Scope for the existing TS-245 fluid presentation; **not authorized as additional implementation in this completed Round 5**.
- **Corrective Work Type:** Existing Task. No issue creation or implementation is authorized by this critique.

### 2. Landing splashes still expose rigid radial facets

- **Problem:** the transient Oil crowns resemble stiff flat strips or angular wedges rising from the surface. Their hard shape remains conspicuous in the otherwise cohesive discharge/deposit sequence.
- **Evidence:** fresh [straight firing](astra-native-stage-1-firing.png), [impact](astra-native-impact.png) and [deployment/straight frames](astra-sequence-deployment.png). Settled deposits remain readable, irregular and surface-supported in these cases, but their broad scalloped outlines do not mask the angular splash event.
- **Severity:** Medium.
- **Impact:** impact looks geometric rather than like thick liquid spreading and collapsing, reducing the finish of a repeatedly visible weapon action.
- **Suggested Improvement:** under separate authorization, soften and vary the crown outlines and their collapse, removing the large rigid radial-strip impression. Reassess from ordinary vehicle distance while retaining bounded cosmetic counts and shared supported gameplay coverage. No additional gameplay hazards are needed.
- **Scope:** In Scope for TS-245 presentation; beyond the completed Round 5 correction authorization.
- **Corrective Work Type:** Existing Task.

## Runtime / Operational Limitations

Only the host viewport was rendered. Live observer scene values establish accepted-clock meter state, not perceived remote hose/mouth/spring smoothness. The new sight glass and brass fittings are most legible in close-up; wide views do not establish fine hose-aging detail at gameplay distance. Hose continuity is supported by sampled poses and endpoints, but box interiors obscure final folding, and exhaustive clipping/stretching across all tuning extremes remains unverified.

The fresh scene uses deliberate observation cameras and cuts, not a complete production chase-camera playtest. Some bank screenshots omit the initial vehicle as it enters the shot; the native grass observations/trace provide the start-surface evidence. Transient airborne flight and cosmetic splash height must not be confused with unsupported permanent deposits. Planar deposition with finite support sampling remains an approximation, not exhaustive terrain coverage.

Reconnect/migration fixtures restore seeded deposited hazards. **Reconnect during the one-second burst and meter reconstruction at that instant remain UNVERIFIED.** Earlier admission/migration failure causes remain unproven. The current passing recovery logs reduce the previous demonstrated gap but do not establish a permanent cure. Earlier overly restrictive meter fixture failures are retained; this critique claims no production networking or firing-clock repair from changing a test assertion.

**Observed performance limits:** the retained repeated-use summary reports median **25.496 ms**, p95 **45.851 ms**, maximum **83.960 ms**, with four loaded scenes and forced drawing. These are process intervals, not a single-client GPU benchmark, an improvement measurement against a controlled baseline, or a 60 FPS guarantee. Capture work and other applications further limit timing interpretation. The 1,500-hazard state fixtures are not a rendered-capacity benchmark.

**UNVERIFIED:** hands-on enjoyment/control feel, audio listening, human acceptance, competitive balance/TS-247, all developer-tuning extremes, arbitrary terrain transitions, a driven ramp crest, exhaustive swept-mesh clearance, EOS/WAN/multi-device behavior, exported-build/other-hardware performance and prolonged soak. Independent engineering review remains separate.

## Recommended Next Round and STOP

If the human authorizes another round, focus narrowly on the dominant discharge outline and impact-crown shape. Preserve the approved character, spring, quantity, rack, hoses/meter behavior and newly corrected supported footprints. An additional native reconnect-during-burst observation would close the specific meter recovery limitation; it should precede any speculative networking change. These are recommendations for a future human decision, not permission to extend the current round or begin TS-247.

**STOP — Story Round 5 is complete at 7.9 / 10, FAIL.** No recommendations were implemented. This reviewer wrote only the formal critique and distinct evidence artifacts; no production source, tests, assets, branches, commits, pushes, Jira issues or PRs were changed. Human review and explicit instruction are required before another correction or critique round.
