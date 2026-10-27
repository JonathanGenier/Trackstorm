# Astra Runtime Critique — Story Round 7

**Story:** TS-245 — Jack-in-the-Box Oil Weapon, Projectile & Fluid Patches

**Branch:** `ts-245-pg`

**Overall Score: 7.9 / 10**

**Quality Assessment: FAIL (<8.0)**

The painted tank now communicates the approved joke directly on the vessel, and the mechanism retains its expressive spring, attached hoses, readable gauge and orderly firing/retraction sequence. Fresh native operation passes. Breaking the airborne hairpin improves the previous continuous folded tail, and the splash no longer depends on tall hooked spokes. However, the resulting airborne fragments still form conspicuous parallel pointed slivers, and complete multiplayer continuation after host loss remains broken in the retained extended scenario. The result is improved but does not meet the integrated 8.0 standard of highly polished, robust operation. This is an experiential judgment, not an arithmetic average or a reward for verification volume.

The user explicitly authorized this additional round after Round 6, overriding the older three-round cap for this assessment. Latest approved lettering supersedes stale Jira wording: one bold black line of Laughing Gas painted onto the enlarged tank, Gas crossed out in red, with larger red handwritten oil beneath. TS-247 balance work remains excluded. This review preserves the approved character, box, spring, rack position, hoses, brass fittings, mouth sequence, one-second forty-parcel burst and thirty-second timer-only hazard.

## Category Scores

| Material runtime category | Score | Evidence and judgment |
| --- | ---: | --- |
| Runtime Functionality | 8.5 | Fresh native firing, supported deposition, gauge progression and replacement pass; retained normal-session lifetime and repeated-use evidence is strong. |
| Visual Quality | 8.3 | Curved painted lettering satisfies the revised treatment. Hardware and character form a coherent assembly; exact small text is less readable at chase-camera distance than in close inspection. |
| Animation / Motion | 8.4 | Loose spring response, hose following and close-before-retract timing remain coherent in fresh chronological captures. |
| VFX / Feedback | 7.6 | Better terminal separation and lower impacts, but elongated pointed fragment bundles remain visibly constructed. Rounded impact lobes merge and disappear, although their brief clustered silhouette is heavy. |
| Physics / Presentation Clearance | 8.3 | Fresh moving samples show no box-interior vertices; retained bank/support and 780-pose sweeps strengthen coverage without proving every continuous pose. |
| Multiplayer / Networking Experience | 7.0 | Fresh impaired admission and retained focused firing reconnect succeed. Extended migration strands a surviving peer. Main reproduces that continuation defect, clarifying ownership without removing its player impact. |
| Runtime Stability | 7.7 | Fresh run exits cleanly. Retained migration has a real invalid continuation outcome; the intermittent rack shutdown warning remains unresolved despite a clean unchanged-source retry. |
| Runtime Integration | 7.8 | Normal play, replacement, recovery from temporary disconnection and accepted-clock reconstruction cooperate. Continued synchronized expiry after authority replacement is not established. |
| Game Feel / Juice | 7.9 | Anticipation, recoil, draining gauge and orderly recovery give the action character. Residual fragment shapes limit the central visual payoff; physical control feel and audio were not evaluated. |

No code-quality, architecture, test-design or delivery score is assigned. No separate score is asserted for human enjoyment, controller ergonomics, general vehicle handling or release performance.

## What Was Exercised

### Review inputs

**VERIFIED — inspected:** root routing, workflow, critique rules, the relevant feature-index entry and Persistent Oil documentation, current Round 7 verification/evidence, prior Round 6 critique, and all four downloaded Jira references: closed, deployed, firing and Laughing oil 2. The assignment supplies the refreshed Jira timestamp `2026-10-13 11:18:40 -0400`, zero comments/subtasks and approved changes; this reviewer did not independently query Jira again.

### Fresh independent native execution and observation

Executed once against the completed existing build with pinned Godot 4.7.2 .NET, local UDP/cache access and NVIDIA GTX 1070 Compatibility rendering:

```powershell
./check-oil-jack.ps1 -GodotPath 'C:\Users\orsin\OneDrive\Desktop\ThanTrick\Trackstorm\.godot\godot-ci\4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Visual -Impaired -VisualOnly
```

