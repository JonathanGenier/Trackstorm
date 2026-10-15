# Astra Runtime Critique — Story Round 2

**TS-245 — Overall Score: 7.5 / 10**

**Quality Assessment: FAIL (<8.0)**

**Reviewed:** 2026-10-12, branch `ts-245-pg`, completed Round 2 result.

The corrected jack now has a recognizable crimson, ivory, black and brass material hierarchy in actual Godot rendering. Firing is substantial and connected, deposited coverage is clearly visible, and the exercised deployment/replacement sequence is coherent. The timer-only lifecycle is supported by retained native evidence. This is a meaningful improvement over the historical 6.6/10 Round 1 result.

The principal remaining shortfall is fluid presentation. The stream repeatedly reads as a glossy ribbon or hose with a cut end, including a detached rod-like tail after emission stops. Ground coverage is readable but often resembles broad black bands or joined rounded stamps, with wetness concentrated in isolated highlights and little visible spreading or impact splatter. Those are material shortcomings for a Story whose approved outcome specifically centers on convincing oily vomit and organic wet deposits. The result is solid, but does not reach the repository's 8.0 highly polished standard. This judgment weights that central visual outcome; it is not an arithmetic average of category scores or a reward for test count.

## Category Scores

| Material category | Score | Observed basis and limits |
| --- | ---: | --- |
| Runtime Functionality | 8.6 | Fresh native deployment, firing, surface deposition and replacement completed; retained full-run evidence supports timer-only persistence and bounded repeated use. |
| Physics | 8.1 | Airborne travel precedes deposition, bank and vertical impacts attach to their surfaces, and native turning remains supported. A driven ramp crest and arbitrary terrain are not established. |
| Multiplayer / Networking Experience | 8.0 | Fresh local UDP admission during a pour completed; retained impaired runs support shared lifecycle and cleanup. Remote visual smoothness and WAN experience were not directly observed. |
| Visual Quality | 7.3 | Restored material hierarchy and strong hazard readability; deposited fluid still has a broad, flat band/stamp appearance. |
| Animation / Motion | 7.7 | Clear opening, extension, firing, return and replacement progression; the fluid's blunt leading/trailing sections weaken motion continuity and liquid character. |
| VFX / Feedback | 7.1 | Strong rearward discharge and unmistakable coverage; impact/spreading feedback remains weak relative to the stream's size. |
| Game Feel / Juice | 7.3 | Visual anticipation and mechanical recovery are coherent. Visual discharge lacks the convincing breakup and splash that would make the substantial pour feel wet and forceful. Audio and hands-on satisfaction are unscored. |
| Runtime Stability | 8.4 | Fresh elevated run completed without accepted warnings/errors; retained final lifecycle and recovery runs completed. This is bounded fixture evidence, not a soak guarantee. |
| Runtime Integration | 8.5 | Jester return, box closure, rack stow and replacement stay coherent; retained contact, handling, recovery and repeated-use evidence supports the surrounding systems. |

Gameplay/fun, physical controls/responsiveness, vehicle movement feel, production camera behavior, audio and developer UI/UX are not independently scored: scripted native inputs and observation-camera captures cannot establish the corresponding human experience. Feature-specific operational quality is included in functionality/integration. Observed performance is reported below without a numeric score because capture overhead and concurrent applications prevent a defensible performance judgment. No engineering/code-quality categories are scored.

## What Was Exercised

### Scope and reference review

- **VERIFIED — reference inspection:** read repository workflow/critique routing, relevant Held Items and Oil developer-options documentation, the Story verification report, Round 1 critique, and the refreshed full Jira snapshot. Its approved later changes supersede the earlier contact quota. Jira has no required children or comments; TS-247 final integration/balance remains excluded.
- **VERIFIED — direct image observation:** viewed all three attached concepts: closed, deployed and firing. Their pixels were not copied into tracked evidence. Model size/proportions are an approved constraint, not a request for redesign. The retained asset-validation result reports unchanged geometry position buffers; this review's palette judgment comes from runtime images, not that validation.

### Fresh rendered execution

Executed the following command against Godot .NET 4.7.2 with the Compatibility renderer on the NVIDIA GTX 1070:

