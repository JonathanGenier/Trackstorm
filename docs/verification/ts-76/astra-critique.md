# Astra Runtime Critique — Story Round 1

**TS-76 — Infield structures and gameplay geometry**
**Overall Score: 8.2 / 10**
**Quality Assessment: PASS (>=8.0)**

Evaluated 2026-09-23 against implementation commit `4d04804` on `ts-76-pg`,
after the recorded comprehensive verification and corrected chamfered GLB export.
This is a runtime/experiential judgment of the approved structural scope, not
an independent engineering review or acceptance of the entire unfinished map.

## Category scores

| Category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.7 | Both ground axes remain usable through repeated native crossings; two cars fit and traverse together without damage. |
| Physics | 8.5 | Crossings retain support without unintended collision; repeated deliberate pier impacts stop outside the solid and consistently register crash damage. |
| Visual Quality — structural form and readability | 8.0 | Ground-level openings are unmistakable; substantial piers, perimeter beams, deck panel joints, parapets, chamfered edges and tapered returns give the junction an intentional structural hierarchy. Neutral shading supports the forms, although the broader scene remains visually unfinished. |
| Runtime Integration | 8.3 | Cars pass smoothly between open terrain and the covered junction; returns and drains sit outside the principal crossing lines, with no visible floating support bases in the inspected views. |
| Runtime Stability | 8.6 | Both normal-access native runs finish cleanly without engine errors or warnings. Repeated reset/impact scenarios remain consistent. |

The overall score is a holistic judgment rather than a category average. The
result is a convincing, robust structural asset for this geometry milestone.
The openings remain visually legible at vehicle scale and the added detail does
not compromise either route. Confidence is strongest in physical traversal and
structural presentation, and narrower in the full player experience because the
camera, manual handling and off-route conditions below were not established.
Final materials (TS-77), environment dressing (TS-81), a new elevated route and
vehicle tuning are not requirements of this score.

## What was exercised

**VERIFIED — directly executed by this reviewer:**

- `check-infield.ps1 -NoBuild -Visual -Case Structure` using Godot 4.7.2 .NET,
  GTX 1070, Compatibility renderer. The clean run passed 2,598 support probes,
  structural collision/terrain-join checks and 102 crossing-envelope rays.
  Twelve native, input-driven passes cover both axes in both directions three
  times each. Each retained 1000/1000 HP and zero unsupported frames. Four
  deliberate 15 m/s pier impacts stopped at approximately Z=-13.45 to -13.47 m,
  with approximately 984.37 HP remaining and repeatable behavior.
  [Clean log](astra-structure-clean.log), [measurements](astra-structure.txt).
- `check-infield.ps1 -NoBuild -Visual -Case TwoCarTunnel` independently rerun.
  Both production cars traversed without damage. The measured primary traversal
  retained zero unsupported frames and 1000/1000 HP, ending beyond the tunnel at
  Z=57.31 m. [Log](astra-two-car.log), [measurements](astra-two-car.txt).
- Inspected actual rendered PNGs with the image viewer: [tunnel](astra-tunnel.png),
  [ground-level clearance](astra-clearance.png), [terrain join](astra-join.png),
  [vehicle crossing](astra-crossing.png), [two-car approach](astra-two-car.png),
  and the generated whole-map overview. The opening is broad compared with the
  cars, the soffit forms a continuous covered area, and the visible returns and
  channels do not obscure the principal entrances. This was image inspection
  of native scenarios, not continuous video observation or manual driving.

The first sandboxed Structure attempt completed the scenarios but emitted log
file and Windows certificate-store access errors. It is retained as
[attempt evidence](astra-structure.log), not counted as a clean run. Repeating
with normal access produced the clean result above.

**INFERRED — corroborating evidence from preceding verification:** the recorded
all-route, six-jump, connected-tour, oval and local network checks support the
claim that the structure integrates without changing the retained course.
Those broader scenarios were not rerun by this reviewer. Their actual outcomes,
including the known uphill-start failures, remain in the [Story report](../ts-76.md).

## Runtime / operational limitations

- **UNVERIFIED:** human keyboard/controller driving feel, sustained combat,
  full-speed off-center impacts, every wing/drain edge approach, airborne
  parapet collisions, and manually recoverable off-route situations. Ray probes
  are not equivalent to native impacts against every structural component.
- **UNVERIFIED:** production chase-camera passage through the tunnel. The
  fixture uses its own elevated capture camera. Its crossing image visibly
  places the deck across much of the upper view; this establishes an obstruction
  in that fixture view only. It proves neither a pass nor a defect in the
  unchanged production chase camera, and no production camera score is assigned.
- **UNVERIFIED:** audio listening, networked tunnel traversal, remote-device or
  authenticated EOS behavior, exported builds, prolonged stress, and measured
  frame-time performance. Two local fixture cars are not a multiplayer test.
- The reported uphill starts in BasinRecovery-85-28 and both TerrainToBank
  cases remain known, approved-deferred vehicle limitations. This critique does
  not reclassify the complete infield suite as passing or resolve those failures.
- The reachable deck route is explicitly absent from approved topology; these
  runs assess the two ground crossings. Neutral structure materials and the
  surrounding unfinished terrain presentation are evaluated only to the extent
  needed to judge geometry readability and integration.

## Human gate

No production changes, corrective issues or further critique round were started.
Per [critique policy](../../critique.md), **“After presenting any formal critique,
STOP and wait for explicit human instruction.”** This PASS advises the human;
it does not authorize acceptance, another round or merge.
