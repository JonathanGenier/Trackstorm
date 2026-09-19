# Historical verification evidence

This directory records historical verification evidence for particular implementations, builds and review rounds. Its reports are not current policy, repository instructions, Jira requirements or substitutes for current verification. Earlier acceptance gaps and delivery decisions describe their recorded point in time; later reports or code may differ.

Use [workflow](../workflow.md#verification) for current completion checks, Jira for assigned requirements, and the [feature index](../features/README.md) for current system behavior. Read a report only when its historical evidence is relevant.

Every completed Jira Story must create or update `docs/verification/ts-<number>.md` using the Story number in lowercase filename form (for example, `TS-86` → `ts-86.md`) and add or update a useful entry in the table below. The report records only implementation and verification evidence actually produced for that Story, including material behavior/systems changed, commands/results actually run, applicable runtime/manual/native evidence, limitations, unresolved risks, and explicitly unverified areas. Never treat these reports as current requirements or infer that old test evidence remains valid after later changes.

| Evidence | Reports |
| --- | --- |
| Searchable DevTools Stats | [TS-98 grouped live rows, filtering, colors and runtime verification](ts-98.md) |
| Automatic Story PR creation | [TS-135 push trigger, duplicate prevention and Jira-free PR creation](ts-135.md) |
| Per-Story verification evidence requirement | [TS-134 workflow/report naming, indexing and historical-evidence boundary](ts-134.md) |
| Configs discovery and staged settings | [TS-97 implementation, acceptance mapping and verification limitations](ts-97.md) |
| Lobby and match entry | [TS-86 application states, map selection, loading/sync and acceptance evidence](ts-86.md) |
| Authoritative Game Loop foundation | [TS-91 Core lifecycle, legacy integration, regression evidence and runtime limitations](ts-91.md) |
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
