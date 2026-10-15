# Astra Runtime Critique — Story Round 6

**Story:** TS-245 — Jack-in-the-Box Oil Weapon, Projectile & Fluid Patches

**Branch:** `ts-245-pg`

**Overall Score: 7.8 / 10**

**Quality Assessment: FAIL (<8.0)**

The clearance correction, enlarged readable tank and corrugated hoses materially improve the approved assembly. The fresh native run preserved expressive spring movement, attached hoses, a draining gauge and orderly replacement without reproducing box penetration. The result nevertheless remains below the integrated Story quality gate: the discharge still exposes long cord-like spans and a sharply folded airborne tail, and current repeated recovery failures leave a material multiplayer integration gap. The lower overall score than Round 5 reflects the current recovery evidence, not a reversal of the visible improvements. It is an experiential judgment, not an arithmetic average or a reward for test volume.

The user explicitly authorized this additional round after Round 5's 7.9/10 result, overriding the earlier three-round cap for this review. The approved box, jester, loose spring, original rack placement, one-second forty-parcel quantity and timer-only lifecycle remain the baseline. This review does not request their redesign or TS-247 balance work.

## Category Scores

| Material category | Score | Observed basis |
| --- | ---: | --- |
| Runtime Functionality | 8.4 | Fresh burst, airborne/vertical deposition, meter and replacement complete successfully; retained full lifecycle establishes timer removal and bounded repeated use. Recovery completeness is assessed separately below. |
| Visual Quality | 8.2 | The larger tank joke is legible from the retained production camera. Brass adapters, strongly ribbed hose outlines and the approved character form a coherent mechanism. Hose aging is more apparent close up than in wide views. |
| Animation / Motion | 8.4 | Loose spring response, passive hose following, mouth closure before retraction and original rack handoff are coherent in chronological samples. |
| VFX / Feedback | 7.5 | Connected substreams and rounded splash fingers improve separation and impact. Long smooth lengths, folded pointed tails and thin hooked splash spokes still look constructed rather than turbulent thick liquid. |
| Physics / Presentation Clearance | 8.3 | Fresh moving samples show no box intrusion; retained boundary and bank checks materially strengthen evidence. Sampled vertices and conservative occlusion do not prove every continuous pose or triangle clear. |
| Multiplayer / Networking Experience | 6.5 | Fresh admission and retained four-peer use pass, but two reconnect attempts and two three-peer migration attempts fail. An earlier admission failure also remains unresolved. |
| Runtime Stability | 7.4 | Fresh presentation exits cleanly. Current shared recovery runs stop with failures; successful retries do not establish consistently reliable sessions. |
| Runtime Integration | 7.6 | Hardware, gauge, accepted clock, supported coverage and replacement cooperate in the successful runs. Complete reconnect and three-peer recovery are not currently established. |
| Game Feel / Juice | 7.8 | Visible anticipation, spring recoil and recovery provide character. The liquid silhouette limits the finish of the central action; physical control feel and audio were not evaluated. |

No separate score is assigned for hands-on gameplay/fun, control ergonomics, general driving feel, audio, isolated performance, code quality, architecture, test design or delivery hygiene.

## What Was Exercised

### Scope and reference inspection

**VERIFIED — review inputs:** read repository routing, workflow, critique rules, the feature index and relevant Persistent Oil documentation, Oil asset notes, the full pasted user request, saved current Jira description (updated `2026-10-13T11:18:40.382-0400`, zero comments), Round 5 critique and current Round 6 verification/evidence. Viewed all four concepts: closed, deployed, firing and Laughing oil 2. Later approved decisions override conflicting historical Jira wording. The source of truth for this review is the supplied current Jira snapshot plus the explicit latest user request; this reviewer did not independently query Jira again.

### Fresh independent native execution

Executed once against the frozen existing build, with the pinned Godot .NET 4.7.2 console executable and required graphics/cache/local UDP access:

```powershell
./check-oil-jack.ps1 -GodotPath '.godot/godot-ci/4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Visual -Impaired -VisualOnly
```

