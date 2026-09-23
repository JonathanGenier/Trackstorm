# Play Menu

The Play Menu is the lobby browser inside the persistent [MenuShell](startup.md). `HangingPlayMenu` composes a header, empty rigid frame, fixed primary action plates, native table controls, decision/form content and independent fabric. The complete assembly uses a 1360×880 design region, uniformly fitted within the viewport above a 44-pixel footer. Search, headings, scrolling rows and status share an inset safe interior clear of the metal rails. The canonical game version uses the Main Menu bottom-left viewport anchoring, margins, type and shadow outside the moving rig, including Direct-IP/LAN fallback. The fallback shares the same label construction and has no centered version header. It never renders the full design mockup or painted sample rows. [Asset provenance](../../assets/frontend/play-menu/README.md) records the project-provided art.

## Navigation and motion

`DevelopmentSession` routes Main Menu Play and Play Menu Back. The departing assembly accelerates upward over 0.34 seconds and clears before the destination becomes visible. Play drops for 0.46 seconds, catches and performs one restrained five-design-pixel settle for 0.36 seconds. Main Menu reuses its original drop/catch/settle; the Loader entrance is unchanged. Input is blocked throughout each rig's motion, and held logical actions must release before dispatch resumes. No transition calls MenuShell media APIs.

Free Play remains disabled with Coming Soon. Host Game opens a native name/access-code form that invokes the existing coordinator Create operation. Back returns to Main Menu. The developer Direct-IP action exposes the existing fallback controls. Joined Lobby remains a separate authority-driven presentation; `OnlineLobbyPanel` retains its membership/rename/leave controls.

Logical input uses the existing remappable player binding owner, with 0.4-second repeat delay and 0.12-second navigation repeat. Mouse and keyboard/controller focus share red selected/hover treatment. Search retains native text entry and caret editing. Up/Down traverses controls, including text fields and scrolling rows. Filter opens a modal choice list: directions highlight, Accept selects, and Cancel discards. Covering Settings/DevTools suspend underlying activation.

## Discovery projection

The existing `LobbyBrowser` remains the discovery store. `PlayMenuSelection` derives the visible rows from its current projection without mutating discovery or session state. Name search is ordinal case-insensitive. All, Open Only, Locked Only and Has Space combine with Search using AND. Mode choices appear only when more than one nonempty advertised mode exists; vanished criteria fall back to All. Ping is display-only. Discovery does not currently measure remote lobby latency, so the production list shows an em dash; no latency estimate is invented.

Rows show Lobby Name, Game Mode, Players, Blocked and Ping. Missing mode metadata is Unknown. The EOS adapter publishes the mode selected by the existing gameplay authority and reads its optional `mode` attribute; older metadata remains compatible. Mode metadata cannot change gameplay rules or admission. Refresh retains its existing status/deadline/failure behavior.

Zero rows produce an explicit empty state. Up to 100 actual discovered rows occupy a fixed scrolling viewport; list changes cannot resize the shell. Refresh/filter/row changes clear pending activation. Selection survives only while its ID remains visible, otherwise focus returns to Search. A mouse single-click selects; double-click joins. Two independent logical Accept edges on the same focused, joinable row within 0.45 seconds request Join. Expiry, changed focus, hidden UI, refresh, overlays and changed row data reset the pending gesture. All joins recheck the latest coordinator row, preserving version, capacity, access-code and authoritative admission gates.

## Retained sessions

Entering Play waits for saved-session detection before exposing browser interaction. Pending lookup shows progress with usable Back; Host, Join and Direct-IP admission cannot cancel that lookup. Passive startup lookup cannot replace Main Menu. No hint or a completed missing-session result exposes the normal browser immediately. Matching metadata marks a candidate in the coordinator and keeps fresh admission gated; visible Play invokes the existing ResumeRetained validation path exactly once for that candidate. Metadata is not reservation authority: only the existing host-confirmed Available response presents Reconnect to previous game? Yes / No. Yes calls DecideRetained(true); No calls DecideRetained(false), and browsing returns only after authoritative abandonment and cleanup clear the retained state. A failed lookup or validation retains the existing browser/status/manual retry semantics without automatically retrying an unconfirmed hint. Networking, reservation and cleanup logic remain in their existing owners.

## Verification

`check-play-menu.ps1 -GodotPath <exe> -Visual` exercises production controls over deterministic discovery, including 0/100 rows, scrolling, filters, double-click, double-Accept, expiry, focus changes, controller-equivalent events, repeated transitions and viewport captures. `check-online-lobby.ps1 -Visual` covers access codes, pending admission, retained validation and idempotent Yes/No. `check-startup.ps1 -Visual` exercises the real MenuShell players and Main/Play transitions. These fixtures do not establish physical-controller ergonomics, audible continuity or remote authenticated EOS interoperability.

[Feature index](README.md) · [Main Menu](main-menu.md) · [Match entry](match-entry.md) · [Reconnection](reconnection.md)
