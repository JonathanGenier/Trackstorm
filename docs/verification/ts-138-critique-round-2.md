# Astra Runtime Critique — Story Round 2

TS-138 · `ts-138-jg` · 2026-09-22 (America/Toronto) · User-authorized correction and independent second critique, following implementation verification.

**Overall Score: 8.3 / 10**

**Quality Assessment: PASS (>=8.0)**

The exercised Play Menu now has a coherent, finished presentation. Search, headings, rows and status fit inside the frame, four complete rows remain visible at rest, and the version has its own unobstructed footer. The material frame-overlap finding from [Round 1](ts-138-critique.md) is resolved in fresh renders at all three tested sizes and over the actual MenuShell background. Pending passive lookup no longer makes Host or Back unusable; explicit retained validation and the authority-confirmed decision remain distinct. No material experiential defect was found in the exercised scope. The score reflects the observed result, not the number of tests or the critique round.

## Category Scores

| Category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.5 | Browser, filters, hosting, join gestures, empty/failure recovery and retained-session paths completed in rendered execution. |
| Controls / Responsiveness | 8.3 | Held pointer, keyboard and synthetic controller Host/Back worked during pending passive lookup; deliberate join gestures and transition gates remained predictable. |
| Visual Quality | 8.2 | Cohesive hanging artwork, clean content insets, readable aligned columns and a separated version footer across tested sizes. |
| UI / UX | 8.3 | Clear normal, empty, pending, error and decision states; filter choices no longer compete with status text, and recovery actions remain legible. |
| Runtime Stability | 8.5 | Three independent rendered runs exited successfully without Godot errors or warnings. |
| Runtime Integration | 8.3 | Repeated Main/Play navigation and real MenuShell persistence passed; controlled-provider validation, resume and abandonment remained correctly gated in use. |

Audio, physical-controller ergonomics, remote-network quality, vehicle/gameplay feel and sustained performance are not scored. Sampled motion and transition assertions support the observations, but do not establish continuous human motion or frame-pacing quality.

## What Was Exercised

**VERIFIED — independently executed for this critique**, sequentially using the final existing Debug build, Godot .NET 4.7.2, Windows OpenGL compatibility renderer and NVIDIA RTX 4070 Laptop GPU:

- `./check-play-menu.ps1 -GodotPath <Godot executable> -Visual -SkipBuild` — **PASS**. Exercised 0/100 rows, scrolling, search/filter combinations and cancellation, native mouse double-click, logical double-Accept, expiry/focus changes, discovery progress/failure, retryable hosting and repeated Main/Play/Direct-IP return. The pending passive-lookup case exercised held mouse clicks, keyboard and controller-equivalent Host/Back, visible upward departure, restored Main interaction and eventual missing-hint recovery. Layout, child bounds, version and footer assertions passed at 640×360, 1280×720 and 1600×900.
- `./check-online-lobby.ps1 -GodotPath <Godot executable> -Visual -NoBuild` — **PASS**. Exercised locked credentials and asynchronous admission; Play entry initiated existing retained validation once. Pointer No followed by a keyboard duplicate produced one abandonment request; controller Yes followed by a keyboard duplicate produced one resume request. Browser content stayed hidden through the unresolved authority decision, and failed recovery preserved dismissal/retry behavior. This used controlled providers and authority seams.
- `./check-startup.ps1 -GodotPath <Godot executable> -Visual -SkipBuild` — **PASS**. Exercised the production main scene, original startup recovery/drop gates, mixed input, repeated navigation, Settings/browser return and persistent MenuShell media. No diagnostic output.

**VERIFIED — directly inspected fresh rendered screenshots:**

- `.godot/play-menu-checks/layout-640x360.png`, `layout-1280x720.png`, `layout-1600x900.png`: content clears both rails; four rows and headings align; `v0.1.29` remains separate from the rig. The smallest layout is necessarily compact, but has no observed clipping or collisions.
- `scrolled-bottom.png`, `filtered-empty.png`, `loading.png`, `discovery-failure.png`, `host-form.png`, `host-failure.png`, `no-previous-game.png`, `filter-choices.png`: scrolled rows, status, forms and choice content remain contained and readable. The filter view has no overlapping status line.
- `flag-a.png` / `flag-b.png`: fabric contours change independently while the rigid composition stays fixed in the sampled views.
- `.godot/online-lobby-checks/retained-choice.png`, `retained-failure.png`, `retained-leaving.png`, `retained-reconnecting.png`, `locked-prompt.png`: decision, progress and recovery text fits; enabled actions and withheld browser controls match the exercised states.
- `.godot/main-menu-checks/play-menu-shell.png`: the corrected interior and version footer remain clear over the actual video background; unavailable online discovery is explicitly identified with a visible Direct-IP/LAN fallback.

These are scripted interactions with rendered production controls plus direct screenshot inspection, not a free-form human play session. Broader comprehensive tests and the separate menu regression are recorded in [the verification report](ts-138.md); this reviewer did not independently repeat those suites.

## Runtime / Operational Limitations

- Authenticated remote EOS, real mode publication/discovery, multiple physical PCs, Internet/NAT and exported builds were not exercised. Controlled-provider retained-flow results do not establish remote interoperability. Historical local UDP reconnect evidence was not rerun for this UI correction critique.
- Controller events were synthetic. Physical hardware ergonomics, controller-only text entry, assistive technology and audible media continuity remain **UNVERIFIED**. Persistent media-instance checks do not establish what a listener hears.
- Screenshot samples and scripted transition assertions do not establish continuous smoothness, precise frame pacing or extended device performance.
- Renders and logs remain local under `.godot`; they are not committed evidence assets. All three runs used approved execution outside the restrictive sandbox to allow normal Godot logging and Windows certificate access.

No implementation changes, new tasks, commits or pushes were made by this reviewer. This PASS does not authorize another improvement round or imply human acceptance. Stop for the human decision required by [the critique policy](../critique.md#mandatory-human-gate).
