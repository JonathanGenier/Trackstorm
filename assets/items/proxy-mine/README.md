# Production Proxy Mine

Original Trackstorm geometry, UVs and procedural patina authored in Blender 5.2.2 LTS. The editable master is `source/ProxyMine.blend`; `source/BuildProxyMine.py` rebuilds it and the runtime `ProxyMine.glb` from original geometry. Run from the repository:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python-exit-code 1 --python assets/items/proxy-mine/source/BuildProxyMine.py
```

The user-approved built-in imagegen sketch is retained unmodified at `reference/ApprovedProxyMineConcept.png`. It guides the segmented red/ivory armor, recessed copper magnetic windings, teeth band and guarded beacon. Its pixels are not used as runtime textures. The sketch's taller silhouette is compressed to the existing mine footprint. There are no newly acquired third-party assets. `sources.json` records tool version, hashes, provenance and measured mesh bounds. Source and reference directories are excluded from Godot import.

Blender uses metres and Z up; glTF converts to Godot Y up. The model origin is the unchanged collider centre. Body geometry stays inside the existing 0.65 m radius; the base is at -0.25 m. The cosmetic beacon extends to +0.348 m, below the previous beacon's +0.425 m top. No collision, physics, animation, lights or cameras are exported. Do not generate collision from this mesh.

The master keeps separate armor panels, braces, coils, rivets, teeth and beacon guards with editable bevels and UVs. Export batches static geometry into `ArmoredBody` (six material surfaces) and `BeaconLens` (one surface), totaling 14,861 triangles. Original 512-pixel patina images are packed into the Blender source and embedded in the GLB; the standalone grayscale `Patina.png` is the reproducible source field. Materials use ordinary portable PBR parameters without runtime shaders or texture dependencies outside the asset.

Godot's default GLB import extracts the six embedded color maps into `ProxyMine_* patina.png` alongside the model. These retained runtime texture derivatives come entirely from the original packed Blender maps, not from external acquisitions. Reimport after rebuilding the GLB to refresh them; the portable GLB itself still contains all image bytes.

Keep `BeaconLens` stable: `ProxyMineVisual` replaces only that material per instance and retains the existing 0.8-second blink period, 0.22-second bright interval and shadow-free red light. Meshes/body materials are shared by Godot's resource cache. `MatchResourceLoader` includes the asset in normal match preparation. Existing `ItemPresentation` owns deployed instances and accepted pose/removal; existing `RackItemVisual` uses exactly the same visual at its established 0.55 scale and mount offset. Rack timing, physics shape, force, damage and network state are unchanged.

The mine follows published position and support normal. There is no authoritative spin in the existing cylinder solver; no cosmetic tumbling or bobbing is introduced that would misrepresent ground clearance. Attraction can slide the model, support changes tilt it, and existing gravity continues its motion off edges. Only a committed impact removes the mine and triggers the existing explosion feedback. Explosion redesign is separate scope.

Run `check-mine.ps1 -GodotPath <exe> -Visual` for terrain, lifecycle, three-peer delivery and sixteen-mine chase-camera checks; `check-car-rack.ps1 -GodotPath <exe> -Visual` covers ordinary rack selection, use and restoration. See [held-item contract](../../../docs/features/items.md#magnetic-proxy-mine).
