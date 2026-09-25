# Historical verification evidence

This directory is excluded from Godot resource importing by `.gdignore`. Evidence CSV files are diagnostic data, not localization tables; screenshots and logs are not runtime assets.

This directory records historical verification evidence for particular implementations, builds and review rounds. Its reports are not current policy, repository instructions, Jira requirements or substitutes for current verification. Earlier acceptance gaps and delivery decisions describe their recorded point in time; later reports or code may differ.

Use [workflow](../workflow.md#verification) for current completion checks, Jira for assigned requirements, and the [feature index](../features/README.md) for current system behavior. Read a report only when its historical evidence is relevant.

Every completed Jira Story must create or update `docs/verification/ts-<number>.md` using the Story number in lowercase filename form (for example, `TS-86` → `ts-86.md`) and add or update a useful entry in the table below. The report records only implementation and verification evidence actually produced for that Story, including material behavior/systems changed, commands/results actually run, applicable runtime/manual/native evidence, limitations, unresolved risks, and explicitly unverified areas. Never treat these reports as current requirements or infer that old test evidence remains valid after later changes.

| Evidence | Reports |
| --- | --- |
| Magnetic Proxy Mine | [TS-147 verification evidence](ts-147.md) |
| Story verification | [TS-161 verification evidence](ts-161.md) |
| Surface-specific acceleration, progressive dirt recovery and split-contact handling | [TS-160 verification evidence](ts-160.md) |
| Lifted vehicle suspension and chassis response | [TS-159 verification evidence](ts-159.md) |
| Final integrated map optimization, collision/gameplay and performance | [TS-83 verification evidence](ts-83.md) |
| Infield pickup placements | [TS-144 verification evidence](ts-144.md) |
| Terrain detail, surface-reactive VFX and authoritative environment presets | [TS-82 verification evidence](ts-82.md) |
| Production map environment dressing and route safety | [TS-81 verification evidence](ts-81.md) |
| Reusable Blender environment asset library | [TS-80 verification evidence](ts-80.md) |
| Two held items and active-slot switching | [TS-146 verification evidence](ts-146.md) |
| Story verification | [TS-139 verification evidence](ts-139.md) |
| Per-player category-balanced item distribution | [TS-145 verification evidence](ts-145.md) |
| Authoritative item seed per match | [TS-140 verification evidence](ts-140.md) |
| Shallow/deep water, host tuning and lifecycle | [TS-79 verification evidence](ts-79.md) |
| Terrain handling, Configs and uphill starts | [TS-78 verification evidence](ts-78.md) |
| Surface/material identity and native transitions | [TS-77 verification evidence](ts-77.md) |
| Blender infield structures and native clearance/collision | [TS-76 verification evidence](ts-76.md) |
| Infield spectacle jump retuning | [TS-142 verification evidence](ts-142.md) |
| Story verification | [TS-141 verification evidence](ts-141.md) |
| Blender infield terrain; slope-start acceptance blocker | [TS-75 verification evidence](ts-75.md) |
| Story verification | [TS-118 verification evidence](ts-118.md) |
| Play Menu and lobby browser | [TS-138 verification evidence](ts-138.md) |
| Blender-authored infield graybox | [TS-74 verification evidence](ts-74.md) |
| Oval weapon spawns and coniferous perimeter | [TS-100 verification evidence](ts-100.md) |
| Persistent Oil deployment and recovery | [TS-117 verification evidence](ts-117.md) |
| Story verification | [TS-116 verification evidence](ts-116.md) |
| Camera obstruction and world-geometry avoidance | [TS-132 verification evidence](ts-132.md) |
| Asphalt and banked-oval handling | [TS-73 accepted driving, clean Developer Options shutdown and final runtime critique](ts-73.md), [historical runtime-feedback correction](ts-73-correction.md), [editor import and multiplayer menu correction](ts-73-menu-correction.md) |
| Modular chain-hung Main Menu, stationary left layout, legacy presentation cleanup, version label and startup flash regression | [TS-122 implementation and verification](ts-122.md), [Astra runtime critique](ts-122-critique.md) |
| Adjustable local collision camera shake | [TS-109 verification evidence](ts-109.md) |
| Vehicle camera free-look and recentering | [TS-108 verification evidence](ts-108.md) |
| Circus Game Loop, frozen results, rematch and recovery integration | [TS-106 verification evidence](ts-106.md) |
| Circus HUD and live scoring | [TS-105 authoritative score, live leaderboard and scoring-feedback verification](ts-105.md) |
| Post-match Application Flow | [TS-87 Podium, authoritative results, repeated rematch, destination cleanup and native verification](ts-87.md) |
| Circus stunt scoring and banking | [TS-103 verification evidence](ts-103.md) |
| Circus combat scoring | [TS-102 applied damage, K/D, streaks and synchronization evidence](ts-102.md) |
| AI implementation and review workflow | [TS-137 critique split, targeted iteration and tooling evidence](ts-137.md) |
| Categorized DevTools Logs | [TS-99 semantic styling, frozen history and native verification](ts-99.md) |
| Searchable DevTools Stats | [TS-98 grouped live rows, filtering, colors and runtime verification](ts-98.md) |
| Automatic PR history guard | [TS-136 prior-PR suppression and recreation prevention evidence](ts-136.md) |
| Automatic Story PR creation | [TS-135 push trigger, duplicate prevention and Jira-free PR creation](ts-135.md) |
| Per-Story verification evidence requirement | [TS-134 workflow/report naming, indexing and historical-evidence boundary](ts-134.md) |
| Configs discovery and staged settings | [TS-97 implementation, acceptance mapping and verification limitations](ts-97.md) |
| Lobby and match entry | [TS-86 application states, map selection, loading/sync and acceptance evidence](ts-86.md) |
| Authoritative Game Loop foundation | [TS-91 Core lifecycle, legacy integration, regression evidence and runtime limitations](ts-91.md) |
| Game Loop network participation | [TS-92 authoritative phase gates, checkpoint recovery, native multiplayer evidence and limitations](ts-92.md) |
| Game Loop results and reset | [TS-93 frozen results, Application Flow handoff, disposal and repeated-match verification](ts-93.md) |
| Unified DevTools navigation and compact Configs | [TS-96 correction, native/runtime checks and layout captures](ts-96.md) |
| Real-scale Trackstorm vehicle | [TS-72 Blender conversion, physics integration and validation](ts-72.md) |
| Active oval gameplay integration | [TS-71 map loading, eight player grid spawns, preserved systems and stress-test limitation](ts-71-map-swap.md) |
| Real-scale banked oval foundation | [TS-71 source geometry, reusable map scene, collision, native lap and scale verification](ts-71.md) |
| Release 0.1.0 baseline | [TS-89 exact tuning promotion, persistence and version verification](ts-89.md) |
| Retained-match menu decision | [TS-68 prompt, abandonment and preserved history verification](ts-68.md) |
| Three-component build version | [TS-70 migration, explicit release authorization and export identity verification](ts-70.md) |
| Game version and compatibility | [TS-66 acceptance mapping, local verification and external EOS limitations](ts-66.md) |
| Disconnected match participants | [TS-65 retention, standings and lifecycle verification](ts-65.md) |
| Active-match fresh admission | [TS-53 implementation, local verification and remaining EOS acceptance](ts-53.md) |
| Gameplay/menu cursor ownership | [TS-67 acceptance and runtime verification](ts-67.md) |
| Remote vehicle tags | [TS-64 clean verification and Story Round 1 critique](ts-64.md) |
| Player activity and kill feed | [TS-60 acceptance and verification evidence](ts-60.md) |
| Runtime Statistic Panel | [TS-58 implementation, Debug/Release UI checks and regression limitations](ts-58.md) |
| Live admin Event Log | [TS-59 acceptance evidence and Story critique](ts-59.md) |
| Host Developer Options | [TS-38 / TS-39 independent checkpoint; migration pending](ts-38.md), [Apply / Discard / Reset correction](ts-38-actions.md) |
| In-game Game Menu and Settings | [TS-61 implementation and Story critique](ts-61.md) |
| Arena audio and music | [TS-34 / TS-35 implementation verification](ts-34.md), [CC0 placeholder replacement and clean-clone verification](cc0-audio.md) |
| Static vehicle visual | [Asset and integration verification](ts-36.md) |
| Death/respawn | [Implementation verification](ts-24.md) |
| Host migration | [TS-46 / TS-51 implementation and verification](ts-46.md), [Lease retirement and demand-driven observation corrections](ts-46-lease-lifecycle.md), [Cloudflare lease implementation, deployment and pending live acceptance](ts-46-cloudflare-leases.md), [earlier self-hosted lease correction](ts-46-trusted-leases.md), [original authority-fencing investigation](ts-46-authority-fencing-analysis.md) |
| FPS and Ping diagnostics | [Implementation verification](ts-32.md) |
| Combat HUD | [Implementation verification](ts-30.md) |
| Match standings | [Implementation and reconnect integration verification](ts-28.md) |
| EOS P2P | [Initial verification](ts-44.md), [second round and four-PC follow-up](ts-44-round-2.md), [ordering correction](ts-44-ordering-correction.md) |
| Chase camera | [Initial implementation](ts-55-camera.md), [direct-heading correction](ts-55-direct-heading.md), [fixed-chase correction](ts-55-fixed-chase.md) |
| Documentation and Jira instruction cleanup | [Migration, issue accounting and preservation audit](ts-56.md) |
| TS-46 phase-specific disconnect correction | [Lobby fresh join and active-match retained resume](ts-46-disconnect-policy.md) |
| TS-46 match-long reservation correction | [Ordinary-client retention, Return cleanup and remaining native gates](ts-46-match-reservations.md) |
