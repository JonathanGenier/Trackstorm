# TS-73 editor import and multiplayer menu correction

User-authorized correction on `ts-73-pg`, version 0.1.7, synchronized with main `aa05b00` on 2026-09-19. Driving settings and physics are unchanged from [the handling correction](ts-73-correction.md).

## Findings and changes

- The committed diagnostic `baseline-network-crossing.csv` was being imported as a Godot translation table. Its vector values became invalid Windows translation filenames, producing the reported repeated file errors. `docs/verification/.gdignore` excludes historical evidence from resource importing. The CSV's generated `.import` and `.translation` sidecars were removed locally; source evidence remains intact.
- The session menu used `HasRetainedDecision` to hide PlayerName, while the browser used `ShowsRetainedDecision`. A pending read-only startup lookup or failed recovery therefore hid the name without displaying a decision. Host Game could remain enabled even though `Create` silently rejected the same state. A new production UI regression reproduced the hidden field after failed recovery before the fix.
- Name visibility now follows the actual modal decision. Create/Join and Host Game share the coordinator's fresh-admission gate. Fresh admission supersedes a read-only startup lookup or failed recovery, invalidating late callbacks while preserving the prior routing hint. It does not claim abandonment. Active membership, unresolved cleanup, explicit reservation validation and confirmed decisions still block replacement.

## Verification

**VERIFIED:** `check.ps1` passed formatting, version/media checks, zero-warning Debug/Release builds, 462 Core tests and 319 non-native Client/transport tests per configuration. Two new deterministic cases cover Create and Join during a delayed lookup, stale callback rejection, prior-hint preservation and rejection of replacement while membership is active.

**VERIFIED:** Headless and rendered `check-online-lobby.ps1` passed the real production controls with a fake provider: authentication states, browser, public/locked join, create/rename, retained decisions, name visibility, fresh hosting after failure and during delayed startup lookup. `check-lobby.ps1` passed eight native UDP sessions, Ready/Start/Return, repeated matches, active admission, interruption, retained capacity and host closure.

**VERIFIED:** Two native Godot editor scans no longer imported the evidence directory or emitted the reported CSV/translation errors. Both editor processes emitted a separate GodotTools `HotReloadAssemblyWatcher` timer error at shutdown; editor execution is therefore not claimed completely error-free. The runtime lobby harnesses emitted no warnings/errors. The previously recorded MP3 startup-check shutdown leak is not resolved by this correction.

Logs are retained under [ts-73-menu-correction](ts-73-menu-correction/). Live authenticated EOS creation on the user's normal profile and separate-PC networking are **UNVERIFIED**; fake-provider and local UDP evidence are distinct from those claims. The reproduced recovery bug fits the reported menu symptoms, but the user's exact saved-session state was not inspected.

## Astra Self-Critique — Story Round 3

**Overall Score: 6.5 / 10 — Quality Assessment: PASS (>=6.0).**

| Category | Score | Evidence / limitation |
| --- | --- | --- |
| Functionality / UI | 7.0 | Reproduced hidden-name state corrected; fresh hosting and late callbacks exercised through production controls. |
| Stability / integration | 6.0 | Full solution and native lobby checks pass; evidence imports fixed; separate editor timer and previously recorded MP3 shutdown issue remain. |
| Architecture / code quality | 8.0 | Client-only changes use existing coordinator cancellation and hint ownership; no gameplay or identity changes. |
| Testing / reliability | 8.0 | Delayed completion and failed recovery regressions now covered; authenticated live EOS remains unverified. |
| Vehicle feel / gameplay | 5.0 | Prior measured handling results retained, but human driving acceptance is still pending. |
| Physics | 7.5 | Prior native handling evidence applies; no physics changes in this correction. |
| Networking | 5.5 | Local eight-player checks pass; prior impaired-network startup correction spike remains documented. |

The integrated Story assessment retains the earlier human-feel and network limitations. This pass fixes the authorized editor/menu regressions without treating those earlier gaps as resolved. Per the [mandatory human gate](../critique.md#mandatory-human-gate), stop after presenting this review. The correction is committed locally; the existing PR is not updated before renewed acceptance.
