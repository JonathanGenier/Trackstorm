# Implemented feature index

Start with the system being changed. Read its document and only the integration documents needed for the assignment; this index is not a requirement to load every feature. Documents describe the current implementation and durable design context. Source code supplies low-level implementation details. [Workflow](../workflow.md#feature-documentation) owns feature-document maintenance and authoring requirements.

| System | Read for | Related systems |
| --- | --- | --- |
| [Fixed-step simulation](simulation.md) | Tick ordering, atomic authority and restore | [Vehicles](vehicles.md), [matches](matches.md), [replication](vehicle-networking.md) |
| [Player input](input.md) | Logical frames, remapping, shaping and suppression | [Settings](settings.md), [vehicles](vehicles.md) |
| [Settings](settings.md) | Local preferences, persistence, display/audio controls | [Input](input.md), [HUD](hud.md) |
| [Vehicles and damage](vehicles.md) | Movement, suspension, surfaces, HP, collision/effect contracts | [Arena](arena.md), [camera](camera.md), [lifecycle](death-respawn.md), [replication](vehicle-networking.md) |
| [Chase camera](camera.md) | Local orientation, inertia and feedback | [Vehicles](vehicles.md), [replication](vehicle-networking.md) |
| [Combat arena](arena.md) | Layout, markers, props, materials and shared spawn geometry | [Vehicles](vehicles.md), [pickups](item-spawns.md), [replication](vehicle-networking.md) |
| [Transport foundation](transport.md) | Opaque gateway, endpoints, GNS fallback and legacy state envelope | [Vehicle networking](vehicle-networking.md), [sessions](sessions.md), [EOS P2P](eos-p2p.md) |
| [Vehicle networking](vehicle-networking.md) | Host input, snapshots, prediction and reconciliation | [Transport](transport.md), [vehicles](vehicles.md), [sessions](sessions.md) |
| [Session flow](sessions.md) | Admission, Ready/Start/Return and Direct-IP development UI | [EOS lobbies](eos-lobbies.md), [vehicle networking](vehicle-networking.md), [matches](matches.md) |
| [Held items](items.md) | Wrench/Missile authority, outcomes and presentation | [Pickups](item-spawns.md), [damage](vehicles.md), [lifecycle](death-respawn.md) |
| [Item spawning](item-spawns.md) | Marker claims, distribution and cooldowns | [Arena](arena.md), [items](items.md) |
| [Death and respawn](death-respawn.md) | Life boundaries, reset policy and participation | [Vehicles](vehicles.md), [items](items.md), [matches](matches.md) |
| [Match scoring](matches.md) | Countdown, attribution, scores and first-to-target | [Lifecycle](death-respawn.md), [sessions](sessions.md), [HUD](hud.md) |
| [EOS identity](eos-identity.md) | Device ID, platform lifetime and configuration | [EOS lobbies](eos-lobbies.md), [EOS P2P](eos-p2p.md) |
| [EOS lobbies](eos-lobbies.md) | Browser, access codes, membership and admission | [Identity](eos-identity.md), [sessions](sessions.md), [EOS P2P](eos-p2p.md) |
| [EOS P2P](eos-p2p.md) | Authenticated transport, framing, reliability and bounds | [Identity](eos-identity.md), [lobbies](eos-lobbies.md), [transport](transport.md) |
| [Combat HUD](hud.md) | Confirmed HP, speed, inventory and layout | [Settings](settings.md), [items](items.md), [matches](matches.md) |

## Specialized routes

- EOS configuration, native prerequisites, portal setup, authenticated checks and export procedures: [EOS development setup](../eos-development.md).
- Dependency and asset provenance, license records and redistribution: [canonical third-party registry](../../THIRD_PARTY.md).
- Past verification results: [historical evidence index](../verification/README.md).
