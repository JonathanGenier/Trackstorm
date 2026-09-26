# Surface and material identity

`SurfaceIdentity` names Asphalt, Grass, Dirt, Mud, Deep Mud, Rock, Concrete and
Water. It describes material; Core `SurfaceHandling` maps the shared observation to the [vehicle handling profiles](vehicles.md#terrain-handling-profiles). Asphalt preserves the vehicle baseline, and Concrete, Dirt, Grass, Mud and Deep Mud have distinct host-tunable responses. Rock retains neutral baseline handling; [Water interaction](water.md) owns immersion, resistance and deep-water damage.

## Authoring and ownership

Blender owns geometry, UVs and material slots. Godot owns runtime materials and
native contact interpretation. A uniform collider may carry a string
`surface_identity` matching a `SurfaceIdentity` name. A blended collider carries
`surface_field` (a texture resource path) and `surface_bounds` (local X/Z origin
and size as a Vector4). Its visual mesh must use the same local coordinates.
Import hooks bind these values from the visual shader material, avoiding an
independently maintained collision mask.

The field is linear RGBA data: dry dirt coverage, soil saturation, exposed rock
coverage and designated water coverage. Preserve channels using lossless import,
no alpha-border repair, no premultiplication and no mipmaps. The shader and CPU
use clamped bilinear sampling with the same texel-center convention. Detail
textures retain mipmaps and have no influence on identity. The CPU caches the
decoded field once per resource path; authoring changes require a fresh runtime.

Dominant identity has a defined precedence: Water at alpha >= 0.5, Deep Mud at
green >= 0.65, Mud at green >= 0.25, Rock at blue >= 0.5, Dirt at red >= 0.5,
otherwise Grass. Visual blending remains gradual around these boundaries.
Uniform Asphalt and Concrete do not require a texture field. Missing authored
identity is unavailable, not an invented material.

`WheelSuspension` resolves native support hit positions through
`SurfaceIdentityResolver`. The center support ray wins; if it misses, the last
supported wheel in the fixed FL/FR/RL/RR order wins. Mixed wheel contacts thus
have a deterministic diagnostic support identity. Each wheel also reports its own
handling material: Core uses those contacts for tire capacity, drive, drag and
track-width traction torque. A center ray cannot hide one side touching grass. Both practice and host/client
prediction use this same observation path, without map names or basin coordinates
in vehicle code. No support, inactive vehicles and airborne observations clear
the current material. Wheel-specific visual feedback resolves its own
contact positions through the same resolver.

Each suspension observation reuses one native ray query in the same FL/FR/RL/RR/center order. Fixed-size scratch values live on the stack and hit dictionaries are disposed after sampling, reducing allocation during network replay. Native observations are still fresh on every step; no material or collision-result cache is introduced by this reuse.

## Current map

The [banked oval](oval-map.md) uses Asphalt; cast structures and outer containment
use Concrete. The [infield authoring pass](../../assets/maps/infield/BuildSurfaces.py)
applies Dirt to main routes, kickers and tabletop landings, Grass to separating
terrain and shoulders, and Rock to existing hill shoulders away from main lanes.
The three basins centered at (-57,-29), (-85,28) and (85,28) use Mud rims and
Deep Mud centers. The northeast basin at (77,-35) has the sole Water center,
with wet-soil banks. Coordinates are Godot X/Z metres.

Water identifies the existing basin bed and bounds the [waterline and immersion field](water.md). Terrain geometry and
collision remain the production terrain; the material pass checks identical
vertex/triangle hashes before and after authoring.

## Developer diagnostics and verification

[Stats](statistics.md) includes **Local surface / material** in Global/Session,
showing the current native observation independently of the selected player.
It is local, read-only and not replicated or authoritative. Client prediction
observations may differ transiently from a host observation. The Player/Vehicle
section labels the committed value **Handling profile** to avoid confusing it
with material identity. Configs exposes Concrete, Dirt, Grass, Mud and Deep Mud categories through the existing host-owned tuning path.

`check-surfaces.ps1 -GodotPath <exe> [-Visual]` samples actual imported collision,
checks all eight materials through practice and host/prediction adapters, drives
representative material transitions, checks airborne clearing and reads the
existing Stats projection/panel. Visual output is in `.godot/surface-checks`.
The infield and oval harnesses own wider route/collision continuity checks.

[Vehicles](vehicles.md) · [Feature index](README.md)

[Terrain effects](terrain-effects.md) resolves wheel contacts through this same identity system for visual tracks, dust, mud spray and water feedback. Visual detail never modifies the field or handling classification.
