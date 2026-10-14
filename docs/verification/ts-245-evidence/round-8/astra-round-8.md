# Astra Runtime Critique — Story Round 8

**Story:** TS-245 — Jack-in-the-Box Oil Weapon, Projectile & Fluid Patches
**Branch:** `ts-245-pg`
**Overall Score: 7.9 / 10**
**Quality Assessment: FAIL (<8.0)**

The tank artwork is a successful improvement toward Laughing oil 2: visible harlequin diamonds surround a curved ivory lettering field, the outlined circus wordmark belongs to the vessel, and the red strikeout/oil correction remains recognizable from the rear gameplay camera. The gauge and expressive mechanism still work. This is a better tank, but the integrated Story still falls short of highly polished, robust operation because of the pointed detached liquid and the unresolved shared migration continuation failure. Those problems were not authorized for correction in this tank-only round. The score evaluates the resulting experience, not the size of the art change, test count or a rounded route to 8.0.

The user explicitly authorized Round 8 after Round 7, superseding the older three-round cap. The approved box/jester, hoses/fittings, spring, mouth, rack, one-second forty-parcel burst, timer-only thirty-second lifetime and fade remain the governing scope. TS-247 is excluded. This reviewer read repository routing, workflow, critique rules, the feature index, current Persistent Oil documentation, the current Round 8 verification section and relevant Round 7 evidence. The parent supplied its complete refreshed Jira read (updated `2026-10-13T11:18:40.382-0400`, zero comments/subtasks); this reviewer did not query Jira independently.

## Category Scores

| Material runtime category | Score | Observed judgment |
| --- | ---: | --- |
| Runtime Functionality | 8.5 | Fresh native firing, deposition, gauge and replacement work; retained full lifecycle and repeated-use evidence support normal operation. |
| Visual Quality | 8.5 | The tank now shares the concept's circus-industrial decoration and direct painted branding; exact fine lettering is distance dependent. |
| Animation / Motion | 8.4 | Spring movement, attached hoses and ordered closure/retraction remain coherent across chronological samples. |
| VFX / Feedback | 7.6 | Clear mouth emission, falling droplets and retained puddles; detached parallel pointed fragments still look constructed. |
| Physics / Presentation Clearance | 8.3 | Fresh moving samples show no box-interior liquid vertices; retained bank and 780-pose checks provide broader finite coverage. |
| Multiplayer / Networking Experience | 7.0 | Fresh impaired admission and retained focused reconnect work. Historical sustained migration leaves a survivor unable to continue. |
| Runtime Stability | 7.8 | Fresh run and final retained rack run are clean. Historical migration failure remains material; the earlier intermittent rack warning is not established as repaired. |
| Runtime Integration | 7.8 | Ordinary firing, temporary-disconnection recovery and replacement cooperate; synchronized continuation after authority replacement remains unresolved. |
| Game Feel / Juice | 7.9 | Characterful deployment, recoil, drain and recovery support the action; fragment silhouettes limit the central liquid payoff. |

These are holistic category judgments, not an arithmetic formula. No score is assigned to code, architecture, test design, PR hygiene, unobserved human enjoyment or release performance.

## What Was Exercised

### Fresh independent native execution — VERIFIED

Executed the completed build once with Godot 4.7.2 .NET, GTX 1070 Compatibility rendering, local UDP and user-cache access:

```powershell
./check-oil-jack.ps1 -GodotPath 'C:\Users\orsin\OneDrive\Desktop\ThanTrick\Trackstorm\.godot\godot-ci\4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Visual -Impaired -VisualOnly -GameplayCamera
```

