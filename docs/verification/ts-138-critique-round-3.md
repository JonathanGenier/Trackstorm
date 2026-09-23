# Astra Runtime Critique — Story Round 3

TS-138 · `ts-138-jg` · 2026-09-22 (America/Toronto) · Final integrated version **0.1.30**.

**Overall Score: 8.1 / 10**

**Quality Assessment: PASS (>=8.0)**

The corrected menu keeps ordinary browsing, Host and Back available when a saved routing hint is unconfirmed or its lookup fails. Explicit checking leads to the existing authority decision; an unsuccessful recovery restores browsing and an explicit retry. The visual containment and footer improvements remain intact. Final independently exercised scenarios pass, but the earlier intermittent pointer misses and lack of authenticated testing against the user's service limit confidence. This is a judgment of the exercised UI, not a claim that the user's live environment has been reproduced or every input failure explained.

The Round 2 PASS missed the now-reproduced combination of an unconfirmed retained hint and failed lookup. It should not have been treated as evidence that this case worked. Its automatic Play-entry validation assessment is superseded by this round's explicit-check behavior and expanded runtime evidence.

## Category Scores

| Category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.4 | Both unconfirmed-hint cases preserve browser/Host/Back and make no automatic resume request; explicit authority decisions and failure recovery pass. |
| Controls / Responsiveness | 7.8 | Final strict held-click, keyboard and synthetic-controller scenarios pass; two earlier rendered pointer misses remain unexplained. |
| Visual Quality | 8.2 | Browser, forms and status fit the frame; aligned complete rows, clear filter choices and separate version footer remain consistent. |
| UI / UX | 8.2 | Lookup failure is status in a usable browser rather than an unsolicited reconnect trap; checking and confirmed Yes/No are visually distinct. |
| Runtime Stability | 8.1 | Final runs complete without diagnostics; earlier verification failures are retained below and not dismissed as proven environmental faults. |
| Runtime Integration | 8.1 | Controlled-provider decision/admission paths and repeated navigation work; real-background composition remains coherent. Remote service behavior is unverified. |

The overall score is a holistic assessment, not a test-count reward. Audio, physical-device ergonomics, remote networking, gameplay/vehicle feel and sustained performance are not scored.

## What Was Exercised

**VERIFIED — independent execution during this same Round 3**, with Godot .NET 4.7.2, Windows OpenGL compatibility renderer and NVIDIA RTX 4070 Laptop GPU:

- Final integrated `./check-play-menu.ps1 -GodotPath <Godot executable> -Visual -SkipBuild` — **PASS**, version 0.1.30, no warnings/errors. For matching metadata without authority confirmation and for failed lookup preserving a hint, repeated Play entry left the browser available, Host opened its form, Back returned to Main, resume-call count remained unchanged and the persisted fixture hint remained unchanged before fixture cleanup. Pending passive lookup, keyboard/controller-equivalent Host/Back, 0/100 rows, filters/search, scrolling, deliberate join gestures, failure recovery, repeated transitions and all three viewport sizes also passed.
- `./check-online-lobby.ps1 -GodotPath <Godot executable> -Visual -NoBuild` — **PASS** independently before the main integration, on the same corrected TS-138 production logic. Explicit Check previous session initiated validation. Only authority confirmation exposed Yes/No. Pointer No plus duplicate keyboard activation produced one abandonment request and awaited acknowledgement; controller Yes plus duplicate keyboard activation produced one resume request. Failed recovery preserved the locator, exposed normal browsing with enabled Host/Back, and allowed an explicit retry.
- Unchanged headless Play — **PASS** during investigation of the pointer misses. This supports the controlled input/state paths but is not rendered or physical-pointer evidence.

**VERIFIED — visual inspection:** fresh final 0.1.30 `hint-no-auto-reconnect.png` and `lookup-failed-no-auto-reconnect.png` show the normal empty browser, enabled Host/Back and explicit Check previous session, with readable status inside the frame. The final small-viewport capture retains a clear version footer. Normal 1280×720 / 1600×900, scrolled and filter views were also inspected during this round; no recurrence of the Round 1 overlap was seen. Directly inspected final integration captures `retained-choice.png`, `retained-failure.png` and real-background `play-menu-shell.png`: authority choice fits, failure restores browsing, and the version/footer and unavailable-service message remain clear.

The implementation agent's post-integration comprehensive, startup and online reruns are recorded in [the verification report](ts-138.md). This reviewer inspected their final screenshots but did not independently rerun those broad checks after integration. The independently rerun final Play check establishes the integrated result for the expanded regression scenarios.

## Earlier Pointer Failures and Investigation

Before scoring, two independent rendered Play attempts failed at different held-click outcomes:

1. The lookup-failure hint case could not find visible Create lobby after the Host click (`CheckUnconfirmedHint`, then line 153).
2. A repeat stopped earlier at `Back blocks interaction` (`_Ready`, then line 111).

The same unchanged scenario suite passed headlessly. The implementation agent added strict activation counting and per-frame focus/pressed/mouse/disabled/interactive diagnostics; an independent rendered run then passed without triggering those diagnostics. The fixture subsequently prepared window focus and the native pointer location before injection, retaining the strict count and diagnostic failure behavior, without automatic retries. Independent rendered runs passed both before integration and on final version 0.1.30 with that preparation.

**Cause remains unknown.** The evidence does not establish whether the earlier misses arose from desktop interference, event delivery, fixture behavior or a product issue. No production input fix was inferred from the later passes. The failures lower confidence in physical-pointer repeatability and remain part of this assessment; the final passing runs do not erase them.

## Runtime / Operational Limitations

- **UNVERIFIED:** authenticated reproduction against the user's live remote service, remote reservation validity and the origin of the user's local routing hint. Controlled providers reproduce the relevant UI/state combinations; they do not establish what happened remotely. This reviewer did not alter or delete user data.
- Physical mouse/controller play, controller-only text entry, audible continuity, assistive technology, exported builds, multi-PC Internet/NAT and extended frame pacing remain unverified. The interactions were scripted native/logical input events with direct screenshot inspection.
- The earlier pointer misses are unresolved despite subsequent strict rendered passes. The user-facing correction addresses unsolicited recovery from a routing hint, not a proven general hardware-input fault.
- Root reported importing new main-branch item assets before its successful final startup rerun. That import recovery and the comprehensive suite were not independently repeated here; no asset-import quality score is inferred.
- Screenshots/logs remain local under `.godot/play-menu-checks`, `.godot/online-lobby-checks` and `.godot/main-menu-checks`; they are not committed evidence assets.

No implementation or harness edits, commits, pushes or task creation were performed by this reviewer. This completes Story Round 3; no fourth critique round exists under the current policy. Stop for the explicit human decision required by [the critique policy](../critique.md#mandatory-human-gate). A PASS does not imply human acceptance or authorize merging.