- **VERIFIED — successful execution:** exit **0**, Compatibility renderer on NVIDIA GTX 1070; host lifecycle **118.21 seconds**. [Fresh log](astra-native-runtime.log), [trace](astra-native-trace.json) and [evidence](astra-native-evidence.txt). No ERROR/WARNING output was accepted. Two initial UDP peers and a third admitted during airborne firing; impairment 30 ms delay, 5 ms jitter, 2% loss. All fresh Welcome messages shown in this run arrived after Connected.
- **VERIFIED — exercised behavior:** native acceleration/braking/reacceleration, straight firing with bump traversal, steering/throttle/handbrake on the bank, airborne activation and landing, vertical first-hit deposition, Oil-to-Wrench replacement and a 23-degree ramp support query. The ramp query is not a driven crest. Each activation retains sixty accepted firing ticks, forty parcels and one logical hazard.
- **VERIFIED — sampled motion/clearance:** spring peaks **0.447 m fore/aft, 0.428 m lateral, 0.449 m vertical**; full nesting before replacement; **40 rendered flight frames, zero sampled liquid vertices inside the box**. Four named firing readbacks place the first liquid vertex **0.018 m** from the displayed mouth. All 119 non-null trace hose endpoint-error samples are zero. This confirms the measured samples, not exhaustive swept hose or triangle clearance.
- **VERIFIED — gauge and shutdown:** directly viewed fresh [full](astra-native-meter-full.png), [partial](astra-native-meter-half.png) and [empty](astra-native-meter-empty.png) captures. The saved partial capture is **0.426**, not exactly half. The run records full/draining/empty/new-pickup reset and **87 observer samples** against accepted clocks, maximum publication lag **12 ticks**, delayed level difference **0.200**. At frame 1900 the mouth is closing (`0.214`) while extension remains `1`; at frame 1910 the mouth is closed while extension is `0.562`, reaching zero by 1930. This supports close-before-retract sequencing.
- **VERIFIED — direct visual observation:** inspected **50 fresh chronological frames** across [deployment](astra-sequence-deployment.png), [straight burst](astra-sequence-straight.png), [airborne](astra-sequence-airborne.png) and [replacement](astra-sequence-replacement.png), plus full-resolution firing, impact, jester, gauge and mouth-closing captures. [Frame hashes](astra-sequence-sources.json) identify exact originals; [capture/log hashes](astra-native-sources.json) identify retained native files. Sheets only resize, arrange and label genuine captures. No generated image substitutes for runtime evidence.
- The native run supplies meaningful fresh execution and capture observation, **not physical keyboard/controller play or live audiovisual observation**. `-VisualOnly` ends before expiry and twelve-additional-use stages. Those outcomes below are retained evidence, not newly executed claims. Some early frames still show the preceding observation camera before its next switch; those frames do not establish the hidden airborne stream.

Primary raw runtime evidence was archived before execution and then restored to its original path. Fresh raw frames remain separately under `.godot/ts-245-round6/astra-native-runtime`; retained files use `astra-*` names. The user's editor was left alone. No production code, assets, tests, workflow, version, main Story report, Git delivery or Jira/PR state was changed by this reviewer.

### Retained final evidence inspected

- **VERIFIED — image/evidence inspection; INFERRED — recorded full-run outcomes:** [full runtime evidence](evidence.txt), [summary](runtime-summary.json) and current Story report record four impaired peers, exact geometry/deadlines, twelve subsequent activations and complete Oil-node cleanup. Activation **1842**, deadline and first absence **3642** establish thirty-second gameplay expiry; the retained [fade image](expired-fading.png) shows cosmetic dissipation afterward. The long run was not repeated here.
- **VERIFIED — production-camera observation:** viewed [tank/gauge](gameplay-meter-full.png), [straight firing](gameplay-stage-1-firing.png) and [airborne](gameplay-stage-3-firing.png). “LAUGHING,” crossed-out “GAS” and red “oil” can be read at the supplied 1280×720 chase-camera framing. The large enamel field communicates the joke more clearly than the former tank. Fine gauge subdivisions are close-view details; no claim of exact small-scale level reading at speed is made.
- **VERIFIED — boundary evidence inspection:** viewed maximum backward-lean captures at default and maximum stream widths and read the [780-pose result](clearance-evidence.txt). It records zero remaining interior vertices and **2,275 hardware-occluded quads** over widths 0.08/0.24/0.8 m. This supports corrected rendered clearance under sampled extremes. At maximum width, far-stream geometry remains conspicuously broad and sheet-like; that tuning extreme is not treated as the default-art requirement.
- **VERIFIED — bank evidence inspection; INFERRED — recorded native support result:** [bank evidence](bank-evidence.txt) records **179.72°** heading change, **32.683 m/s**, **14.633 m/s** lateral slip, 114 accepted impacts and **168 supported pieces**, with zero unsupported retained pieces. Viewed the [hard-reversal deposits](bank-bank-1-deposits.png). Gray angular tire trails are separate from black Oil and are not used as an Oil defect. This production route was not independently rerun here.
- **VERIFIED — current failure evidence inspection:** the [early admission failure](admission-failure.log) records a Welcome during Connecting, followed by an unassigned peer and stage-6 timeout. Its unchanged full-run retry passed. Both reconnect attempts fail at the Salvo checkpoint assertion before subsequent Oil recovery assertions; the [retry](reconnect-retry.log) explicitly reports incorrect partial ammunition/cooldown/flying-round recovery. Both three-peer migration attempts fail at stage 8 with recoverable-checkpoint agreement/authority-fencing failure, including the [retry](migration-retry.log). The [two-peer retry](migration-two-peer-retry.log) passes after its initial failure. These are current operational outcomes, not evidence that TS-245 caused them or that a pre-existing baseline cause has been proven.
- **INFERRED — additional integration confidence:** the retained report records passing contacts/scoring (150 points and 1,500 hazards), twenty handling trials, 482 rack handoffs, Oil developer controls and startup. These were not independently repeated. Historical Round 5 recovery passes cannot replace the present failures.

