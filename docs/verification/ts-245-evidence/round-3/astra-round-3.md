# Astra Runtime Critique — Story Round 3

**TS-245 — Overall Score: 7.7 / 10**

**Quality Assessment: FAIL (<8.0)**

**Reviewed:** 2026-10-12, branch `ts-245-pg`, completed user-authorized third polish.

The jack now has a clearly white grinning face, coherent red/black hat with two gold bells, flatter collar and vivid white/red box with gold/brass hardware. The stronger head motion and rearward carriage give the mechanism a more legible character. Deployment, return and replacement form a coherent sequence in the fresh native execution. The shorter burst retains substantial visible coverage, and the exercised lifecycle remains functional.

The central fluid presentation still falls below the highly polished standard. Multiple strands are visible, but they predominantly form long, angular parallel cords. The detached end remains spear-like; a fresh shutdown capture also shows a large folded V-shaped connection. Impact crowns and flecks are present, but their thin spikes and the broad scalloped trails do not consistently read as turbulent wet oil. Deposits have stronger sheen, yet large soft white patches can look patterned rather than subtly reflective. These are visible in the actual runtime and materially affect the expressly requested outcome. The score reflects those observations, not the number of tests or the fact that this is the last permitted Story round.

## Category Scores

| Material category | Score | Observed basis and limits |
| --- | ---: | --- |
| Runtime Functionality | 8.6 | Fresh native opening, readiness, sixty-tick/forty-parcel firing, contact deposition and replacement complete. Retained full-run evidence supports timer-only expiry and repeated use. |
| Physics | 8.2 | Native airborne travel/landing, banked driving and vertical impacts complete; visible deposition follows impacted surfaces. Sampled spring motion stays controlled. No exhaustive mesh-clearance proof or driven ramp crest. |
| Multiplayer / Networking Experience | 7.9 | Fresh local UDP active-pour admission completes; retained impaired state/cleanup and recovery runs pass. The earlier unexplained admission timeout remains an uncertainty; remote visual smoothness and WAN operation are unobserved. |
| Visual Quality | 7.7 | The clown expression and palette are readable and much closer to the approved direction. Fluid silhouettes and deposit highlights still look synthetic. |
| Animation / Motion | 7.8 | Head sway, compression, lid closure, carriage return and rack replacement progress coherently. Long cord-like fluid sections and a folded shutdown shape weaken continuity. |
| VFX / Feedback | 7.3 | A substantial rearward burst and persistent coverage are unmistakable. Added splashes communicate impacts, but the cord/spike character falls short of convincing turbulent liquid. |
| Game Feel / Juice | 7.6 | Stronger visual anticipation, head response and a concentrated burst improve the effect. This scores visual impact/recovery only; hands-on satisfaction and audio are unverified. |
| Runtime Stability | 8.2 | Fresh rendered fixture exits successfully without accepted ERROR/WARNING output. The isolated retained full run succeeds; the earlier unexplained admission failure prevents an unqualified robustness claim. |
| Runtime Integration | 8.5 | Observed mechanism transitions preserve the surrounding car/rack sequence. Retained contact/scoring, handling, replacement and recovery evidence supports integration. |

This is a weighted experiential judgment, not an arithmetic mean. Gameplay/fun, physical controls, overall vehicle handling feel, production chase camera, audio and live developer UI are not independently scored. Performance is reported with its measurement limits below. No architecture, code-quality, test-design or delivery category is scored.

## What Was Exercised

### Scope and references

- **VERIFIED — reference inspection:** read repository routing, workflow review/verification rules, `docs/critique.md` in full, the current Story verification report, Round 2 critique, relevant Held Items/Oil tuning documentation and the full updated Jira snapshot. The snapshot has no required children or comments. Latest approved decisions require one-second firing, the specific clown/mechanism polish and a single timer-only hazard; older two-second/contact-quota wording is superseded. TS-247 balance/final-integration work remains excluded.
- **VERIFIED — visual reference inspection:** viewed all three supplied closed/deployed/firing concepts. The review uses their expression, color hierarchy and liquid character while respecting the approved existing overall model size. It does not demand concept-level photorealism from this renderer.

### Fresh native reviewer execution

Executed the following command with the user-cache/log/certificate access required by native Godot:

```powershell
./check-oil-jack.ps1 -GodotPath '.godot/godot-ci/4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Visual -VisualOnly
```

