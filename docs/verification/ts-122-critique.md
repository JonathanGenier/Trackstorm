# Astra Runtime Critique — Story Round 1

Story: TS-122. Branch: `ts-122-jg`. Date: 2026-09-21.

**Overall Score: 7.6 / 10**

**Quality Assessment: FAIL (<8.0)**

The menu is coherent, legible and operational in the exercised mouse flows. Its hanging assembly, icon placement and four-entry hierarchy clearly realize the supplied composition. Presentation still falls below the highly polished threshold: selection floods the metal with intense red, and the Coming Soon badge looks like a conventional flat UI rectangle attached to otherwise dimensional artwork. These judgments concern the new menu only; unchanged background media, watermarks and existing destination-screen styling are excluded.

## Category Scores

| Category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.2 | Settings and Play round trips, inert Garage, and clean Quit directly exercised. Broader failure/retry coverage is supporting harness evidence. |
| Controls / Responsiveness | 7.8 | Mouse targets responded predictably; disabled entry did not activate. Native keyboard tool input was inconclusive, so this is not a full keyboard/controller assessment. |
| Visual Quality | 7.2 | Strong silhouette, legible labels and consistent icon/plate structure, but selected material and status-badge treatment need refinement. |
| VFX / Feedback | 7.5 | Selected entry is unmistakable and disabled state clear; red treatment overwhelms material detail. |
| UI / UX | 8.0 | Four choices are easily scanned; returning from Settings preserves its selection; returning from browser restores Play and the settled assembly. |
| Runtime Stability | 8.2 | Completed interactive session and menu Quit exited with code 0 and no log diagnostics. No crash or invalid visible state in exercised flows. |
| Runtime Integration | 8.0 | Existing Settings/browser open and return correctly while the background remains present. Exact media continuity is supported by the verified harness, not direct audio observation. |

The overall score is a judgment of the integrated result, not an arithmetic average. Visual presentation is central to this Story and weighs heavily. Animation feel, audio and measured performance are not assigned scores without adequate direct evidence.

## What Was Exercised

- **VERIFIED — independent active runtime:** launched the actual Godot main scene on the interactive desktop with `--max-fps 60 --quit-after 18000`; observed the settled menu at 1280×720. Clicked Settings, then Back; the menu returned settled with Settings selected. Clicked Garage; it stayed disabled and did not steal selection. Clicked Play, then the browser's Back to Main Menu; the assembly returned settled with Play selected. Clicked Quit; the process exited cleanly with code 0. Session log: `.godot/ts-122-astra-final-observation.log`.
- **VERIFIED — independent visual inspection:** inspected `.godot/main-menu-checks/drop.png`, `settled.png`, `settings-selected.png`, `layout-640x360.png`, and `layout-1600x900.png`, plus the supplied `Main_Menu_full.png` composition. All four labels and status remain in bounds in the inspected size captures; 640×360 retains a readable primary hierarchy. The live menu corroborated the same selected-surface and badge appearance.
- **VERIFIED — evidence inspection, not independent rerun:** read `.godot/ts-122-final-startup.log`, which reports successful phase-aware recovery, entrance input gating, mixed synthetic input, disabled Garage, repeated navigation, stable targets, Settings/browser return and persistent media, with exit code 0 and no diagnostics. The implementation agent supplied the other completed final-check results documented in [Story verification](ts-122.md). These do not substitute for experiential observations or determine the score.
- **INFERRED:** the entrance progresses from above-screen drop to the settled assembly, supported by distinct runtime captures and successful production-path assertions. The full timing, catch and rebound were not continuously observed by this critic.

## Runtime / Operational Limitations

