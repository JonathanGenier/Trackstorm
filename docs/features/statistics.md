# Runtime Statistic Panel

F2 opens or selects the read-only Stats tab in the full-window [DevTools shell](devtools.md), including
local practice, multiplayer lobby and arena states. Escape and the shell's persistent Close button dismiss DevTools.
Stats is available in Debug and exported Release without the mutating Dev
Mode build flag. Presentation uses the full available shell content area with native
tabs, a search field, a player selector and scrolling. Diagnostic labels are white
and values are blue; there are no gameplay controls in the Stats content.

## Ownership and refresh

`SimulationBootstrap` composes the shell's `StatisticPanel` with a read-only capture delegate and the existing credential-safe identity diagnostic projection.
`RuntimeStatistics` reads the currently active `DevelopmentSession`,
`LobbyNetworkDriver`, `VehicleNetworkDriver` and optional practice `VehicleArena`.
`VehicleStatistics` formats immutable Core snapshots. `StatisticView` and
`StatisticSection` are ephemeral display projections, discarded at the next
refresh; no diagnostic object becomes gameplay authority or is replicated.

`StatisticEntry` splits the existing allowlisted compact text into presentation rows,
including legacy vehicle labels. It never reads runtime owners or expands the safe
projection. Plain native labels render values literally, with no markup interpretation.
Unlabelled status fragments retain their full text under a State label.

Values refresh immediately on open/selection and every 200 ms while visible.
Hidden Stats content does not poll owners. Selection uses stable IDs, defaults to the local
player, and falls back to an available ID when the selected entity departs. Lobby
members without a vehicle remain inspectable with explicit unavailable vehicle
state. Teardown replaces the view with absent-owner information instead of
retaining the previous session's values.

Global/Session and Player/Vehicle use separate tabs. Close stays outside scrolling;
the player selector remains above the player tab's independently scrolling values.
One search field above both tabs filters category, label and displayed value text
using a case-insensitive substring match with leading/trailing whitespace ignored.
A category match reveals every row in that category; otherwise only matching rows
remain, with their category headings. Each view reports when it has no matches.
Clearing search restores all rows. Search is retained across player selection and
shell tab switches, and filters each new live projection at the normal refresh rate.
Typing only changes presentation of the current projection; it does not recapture,
cache a separate diagnostic model, change selection or write to an owner.
While search has focus, printable keyboard bindings (including P/Pause and remapped
menu letters) are treated as text. Direct F1/F2/F3 and Escape shortcuts, nonprintable
navigation keys and controller navigation retain the shared shell behavior.
Opening suppresses local gameplay input only. The scene tree, physics, simulation,
networking and audio continue. The existing settings menu suspends navigation
while covered; closing restores its focus and input suppression if it was open.
No preference, gameplay configuration or persistence command is invoked by Close.

## Diagnostics and source boundaries

| Category | Current values and owner |
| --- | --- |
| Session / authority | Host/client/local-practice role, safe numeric session/player IDs, lobby phase, connected count, vehicle count and prototype capacity, world tick, effective configuration revision; existing lobby/arena owners |
| Player / vehicle | Connected/Ready, local identity, HP/max HP, lifecycle/life generation, confirmed state tick, observed horizontal speed/position; lobby roster and Core vehicle aggregates |
| Physics / environment | Grounded/airborne, legacy Concrete/Mud handling profile, handbrake application, sliding, steering, front/rear slip, longitudinal/lateral acceleration, four wheel compressions; committed `VehicleState` |
| Combat / lifecycle | Current same-life inventory, participation gate, last damage amount/tick and numeric instigator, last damaging collision tick, respawn deadline and remaining seconds; `ItemAuthority` or accepted item publication plus `VehicleSnapshot` |
| Arena / match / spawning | Match phase, kill target, winner, countdown, available/cooling item markers and their deadlines, active projectile count; host world/item/spawn authorities or accepted client publications |
| Ranking | Current rank, kills, deaths; existing `MatchRanking` projection of match and current roster |
| Networking / synchronization | Transport, connection/replication state, local client snapshot age and interpolation delay, reconnect policy/checkpoint/generation; current drivers |
| Network Diagnostics | The existing allowlisted Developer Options diagnostic projection, relocated without duplication: safe EOS lifecycle/one-way identity fingerprint, transport and connection mode, host role and lobby/match state, failure state, RTT/quality, reconnect/checkpoint state, configuration revision, AuthorityEpoch and migration/checkpoint status; current online/session/network owners |
| Player networking | Fresh roster RTT, acknowledged input, local-client prediction error and large corrections, available local-upstream quality/estimated loss; existing latency, replication, prediction and smoothing owners |

