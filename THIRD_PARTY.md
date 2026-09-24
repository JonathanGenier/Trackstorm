# Third-Party Provenance and Licensing Registry

This is the canonical entry point for repository dependency and asset provenance/licensing. The records below either contain the provenance or identify the authoritative detailed record. Preserve original license notices and manifests in their linked locations; a summary here does not replace their terms. [Workflow](docs/workflow.md#dependencies-assets-and-licenses) owns the maintenance procedure.

## Registry routes

| Dependency / asset area | Authoritative record |
| --- | --- |
| GdUnit4 plugin | [Record below](#gdunit4) and [original MIT notice](addons/gdUnit4/LICENSE) |
| GNS binding, native transport and bundled dependencies | [Transport provenance, pinned versions, hashes and license notices](docs/licenses/transport/README.md); package selection in [GameNetworkingSockets.props](code/Client/Networking/GameNetworkingSockets.props) |
| Official EOS SDK | [EOS provenance, hashes, agreement and redistribution requirements](docs/licenses/eos/README.md); verified acquisition in [setup-eos.ps1](setup-eos.ps1) |
| Arena models/textures | [Record below](#prototype-arena-assets) and [source manifest](assets/arena/sources.json) |
| Banked oval foundation | [Source manifest](assets/maps/oval/sources.json) and [authoring notes](assets/maps/oval/README.md); project-supplied Higgsfield 3D Jutsu Blender master explicitly assigned for Trackstorm use and retention, with unchanged source checksum and derived assets. No separate third-party license is asserted; no external textures or models were acquired |
| Oval conifer perimeter and grass | [Original-art manifest](assets/maps/oval/environment-sources.json) and [authoring notes](assets/maps/oval/README.md); Blender-authored project assets, reusing existing licensed asphalt/concrete maps without new third-party acquisitions |
| Infield layout graybox | [Original-authoring manifest](assets/maps/infield/sources.json) and [authoring notes](assets/maps/infield/README.md); project-authored Blender geometry, with Jira attachment 10017 used only as layout direction. No attachment pixels or third-party assets are distributed |
| Infield terrain | [Original terrain manifest](assets/maps/infield/terrain-sources.json) and [terrain authoring notes](assets/maps/infield/TERRAIN.md); original Blender geometry and vertex colors derived from the retained topology, with no new third-party acquisitions |
| Infield structures | [Original structural manifest](assets/maps/infield/structures-sources.json) and [authoring notes](assets/maps/infield/STRUCTURES.md); original Blender concrete junction structure, neutral shading and imported collision, with no third-party acquisitions |
| Item models/particles | [Record below](#item-combat-assets) and [source manifest](assets/items/sources.json) |
| Arena audio | [Acquisition, licenses and processing](assets/audio/README.md), [checksummed selection manifest](assets/audio/sources.json), and original notices under `assets/audio/kenney/*/License.txt`. Kenney Impact/Interface/Sci-fi 1.0 are CC0. The ten Freesound placeholders are CC0-1.0 third-party derivatives committed with source; their nine exact recording IDs, authors, source/preview URLs, processing and SHA-256 hashes are in the manifest. Attribution is not required. Three unchanged TS-35 songs are project-provided assets. Python/FFmpeg are external development tools, not distributed runtime dependencies |
| Startup/frontend media | [Runtime/import notes](assets/frontend/README.md) and [checksummed source/derivative manifest](assets/frontend/sources.json); the TS-85 MOV files and MP3 are project-provided assets with no third-party license asserted. Both unchanged MOV sources are retained through Git LFS. The Splash derivative preserves synchronized embedded audio, while the MenuShell derivative is deliberately silent and uses the unchanged authoritative frontend MP3 separately. Ogg Theora/Vorbis derivatives are required by Godot's built-in video player. FFmpeg is an external authoring tool, not a distributed runtime dependency |
| Play Menu artwork | [Provenance](assets/frontend/play-menu/README.md) and [hash manifest](assets/frontend/play-menu/sources.json); unchanged project-provided TS-138 component PNGs, runtime atlas regions, native dynamic content and project-authored cloth shader. No new third-party license or font redistribution |
| Main Menu artwork | [Authoring/provenance](assets/frontend/main-menu/README.md) and [hash manifest](assets/frontend/main-menu/sources.json); project-supplied TS-122 component PNGs plus built-in imagegen fabric derivatives. Runtime atlas regions and project-authored shaders preserve modularity and selection geometry. The joined Lobby reuses the blank plate artwork. No new third-party license is asserted or font file redistributed |
| Joined Lobby scenery and previews | [Authoring/provenance](assets/frontend/lobby/README.md) and [hash manifest](assets/frontend/lobby/sources.json); built-in imagegen environment-only edit of the user-approved TS-139 reference, plus native captures of existing production maps. Live vehicle meshes retain their existing provenance. No new third-party license or font redistribution |
| Gameplay vehicle | [Vehicle manifest](assets/vehicles/sources.json), [Kenney original notice](assets/vehicles/kenney/License.txt) and [authoring notes](assets/vehicles/README.md); CC0 Car Kit 3.1 and Poly Haven 1K materials |
| HUD reference/component images | [HUD manifest](assets/hud/sources.json), including supplied asset sources, hashes and approval provenance; these are project-supplied assets and no third-party license is asserted. The [standings frame](docs/features/standings.md) reuses the recorded health steel sample with original procedural geometry; no additional font or texture was acquired. The [Game Menu](docs/features/game-menu.md) likewise reuses the recorded steel sample with original procedural chains, spikes, striped canopy and clown crest, guided by the four project-supplied TS-61 Jira mockups; no attachment pixels, external fonts or new third-party assets are distributed |
| Godot .NET SDK | Version selection in [Client project](Trackstorm.Client.csproj); [upstream source/license](https://github.com/godotengine/godot) |
| Cloudflare authority fencing | [Backend package manifest](services/authority-lease/package.json) and [lockfile](services/authority-lease/package-lock.json) pin npm-registry dependencies. Runtime `jose` 6.2.12 (MIT, Filip Skokan): [upstream](https://github.com/panva/jose), [original license](docs/licenses/authority-jose.txt). Optional Cloudflare tooling: Wrangler 4.134.0 (MIT OR Apache-2.0) and Miniflare 5.20260917.0-alpha (MIT), the version selected by that Wrangler release; [upstream source/notices](https://github.com/cloudflare/workers-sdk). The lockfile records transitive versions, registry URLs, integrity hashes and licenses, including workerd (Apache-2.0). Tool packages retain their upstream notices in the backend install; none are game Client dependencies. Cloudflare hosts the supported service; the old ASP.NET/NuGet backend is removed |
| StyleCop analyzers | Version selection in [Directory.Build.props](Directory.Build.props); [upstream source/license](https://github.com/DotNetAnalyzers/StyleCopAnalyzers) |
| NUnit, NUnit3TestAdapter and Microsoft.NET.Test.Sdk | Version selections in [Core tests](code/Tests/Trackstorm.Core.Tests.csproj) and [Client/transport tests](code/TransportTests/Trackstorm.Transport.Tests.csproj); upstream source/license: [NUnit](https://github.com/nunit/nunit), [adapter](https://github.com/nunit/nunit3-vs-adapter), [test SDK](https://github.com/microsoft/vstest) |

Restored NuGet package metadata supplies the license declaration for the selected package version, including transitive dependencies. Review that metadata when changing package versions; the source links above are discovery routes, not substitutes for pinned package notices. Godot and .NET are development/runtime prerequisites; this registry does not assert a new license for their bundled components.

## GdUnit4

- Name: GdUnit4
- Version: 6.2.0
- Godot Asset Library source: https://godotengine.org/asset-library/asset/4390
- Upstream repository: https://github.com/godot-gdunit-labs/gdUnit4
- Pinned source revision: `d18770221c2df4a3c991a42fdce7907df40eea75`
- Acquired archive SHA-256: `2253D2A4EB37C4DFCF6825D803E3ACA450EFBC371C9EF31E2F3DE7A320FA5802`
- License: MIT
- Attribution: Copyright (c) 2023 Mike Schulze

The required copyright and permission notice is preserved in
`addons/gdUnit4/LICENSE` alongside the distributed plugin files.

## Prototype Arena Assets

Acquired 2026-09-13. All assets below are **CC0 1.0 Universal**. Attribution is not required; provenance is retained here and in the upstream Kenney license files. Poly Haven's asset license is documented at https://polyhaven.com/license and the dedication at https://creativecommons.org/publicdomain/zero/1.0/.

| Source | Version / selection | Project location and use |
| --- | --- | --- |
| [Kenney City Kit (Industrial)](https://kenney.nl/assets/city-kit-industrial) | 2.0; building-a, chimney-large, shipping-container-a, original colormap | `assets/arena/kenney/city`; industrial skyline and collidable containers |
| [Kenney Factory Kit](https://kenney.nl/assets/factory-kit) | 3.0; machine, original colormap | `assets/arena/kenney/factory`; exterior industrial machinery |
| [Kenney Racing Kit](https://kenney.nl/assets/racing-kit) | Downloaded archive identifies itself as 2.0 (website lists 1.0); ramp | `assets/arena/kenney/racing`; low salvage ramp with rusty sheet material |
| [Poly Haven Concrete Road Barrier](https://polyhaven.com/a/concrete_road_barrier) | 1K glTF, binary mesh, diffuse/normal/ARM textures | `assets/arena/polyhaven/concrete_road_barrier`; two fixed barriers |
| [Poly Haven Barrel_01](https://polyhaven.com/a/Barrel_01) | 1K glTF, binary mesh, explosive-variant diffuse/normal/ARM textures | `assets/arena/polyhaven/Barrel_01`; three inert movable obstacles; the painted warning does not imply a damage mechanic |
| [Poly Haven Asphalt 04](https://polyhaven.com/a/asphalt_04) | 1K diffuse, OpenGL normal, roughness JPEGs | `assets/arena/polyhaven/asphalt_04`; hard ground and brown-tinted prototype Mud material |
| [Poly Haven Concrete](https://polyhaven.com/a/concrete) | 1K diffuse, OpenGL normal, roughness JPEGs | `assets/arena/polyhaven/concrete`; retaining walls and industrial buildings |
| [Poly Haven Rusty Metal 05](https://polyhaven.com/a/rusty_metal_05) | 1K diffuse, OpenGL normal, roughness JPEGs | `assets/arena/polyhaven/rusty_metal_05`; container, chimney and machinery treatment |
| [Poly Haven Rusty Metal Sheet](https://polyhaven.com/a/rusty_metal_sheet) | 1K diffuse, OpenGL normal, roughness JPEGs | `assets/arena/polyhaven/rusty_metal_sheet`; salvage ramp and container treatment |

`assets/arena/sources.json` records upstream download URLs and SHA-256 hashes for the imported source files. Poly Haven downloads were also verified against upstream API MD5 metadata. Models and textures retain their original bytes. Kenney original colormaps are retained to resolve GLB dependencies, but production meshes receive dirty concrete/rust material overrides. Godot `StandardMaterial3D` resources in `assets/arena/materials` use 1K maps with world-space triplanar mapping; the two Poly Haven models use their imported PBR materials. Collision uses independently authored primitives/one convex ramp, never the visual triangle meshes. No protected franchise content, vehicles or logos were imported.

The upstream glTF API labels shared resolution-independent binary geometry with an `8k` URL segment. These binary files contain geometry, not 8K textures; every imported gameplay texture is 1K. No specified asset was substituted. Mud reuses the selected ground maps with a distinct brown material treatment rather than introducing an unrequested asset dependency.

## Item combat assets

Kenney Weapon Pack (2016-01-20), original author publication at https://opengameart.org/content/weapon-pack, and Particle Pack 1.1 (archive license) from https://kenney.nl/assets/particle-pack are CC0. Only the rocket mesh/material, fire, smoke and spark textures are included. Original licenses and file/archive hashes are under `assets/items`. The original Weapon Pack mirror was approved for acquisition because its old Kenney page is unavailable. Projectile/pickup materials and the damage-flash shader are project-created.

## Infield material field

The original material control field and UV pass are recorded in [surface-sources.json](assets/maps/infield/surface-sources.json). Runtime shading reuses the existing original oval grass and CC0 Poly Haven Asphalt 04 detail and Concrete material listed above. No new third-party assets were acquired. The Jira concept is an art-direction reference and is not redistributed as a runtime asset.
