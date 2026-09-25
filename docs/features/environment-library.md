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
