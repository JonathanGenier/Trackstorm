# Post-match Application Flow

Application Flow owns **Game Loop Finished → Podium → Rematch / Lobby / End Match / Main Menu / Quit**. `DevelopmentSession` captures a detached `PostMatchContext` only after the authoritative final result and entry/recovery synchronization are available. The context fences the immutable `FinalMatchResults` and participant display metadata to the current logical session and match generation. Game Loop does not navigate or load a scene.

## Podium and retention

`PodiumScene` is a dedicated full-screen native presentation with first/second/third plinths, a paged final standings table and explicit destination controls. Winner, rank, kills, deaths and per-match wins come directly from the ordered Core results. No UI ranking or scoring runs. Names, connectivity and ping use current session metadata; the detached handoff retains display names if metadata is unavailable. Offline table rows dim and show `--` ping. Eight rows per page expose all retained participants, including departed history.

The Finished arena remains alive behind Podium so its existing checkpoint, participant, migration and reconnect owners remain valid. Presentation is hidden, input passed to the arena is neutral, and the native pointer is released. The existing Finished participation rules remain authoritative. Arena audio continues under its existing owner until match teardown. The held in-game standings board and combat HUD yield to Podium; Game Menu and DevTools can still appear above it. Podium uses the shared theme and remapped logical navigation, ignores held Accept on entry/overlay return, and consumes default native UI navigation to prevent duplicate activation.

## Destinations and cleanup

| Destination | Authority and resulting state |
| --- | --- |
| Rematch | Current synchronized host only. Core `LobbyAuthority.Restart` rejects unmatched/pending transport admission, applies the normal Return cleanup, retains connected identities/map/tuning/session, clears disconnected reservations/departed history/readiness, and increments match generation. No remote restart command exists. Every peer disposes its old driver/native arena, clears result/loader/held-input state and traverses Match Loader / Sync. Only that barrier can initialize the fresh Game Loop. Host restart explicitly includes connected participants; it does not wait for another lobby Ready vote. |
| Return to Lobby | Current synchronized host only. The existing authoritative Return ends the match for everyone, releases reservations/history, resets readiness and produces a valid joined lobby. Connected identities, selected map, tuning and session journal survive. |
| End Match | Same authoritative Return semantics as Return to Lobby: ends the shared match and keeps the joined lobby. It does not invent a session-destruction protocol. Both labels are exposed as required destinations, with explanatory tooltips. |
| Main Menu | Any participant leaves through existing reliable Leave, transport disposal and online membership cleanup. Explicit Leaving presentation holds until cleanup completes; online failure exposes Retry cleanup. Automatic transport attachment is suppressed during exit. Then the existing MenuShell resumes at Main Menu. Existing retained-reservation rules for an individual departure remain unchanged. |
| Quit Game | Any participant requests the bootstrap's existing controlled shutdown. It pumps session/membership cleanup, permits retries, flushes settings and tears down native owners. MenuShell does not restart media during shutdown. |

Shared actions revalidate current handoff, host authority, recovery/migration and synchronization at dispatch time. A stale or rapid duplicate action cannot restart another generation. A rejected restart retains Podium and provides retry feedback. Authority migration/recovery disables shared controls without discarding results; local departure remains available. Resource/synchronization failures use the existing bounded match-entry failure path.

## Verification

`RematchTests` covers host-only restart, transport consistency, retained identity/map, released reservations, repeated generations and replica/codec acceptance. `PostMatchContextTests` uses mode ordering deliberately different from kill ordering and checks retained names, changing connectivity and generation/session fences.

`check-post-match.ps1 -GodotPath <exe>` runs actual startup and two production sessions in isolated native worlds over local UDP. It exercises three Finished handoffs with two immediate rematches, explicit Loader/Sync, fresh mode state, duplicate/non-host rejection, Return to Lobby, End Match, Main Menu/MenuShell continuity, retained offline results and controlled Quit. Finished is a trusted authoritative fixture; `check-match.ps1` separately exercises actual combat completion. `-Visual` captures Podium at multiple viewport sizes and checks native pointer release. Existing lobby, standings, reconnect, migration and menu harnesses cover surrounding ownership contracts.

These checks do not prove separate-PC EOS connectivity, live service failure/retry, Internet soak behavior, audible quality or physical-controller ergonomics.

[Feature index](README.md) · [Game Loop](game-loop.md) · [Match entry](match-entry.md) · [Sessions](sessions.md) · [Reconnection](reconnection.md) · [Standings](standings.md)