- **PASS**, exit 0, host lifecycle **117.787 seconds**, no ERROR/WARNING diagnostics. [Fresh log](astra-native-runtime.log), [fixture evidence](astra-native-evidence.txt), [218-sample trace](astra-native-trace.json).
- Two real UDP peers initially; a third admitted during airborne firing. Impairment was 30 ms delay, 5 ms jitter and 2% loss. Both Welcome messages follow Connected.
- Native acceleration/braking/reacceleration, banked straight firing, steering/throttle/handbrake, airborne emission/landing, vertical first-hit deposition and Oil-to-Wrench replacement ran. The separate 23-degree ramp support query is not a driven crest. Bursts retained sixty firing ticks, forty emitted parcels and one logical hazard; turning recorded 331 supported ticks and 4.509 m/s peak slip.
- **87 observer gauge samples** passed accepted-clock checks, with maximum publication lag twelve ticks and delayed level difference 0.200. Inspected actual 1280×720 [full](astra-native-meter-full.png), [partial](astra-native-meter-half.png) and [empty](astra-native-meter-empty.png) gameplay views. The saved partial frame corresponds to the last logged level **0.267**, not exactly half. The stream partially obscures the barrel during discharge; the right-hand gauge remains distinguishable. Fresh-pickup reset passed.
- Forty rendered flight samples reported zero liquid vertices inside the box. Four named firing readbacks measured the first liquid vertex 0.018 m from the displayed mouth. Spring peaks were **0.446 / 0.424 / 0.438 m** fore/aft, lateral and vertical; maximum non-null hose endpoint error was zero. At frame 1910 mouth opening was 0.290 with full extension; at 1920 it was closed with extension 0.618; at 1930 extension was zero. These sampled measurements support ordered recovery and attachment, not exhaustive swept-volume clearance.
- Directly inspected **48 chronological samples** in [driving](astra-sequence-driving.png), [turning](astra-sequence-turning.png), [airborne](astra-sequence-airborne.png) and [replacement](astra-sequence-replacement.png) sheets, plus full-resolution [airborne frame 1158](astra-frame-01158.png). The airborne camera faces the car's front; the rear branding judgment comes from the rear meter/driving views. Full-resolution rear meter images and retained close-ups were evaluated separately from the downsized sheets.

This is fresh native scripted execution followed by capture inspection, **not human playtesting or live audiovisual observation**. The sheets use actual 60 Hz simulation-frame numbers, not wall-clock video timing. VisualOnly ends after replacement; it does not freshly verify thirty-second expiry or twelve repeated uses. The user's editor was left untouched.

### Reference fidelity — VERIFIED by direct image inspection

Viewed all four actual concepts: closed, deployed, firing and the primary Laughing oil 2 tank reference. The primary tank has harlequin paint, industrial bands/fittings, a gauge and painted branding; it does not have a clown face on the tank. The approved face on the box remains appropriate and unchanged.

The new barrel captures that design hierarchy substantially better than a largely plain lettering field. [Retained close-up](meter-full.png) shows the diamonds, scalloped ivory paint, ochre outline/flourish, red correction and curvature. [Fresh rear gameplay view](astra-native-meter-full.png) retains the broad black wordmark, red oil and patterned vessel identity. Fine stars/pinstripes become small color detail at gameplay distance; the right-side gauge and curvature partly crowd the Gas end of the wordmark. The treatment is brighter and more graphic than the reference's heavily worn industrial finish, but it fits the rendered approved assembly. I do not recommend another mechanism redesign or invent a tank face requirement. Exact lettering is easier to read close up than while driving.

### Retained verification — inspected separately

**VERIFIED — evidence inspection; INFERRED — recorded operational outcomes:** the current [full-run evidence](evidence.txt) records activation **1858**, original deadline and first absence **3658**, then cosmetic fade. Twelve repeated activations finish with no hazards or owned Oil representations on all four peers. This was the implementation verifier's full run, not the fresh critique execution.

[Production-bank evidence](bank-evidence.txt) records a 179.72-degree reversal and 168 supported deposited pieces with zero unsupported pieces; inspected its chronological bank sequence. [Clearance evidence](clearance-evidence.txt) records 780 sampled poses. [Focused firing reconnect](oil-reconnect-final.log) records accepted-state restoration, forty parcels per use and synchronized timer/fade cleanup. [Final rack log](rack-final.log) records 482 passing handoff checks without ERROR/WARNING output. Contacts, twenty handling trials, startup and the repository gate are reported in the current Story verification; they are not independently rerun here and their count does not determine this score.

The prior [extended Oil migration failure](../round-7/oil-migration-final.log) leaves peer 0 inactive at tick 491 with an unfaded hazard while the successor reaches 2435 with no hazard. The [archived main continuation control](../round-7/main-continuation.log) likewise freezes a survivor at 428 while its successor reaches 611. [Control provenance](../round-7/main-control-provenance.json) records 709 byte-identical C# files and two fixture-only differences. This supports the shared baseline ownership of that specific continuation failure, not attribution to TS-245 or a diagnosis of every historical migration failure. Migration was neither rerun nor repaired in Round 8.

