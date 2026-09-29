# Shared corner HUD artwork

`CornerAssembly.png` is the current blank shared chassis, generated with the built-in image tool from `reference/ApprovedCornerHud.png`, the user-approved worn-steel concept. The reference is excluded from Godot import/export by `.gdignore`. The generated PNG is retained byte-for-byte; hashes are in `sources.json`. No third-party assets or font files were acquired or redistributed.

The 1926x817 RGBA source contains transparent generator padding. `CornerAssembly.gdshader` samples the visible source region `(11,292,1901,457)` into a 600x144.24 design-pixel frame. The frame is positioned at viewport size minus its scaled size, so the flat base/right boundary reach the window edges without the old 16-pixel margin. The exposed upper contour retains shaped armor shoulders, short spikes, bolts and the skull divider.

All item identities, icons, resource bars, yellow selection perimeters, speed numbers and units are live native UI. The shader draws sixteen thick red/orange speed cells in the existing recessed channel using an elliptical center `(433,706)`, radii `(300,333)`, and inner-radius fraction 0.8 in source pixels. The channel's unused cells remain dark. The original generated dark segments remain only as substrate; no illuminated gauge or resource values are baked into the texture.

Each item well uses 159x86 local pixels, positioned at `(240,48)` and `(430,48)`. Its heading, centered icon area and percentage/rail row are distinct. Imported SVG alpha bounds define atlas regions once at load, removing unequal source padding. The Nitro and Machine Gun SVGs are project-authored silhouettes matching the approved jet/gun direction. Five cyan fuel cells represent continuously draining 20% portions of Boost charge; they never imply a number of uses. The other percentage rail is muted gold. Yellow outlines represent confirmed physical-slot selection even when empty.

Typography uses a Godot `SystemFont` with installed Impact, Roboto Condensed and Arial Narrow preference, with Godot's fallback if none is installed. Windows verification uses Impact. No operating-system font bytes are copied into the repository; appearance on systems without those fonts is not guaranteed identical.

## Generation provenance

Tool: built-in `image_gen.imagegen`, reference-guided precise-object edit, `transparent_background=true`; no CLI/API fallback. Exact production prompt: [corner-assembly-prompt.txt](corner-assembly-prompt.txt). The source concept was generated in the same conversation from the original HUD and approved by the user on 2026-09-28. The production derivative deliberately removes all baked data and selection, retaining the approved armor silhouette. Fidelity is assessed through native captures, not an asserted numerical similarity score.

Health.png and Timer.png remain in active use, together with the health/timer branches of Component.gdshader. Active thrust uses a slot-local cyan underline; it clears on release without removing remaining fuel.