- Tool-generated Down, Up and S key pulses did not visibly move selection during this critic's session. It was not possible to distinguish input-injection timing/focus from application behavior in this bounded exercise; native keyboard ergonomics remain inconclusive, not a confirmed defect. The implementation agent separately reports manual Down navigation skipping Garage, and the final synthetic input harness passed.
- No physical controller or direct audio listening was available. Audio continuity, auditory feedback and hardware-controller feel remain **UNVERIFIED** here.
- Screen snapshots show background progression, but cannot establish smooth frame pacing, subtle cloth movement, or the quality of the complete entrance. The visible FPS counter is not a frame-time measurement. These qualities remain **UNVERIFIED** beyond harness assertions and sampled frames.
- Earlier sandbox launches were not visible to the desktop automation tool and logged a certificate-store access error. An escalated desktop launch resolved access; an initial desktop session closed before capture for an undetermined reason, without log diagnostics. The final bounded interactive session completed normally. Sandbox access failures are not scored as menu defects.
- Multiplayer session admission, driving and gameplay are outside this menu critique; only the browser entry/return integration was exercised. Existing browser session-recovery text and background media artifacts are outside this Story's presentation assessment.

## Recommendations

### 1. Preserve metal detail in the selected state

- **Problem:** the selected plate's face, frame and spikes become nearly uniformly saturated red, emphasizing fine speckle and reducing the dimensional metal separation visible in the unselected artwork and reference.
- **Evidence:** independently inspected `settled.png`, `settings-selected.png`, `layout-1600x900.png`, and the live selected Settings/Play states. The supplied composition concentrates stronger red illumination on the frame while retaining a darker, more varied face.
- **Severity:** Medium.
- **Impact:** selection is clear, but the most prominent interactive surface looks harsher and less materially convincing than the rest of the assembly.
- **Suggested Improvement:** rebalance selected coloration to preserve dark face values and metallic highlights, concentrating stronger red on edge illumination and lamps. Compare selected and unselected states at 640×360 and 1280×720 over the unchanged moving background.
- **Scope:** In Scope.
- **Corrective Work Type:** Existing Task.

### 2. Integrate the Coming Soon badge with the artwork

- **Problem:** the status backing is a plain dark rectangle with a fine border and smooth text, visibly less dimensional than the surrounding distressed metal and the reference's fastened plaque.
- **Evidence:** inspected settled/size captures and the live disabled Garage. The discrepancy is especially apparent at 1280×720 and 1600×900.
- **Severity:** Low.
- **Impact:** the necessary status is understandable, but the prominent middle plate reads as a mixture of finished artwork and placeholder UI treatment.
- **Suggested Improvement:** give the reusable blank status backing a restrained metal edge/fastener treatment and match its lettering contrast and finish to the plate while retaining independent status text and small-size readability.
- **Scope:** In Scope.
- **Corrective Work Type:** Existing Task.

## Recommended Next Round

If authorized by the human, prioritize selected-material refinement, then integrate the status badge. Re-run affected visual/navigation checks and observe the full entrance and repeated input transitions continuously where tooling permits. Confirm native keyboard and physical-controller behavior and listen to media continuity before making broader claims about input and animation/audio polish.

