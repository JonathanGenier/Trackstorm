# Reusable environment assets

The [Blender environment library](../../assets/environment/README.md) supplies
sixteen independently reusable GLB scenes for the approved rocky off-road map
direction: outcrops, scrub, grass, existing conifers and modular perimeter/drainage
infrastructure. The separate [production dressing layer](../../assets/maps/infield/DRESSING.md)
instances its rocks, loose stones, low vegetation and perimeter fixtures. The active
[oval and infield](oval-map.md) retain their terrain, routes, obstacles and structure.

Blender owns editable metre-scale geometry and collision source meshes; Godot
imports each scene with a ground-centred identity root. Repeated PackedScene
instances share mesh/material resources and keep independent transforms. Linear
modules snap along X at their documented 2/3/4 m lengths. Individual components
and asset-marked collections remain editable in the retained blend.

`ImportAsset.gd` assigns shared concrete and original mineral stone materials, the production
forest color treatment, collision layer 1, and decoration distance limits. This
is import-time configuration; no runtime authoring script or Core dependency is
introduced. UV islands and semantic material slots remain available for later
authoring. Imported native LODs simplify visuals independently of explicit convex
collision pieces. Vegetation and elevated detail collision exclusions, hard
cutoffs and MultiMesh placement guidance are defined in the asset notes.

Rock collision hulls are simplified during import with Godot's single-hull
convex simplifier (at most 32 vertices per authored stone). The detailed Blender
collision source remains editable, while erosion-scale facets do not multiply
native sweep/contact work during host simulation and prediction replay. Separate
stones/ledge strata, transforms, collision layers and visible meshes are retained;
non-rock collision is unchanged. Reimport the five rock GLBs after changing the
hook; an existing imported cache can otherwise retain the old hulls.

`check-rock-collisions.ps1 -GodotPath <path>` exercises all five imported rock
types through practice and network adapters at 3/12/35 m/s and three approach
angles, holding contact, reversing, re-contacting twice and separating. Intact
colliders remain enabled so destruction cannot hide trapping. The fixture records
penetration, contact continuation and step timings. `-Visual` also saves rendered
contact/separation views. The scene accepts `rock`, `speed`, `angle`, `adapter`,
`offset`, `scale`, `grade` and `label` as `name=value` user arguments for focused
glancing, small-rock and sloped-ground trials. Native rigid-body solver time is
outside the practice adapter's measured callback; those timings are not total
frame times. `check-rock-collision-network.ps1` additionally exercises two real
UDP worlds with delay/loss, authority/prediction, repeated contact, reverse exits
and authoritative damage convergence.

The repeated-contact extension `check-repeated-collisions.ps1` retains intact
geometry while testing slow pressure, close/embedded poses and drops onto all five
rock models. It supplements the speed/angle matrix and verifies physical recovery
without relying on destruction to clear the obstacle.

The existing `handling_playtest.tscn` also accepts `--rock-playtest` to isolate
interactive commands, traces and screenshots under `.godot/ts-267/playtest` on
the real map. Combine with `--handling-network` for the production network
collision adapter; use the [handling command protocol](vehicles.md#interactive-handling-verification).

Shared Rock/Concrete materials replace imported mesh slots directly. Unused
embedded texture/material copies are therefore not retained underneath overrides;
the Blender source and authored UV/material identities remain unchanged.

`check-environment.ps1 -GodotPath <path> -Visual` exercises native import,
resource reuse, scale/orientation, materials, collision and rendered views in an
isolated gallery plus temporary map context. Its rigid-body impacts are asset
collision verification, not a production-car or final placement test. The fixture
does not write to the production map. Source reopen/UV/provenance verification
uses Blender's `AuditLibrary.py`.

[Destructible environment](destructible-environment.md) adds match-owned staged rocks and cleared soft cover. Both native adapters consume the same Core state; version-three resume checkpoints and nested migration retain damage, stages, movement continuation and plant bits without replaying impacts. New matches restore authored state.
