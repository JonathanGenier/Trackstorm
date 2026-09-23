# Astra Runtime Critique — Story Round 1

TS-75, `ts-75-pg`, 2026-09-23. Independent runtime evaluation after the recorded
comprehensive verification. No implementation changes were made for this review.

**Overall Score: 6.5 / 10**

**Quality Assessment: FAIL (<8.0)**

The terrain supports coherent moving routes and a repeatable, recoverable jump,
but ordinary uphill restarts can leave a supported car unable to proceed.
That directly prevents a robust terrain-driving experience. The terrain-only
constraint is respected; it does not waive recovery acceptance or make the
integrated result pass. Rendered route colors communicate the broad layout,
while local elevation and landing shapes remain difficult to read.

## Category scores

| Relevant category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 6.0 | Native jump and geometry succeed; supported basin restart fails. |
| Vehicle / Movement Feel | 5.5 | Automated moving traversal recovers cleanly, but throttle cannot reliably restart uphill. Human handling feel is unverified. |
| Physics | 6.5 | Real airborne travel, supported landing and intact HP; the supported stall undermines predictable motion on grades. |
| Runtime Integration | 6.0 | Terrain, vehicle and original bank work in moving cases; outward bank starts also fail in supplied runtime logs. |
| Visual Quality | 6.5 | Broad dirt/grass routes are readable and continuous; shallow relief and jump phases have weak visual separation in the inspected views. |

Scores judge the integrated outcome rather than averaging test counts. No score
is assigned to subjective fun, audio, human controls, normal chase-camera behavior,
network quality or sustained performance without sufficient direct observation.

## Current Jira spectacle and damage clarification

