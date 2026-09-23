# Astra Runtime Critique — Story Round 1

TS-138 · `ts-138-jg` · 2026-09-22 · Independent Astra review of the completed Play Menu runtime after implementation verification.

**Overall Score: 7.7 / 10**

**Quality Assessment: FAIL (<8.0)**

The exercised flows are reliable and the hanging artwork gives the menu a clear identity. Host, locked-lobby and retained-session decisions are readable and coherent. However, the main browser's content visibly overlaps the frame's metal rails, including its column heading and status text. That central presentation defect keeps this UI Story below the highly polished 8.0 threshold. The overall score weights the browser's presentation and usability heavily; it is not an arithmetic average or a reward for automated test counts.

## Category Scores

| Category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.5 | Search/filter combinations, empty/pending/failure states, host and join actions, retained decisions and repeated navigation completed in rendered execution. |
| Controls / Responsiveness | 8.1 | Native input events exercised deliberate mouse double-click and logical double-Accept, expiry, focus changes, filter selection/cancel and transition input gates. |
| Visual Quality | 7.1 | Strong cohesive art and independent fabric movement; browser controls and text intrude onto the frame instead of fitting its interior. |
| UI / UX | 7.3 | Clear action hierarchy and recovery forms, but the main table/status layout lacks the inset and visual containment present in the forms. |
| Runtime Stability | 8.5 | All three independent rendered runs completed cleanly outside the restrictive sandbox, with no Godot errors or warnings. |
| Runtime Integration | 8.2 | Main/Play return paths, Settings/browser return and persistent MenuShell media passed the real startup integration; retained-flow presentation passed controlled-provider integration. |

Audio, physical-controller ergonomics, remote networking quality, gameplay/vehicle feel and sustained performance are not scored. Motion evidence supports integration and visual observations, but this review does not claim a continuous human motion or frame-pacing assessment.

## What Was Exercised

**VERIFIED — independently executed for this critique**, using the existing final Debug build, Godot .NET 4.7.2, Windows OpenGL compatibility renderer and NVIDIA RTX 4070 Laptop GPU:

- `./check-play-menu.ps1 -GodotPath <Godot executable> -Visual -SkipBuild`: passed. Exercised 0/100 rows, fixed scrolling, search plus filters, filter cancellation and choices, mouse and keyboard/controller-equivalent join gestures, expiry/focus changes, pending/failing discovery, retryable hosting, three Main/Play cycles, Direct-IP return and 640×360 / 1280×720 / 1600×900 layouts.
- `./check-online-lobby.ps1 -GodotPath <Godot executable> -Visual -NoBuild`: passed. Exercised asynchronous admission, locked-lobby access, retained lookup/validation, authoritative decision gating and idempotent reconnect/release controls using a controlled provider.
- `./check-startup.ps1 -GodotPath <Godot executable> -Visual -SkipBuild`: passed. Exercised real MenuShell media, phase-aware recovery, drop input gating, mixed input navigation, repeated navigation and Settings/browser return.

**VERIFIED — directly inspected rendered images** in `.godot/play-menu-checks/`: all three `layout-*` sizes, `filtered-empty.png`, `discovery-failure.png`, `scrolled-bottom.png`, `host-form.png`, `filter-choices.png`, and the two `flag-*` samples. The samples show changing fabric contours with the rigid assembly remaining in place. Inspected fresh `.godot/online-lobby-checks/retained-choice.png`, `retained-failure.png` and `locked-prompt.png`; the decision text fits and recovery actions remain clear. Inspected the fresh `.godot/main-menu-checks/play-menu-shell.png` over the actual video background; the offline message now clearly distinguishes unavailable discovery from a successful empty search.

These were scripted interactions with rendered production controls and direct screenshot inspection, not a free-form human mouse/controller play session. The implementation team's broader test and local UDP reconnect results remain recorded in [the verification report](ts-138.md); they are not represented as independently repeated here.

## Material Finding

### Browser content overlaps its containing artwork

- **Problem:** Search, table rows and column headings extend over the side rails, while status text begins on the left metal rail. The browser looks superimposed on the frame rather than fitted into it. Host and retained forms demonstrate a cleaner inset in the same assembly.
- **Evidence:** Fresh `layout-1600x900.png` places the left edge of the browser near x=203, over a metal rail whose interior begins around x=237. `LOBBY NAME` and `Lobby browser updated.` cross the textured rail. The same issue is visible in the 1280×720 and 640×360 captures, in `discovery-failure.png`, and in the actual-background `play-menu-shell.png` status line. Row backgrounds also obscure the rail. These approximate coordinates describe visual inspection, not a pixel-boundary measurement.
- **Severity:** Medium.
- **Impact:** Repeatedly used content competes with bright, detailed artwork, weakening readability and the intended physical-panel composition. The effect persists at every exercised size and in normal, empty and failure states.
- **Suggested Improvement:** Fit the browser controls, header, scrolling rows and status text inside a shared safe interior rectangle, with consistent padding from both rails. Preserve aligned columns and usable widths across the three tested sizes; check the full-scroll boundaries and long failure text after resizing.
- **Scope:** In Scope.
- **Corrective Work Type:** Existing Task — correction within the existing TS-138 Play Menu implementation; no new feature is needed.

## Runtime / Operational Limitations

- Initial sandboxed Play execution reached its success marker but emitted user-log write and Windows certificate-store errors. The unrestricted rerun passed without those diagnostics; they are treated as execution-environment restrictions, not a product defect.
- Authenticated EOS, real remote discovery/mode publication, multi-PC Internet/NAT and exported builds were not exercised. Controlled-provider UI results do not establish network interoperability.
- Controller events were synthetic. Physical devices, controller-only text entry and audible continuity were not evaluated. Media persistence assertions do not establish what a listener hears.
- Screenshots sample visual states; smoothness, precise frame pacing, accessibility with assistive technology and extended device performance remain unverified.
- Evidence images remain local in `.godot`; they are not committed artifacts.

## Recommended Next Round

If the human authorizes correction, address the shared browser interior layout first, rerun the affected rendered checks, and inspect normal, scrolled, empty and error states at all three resolutions and over the real MenuShell background. Preserve the already working join, retained-session and transition behavior.

No corrective implementation, new issue, commit or push was performed by this reviewer. **Stop for explicit human instruction**, as required by [the critique policy](../critique.md#mandatory-human-gate).