- **VERIFIED — fresh execution:** exit code **0**, Godot .NET 4.7.2 Compatibility renderer on NVIDIA GTX 1070. The host lifecycle reports approximately **77.9 seconds**. The [reviewer's native log](astra-native-runtime.log) records native acceleration/braking/reacceleration, sixty ticks and forty emitted parcels in one hazard, banked steering/throttle/handbrake, airborne activation/landing, a joining third peer during active firing, vertical first-hit deposition and Oil-to-Wrench replacement. It also records a supported 23-degree asphalt ramp query, not a driven crest.
- **VERIFIED — fresh motion/clearance assertions:** turning records 331 supported ticks and 4.509 m/s peak lateral slip. Rendered spring per-axis peaks are fore/aft **0.428 m**, lateral **0.437 m**, vertical **0.418 m**. Full nesting before replacement passed. Images show the deployed assembly at the rear and coherent return; these bounded assertions/captures do not establish that every possible swing, tuning value or mesh pair is clear.
- **VERIFIED — direct fresh image/temporal observation:** viewed fresh straight and airborne firing, impact, face/hat and curved-deposit images at full resolution. Inspected forty selected chronological captures across deployment/inertia, burst/shutdown, turning, airborne/landing and replacement, with full-resolution inspection of frames `00576` and `00600`. The [fresh firing](astra-native-firing.png), [impact](astra-native-impact.png) and [shutdown](astra-native-shutdown.png) images retain the material concerns. The temporal contact sheets and fresh trace/log copy are ignored under `.godot/ts-245-round3/astra-review/`.
- This was **automated native exercise and capture observation**, not interactive driving or a human playtest. Frame labels are capture request/source identifiers; readback can complete later. I do not equate their spacing with wall-clock smoothness.
- `-VisualOnly` stops after presentation/replacement. It intentionally does **not** freshly execute the exact thirty-second cleanup or twelve subsequent activations. Those conclusions below rely on retained full-run evidence. The tracked primary run was not overwritten. The ignored runtime directory contains some older files from stages omitted by this short run; those were not counted as fresh evidence.

### Retained full-run evidence

- **VERIFIED — direct image observation:** viewed the retained closed/ready/detail, straight/turning/airborne/wall firing, impact and all four deposition views. Viewed twelve chronologically distributed frames from each of `deployment-straight.webp`, `turning.webp`, `airborne.webp` and `replacement.webp`, using extracted contact sheets. These show transitions and changes in shape, not measured real-time playback. The existing [recording manifest](recordings.json) supplies simulation frame numbers and source hashes.
- **VERIFIED — evidence inspection; INFERRED — recorded lifecycle outcomes:** [runtime log](runtime.log), [summary](runtime-summary.json) and trace evidence support the full four-peer impaired local UDP run with 30 ms delay, 5 ms jitter and 2% loss: exact activation **1844**, deadline/removal **3644**, twelve subsequent host/remote activations, and final zero hazards/owned Oil visuals on all four peers. The observed hazard peaks at forty airborne parcels and 59 additional pieces. These are bounded fixture results, not human network experience.
- **VERIFIED — evidence inspection; INFERRED — recorded interaction/recovery outcomes:** [contacts](contacts.log) records two owner and three rival entries preserving a single hazard, 150 agreed points and no repeated-overlap award, plus four peers retaining 1,500 exact hazards. [Handling summaries](handling-summary.json) cover twenty native trials including individual wheels, speeds, braking, countersteering, handbrake, landing and recovery during continuous overlap. [Rack](rack.log) records 482 handoff checks. [Reconnect](reconnect.log) records three resyncs including 125 seconds offline; [migration](migration.log) records sequential authority recovery on three native peers. These were inspected, not independently rerun for this critique.
- [Coverage comparison](coverage.log) supports forty parcels and comparable sampled straight-road area at 0/10/20 m/s, not identical coverage or accepted balance. [Repository gate](check.log) records warning-free Debug/Release builds, 1,339 Core and 462 transport tests. Those establish supplied verification context and do not raise the visual score.

## Material Problems and Recommendations

### 1. The burst still reads as angular cords, with a conspicuous folded shutdown shape

- **Problem:** the smaller strands largely follow the same long zigzag spine. Sustained and detached sections resemble glossy cables or pointed strips more than irregular turbulent liquid. In one fresh transition, a thick straight section folds back into a large V and appears to reach toward bumper height while the head has already risen away from the discharge. This is a visible connection artifact; its implementation cause and gameplay consequences are not established by this review.
- **Evidence:** [fresh firing](astra-native-firing.png), [fresh shutdown/source frame 00576](astra-native-shutdown.png), [fresh impact](astra-native-impact.png), retained [airborne firing](stage-3-firing.png), and the fresh temporal frames 00546–00600 and 01158–01200. The cord-like character recurs across retained and fresh observations; the particularly large V is one directly captured occurrence.
- **Severity:** Medium.
- **Impact:** this is the weapon's principal action and lasts only a second. Its dominant silhouette undermines the requested forceful oily-vomit effect even though direction, volume and deposition remain readable.
- **Suggested Improvement:** if the human selects future corrective work, eliminate long folded bridges during source/flight/deposition transitions; validate every start/shutdown boundary with native temporal captures. Preserve cohesive volume while giving strands independently changing necks, short torn lobes and localized breakup so they do not share a rigid repeated zigzag. Keep forty-parcel dose, authoritative rearward trajectory and bounded ownership intact.
- **Scope:** In Scope.
- **Corrective Work Type:** New Corrective Task, only after an explicit human decision on disposition; none created by this review.

### 2. Deposits and impact crowns retain an overly geometric material language

- **Problem:** edges are more irregular and satellites exist, but settled straight/curved deposits still read mainly as broad joined lobes. Strong soft white highlight patches repeat across the sun-facing ends, while shaded wall oil appears nearly flat. Visible impact crowns are mostly thin radial spikes, which provide activity without a convincing liquid sheet/splash transition.
- **Evidence:** [straight deposits](straight-deposits.png), [curved deposits](curved-deposits.png), [airborne deposits](airborne-deposits.png), [wall deposits](wall-deposits.png), and [fresh impact](astra-native-impact.png). Fresh curved-deposit observation reproduces the highlight/band impression. The paired gray driving arcs are skid marks and are excluded from this finding.
- **Severity:** Medium.
- **Impact:** hazard coverage is readable, but the approved organic, wet-black spill character remains inconsistent between viewing angles. More visible sheen has not fully resolved the painted-band impression from Round 2.
- **Suggested Improvement:** if selected by the human, refine deposit-scale asymmetry and small-scale surface response together, reducing broad white patterned glare while retaining black wetness. Give impacts short, irregular liquid lobes/flecks that visibly settle into the coverage instead of relying on thin spike crowns. Recheck lit and shaded driving angles, curved/airborne trails and footprint/contact agreement without changing balance or total dose.
- **Scope:** In Scope.
- **Corrective Work Type:** New Corrective Task, only after an explicit human decision on disposition; none created by this review.

## Runtime / Operational Limitations

The [earlier full-run admission timeout](earlier-admission-failure.log) remains unresolved evidence: a joining peer sent no inputs and stage 6 timed out. The later isolated complete run passed after diagnostics were added, without an admission-logic change. The fresh short run also completed active admission, but does not explain or eliminate that earlier failure. I do not attribute it to concurrency as an established cause.

Only the host viewport was rendered; remote state/presentation assertions cannot establish perceived remote continuity. Capture readback and encoding alter pacing, and the WebPs use simulation-frame timing. Deliberate observation-camera cuts and overhead views do not evaluate the production chase camera.

The retained forced-render repeated-use stage reports median **28.197 ms**, p95 **63.459 ms**, maximum **101.403 ms**, with at most six overlapping hazards. Four native scenes, forced draws, other applications and capture overhead prevent attribution of those intervals to the Oil effect or a controlled performance score. Neither that run nor this capture run establishes 60 FPS; the 1,500-hazard state checks do not establish rendered capacity.

**UNVERIFIED:** human keyboard/controller feel, enjoyment and competitive balance, audio listening, live developer-option interaction/all tuning extremes, exhaustive swept-mesh clearance, a driven ramp crest, prolonged soak, release-export performance, other hardware, authenticated EOS/WAN/multi-device operation and independent engineering review. The bottom spring/closure is supported by captured geometry and nesting checks, but the closed box obscures internal motion. Kickback is exercised as part of the burst; its isolated perceived strength is not separable from simultaneous chassis/spring motion in these samples. Human acceptance is still pending.

## Recommended Next Round and STOP

**No fourth Story critique round exists under `docs/critique.md`.** The human must decide whether to accept 7.7/10 for the milestone, defer the remaining visual concerns, or explicitly determine another disposition. If corrective work is selected, prioritize the folded/cord-like fluid transition, then deposit/splash material quality; this report does not authorize implementation or a fourth critique. TS-247 is not silently expanded.

**STOP — Story Round 3 is complete at 7.7 / 10, FAIL.** Per the mandatory human gate, no recommendation was implemented and no code, assets, tests, feature documents, Jira issues or PR administration were changed by this review. Only this report and the explicitly identified reviewer evidence were added. Await explicit human direction.