Client vehicle values are the last confirmed authority boundary, not predicted
health or native render transforms. Prediction metrics are separately labeled.
During interrupted replication the boundary can be stale; its source tick,
snapshot age, connection state and suspended/checkpoint state remain visible.
Local host prediction, host upstream RTT, unsupported transport quality, inventory
in practice and absent match rules are unavailable rather than invented zeros.
The actual prototype capacity comes from `ArenaConfiguration.SpawnCount`.

The legacy handling profile is the last supported profile while airborne, explicitly
labeled. Timers are projections of existing fixed 60 Hz deadlines, clamped at
zero; the panel does not advance them. Matches use a kill target and have no time
limit. Items are consumables, with no independent reusable-item cooldown telemetry.
AuthorityEpoch, migration and retained-checkpoint state come from the current
lobby/migration owners. This integration only relocates their existing safe
projection; it does not add a second migration or reconnect implementation.

Only allowlisted fields are formatted. Raw provider failures, authentication
objects, player-entered names, access codes, reusable credentials, damage contexts
and item capability tokens are not accepted by the display projection. This is a
current-state view, not an event history or Event Log.

## Arena Tools retirement

Inspection of the current implementation and the pre-Developer-Options UI found:

| Former capability | Current destination |
| --- | --- |
| Host/client, vehicle count, HP/lifecycle/surface/support, handbrake/sliding | Statistic Panel global and player categories |
| Prediction error, snapshot age, interpolation, acknowledged input, hard corrections | Statistic Panel networking categories and the relocated safe Network Diagnostics projection |
| Held-item label | Statistic Panel and ordinary combat HUD |
| Reset practice arena / Detonate nearby | Existing Developer Options actions through the original practice owner |
| Give item | Existing host-only Developer Options grant path |
| Use held item button | Already removed; normal remappable gameplay item input remains |
| Network impairment controls | Existing capability-gated, host-only Developer Options path |

The separate Arena Tools toggle/UI was already removed when Developer Options
landed. No Arena-Tools-only scene or resource remains to remove, and this feature
does not recreate a fallback interface. Dev Mode remains the mutation surface.

## Extension and verification

Future features with materially useful diagnostic state should extend their
owning read-only API and add a category/field to this projection in the same
feature change, with applicable tests/documentation. Avoid dumping arbitrary
internals, creating another state owner, or adding mutation delegates to the view.

`check-statistics.ps1 -GodotPath <exe>` exercises the actual F2/DevTools UI, live owners,
two local UDP peers with separate physics worlds, selection/departure, missing
state, menu suppression/restoration, viewport bounds and practice targets.
`-Visual` also captures both tabs and filtered physics rows at 640×360, 1280×720 and 1920×1080.
`-ExportPath <exported exe>` runs the same harness from the Release executable.
Pure `StatisticProjectionTests` cover real snapshot formatting, stale item-life
rejection, absent diagnostics, deadline clamping, lifecycle refresh and exclusion
of credential-canary contexts/capability tokens, plus compact row formatting and search.
The native harness verifies keyboard search, category/row filtering, empty/cleared
search, colors and live filtered updates without configuration or selection changes.
Existing menu/settings/network
checks cover surrounding behavior. Local synthetic tests do not establish
physical-controller ergonomics or separate-PC authenticated EOS connectivity.

[Feature index](README.md) · [Developer Options](developer-options.md) · [Game Menu](game-menu.md) · [Vehicles](vehicles.md) · [Networking](vehicle-networking.md) · [Reconnection](reconnection.md)

Combat diagnostics include each vehicle's authoritative remaining Oil traction duration. Arena diagnostics include the current persistent patch count from the existing item publication.

The Global/Session tab also shows the local vehicle's current [material identity](surfaces.md), sampled from native support independently of the selected player and confirmed gameplay state. Airborne or unauthored support is unavailable. This read-only field has no Configs controls.
