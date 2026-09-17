# Trackstorm

Trackstorm is a Godot 4.7.2 C# vehicle-combat prototype targeting .NET 10. It includes an eight-vehicle industrial arena, grounded arcade movement and damage, local practice, host-authoritative multiplayer, Wrench/Missile combat, pickups, death/respawn, first-to-target scoring, a chase camera, settings and a combat HUD.

Normal multiplayer uses EOS Device ID identity, lobby discovery and EOS P2P gameplay transport. Direct-IP/GameNetworkingSockets is an explicit development fallback. These are prototype systems with documented limits; see the [feature index](docs/features/README.md) for current behavior and integration boundaries.

[Cloudflare authority fencing](docs/features/authority-leases.md) coordinates one short lease per session while gameplay remains P2P. The service owner deploys through the [GitHub-connected dashboard workflow](docs/authority-lease-service.md) and bundles its HTTPS endpoint once. The actual endpoint is pending deployment; ordinary developers and players need no Cloudflare account or Node/npm/Wrangler setup.

## Solution layout

| Project | Purpose |
| --- | --- |
| `Trackstorm.Client.csproj` / `code/Client` | Godot runtime, presentation, input and native network adapters |
| `code/Core/Trackstorm.Core.csproj` | Engine-independent authoritative gameplay and portable contracts |
| `code/Tests/Trackstorm.Core.Tests.csproj` | Deterministic NUnit Core tests |
| `code/TransportTests/Trackstorm.Transport.Tests.csproj` | Pure Client/transport tests and separately selected native tests |
| `third_party/eos/Epic.OnlineServices.csproj` | Pinned official EOS C# binding, consumed by Client |

`Trackstorm.sln` groups these projects. [Engineering standards](docs/standards.md) define production ownership, dependency direction and testing conventions.

## Development entry points

Run `./setup-eos.ps1` on a fresh checkout to acquire the pinned SDK, then `./check.ps1` for repository verification. [EOS setup](docs/eos-development.md) explains native prerequisites, development configuration, online testing and export checks. [THIRD_PARTY.md](THIRD_PARTY.md) routes to dependency/asset provenance and redistribution requirements.

Open `project.godot` with the matching Godot .NET editor. The main scene is `scenes/main.tscn`; it opens the multiplayer browser. Launch with `-- --local-practice` for the local rigid-body arena. The browser's **Developer fallback: Direct-IP / LAN** exposes address-based hosting/joining.

## Contributor documentation

- [Agent routing](AGENTS.md)
- [Assignment, documentation and delivery workflow](docs/workflow.md)
- [Engineering standards](docs/standards.md)
- [Quality critique](docs/critique.md)
- [Current feature index](docs/features/README.md)
- [Historical verification evidence](docs/verification/README.md)
