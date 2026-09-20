# Implemented feature index

Start with the system being changed. Read its document and only the integration documents needed for the assignment; this index is not a requirement to load every feature. Documents describe the current implementation and durable design context. Source code supplies low-level implementation details. [Workflow](../workflow.md#feature-documentation) owns feature-document maintenance and authoring requirements.

| System | Read for | Related systems |
| --- | --- | --- |
| [Startup application flow](startup.md) | Preloader, Splash, persistent MenuShell/Loader, global initialization and Main Menu reveal | [Game Menu](game-menu.md), [Settings](settings.md), [audio](audio.md) |
| [Lobby and match entry](match-entry.md) | Browser, admission, joined Lobby, map selection and Match Loader / Sync | [Sessions](sessions.md), [reconnection](reconnection.md), [Game Loop](game-loop.md) |
| [Game versioning](game-versioning.md) | Canonical build, Story CI sequencing and exact multiplayer compatibility | [Sessions](sessions.md), [EOS lobbies](eos-lobbies.md), [reconnection](reconnection.md) |
| [Fixed-step simulation](simulation.md) | Tick ordering, atomic authority and restore | [Vehicles](vehicles.md), [matches](matches.md), [replication](vehicle-networking.md) |
| [Player input](input.md) | Logical frames, remapping, shaping and suppression | [Settings](settings.md), [vehicles](vehicles.md) |
| [Game Menu](game-menu.md) | ESC overlay, logical focus navigation, category hierarchy and clean exit | [Settings](settings.md), [sessions](sessions.md), [input](input.md) |
| [Settings](settings.md) | Local preferences, persistence, display/audio controls | [Input](input.md), [HUD](hud.md) |
| [DevTools shell](devtools.md) | Shared full-window Configs/Stats/Logs navigation, shortcuts, close behavior and input suppression | [Developer Options](developer-options.md), [Statistic Panel](statistics.md), [Event Log](event-log.md), [Game Menu](game-menu.md) |
| [Developer Options](developer-options.md) | Host tuning, runtime mapping, persistence, replication, F1 and authorized actions | [DevTools shell](devtools.md), [Statistic Panel](statistics.md), [Settings](settings.md), [vehicle networking](vehicle-networking.md) |
| [Statistic Panel](statistics.md) | Searchable F2 Stats tab, read-only live session, player, physics, combat and network diagnostics in Debug/Release | [Developer Options](developer-options.md), [Game Menu](game-menu.md), [vehicles](vehicles.md), [vehicle networking](vehicle-networking.md) |
| [Event Log](event-log.md) | F3 history, structured committed outcomes, timestamps, bounded retention and reliable event replication | [Sessions](sessions.md), [vehicles](vehicles.md), [items](items.md), [reconnection](reconnection.md) |
| [Player activity feed](activity-feed.md) | Player-safe presence and kill/death messages, five-row HUD, expiry and replay protection | [Event Log](event-log.md), [HUD](hud.md), [matches](matches.md) |
| [Arena audio](audio.md) | Vehicle/combat feedback, arena playlist, buses and committed CC0 sounds | [Settings](settings.md), [items](items.md), [matches](matches.md) |
| [Vehicles and damage](vehicles.md) | Movement, suspension, surfaces, HP, collision/effect contracts | [Arena](arena.md), [camera](camera.md), [lifecycle](death-respawn.md), [replication](vehicle-networking.md) |
| [Chase camera](camera.md) | Local orientation, inertia and feedback | [Vehicles](vehicles.md), [replication](vehicle-networking.md) |
| [Combat arena](arena.md) | Retained prototype fixture, markers, props and materials | [Vehicles](vehicles.md), [pickups](item-spawns.md), [replication](vehicle-networking.md) |
| [Banked oval map](oval-map.md) | Active practice/gameplay map, Blender source, real scale, banking, eight player grid spawns and collision | [Combat arena](arena.md), [vehicles](vehicles.md) |
| [Transport foundation](transport.md) | Opaque gateway, endpoints, GNS fallback and legacy state envelope | [Vehicle networking](vehicle-networking.md), [sessions](sessions.md), [EOS P2P](eos-p2p.md) |
| [Vehicle networking](vehicle-networking.md) | Host input, snapshots, prediction and reconciliation | [Transport](transport.md), [vehicles](vehicles.md), [sessions](sessions.md) |
| [Session flow](sessions.md) | Admission, Ready/Start/Return and Direct-IP development UI | [EOS lobbies](eos-lobbies.md), [vehicle networking](vehicle-networking.md), [matches](matches.md) |
| [Reconnection and session resume](reconnection.md) | Stable identity, retained-match menu choice, abandonment, authenticated rebind and full checkpoint recovery | [Sessions](sessions.md), [vehicle networking](vehicle-networking.md), [EOS P2P](eos-p2p.md) |
| [Host migration](host-migration.md) | Authority epochs, election, retained checkpoints, restore and failure | [Reconnection](reconnection.md), [sessions](sessions.md), [vehicle networking](vehicle-networking.md), [EOS P2P](eos-p2p.md) |
| [Authority leases](authority-leases.md) | Cloudflare per-session fencing, short expiry, authentication and service outage policy | [Host migration](host-migration.md), [dashboard deployment](../authority-lease-service.md) |
| [Held items](items.md) | Wrench/Missile authority, outcomes and presentation | [Pickups](item-spawns.md), [damage](vehicles.md), [lifecycle](death-respawn.md) |
| [Item spawning](item-spawns.md) | Marker claims, distribution and cooldowns | [Arena](arena.md), [items](items.md) |
| [Death and respawn](death-respawn.md) | Life boundaries, reset policy and participation | [Vehicles](vehicles.md), [items](items.md), [matches](matches.md) |
| [Authoritative Game Loop](game-loop.md) | Post-sync initialization, phases, countdown, participation, frozen results handoff and disposal/reset ownership | [Matches](matches.md), [simulation](simulation.md), [sessions](sessions.md) |
| [Match scoring](matches.md) | Countdown, attribution, Circus combat/K/D/streaks, pending stunt detection/banking and first-to-target | [Game Loop](game-loop.md), [lifecycle](death-respawn.md), [sessions](sessions.md), [HUD](hud.md) |
| [EOS identity](eos-identity.md) | Device ID, platform lifetime and configuration | [EOS lobbies](eos-lobbies.md), [EOS P2P](eos-p2p.md) |
| [EOS lobbies](eos-lobbies.md) | Browser, access codes, membership and admission | [Identity](eos-identity.md), [sessions](sessions.md), [EOS P2P](eos-p2p.md) |
| [EOS P2P](eos-p2p.md) | Authenticated transport, framing, reliability and bounds | [Identity](eos-identity.md), [lobbies](eos-lobbies.md), [transport](transport.md) |
| [Match standings](standings.md) | Shared Core ranking, held leaderboard, final results and per-player ping | [Matches](matches.md), [HUD](hud.md), [sessions](sessions.md), [transport](transport.md) |
| [Combat HUD](hud.md) | Confirmed HP, speed, inventory, layout and remote vehicle name/HP tags | [Settings](settings.md), [items](items.md), [matches](matches.md), [sessions](sessions.md) |

## Specialized routes

- EOS configuration, native prerequisites, portal setup, authenticated checks and export procedures: [EOS development setup](../eos-development.md).
- Dependency and asset provenance, license records and redistribution: [canonical third-party registry](../../THIRD_PARTY.md).
- Past verification results: [historical evidence index](../verification/README.md).

| [Post-match Application Flow](post-match.md) | Dedicated Podium, authoritative results, rematch, lobby/menu return and controlled exit | [Game Loop](game-loop.md), [match entry](match-entry.md), [sessions](sessions.md), [standings](standings.md) |