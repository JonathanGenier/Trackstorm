# Astra Runtime Critique — Story Round 2

**Story:** TS-76, approved elevated-crossing follow-up, 2026-09-23.

**Overall Score: 8.1 / 10**

**Quality Assessment: PASS (>=8.0)**

The completed crossing is coherent and forgiving in the exercised driving cases.
The broad dirt tabletops now visibly carry the east/west route onto the concrete
deck, while the lower north/south passage remains a distinct usable route. The
removed end barriers no longer contradict the jump approach. Offset crossings,
slow full approaches, side climbs and a real jump recovery work without damage.
The result clears the quality gate for this geometry-focused scope; this is not
a score for finished materials, the entire map or human driving feel.

## Category Scores

| Relevant category | Score | Runtime basis |
| --- | --- | --- |
| Runtime Functionality | 8.5 | Both slow approaches, six offset deck crossings and all four side climbs completed with full health. |
| Physics / Movement Outcome | 8.2 | Continuous support across the deck; side crests unload briefly and recover; the rendered jump lands and settles without collision damage. Human control feel is not scored. |
| Visual Geometry / Readability | 8.0 | Broad continuous approaches, rounded shoulders, an open elevated crossing and recognizable lower opening read clearly in rendered views. Final surface art is outside scope. |
| Runtime Integration | 8.0 | The upper route works in both directions and two production cars retain the lower passage. Existing surrounding slope-start limitations constrain wider recovery confidence. |
| Runtime Stability | 8.5 | All three independently executed cases completed without logged errors or warnings. This is bounded scenario evidence, not a soak test. |

The overall score is a judgment of the observed scoped result, not an average of
test counts or a reward for the amount of implementation work. Unverified human
control and camera behavior limit confidence above this score.

## What Was Exercised

After completed implementation verification, this reviewer independently ran
Godot 4.7.2 .NET against the final assets with the existing native fixture:

```powershell
./check-infield.ps1 -GodotPath <Godot-4.7.2-console> -NoBuild -Case Tabletop
./check-infield.ps1 -GodotPath <Godot-4.7.2-console> -NoBuild -Visual -Case WestJump14
./check-infield.ps1 -GodotPath <Godot-4.7.2-console> -NoBuild -Visual -Case TwoCarTunnel
```

- **VERIFIED — Tabletop traversal:** two complete 6 m/s approaches crossed the
  bridge and exited with 1000/1000 HP. These are recoverable slow approaches,
  not fully grounded crawls: each recorded 69 unsupported frames over the full
  route, including a 37-frame kicker flight. Six crossings at center and ±7 m
  offsets, in both directions, had zero unsupported frames and full health.
  Four continuous 8 m/s side approaches climbed onto the table, ending grounded
  with full health; brief crest unloads were 3–7 frames.
  [Log](astra-tabletop.log), [measurements](astra-tabletop.txt).
- **VERIFIED — Final rendered west jump:** the requested 14 m/s run produced
  10.90 m/s horizontal launch speed, 1.07 s flight and 11.23 m airborne horizontal
  travel; it landed on the raised tabletop and recovered grounded with full
  health. Peak vehicle-origin height was 8.36 m, not tire clearance.
  [Log](astra-west-jump.log), [measurements](astra-west-jump.txt),
  [airborne capture](astra-WestJump14-air.png),
  [landing capture](astra-WestJump14-landing.png).
- **VERIFIED — Two cars:** both production cars traversed the retained lower
  tunnel without damage. The primary car maintained support throughout the
  route. This was one local scene, not two network peers.
  [Log](astra-two-car.log), [measurements](astra-two-car.txt),
  [approach capture](astra-TwoCarTunnel-drive.png).
- **VERIFIED — Render inspection:** directly viewed the final
  [tabletop](tabletop.png), [overview](overview.png),
  [lower clearance](tunnel-clearance.png) and
  [side approach](TabletopSide-1_1-drive.png), plus the newly generated jump and
  two-car captures. The upper route has a generous continuous landing surface;
  the side profile reads as a climbable mound rather than a vertical wall.
  The lower opening is preserved between the piers. These are fixture-camera
  captures, not observation of the production chase camera.
- **VERIFIED — Common runtime probes:** each independent run passed the 2,598
  support probes and imported geometry checks, including sampled mirrored main
  side profiles. This supports the observed traversability; it does not prove
  every possible driving line.
- **INFERRED / supporting verification:** the final integrated report records
  all ten original routes, six jump cases, structural crossings/impacts, the
  connected tour and corrected northwest basin approach passing. The reviewer
  read that evidence but did not independently repeat those entire suites.
  See [final measurements](infield-final.txt) and the
  [Story report](../../ts-76.md#approved-follow-up--round-2-2026-09-23).

## Runtime / Operational Limitations

The full infield suite is **not green**. Its final run fails at the previously
recorded `BasinRecovery-85-28` stationary uphill start; both original
`TerrainToBank` stationary starts also fail. These remain unresolved surrounding
vehicle limitations, not passes. The source/verification record attributes them
to unchanged geometry and the existing uphill-start behavior; this reviewer did
not independently rerun those failures. Consequently this critique does not
claim reliable stopped recovery everywhere on the map.

Main side-profile symmetry applies to the completed tabletop approaches. The
outer collar and post-kicker transitions join asymmetric retained terrain;
whole-map or whole-collar symmetry is not claimed.

**UNVERIFIED:** human keyboard/controller feel, arbitrary approach angles and
speeds, stopped restarts on all side slopes, production chase-camera occlusion,
audio quality, dense combat, sustained performance, exported builds, remote
devices and authenticated EOS. Native logical-input driving and still captures
do not establish subjective handling, camera comfort or multiplayer balance.
No performance benchmark or complete live-motion observation is claimed.

The score excludes deferred material identity and environment dressing. No
production fixes or additional critique round were performed during this review.

**Human gate:** stop after presenting this critique. Acceptance, further
corrections or another round require explicit human instruction under
[the critique policy](../../../critique.md#mandatory-human-gate).
