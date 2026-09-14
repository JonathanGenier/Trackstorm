# Third-Party Dependencies

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
