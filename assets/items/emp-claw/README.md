# EMP Claw production assets

Original Blender geometry, packed albedo/roughness/metallic/normal maps, rigid pivots and animation
storyboard follow the four user-supplied Jira concept attachments: launcher
10155, three stowed rounds 10156, activated claw 10157 and landed claw 10158.
Reference pixels are design input and are not redistributed. No new external
asset, dependency or audio is used. See [sources.json](sources.json).

`source/EmpClaw.blend` is the editable master. It links the unchanged production
Car for scale and contains individual mechanical components. The launcher and
claw are separate reusable assemblies; the three round sockets instantiate the
same claw in Godot. Named timeline markers show stowed, raised, emerged, seated,
ready, fired, landed, inflated, deflated and released poses. Runtime owns timing;
the GLBs intentionally export rigid hierarchies without animation tracks.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python-exit-code 1 --python assets/items/emp-claw/source/BuildEmpClaw.py
```

Rebuilding replaces generated masters; preserve subsequent manual edits first.
Blender 5.2.2 was used. The authoring script uses Godot coordinates for dimensions
and converts to Blender +Z up / +Y forward. Runtime is metres, +Y up / -Z forward.
Godot's extracted material PNGs are retained alongside their embedded GLB masters,
following the Wrench asset convention. Source files are excluded from runtime
import with `.gdignore`.

The saddle is fixed to `WeaponRack`. `TurretYaw` owns full horizontal rotation,
`CannonLift` rises 0.45 m and seats down 0.18 m, `CannonPitch` owns elevation,
`BarrelExtension` travels 0.99 m forward, its middle sleeve travels 0.50 m,
and `BarrelRecoil` moves 65 mm at discharge.
The fully deployed pitch pivot is rack + `(0, .47, 0)`, exactly the existing shared
weapon pivot `(0, 1.81, 1.845)`. The main air tank, gauge, supply plumbing and saddle
remain fixed; a secondary accumulator, regulator and return hoses follow the cannon.

The receiver, diamond-painted sleeve and flared muzzle have separate steel bands,
fasteners and guides. Each clown face has sculpted cheek/forehead volumes, raised
brows, an inset eye assembly, two rows of teeth, rear induction coils, armored jaws,
hinge caps and hydraulic lines. Primitive UVs preserve continuous nose shading;
custom armor uses per-part unwraps. The 512-pixel packed maps contain original
irregular enamel chips, fine scratches, roughness variation and micro-normal detail.

`RoundSocket_0..2` preserve original individual identity through visual depletion.
Their storage positions are rack-local `(x, -.125, .28)`, with x = -.37, 0, .37 m.
The rear-facing magazine is suspended beneath the fixed rack, independent of yaw
and pitch. Only the next round's loading sled transfers out of storage after a shot.
Each claw exposes `BalloonNose/NoseHitAnchor`, `Attachment`, `TrailSocket`, and four
`JawHinge_*` / `GripContact_*` pairs. Those are presentation/attachment points, not
hitboxes or a new collision system. The nose membrane is independently named.

See [EMP presentation](../../../docs/features/items-emp-claw.md) for the integration
contract, verification fixture and current gameplay boundaries.