## Material Findings and Recommendations

### 1. Historical shared migration continuation remains unresolved

- **Problem:** after authority replacement, a survivor can remain inactive with stale hazard presentation while the successor continues and expires the hazard.
- **Evidence:** retained extended Oil migration and archived main control linked above. This is historical direct runtime evidence, not a new Round 8 reproduction.
- **Severity:** High.
- **Impact:** the affected player cannot continue the match and does not receive expected hazard cleanup. Shared baseline ownership does not remove this experiential impact.
- **Suggested Improvement:** separately authorize shared post-installation authority/fencing/participant continuation investigation. Preserve assertions that every surviving peer advances through the original Oil deadline and visual cleanup; checkpoint installation alone is insufficient.
- **Scope:** recovery evidence is In Scope for the integrated Story; shared migration repair is an **Out-of-Scope Recommendation** for the approved tank-art round.
- **Corrective Work Type:** New Corrective Task if authorized. No issue was created.

### 2. Airborne liquid still breaks into pointed parallel fragments

- **Problem:** detached groups retain straight, sharply tapered silhouettes that resemble narrow splinters among round droplets.
- **Evidence:** fresh full-resolution [frame 1158](astra-frame-01158.png) and [airborne chronology](astra-sequence-airborne.png); the current retained [inspection-camera firing view](stage-3-firing.png) makes the same shape clearer. Consecutive samples establish persistence beyond one still.
- **Severity:** Medium.
- **Impact:** discharge reads less convincingly as viscous turbulent liquid than the approved firing reference, limiting polish of the central action despite the improved tank.
- **Suggested Improvement:** if authorized, shorten detached high-aspect-ratio groups, round their ends and offset breakup between branches. Compare consecutive default-width airborne frames at the production camera while preserving trajectory, authoritative parcels, coverage, clearance and existing cosmetic budgets.
- **Scope:** fluid presentation is In Scope for TS-245, but further correction is an **Out-of-Scope Recommendation** for this tank-only authorization.
- **Corrective Work Type:** Existing Task, subject to explicit human authorization.

## Runtime / Operational Limitations

Fresh observer measurements establish accepted state progression, not simultaneous displayed remote smoothness. Only the host viewport was inspected. Extended expiry, repeated use, bank support and reconnect conclusions here rely on retained evidence. The earlier intermittent rack shutdown warning was absent from the final Round 8 rack run; an artwork change does not establish its repair.

Retained repeated-use process intervals are median **86.884 ms**, p95 **140.356 ms**, maximum **149.488 ms**. This is slower than the prior sample, but forced rendering, multiple loaded arenas, capture and running user applications prevent a controlled before/after comparison or Oil-specific attribution. It does not establish normal-gameplay FPS or live smoothness. Asset dimensions/budgets are reported unchanged.

**UNVERIFIED:** human keyboard/controller feel and enjoyment, audio listening, EOS/WAN/multi-device operation, other hardware and release export, exhaustive continuous-pose clearance, all tuning combinations, prolonged soak and TS-247 balance. Independent engineering review remains separate.

Fresh copies and contact sheets use `astra-*` names. [Provenance manifest](astra-native-sources.json) records raw source SHA-256 separately from retained SHA-256; copied text is UTF-8/LF with incidental trailing whitespace normalized. Derived sheets list each original frame hash. Existing inspected references/logs have review-time source hashes. No production, asset, gameplay, networking, Git, Jira or PR changes were made by this reviewer.

## Recommended Next Round and STOP

Accept or reject this tank-art outcome on its merits; another tank redesign is not the recommended route to solving the remaining integrated findings. If further work is authorized, prioritize separately owned sustained migration continuation, then detached-liquid silhouette refinement. Preserve the approved assembly and the improved barrel artwork.

**STOP — Story Round 8 is complete at 7.9 / 10, FAIL.** No recommendation was implemented. Under `docs/critique.md`, further correction or another critique requires explicit human direction; no human acceptance or playtest pass is claimed.
