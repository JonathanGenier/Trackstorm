# Twin Fang Machine Gun

The Harlequin finish uses original `Harlequin.svg` paint artwork and
`Harlequin.gdshader` rest-space mapping, guided by the user-approved TS-320
`back 1.png` and `front 1.png` views (Jira attachments 10164/10165). Reference
pixels are not shipped. The atlas contains diamond, scroll, scallop and star
panels; its mipmapped import supports close and driving-camera distances.
Receiver faces use planar mapping; shrouds and both drum shells use cylindrical
mapping. Paint chips and roughness variation remain attached during articulation.
The runtime skin preserves the production GLB, Blender source, cartridge belts
and unrelated car/rack materials. See `sources.json` for artwork checksums.
The palette uses deep red, charcoal and warm aged cream, with dark frontal
star panels, restrained top ribbons, brass rims and red drum spokes. Wear is
biased toward panel boundaries and drum edges; fine scratches fade at distance.

Original Blender-authored production geometry based on the user-approved
`reference/ApprovedTwinFang.png` (built-in imagegen, October 11, 2026).
No external model, texture, font or runtime dependency was acquired.

Run Blender 5.2 with `--background --python-exit-code 1 --python
assets/items/machine-gun/source/BuildTwinFang.py` to rebuild the editable master,
runtime GLBs and checksummed manifest. The source links the unmodified production
Car. Authoring/reference directories are excluded from Godot import.

Godot coordinates are metres, Y up, -Z forward. The model origin is the existing
accepted-aim pivot. `FangYaw` contains the cradle and two underslung drums;
`FangPitch` contains `Gun_-1` and `Gun_1`. Muzzle sockets sit at X ±0.28896,
Z -1.8999. The authoritative shared muzzle plane lies between the two barrels.
The heavy receiver pair spans about one metre, supported by a 1.10 m cradle
bridge, thick cheeks and a 0.80 m yaw race. Receiver tails taper into a 0.218 m
swept envelope for overhead clearance. Suspended drums are 30% larger than the
initial model. The barrel section ahead of Z -0.18 remains lengthened 75.5%; the
bulk refinement does not extend the muzzle farther. Feed guides sit forward of
the pitch cheeks, and the animated feeds retain the concealed central route.
Each individual barrel flashes for its own alternating authoritative round; there are no extra shots.

The unchanged rack's crossmembers prevent the sketch's exposed feeds from crossing
the entire deck at arbitrary headings. Runtime curves therefore route through a
concealed central bearing throat, emerging above and below the fixed structure.
Drums use a central envelope to clear lift posts during yaw. These are
production-fit adaptations, not evidence of a literal pixel-for-pixel reproduction.

`TwinFangVisual` instances cartridges and dark links along an arc-length sampled
curve. Confirmed rounds advance a bounded feed animation and recoil envelope;
accepted pitch reshapes the feeds, while their parent supplies yaw. Release,
blocked/unready aim, replacement and lifecycle teardown stop emission and feed.
Smoke reuses the existing licensed Kenney smoke texture. Worn-metal and muzzle
shaders are original project work.

`BulletStreak.gdshader` is an original procedural project shader with no external
textures or dependencies. One arena-owned MultiMesh draws travelling gold bullet
streaks (3.2 m) and brighter tracers (6 m), clipped at the accepted endpoint. Camera-projected width, a luminous emission core and at least 90 ms of cosmetic travel for short paths keep shots readable at driving distance. These defaults add no bloom or lights and do not delay authoritative hits. Each gun repeats gold, gold, colored: left red, right green. Each begins at its
rendered barrel socket after that frame's vehicle pose, aim and recoil updates and keeps the authoritative hit endpoint. The authoritative actual-round ordinal
supplies barrel parity for the rig and flight presentation, independent of ammo-cost tuning. The shared central
ray continues to own collision, spread and damage; barrel sockets are cosmetic.

`check-twin-fang.ps1` renders imported production poses and checks feed/release
behavior. `check-weapon-aim.ps1` and `check-machine-gun.ps1` cover actual authority
and UDP peers. The dedicated visual fixture is scripted presentation evidence,
not proof of network firing or exhaustive mesh clearance.

`check-machine-gun-vfx.ps1 -Motion` checks exact barrel origins, endpoint clipping,
bounded flight and eight scripted shooters, with rendered 30/60 FPS captures.
`check-weapon-aim.ps1 -MachineGun -Visual -Impaired` runs the Machine Gun portion
of the existing three-peer native aiming fixture, including production pool delivery.

`BulletImpact.gdshader` is original procedural project work with no external
textures or dependencies. A separate fixed arena MultiMesh draws angular chips,
soft neutral dust and fine sparks at confirmed contacts, with local material
interpretation and bounded lifetime. It introduces no fire or explosion effect.
`check-machine-gun-impact.ps1` renders the surface variants and sustained load;
[impact presentation](../../../docs/features/items-machine-gun.md#impact-presentation)
documents pool bounds and the local-probe limitation.