- **VERIFIED:** exit 0, host lifecycle **105.35 seconds**, no ERROR/WARNING output; [fresh log](astra-native-runtime.log), [evidence](astra-native-evidence.txt), [trace](astra-native-trace.json). Two real UDP peers initially and a third admitted during airborne firing, with 30 ms delay, 5 ms jitter and 2% loss. Both recorded Welcome messages follow Connected.
- **VERIFIED:** native acceleration/braking/reacceleration, straight burst and bump traversal, steering/throttle/handbrake on the bank, airborne activation/landing, vertical first-hit deposition, Oil-to-Wrench replacement, and the 23-degree asphalt ramp support query. The ramp query is not a driven crest. Each burst retains sixty firing ticks, forty emitted parcels and one logical hazard. Turning records 331 supported ticks and 4.509 m/s peak slip.
- **VERIFIED:** forty rendered flight samples report zero liquid vertices inside the box. Four named firing readbacks place the first liquid vertex 0.018 m from the displayed mouth. Spring peaks are **0.446 / 0.429 / 0.446 m** fore/aft, lateral and vertical. Non-null trace hose endpoint errors have maximum zero. These are sampled measurements, not a complete swept-volume proof.
- **VERIFIED:** 86 observer gauge samples against accepted clocks, maximum publication lag twelve ticks and delayed level difference 0.200. Viewed [full](astra-native-meter-full.png), [partial](astra-native-meter-half.png) and [empty](astra-native-meter-empty.png). The saved partial image is level **0.449**, not exactly half; firing geometry partially occludes this inspection-camera close-up. Full/draining/empty/new-pickup reset passed. Trace frame 1900 has mouth opening approximately 0.14 with extension 1; frame 1910 has closed mouth with extension approximately 0.65; extension reaches zero by 1930.
- **VERIFIED — direct capture observation:** inspected full-resolution tank, straight firing, impact, airborne firing, gauge and mouth-closing captures, plus **39 chronological images** arranged in [deployment](astra-sequence-deployment.png), [straight](astra-sequence-straight.png), [airborne](astra-sequence-airborne.png) and [replacement](astra-sequence-replacement.png) sheets. Also inspected full-resolution airborne frames [1146](astra-frame-01146.png) and [1158](astra-frame-01158.png). Sheets resize and label genuine rendered frames; their spacing is based on **60 Hz simulation frames**, not recorded wall-clock video. Early airborne-sheet frame 1110 still shows the previous deposit camera and does not establish the hidden discharge.

This was meaningful fresh native automated execution followed by direct capture observation, **not physical keyboard/controller playtesting or live audiovisual observation**. VisualOnly ends before expiry and twelve-additional-use stages. Fresh raw frames remain under `.godot/ts-245-round7/runtime`; retained copies use `astra-*` names. [Source hashes](astra-native-sources.json) identify originals and copied outputs. The user's editor was untouched. No production, test, asset, Git, Jira or PR changes were made.

### Retained evidence reviewed separately

- **VERIFIED — evidence inspection; INFERRED — recorded operational outcomes:** the [full four-peer evidence](evidence.txt) records activation 1834, deadline and first absence 3634, exactly thirty seconds, followed by cosmetic fade; twelve repeated uses conclude with no hazards or owned Oil representations on all four peers. Viewed the retained [expiry fade](expired-fading.png). These stages were not independently repeated by this reviewer.
- **VERIFIED — image inspection:** retained [production chase-camera tank](gameplay-meter-full.png) shows the paint in actual 1280×720 framing. The broad joke and red correction remain visible, while fine lettering and exact gauge divisions need closer viewing. Retained [hard-reversal bank deposits](bank-bank-1-deposits.png) show coverage on the bank; gray tire marks are separate from Oil. The reported 179.72-degree grass-to-asphalt reversal, 168 supported pieces and zero unsupported deposits are retained measurements, as are the 780-pose clearance sweep and eighteen developer controls.
- **VERIFIED — recovery-log inspection:** [focused Oil reconnect](oil-reconnect-final.log) restores an observer at tick/start 430 with one emitted/in-flight parcel and original deadline 2230. Displayed restored gauge is 0.833 versus accepted 0.867, extension 1. The firing-owner case resumes at tick 2551, start 2478, forty parcels and deadline 4278; both cases complete synchronized fade cleanup. Generic reconnect passes three resynchronizations including 125 simulation seconds offline. These are current passes, not proof that historical failures were repaired.
- **VERIFIED — failure-log inspection:** [extended Oil migration](oil-migration-final.log) leaves peer 0 inactive at tick 491 holding an unfaded hazard, while successor peer 2 reaches 2435 with no hazard/visual. [Archived main continuation](main-continuation.log) reproduces a survivor frozen at 428 while its successor reaches 611. [Control provenance](main-control-provenance.json) records 709 byte-identical C# files and only two changed verification fixtures; the retained fixture diffs/provenance support this specific baseline reproduction. It does not establish the cause of every previous pre-agreement failure. Short migration passes do not establish sustained participation.
- **VERIFIED — rack outcome inspection:** [first rack attempt](rack-final.log) passes 482 behavioral checks but fails shutdown with 22 leaked ObjectDB instances and two resources in use. [Unchanged-source verbose retry](rack-verbose-retry.log) passes 482 checks, exit 0, 248.74 seconds without ERROR/WARNING output. The warning is intermittent and **not claimed fixed**. Reported contacts/scoring, twenty handling trials, startup and repository checks are additional retained confidence, not freshly executed critique results.