```powershell
./check-oil-jack.ps1 -GodotPath '.godot/godot-ci/4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Visual -VisualOnly
```

- **VERIFIED — first attempt, environment failure:** the sandboxed run executed all presentation scenarios and emitted the scenario-success marker, but the wrapper correctly failed with exit code 1 because Godot could not open its user log or read the Windows certificate store. This is not counted as a clean PASS. The original output is preserved in ignored `.godot/ts-245-round2/astra-review/sandbox-runtime.log`.
- **VERIFIED — second attempt, clean PASS:** reran the identical command with the required system access. Exit code 0; no accepted ERROR/WARNING output. Host lifecycle duration in the log is approximately 79.6 seconds. The fixture exercised native straight bank driving, steering/throttle/handbrake turning, airborne emission and landing, a joining third peer during firing, vertical first-hit deposition, and replacement while Oil owns the rack. Native turning records 331 supported ticks and 4.509 m/s peak lateral slip. A native 23-degree asphalt ramp query passed; this was not a driven crest.
- **VERIFIED — direct fresh capture inspection:** inspected new straight, turning and airborne firing frames, curved deposits, and twelve chronological native frames from simulation frame 324 through 636 covering deployment, emission and return. The fresh result reproduces the restored palette, cohesive stream, visible trail and ribbon/cut-tail concerns. Observation was through scripted native execution and direct captures, not interactive driving or human playtesting. The fresh runtime log, trace and captures are under ignored `.godot/ts-245-round2/runtime/`.
- `-VisualOnly` intentionally ends after presentation/replacement. It does **not** freshly verify the 30-second deadline, twelve-use sequence or final four-peer cleanup. Those conclusions use the independently retained complete run below. No retained file in this tracked evidence directory was replaced by the short run.

### Retained full lifecycle and temporal evidence

- **VERIFIED — direct visual inspection:** inspected the selected [closed box](closed-box.png), [deployed head](deployed.png), straight/turning/airborne/wall firing, all four deposit views, and replacement-ready image. The older automatically named closed/ready images catch transitions and were not treated as exact state portraits.
- **VERIFIED — temporal frame inspection:** extracted and inspected twelve chronologically distributed frames from each of the five retained recordings: [deployment/straight](deployment-straight.webp), [turning](turning.webp), [airborne](airborne.webp), [wall](wall.webp) and [replacement](replacement.webp), with additional full-resolution frames around emission and shutdown. This establishes progression between observed states; it is not a claim of real-time playback or wall-clock smoothness. Review contact sheets and extracted frames remain ignored under `.godot/ts-245-round2/astra-review/`.
- **VERIFIED — evidence inspection; INFERRED — recorded functional outcomes:** [jack runtime log](jack-runtime.log), [trace](jack-trace.json) and [summary](summary.json) support the complete impaired four-peer run: activation tick 1820, deadline/removal tick 3620, twelve subsequent host/remote activations, and final zero Oil visual owners on all four peers. Peak observed flight is 38 parcels, with 53 additional segments and 0.410 m head offset. These outcomes were not rerun in the shorter critique command.
- **VERIFIED — evidence inspection; INFERRED — recorded interaction/recovery outcomes:** [contacts](contacts.log) records two owner and three rival entries preserving one hazard, agreed 150 points, no continuous-overlap repeat awards, and four-peer admission retaining 1,500 exact timer-only hazards. [Handling](handling.log) records 20 native trials, including wheel isolation and full recovery while overlapping. [Rack](rack.log) records 482 handoff checks. [Reconnect](reconnect.log) and [migration](migration.log), read with the Story report, support retained state and authority replacement. Earlier failed migration attempts remain disclosed in the evidence bundle; they are not presented as passes. These logs support robustness, not hands-on handling enjoyment.
- **VERIFIED — completion-context inspection:** [repository gate](check.log) records 1,327 Core and 462 non-native transport tests and successful builds; [startup](startup.log) records the pinned runtime startup. These establish the supplied verification context and do not determine visual scores.

## Material Problems and Recommendations

### 1. Cohesive stream still reads as a ribbon with cut ends