Before this Round 1 was presented, the reviewer directly read the live
[TS-75 description and comment 10113](https://jonathangenier.atlassian.net/browse/TS-75).
The acceptance scope includes memorable, crowd-impressing Carnage Circus jump
experiences. Its examples of 20–30 feet airborne height and roughly 70 feet travel
express the intended spectacle scale; they are not minimum dimensions for every
jump. This section completes the same Round 1 against that clarification and
does not represent another runtime run or critique round. The overall score
remains **6.5 / 10 — FAIL**.

**VERIFIED:** the inspected six jump records show 0.87–1.30 s of flight and
approximately 10.93–21.42 m horizontal launch-to-landing travel. The directly
rerun 16 m/s case traveled about 15.42 m during its 1.07 s flight. These are real
arcs with dirt landing/recovery, not merely tire hops. The recorded peak vehicle
origin values of 4.35–5.04 m are **world Y coordinates**, not airborne height
above local terrain. Neither those numbers nor the authored 2.4 m kicker proves
an above-ground flight height; this review did not measure the ground below each
apex. The upper travel result is about 70 feet, but that coincidence alone cannot
establish an impressive overall jump experience.

**UNVERIFIED:** the current evidence does not establish the subjective
crowd-impressing spectacle acceptance criterion. The sampled stills show an
airborne vehicle and a recoverable landing, but they do not demonstrate the
complete arc's dramatic effect in motion, at driving height or from a spectator
view. The numerical flight records support meaningful airtime, not a blanket
spectacle pass. There is also insufficient evidence to conclude that the final
geometry was constrained *solely* by landing damage; no such causal finding is
made here. These acceptance questions remain open rather than being silently
waived by the later retuning task.

The reported 100/100 HP and the existing harness's no-damage assertions are
observations of the exercised geometry, **not product constraints on jump size**.
A valid, stable landing that loses HP under the existing damage model must be
reported as a damage-model limitation rather than automatically condemned as bad
terrain. Do not shrink or flatten jumps solely to satisfy that assertion. Jira
assigns landing-versus-crash correction to TS-141 and subsequent jump re-playtest
and retuning to TS-142; comment 10113 explicitly places TS-141 after TS-75. This
critique does not reverse that order or authorize either follow-up. The supported
uphill-start blocker is a separate issue from landing damage.

## What was exercised

- **VERIFIED — direct independent execution:** `check-infield.ps1 -NoBuild -Case WestJump16 -Visual`
  using Godot 4.7.2 .NET, Compatibility rendering on the GTX 1070. The clean rerun
  passed 2,598 support probes and the selected drive. Flight lasted 1.07 s,
  landed at corridor metre 59.80, recovered grounded and finished with 100/100 HP.
  See [runtime log](astra-WestJump16-retry.log) and [measurements](astra-WestJump16-evidence.txt).
- **VERIFIED — direct independent execution:** the same script with
  `-NoBuild -Case BasinRecovery-85` failed with a native supported stall at
  `(-105.341995, 0.626933, 28.044296)`. Forward Y was `0.03520142`, wheel
  compressions were approximately 0.097–0.099 m and velocity remained near zero.
  See [runtime log](astra-BasinRecovery-85.log) and [geometry/initial-pose evidence](astra-BasinRecovery-85-evidence.txt).
- **VERIFIED — direct visual inspection:** final [overview](overview.png),
  [west layout](west-layout.png), [route drive](WestLoop-drive.png),
  [tunnel](tunnel.png), [blocked basin view](BasinRecovery-85-28-drive.png),
  and both supplied and independently regenerated jump
  [airborne](astra-WestJump16-air.png) / [landing](astra-WestJump16-landing.png) images.
  The car visibly leaves the terrain and returns to it. No holes or gross
  interpenetration are apparent in these sampled views.
- **VERIFIED — supplied records inspected, not independently rerun:**
  [full infield measurements](infield-evidence.txt) record all ten routes,
  six 14/16/18 m/s jumps and the northern basin drives before the failing
  southwestern start. [Connected tour](ConnectedLoopTour-evidence.txt),
  [two-car tunnel](TwoCarTunnel-evidence.txt), and the remaining basin record
  support broader traversability. Both [west](TerrainToBank.log) and
  [east](TerrainToBankEast.log) outward bank starts record supported stalls.
  [Oval verification](oval-final.log) records 3,087 passing checks;
  [network logs](network-final.log) record passing separate-process host/client
  runs. These records inform integration confidence, not claims that this reviewer
  personally played all those scenarios. Passing build/test records are not
  evidence of experiential polish.
- **INFERRED:** the stall is consistent with the documented existing
  opposing-motion braking/drive selection limitation. This critique reproduced
  the symptom, not a controlled causal experiment proving the exact Core branch.
  Repeated stalls on gentle supported grades make ordinary stop/restart recovery
  unreliable even though moving trajectories work.

## Runtime / operational limitations

The first visual run reached the integration success marker but its wrapper
failed on sandbox-only user-log and certificate-store errors
([initial log](astra-WestJump16.log)). Repeating with authorized local settings
access removed those errors and passed. The basin reproduction then used the
same access and failed on the gameplay stall, not an environment error.

This was automated native execution with rendered still inspection, not human
keyboard/controller playtesting or continuous live-motion observation. Subjective
input feel, all off-route stop/restart poses, ordinary chase-camera occlusion,
opposing traffic, dense combat, authenticated EOS, remote machines, exports and
audio were not verified. The network log contains headless frame stalls; it is
not a rendered performance benchmark or sufficient evidence to attribute a
performance defect to terrain. No low-end or sustained frame-time claim is made.
The review did not independently open/sculpt/export the Blender source.
Water behavior, extra structures and environment dressing are outside this Story.

## Material findings and recommendations

### 1. Supported uphill starts can trap the vehicle

- **Problem:** Forward driving cannot proceed from the tested gentle uphill
  basin approach; both recorded outward bank starts exhibit the same outcome.
- **Evidence:** Independent `BasinRecovery-85` stall above, plus the two bank logs.
  All four wheels remain supported, so successful collision support alone does
  not establish recovery.
- **Severity:** High.
- **Impact:** A player who stops or loses momentum on an ordinary grade can be
  prevented from continuing the intended route or returning to the oval. Passing
  moving routes and jumps cannot compensate for this basic recovery failure.
- **Suggested Improvement:** If separately authorized, correct uphill
  brake/drive arbitration in the vehicle system and demonstrate forward restarts
  from rest and slight rollback on the basin and both outward bank starts, then
  rerun the complete infield suite and affected vehicle checks. Preserve TS-74
  topology and do not flatten away the scenario to conceal the limitation.
- **Scope:** **Out of Scope — Out-of-Scope Recommendation.** The explicit
  decision is “Keep TS-75 terrain-only; report the blocker.” No Core correction
  is authorized by this critique.
- **Corrective Work Type:** New Corrective Task, only if the human authorizes it.

### 2. Local terrain relief lacks clear visual cues

- **Problem:** Dirt and grass distinguish routes, but nearly uniform surface
  shading makes the jump/landing profile and basin depth look shallow or flat in
  the sampled rendered views. The jump car's pose and shadow communicate height
  more strongly than the ground does.
- **Evidence:** West layout, route drive, basin and independent jump images
  inspected above. This is a directly observed readability limitation; actual
  missed jumps by human players have not been demonstrated.
- **Severity:** Medium.
- **Impact:** Drivers receive weaker advance information about crests,
  depressions and landing direction, reducing confidence when choosing speed.
- **Suggested Improvement:** If another terrain refinement round is authorized,
  strengthen restrained local slope/crest/landing value cues within the existing
  dirt/grass material approach. Validate from the ordinary driving camera and at
  approach speed; retain soft transitions and the approved geometry/topology.
  Additional structures, water or acquired detail assets are not prerequisites.
- **Scope:** In Scope, as terrain presentation refinement.
- **Corrective Work Type:** Existing Task (terrain presentation work).

## Recommended next round

First resolve the human decision on the reported vehicle dependency. If the
human authorizes the separate Core correction and another critique round,
prioritize supported uphill restart recovery, rerun the complete infield suite
and both outward bank cases, and observe ordinary driving-camera terrain
readability. Only then consider the bounded presentation refinement above.
There is no authorized correction or Round 2 in this report. Stop for the human
decision required by [critique policy](../../critique.md).

For jump acceptance, any human-authorized further assessment should observe full
arcs in motion from driving and spectator views, measure apex clearance against
the local terrain, and judge memorable height/distance/airtime together with
readability and recovery. Use the illustrative Jira dimensions as context, not
hard per-jump gates. Preserve the intended spectacle through TS-141's later
damage correction and TS-142's assigned re-playtest; do not make zero landing HP
loss the terrain-design objective. This is an evidence/acceptance qualification,
not authorization for immediate retuning or a new critique round.
