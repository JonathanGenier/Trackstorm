# Play Menu artwork

Project-supplied PNG components assigned for TS-138 on 2026-09-22. The supplied filename `Fame.png` is retained verbatim. No external artwork or font was acquired and no third-party license is asserted. `sources.json` records unchanged source hashes.

The runtime uses separate atlas regions for the header and empty outer frame, independently placed buttons, and a separate `Flag.png` cloth sprite. Dynamic native labels, search, filter choices, rows, status and decision controls are drawn over this shell. The supplied full mockup and painted lobby-list rows are design references only and are never rendered. Empty/filtered lists contain no decorative fake rows.

`Fabric.gdshader` is project-authored. It fixes the upper attachment region while applying small traveling UV ripples to loose fabric; rigid header/support textures are independent. The existing Main Menu chain atlas is reused. No imagegen editing was used. Text uses Godot's existing fallback font, without redistribution of a font file.