- **Problem:** the new connected discharge has adequate volume and direction, but its long smooth silhouette, narrow peripheral lines and abruptly terminated sections frequently resemble a strip or hose. After firing, a rigid-looking detached section remains visible as the last liquid travels to impact.
- **Evidence:** [straight firing](stage-1-firing.png), [airborne firing](stage-3-firing.png); zero-based frames 12 and 20 of `deployment-straight.webp`, frame 18 of `turning.webp`, and frame 17 of `airborne.webp`. Fresh native frames 456–576 reproduce the leading/cut-tail character. This is a visual finding, not a claim that authoritative gravity or collision is absent.
- **Severity:** Medium.
- **Impact:** discharge is readable but its material motion falls short of convincing thick, energetic vomit, especially at onset and shutdown.
- **Suggested Improvement:** retain the cohesive central flow and bounded trajectory, but make the leading and trailing sections taper and break into short irregular liquid lobes; vary the broad silhouette and provide visible localized impact splash. Validate start, sustained flow and shutdown from ordinary driving distance. Avoid returning to evenly spaced beads or changing approved model proportions.
- **Scope:** In Scope.
- **Corrective Work Type:** New Corrective Task, on the same Story branch, only if the human authorizes it.

### 2. Deposits read more as painted bands than organic wet puddles

- **Problem:** substantial coverage is now visible, including a proper curve, but much of the surface is nearly uniform black. Large rounded joined sections dominate the silhouette; isolated bright highlights do not consistently communicate wet liquid. The vertical deposit is particularly flat. Small splashes may exist, but they are not prominent enough in the inspected driving-scale frames to resolve this impression.
- **Evidence:** [straight deposits](straight-deposits.png), [curved deposits](curved-deposits.png), [airborne deposits](airborne-deposits.png), [wall deposits](wall-deposits.png) and late frames of `turning.webp`. The paired gray lines near the curve are vehicle skid marks, not Oil, and are excluded from this finding.
- **Severity:** Medium.
- **Impact:** players can locate coverage, but the intended wet spill character and impact satisfaction remain underdeveloped.
- **Suggested Improvement:** preserve visible/gameplay coverage agreement while adding stronger local asymmetry, occasional lobes/satellites and a brief readable spread at accepted impacts. Tune surface response so subtle wet variation survives representative driving angles and shade without broad white glare or rainbow coloration. Recheck straight and curved coverage together with actual tire interaction.
- **Scope:** In Scope.
- **Corrective Work Type:** New Corrective Task, on the same Story branch, only if the human authorizes it.

## Runtime / Operational Limitations

The retained recordings use simulation-frame spacing. PNG readback/encoding slows capture; sampled frames cannot certify microstutter or perceived input latency. Observation cameras include deliberate fixture cuts and overhead views, so they do not evaluate the production chase camera. Only the host viewport is rendered; remote physics/presentation nodes and state assertions do not prove remote perceived continuity.

The retained non-capture repeated-use stage reports median 16.666 ms, p95 33.332 ms and maximum 43.037 ms render intervals with at most six active hazards. Capture stages report median 43.854 ms and p95 140.557 ms. Concurrent local applications, multiple fixture arenas and readback preclude a controlled benchmark or 60 FPS claim. The 1,500-hazard state tests do not establish 1,500-hazard rendered performance.

**UNVERIFIED:** human keyboard/controller play, enjoyment/competitive balance, audio listening, live developer-option interaction, driven ramp-crest behavior, prolonged soak, release-export performance, other hardware, authenticated EOS/WAN/multi-device play and independent engineering review. No human playtesting or acceptance is claimed. TS-247 remains outside this critique's corrective scope.

## Recommended Next Round and STOP

If the human authorizes Story Round 3, prioritize the stream's onset/shutdown silhouette and impact transition, then deposited wetness and organic edge variation. Preserve the corrected palette, accepted model size, timer-only one-hazard behavior and current interaction/recovery semantics. Re-run affected verification and observe the revised native result before any new score.

**STOP — Round 2 is complete at 7.5 / 10, FAIL.** Per `docs/critique.md`, another implementation or critique round requires explicit human direction. No recommendation was implemented, no corrective Jira issue was created, and no production/test/asset/feature-document change was made by this review. Human acceptance remains pending.
