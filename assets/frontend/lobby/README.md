# Joined Lobby presentation assets

TS-139 uses the user-approved 2026-09-23 Carnage Circus mockup as visual direction. `CarnageCircus.png` is a built-in imagegen edit with all vehicles and interface removed. The original generated output remains in the local generated-images directory. `sources.json` records the reference checksum, tool mode, edit instructions and distributed hashes. This is project-supplied/generated artwork; no third-party license is asserted and no font files are redistributed.

The fixed camera composites actual replaceable Wasteland vehicle meshes, native lighting and soft procedural contact shadows over the environmental matte. The matte supplies the tent, scaffolding, carnival bulbs, stage, atmospheric lighting and fog; these are baked scenery, not dynamic 3D effects. Names, readiness, ownership, map panel and every control are live native UI. No illustrated cars or fake participant labels remain in the background.

`OldMap.png` and `NewMap.png` are native captures of the existing production map scenes, not invented map art. Regenerate with Godot .NET: `Godot --path . --script assets/frontend/lobby/render-map-previews.gd`, then refresh their manifest hashes and imports. This authoring script loads maps only to capture thumbnails; the joined Lobby loads the two PNGs and never instantiates either map.

Buttons reuse the approved Main Menu blank metal plate. Vehicle asset provenance remains in `assets/vehicles/sources.json`; map provenance remains in their existing registries. The 1280×720 design canvas scales uniformly with letterboxing. The background is 1672×941; it is stretched to the design canvas with negligible aspect-ratio difference.
