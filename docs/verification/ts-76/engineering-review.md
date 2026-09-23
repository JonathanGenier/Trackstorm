# TS-76 independent engineering review

Verdict: **PASS** (static engineering review after export correction).

Reviewed pushed commit `291be2e` and the subsequent corrected export in the working tree on `ts-76-pg`, PR #90, against
`origin/main` at `1a02028`. This is independent static engineering review,
not the Astra runtime/playtest critique. Read workflow, engineering standards,
Client routing, oval-map and infield authoring/source documents, complete Story
diff, fixture code and available verification logs. The implementation agent
provided the exact Jira TS-76 requirements and acceptance criteria. The two
pre-existing user edits are excluded.

## Resolved finding

**P2 — Export did not evaluate authored bevel modifiers; corrected.**
`assets/maps/infield/BuildStructures.py` retains bevel modifiers in the editable
source but calls `bpy.ops.export_scene.gltf` without `export_apply=True`.
Independent inspection of the committed GLB JSON finds deck panels, piers and
retaining wings each contain only 24 vertices and 36 triangle indices: the
original six-sided solids, without their authored chamfers. The production
asset therefore differs from the retained source and from the documented
statement that chamfers are evaluated on export. Enable modifier evaluation,
regenerate the GLB/source manifest, import again, and re-run affected geometry,
clearance and native collision/traversal checks before runtime critique.

The implementation agent added `export_apply=True` and regenerated the assets.
Independent re-inspection confirms all 62 exported meshes now include evaluated
chamfer geometry and all 62 nodes retain identity transforms. Representative
deck, pier and retaining-wing meshes now each contain 216 vertices and 324
indices. All four source/export/authoring/layout manifest hashes match the
corrected files. The material source/export fidelity defect is resolved; no
outstanding static engineering findings remain.

## Other review results

- Terrain blend, terrain GLB, layout and terrain manifests are unchanged against
  main. Existing routes, jump profiles, natural terrain and obstacle reservations
  are preserved. Omitting new elevated approaches is consistent with the explicit
  terrain/topology constraint and Jira's applicable-structures wording.
- The GLB contains 62 named collision meshes with identity node transforms.
  All four manifest hashes independently match current files.
- Scene and offline scene-baker both instance the production structural asset.
  Import suffixes provide static triangle collision; terrain import removes the
  inherited tunnel solids and their child colliders. No final CSG, runtime
  geometry generation, handling change, Core change or Shared layer is added.
- Collision and traversal fixture additions use imported geometry and real
  production input/native physics. Repeated crossings in both directions and
  deliberate repeated pier impacts are meaningful coverage. Awaiting/reset-pose
  assertion prevents an old native pose from corrupting impact observations.
- Neutral shading, original-asset registry/manifest and editable Blender source
  remain appropriately scoped. Main ancestry, version increment to 0.1.38 and
  Windows version representation are consistent with the inspected main.
- `git diff --check origin/main...HEAD` reported no whitespace errors.

## Evidence boundaries

The final `check.ps1` log records successful Debug/Release builds, 556 Core tests
and 352 non-native transport tests. Asset-dependent runtime cases and Astra
critique are being completed by the implementation agent. They are pending delivery evidence,
not additional static defects. At review time the full infield log records the
known BasinRecovery-85-28 uphill-start stall; no passing full-suite claim is made.
The recorded geometry/native results must be refreshed after the export fix;
that re-verification is in progress and is required for final delivery.
This review did not run heavy builds or native runtime in parallel with those
checks. Human control feel, exported builds and remote devices are not verified
by this review.