No recommendations have been implemented. **Stop for explicit human instruction**, as required by [the critique human gate](../critique.md#mandatory-human-gate). A failed score does not prevent the human from accepting the result as-is.

---

# Astra Runtime Critique — Story Round 2

Story: TS-122. Branch: `ts-122-jg`. Date: 2026-09-21.

**Overall Score: 7.7 / 10**

**Quality Assessment: FAIL (<8.0)**

**Approved adjustment achieved:** the menu is visibly smaller and positioned on the left, leaving the right-hand background title unobstructed at all three inspected resolutions. The assembly remains coherent and within the viewport, and relocated mouse targets work in the exercised flows. This is a successful implementation of the requested layout adjustment. The integrated score remains below the general Story polish threshold because the earlier material/badge findings remain visible and the reduced status lettering is particularly small at 640×360. The user did not authorize the Round 1 polish suggestions; this critique does not authorize them either.

## Category Scores

| Category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.2 | Current native Settings/browser return and Quit succeeded. |
| Controls / Responsiveness | 8.0 | Reduced, relocated mouse targets correspond to the visible plates and activate correctly. Keyboard/controller behavior was not independently retested this round. |
| Visual Quality | 7.4 | Left composition balances the background substantially better; retained selected coloration/status treatment and tiny low-resolution status prevent a highly polished rating. |
| VFX / Feedback | 7.5 | Selection remains clear at the reduced scale; previous saturated material concern persists. |
| UI / UX | 7.8 | Main actions remain easy to identify; secondary status legibility is weak in the smallest capture. |
| Runtime Stability | 8.2 | Final native playtest completed with a clean exit and no runtime log diagnostics. |
| Runtime Integration | 8.0 | Settings/browser round trips restore the settled left-hand menu and appropriate selected action. |

The overall score judges the integrated menu; it is not an average or a failure verdict on the narrowly requested placement change.

## What Was Exercised

- **VERIFIED — direct current visual inspection:** inspected the new `layout-640x360.png`, `layout-1280x720.png`, and `layout-1600x900.png` under `.godot/main-menu-checks`. The menu occupies the left portion consistently, its upper chains reach the viewport edge, and the background title is clear. Primary labels remain identifiable, without clipping.
- **VERIFIED — current native mouse playtest:** launched the production scene with `--max-fps 60`; observed the smaller left assembly at 1280×720, opened Settings through its relocated plate, clicked Back, opened Play through its relocated plate, returned from the browser, then clicked the relocated Quit plate. Settings and browser returns retained the settled composition with Settings and Play selected respectively. Quit exited with code 0. Log: `.godot/ts-122-astra-round2-retry.log`.
- **Supporting verification, supplied by implementation agent:** current `check-fast.ps1 -Area Client`, rendered startup verification and comprehensive `check.ps1` passed, including 500 Core and 337 transport tests. These are supporting evidence, not independent reruns or a basis for visual scoring.

## Runtime / Operational Limitations

The first escalated launch exited cleanly before a window could be captured; the bounded retry succeeded. One initial mouse action encountered changing desktop foreground/occlusion, so it was not counted as successful activation; after reactivating the game, Settings opened normally. No Godot editor interaction was performed.

The complete entrance and continuous frame pacing were not independently observed. No audio listening, physical controller, native keyboard navigation, Garage click, long-duration use or multiplayer session was independently retested in Round 2. Prior-round evidence and the current harness must remain distinguished from this round's direct observations. No score is assigned to audio, full-motion feel or measured performance. Unchanged background artifacts and destination-screen styling are excluded from this assessment.

## Recommendation

- **Problem:** at 640×360, uniform shrink leaves the Coming Soon status substantially smaller than the main labels and difficult to read at native image size.
- **Evidence:** direct inspection of the current 640×360 capture compared with the 1280×720 capture; the status is recognizable by context but is no longer comfortably readable. The live 1280×720 status remained readable.
- **Severity:** Low.
- **Impact:** players at the smallest supported viewport have less immediate explanation for the disabled Garage entry, although its dim appearance still communicates unavailability.
- **Suggested Improvement:** if separately authorized, preserve the requested overall size/left placement while giving secondary status text a minimum readable rendered size at small viewports; validate against the exact 640×360 native-size capture.
- **Scope:** In Scope for the Story's menu usability; outside the already completed, specifically authorized uniform-layout adjustment unless the human approves further refinement.
- **Corrective Work Type:** Existing Task.

The Round 1 material and badge recommendations remain unchanged advisory findings, supported again by the current captures and live menu. Their severity, impact and proposed corrections remain as recorded above. They were not implemented or newly authorized in this round.

## Recommended Next Round

The requested layout can be accepted as-is. If the human instead authorizes further refinement, first decide whether to address small-viewport status readability, then explicitly select any retained Round 1 visual suggestions. Do not begin another round automatically. No implementation was changed during this critique; **stop for explicit human instruction** under the critique human gate.
