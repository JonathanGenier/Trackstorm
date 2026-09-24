# Astra Runtime Critique — Story Round 1

TS-144, `ts-144-pg`, 2026-09-24. Independent runtime assessment after the
completed Story verification; no source-code review or implementation changes.

**Overall Score: 8.1 / 10**

**Quality Assessment: PASS (>=8.0)**

The seven placements form a deliberate, readable extension of the existing
pickup layout. The easy entry and connector give accessible alternatives, the
underpass stays separate from upper travel, and the two loop turns give reasons
to follow those routes. The shoreline offers a demonstrably dry collection line
beside a real water penalty. The elevated pickup rewards a successful jump over
a useful speed range without being granted on the tested ordinary crossings.
These distinctions are visible and work in native traversal. The score assesses
this placement Story, not general arena art, vehicle tuning or overall networking
performance, and is not an average rounded up to the gate.

## Category Scores

| Runtime / experiential category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.7 | All seven approaches work through both native adapters; intended jump awards and ordinary-crossing exclusions reproduce. |
| Feature-Specific Operational Quality — route choices | 8.2 | Distinct easy, tunnel, shoreline, jump and turn locations; dry safe line and actual inward-water penalty. Competitive route preference remains unverified. |
| Physics | 8.1 | Successful approaches finish dry with full HP; real airborne collection, tabletop landing and recovery. Score is limited to exercised pickup traversal. |
| Visual Quality — pickup readability | 8.0 | Bright marker silhouettes and labels remain distinguishable in sampled views spanning all seven placements and five presets, including the dark underpass and night jump. |
| VFX / Feedback — visible state | 8.0 | Active marker/particles and post-claim RECHARGING state are distinct in rendered captures; no audio judgment is included. |
| Runtime Stability | 8.5 | Independent rendered replay completes without logged warning, exception or crash. No long-session stability claim. |
| Runtime Integration | 8.3 | Native route replay agrees with recorded multiplayer claims, cooldown, repeated use and retained-state recovery; no new placement-specific conflict was observed. |

## What Was Exercised

- **VERIFIED, independently executed:**
  `./check-infield.ps1 -GodotPath 'C:/Users/orsin/OneDrive/Desktop/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Case Pickup -Visual`.
  Exit 0, Godot 4.7.2 .NET, OpenGL Compatibility / GTX 1070; 2,598 support
  probes and the complete Pickup route subset passed. See [replay log](astra-replay.log)
  and [replay measurements](astra-replay-routes.txt). Native logical input drives
  the traversals; scenario resets are setup rather than evidence of traversal.
- **VERIFIED:** hosted easy-south, easy-north, underpass, water-edge, west-turn
  and east-berm acquire their pickups with zero immersion and 1000 HP. Hosted
  jump approaches at 14/16/18 m/s acquire airborne, nearest distances
  2.169/1.377/1.164 m. The hosted ordinary crossing and tabletop-only approach
  remain 4.095/4.094 m away and do not acquire. Underpass travel cannot take the
  elevated pickup. Practice replay independently covers corresponding routes,
  slow-crossing exclusions, airborne travel and grounded recovery.
- **VERIFIED:** the 12 m/s shoreline line remains dry; a poor inward line
  collects and then reaches 1.002 m immersion. This establishes a real hazard
  distinction, not an inferred distance to decorative water.
- **VERIFIED, visually inspected:** rendered marker views covering trail under
  EmberSky, north under NeonSunset, tunnel under Apocalypse, water and west turn
  under ClearBlue, jump and east turn under Night. Additional Night water and
  Apocalypse jump views were inspected. Actual shoreline driving, north and
  west-turn approach, jump airborne and landing captures were also viewed.
  The replay generated all 35 marker/preset combinations; not every one of
  those 35 images was individually inspected. Saved representative images are
  alongside this report; the full replay image set is in `.godot/infield-checks`.
- **VERIFIED as recorded execution evidence, not independently rerun in this
  critique:** [impaired eight-peer pickups](spawns-impaired.log),
  [separate-process vehicle networking](network.log), [reconnect](reconnect.log)
  and [migration](migration.log) were inspected. These record consistent claims,
  cooldown and repeated category history; three reconnects including 125 seconds
  offline; and three-peer authority migration with retained item/spawn state.
  The [Story report](../ts-144.md) records the other completed final suites,
  including all twenty oval pickup approaches and Old Map regression.
- **INFERRED:** the two turn rewards should encourage route selection and
  contestable movement. Automated single-route driving demonstrates access,
  not that humans will prefer these routes or find their combat balance ideal.

## Runtime / Operational Limitations

- Native human keyboard/controller operation was unavailable. This was actively
  executed automated driving plus inspection of rendered frames and native
  outcomes, not a human-controlled play session or continuous live visual
  observation. Controls, subjective enjoyment, sustained combat balance and
  audio are **UNVERIFIED** and are not given fabricated category scores.
- The camera frames inspected establish visibility from those viewpoints only.
  They do not prove every approach angle or the complete live chase-camera
  experience. The landing image shows a pitched recovery pose; successful
  measured grounded recovery supports traversal stability, not a smooth-motion
  or tactile-feel claim from one still.
- Multiplayer correctness evidence is local UDP. Remote machines, authenticated
  EOS/NAT, Internet conditions and exported builds are **UNVERIFIED**. The
  eight-peer pickup fixture positions cars to isolate claims, so it does not
  establish eight-human contested driving quality.
- The inspected headless network log records substantial timing stalls: host
  frame maximum 3287.648 ms / P99 177.841 ms; client maximum 701.679 ms / P99
  98.632 ms, with client prediction error P99 0.476 m / maximum 2.338 m.
  These are operational evidence of stalls in that run, despite convergence.
  No causal attribution to the seven placements or smooth rendered multiplayer
  performance is justified. Rendered performance was not quantitatively measured;
  no FPS budget or performance category score is asserted.
- Default radius and the tested approach ranges define this assessment. Custom
  pickup radius, arbitrary speeds, combat pushes, alternate vehicle tuning and
  exhaustive shoreline recovery angles remain unverified. Existing geometry and
  presentation constraints are preserved scope, not newly evaluated redesigns.

The narrow placement result meets the exact 8.0 gate with limited headroom.
No material in-scope runtime defect emerged from this assessment. This PASS is
advice to the human, not acceptance, authorization for another round, or a claim
that the limitations above are resolved. Per [critique policy](../../critique.md),
stop for the human decision; no corrective implementation was performed.
