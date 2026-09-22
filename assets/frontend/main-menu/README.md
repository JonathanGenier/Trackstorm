# Main Menu artwork

Project-supplied TS-122 references, authorized in the implementation request on 2026-09-21. No external asset library or third-party license is asserted. `sources.json` records the supplied filenames and SHA-256 hashes, including design-only references.

`Header.png`, `Plate.png`, `Icons.png`, and `Chain.png` preserve the supplied bytes. Runtime AtlasTexture regions make the four icons independently reusable, remove plate canvas padding, and repeat a chain section without stretching links. The header contains no flags, suspension chains, plates or labels. The complete menu reference is never rendered underneath the components.

The selected plate is the **same** blank texture through `Plate.gdshader`: identical alpha, spikes, lamps and alignment. The mismatched draft Active_button silhouette is deliberately not used. Labels use separately rendered text; the status plaque is a blank native stylebox with independent wording. Windows uses the installed Impact face, with Arial Black and Godot fallback fonts; no font file is redistributed.

`FlagRed.png` and `FlagCheckered.png` were completed with the built-in imagegen tool from the supplied flag references. Both preserve real alpha. Red RGB visible in some image previews belongs to transparent pixels; it is not an opaque background. Fabric is separate from the rigid header and tucked beneath its outer metal supports. `Fabric.gdshader` pins the upper 24% of each sprite and deforms only lower cloth sampling. Metal never uses this shader. Red fabric is reused mirrored on the left, with a separate checkered layer.

## Final generation prompts

Red flag edit: “Extract the supplied red carnival flag as one isolated fabric-only sprite. Remove every metal pole, ring, clamp, spike and rigid support. Reconstruct a complete clean sloping upper attachment edge, preserve weathered red/black cloth and torn tails. Genuinely transparent alpha surroundings and holes. No text.” Follow-up: “Remove the entire red glow/black background and all cast shadow. Return this exact cloth as a clean isolated cutout with genuinely transparent alpha surroundings and transparent holes. No colored matte, no glow. Preserve fabric completely.”

Checkered flag edit: “Extract ONLY the complete black and ivory checkered fabric flag as a single isolated fabric sprite. Remove red flag and ALL rigid metal supports/poles/clamps. Finish a clean fabric upper attachment edge sloping slightly upwards from left to right, and complete any formerly hidden fabric edge. Preserve torn worn dirty cloth detail, checkerboard scale and ragged red stained tails. Genuinely transparent alpha background and holes, no glow, no shadows outside fabric. Single cloth only, no text.”