## Material Findings and Recommendations

### 1. Complete multiplayer recovery remains unreliable in current evidence

- **Problem:** two current reconnect attempts and two three-peer migration attempts fail; an initial live-admission attempt also failed. A passing two-peer retry and successful normal-use/admission runs establish partial functionality, not robust complete recovery.
- **Evidence:** [reconnect retry](reconnect-retry.log), [three-peer migration retry](migration-retry.log), [admission failure](admission-failure.log), alongside the retained first-attempt logs and successful retry records. Reconnect stopped before Oil assertions, so recovered Oil behavior is unknown on that route rather than demonstrated corrupt.
- **Severity:** High.
- **Impact:** a player joining/resuming or losing the host may fail to enter or continue a match containing this feature. The integrated preservation requirement cannot currently be called fully verified.
- **Suggested Improvement:** with separate human authorization, reproduce against a controlled baseline and diagnose checkpoint/admission ordering with exact selected boundaries, then repeat complete reconnect and three-peer migration, including a retained Oil hazard. Add active-burst/meter recovery observation once the shared route completes. Do not weaken assertions or treat repeated retries as a repair.
- **Scope:** Required integration evidence is **In Scope** for TS-245. General transport/recovery repair is an **Out-of-Scope Recommendation** for this narrowly approved polish round until cause and ownership are established. No causal attribution to the new visual implementation is made.
- **Corrective Work Type:** **New Corrective Task** if shared recovery repair is authorized; no issue or new work is created by this critique.

### 2. The main liquid action still exposes constructed strand and splash shapes

- **Problem:** separated strands improve the previous dominant ribbon, but the airborne stream retains long smooth parallel lengths and a sharply bent, pointed terminal bundle. Some landing crowns look like repeated thin hooked spokes. Rounded cross-sections alone have not fully removed the rigid impression.
- **Evidence:** fresh [airborne firing](astra-native-stage-3-firing.png), especially its lower folded tip; chronological [airborne frames](astra-sequence-airborne.png) 1134–1182; fresh [straight firing](astra-native-stage-1-firing.png) and [impact](astra-native-impact.png). The complete action is cohesive and forceful, and no box penetration was reproduced, but these silhouettes remain visible at the supplied vehicle observation distance.
- **Severity:** Medium.
- **Impact:** the frequently repeated weapon action remains less convincing as turbulent thick liquid than the approved firing reference, despite improved spray and mechanical presentation.
- **Suggested Improvement:** only if another correction is authorized, concentrate on terminal breakup and silhouette variation: remove sharply folded pointed bundles, shorten persistent smooth spans, and vary splash finger thickness/merging/collapse so impact reads as spreading liquid rather than radial hooks. Preserve clearance, forty parcels, one-second clock, footprint and bounded cosmetic cost. Reassess chronological default-width captures before considering any broader art changes.
- **Scope:** **In Scope** for the TS-245 fluid presentation, but no further implementation is authorized by this review.
- **Corrective Work Type:** **Existing Task**.

## Runtime / Operational Limitations

Only the host viewport was rendered in the fresh run. Observer accepted-state gauge measurements do not establish perceived remote spring/hose smoothness or simultaneous displayed levels. Reconnect during the one-second burst and its reconstructed meter are **UNVERIFIED**, as is current complete reconnect and three-peer migration. Shared failures have not been proven baseline defects and are not dismissed because relevant production paths were unchanged.

Boundary checks sample mesh vertices and poses; a zero count is not a mathematical guarantee for all triangle interiors, continuous poses, tuning combinations or hose clearances. Conservative hardware occlusion can shorten old liquid during reversals. Finite terrain support sampling remains an approximation. The present evidence does not establish every possible bank entry or the exact unknown coordinates of the human's earlier playtest.

**Observed performance limits:** retained four-arena forced-render intervals are median **39.341 ms**, p95 **112.916 ms**, maximum **144.741 ms**. These are substantial fixture intervals, but capture work, multiple loaded arenas and other applications prevent attribution to Oil or a controlled regression claim. They do not establish normal gameplay FPS, a 60 FPS guarantee or release performance. No independent benchmark was run.

**UNVERIFIED:** physical keyboard/controller feel, real-time audiovisual smoothness, audio listening, human enjoyment/acceptance, competitive balance/TS-247, driven ramp crest, arbitrary terrain, all developer extremes, EOS/WAN/multiple devices, exported build/other hardware and prolonged soak. Independent engineering review remains separate.

## Recommended Next Round and STOP

If the human requests further work, prioritize a separately bounded investigation of current recovery failures before claiming complete integration. The residual stream-tail/splash silhouette is the only additional presentation recommendation; keep the now-readable tank, corrugated hoses, approved character and spring, restored rack position and corrected clearance intact. This recommendation grants no authority to repair shared networking or begin another polish round.

**STOP — Story Round 6 is complete at 7.8 / 10, FAIL.** No recommendation was implemented. The user must decide whether to accept this checkpoint, authorize a scoped correction, or defer the remaining issues. A target of 8.0 does not change the observed score.