## Material Findings and Recommendations

### 1. Host migration still leaves a participant unable to continue

- **Problem:** restored state can look correct at installation while a surviving participant remains inactive and never reaches hazard expiry/cleanup.
- **Evidence:** [final extended Oil migration](oil-migration-final.log), supported by [main continuation control](main-continuation.log). The former retains the hazard on the frozen participant after the successor has expired it.
- **Severity:** High.
- **Impact:** a player can lose participation in a match and retain stale hazard presentation after host loss. Baseline provenance prevents assigning this defect to TS-245, but does not make the integrated experience robust.
- **Suggested Improvement:** separately authorize investigation of post-installation authority/fencing/participant continuation, retaining the extended per-peer progress and cleanup assertions. Verify every survivor advancing through original Oil expiry and cosmetic cleanup after correction; do not accept checkpoint installation alone as recovery success.
- **Scope:** required recovery evidence is **In Scope** for the Story. Shared migration repair is an **Out-of-Scope Recommendation** for this approved visual-polish round; the baseline reproduction supports separate ownership.
- **Corrective Work Type:** **New Corrective Task** if the human authorizes shared recovery work. No issue was created.

### 2. Broken liquid tails still resolve into pointed parallel slivers

- **Problem:** segmentation removes the continuous folded hairpin, but several separated groups remain long, straight and sharply tapered, reading as splinters or narrow blades amid round droplets. This is visible across successive samples, not just one unfortunate still.
- **Evidence:** [fresh airborne firing](astra-native-stage-3-firing.png), full-resolution [frame 1146](astra-frame-01146.png) and [frame 1158](astra-frame-01158.png), and the [chronological airborne sheet](astra-sequence-airborne.png). The former connected tail is improved; the residual concern is the shape and coherence of the detached groups. Fresh [impact](astra-native-impact.png) shows lower, merged lobes replacing the previous hooked crown.
- **Severity:** Medium.
- **Impact:** the central discharge remains less convincing as thick turbulent liquid than the approved firing reference, limiting the finish of an otherwise expressive mechanism.
- **Suggested Improvement:** if further polish is authorized, shorten detached high-aspect-ratio groups, round their terminal silhouettes and vary breakup per branch so several parallel pointed pieces do not detach together. Validate consecutive default-width airborne frames and impact settling at the production camera. Preserve authoritative parcels, trajectory, coverage, clearance and cosmetic budgets; no hardware redesign is recommended.
- **Scope:** **In Scope** for the TS-245 fluid presentation.
- **Corrective Work Type:** **Existing Task**.

## Runtime / Operational Limitations

The fresh run renders the host viewport. Accepted observer gauge samples establish state progression, not perceived remote spring/hose smoothness or simultaneous displayed levels. Reconnect and extended migration judgments here use retained native logs, not an independent rerun. The fresh presentation run does not reach thirty-second cleanup. No general recovery fix or all-green integration claim is made.

Sampled clearance is not exhaustive for triangle interiors, arbitrary continuous poses, hose sweeps, surfaces or tuning combinations. Native support sampling remains finite. Capture timing and forced rendering do not establish live audiovisual smoothness. Retained repeated-use process intervals (median 29.417 ms, p95 72.143 ms, maximum 118.232 ms) include multiple loaded arenas and capture overhead; they are not GPU measurements, a normal-gameplay FPS guarantee or an isolated Oil regression benchmark.

**UNVERIFIED:** human controller/keyboard feel and enjoyment, audio listening, authenticated EOS/WAN/multi-device recovery, release export and other hardware, all tuning combinations, exhaustive clearance, prolonged soak, and TS-247 balance. Independent engineering review remains separate.

## Recommended Next Round and STOP

If the human authorizes more work, prioritize separately scoped shared migration continuation work and retain the new sustained cleanup test. The only further presentation recommendation is detached-liquid silhouette refinement; preserve the now-correct painted tank and approved mechanism. The intermittent rack shutdown warning should remain disclosed and receive targeted investigation if it recurs; its clean retry is not a repair.

**STOP — Story Round 7 is complete at 7.9 / 10, FAIL.** No recommendations were implemented. Human acceptance or explicit authorization is required before any correction or further round. The target of 8.0 does not change the observed assessment.
