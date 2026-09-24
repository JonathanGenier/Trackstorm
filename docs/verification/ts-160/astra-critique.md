# Astra Runtime Critique — Story Round 1

TS-160, 2026-09-24. Independent runtime/experiential review of the completed handling result, after comprehensive verification. TS-159 suspension is a preserved integration; TS-161 collision behavior is excluded. This is not a source or engineering review.

**Overall Score: 6.5 / 10**

**Quality Assessment: FAIL (<8.0)**

The local handling result is coherent: measured acceleration, surface differentiation, modest-slide recovery and small corrections behave predictably in the retained scenarios, and the newly executed rendered jump routes recover without damage. The integrated experience does not reach the quality gate. Eight impaired local peers fail correction acceptance, the isolated-host diagnostic fails with stalled acknowledgements, and substantial execution stalls remain. Continuous human control and broader drift recovery are not established. The score is a holistic judgment, not an average or a reward for passing test counts.

## Category Scores

| Material category | Score / 10 | Runtime basis and limits |
| --- | ---: | --- |
| Runtime Functionality | 7.5 | Local surface/acceleration/recovery behavior and rendered route completion work in the exercised cases; multiplayer completion is not reliable at eight peers. |
| Controls / Responsiveness | 7.0 | Retained small opposite steering corrections remain bounded; throttle lift/countersteer progressively reduce dirt yaw. Segmented inputs do not establish uninterrupted human responsiveness. |
| Vehicle / Movement Feel | 7.0 | Momentum is retained and modest slides settle without arbitrary spin in the traces; subjective enjoyment, large-angle recovery and physical-controller feel remain unverified. |
| Physics | 8.0 | Newly executed WestJump14/16/18 routes launch, land, recover grounded and retain 1000 HP; retained native surface and landing evidence supports stable scoped integration. This score excludes TS-161 collision behavior. |
| Multiplayer / Networking Experience | 4.5 | Two-peer acceptance passes with a 5.198 m maximum correction; eight-peer correction acceptance fails and the isolated-host diagnostic stalls host acknowledgements. |
| Observed Performance | 4.0 | Retained headless runtime telemetry records substantial frame stalls, especially at eight peers. This is execution performance, not a claimed measurement of rendered player FPS. |
| Runtime Stability | 6.5 | The fresh rendered check exits cleanly; the eight-peer isolated-host run raises a resynchronization failure. No broad crash-free or sustained-session claim is made. |
| Runtime Integration | 6.5 | Handling integrates with native surfaces and existing suspension in the exercised routes, but its full multiplayer runtime acceptance remains unresolved. |

Gameplay/Fun, audio, game feel/juice, UI, camera motion and animation continuity are not separately scored: the available direct observation does not establish their experiential quality. Still images establish visible poses and framing only.

## What Was Exercised

- **VERIFIED — directly executed by this reviewer:** `./check-infield.ps1 -GodotPath C:/Users/orsin/OneDrive/Desktop/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe -NoBuild -Case WestJump -Visual`. The clean rerun exited 0 with no error/warning, 2,598 support probes and the filtered WestJump cases passing. [Log](astra-westjump.log), [runtime measurements](astra-westjump-evidence.txt).
- **VERIFIED — directly viewed:** newly generated viewport PNGs for WestJump14 approach, airborne and landing states, plus WestJump18 landing. The car and route are readable; airborne wheel separation and the nose-down landing pose are visible. These discrete captures do not demonstrate motion smoothness. [Approach](astra-WestJump14-drive.png), [airborne](astra-WestJump14-air.png), [landing](astra-WestJump14-landing.png), [18 landing](astra-WestJump18-landing.png).
- **VERIFIED — direct runtime measurements from that execution:** WestJump16/18 recovered grounded with 1000 HP, respectively 15.80/21.90 m airborne travel and peak centerline error 1.00/1.10 m. These are selected routes, not exhaustive landing guarantees.
- **VERIFIED — retained operational evidence inspected, not independently replayed in this critique:** [dirt/grass per-tick traces](playtest-traces.json) show dirt yaw moving from -0.662 to -0.467 rad/s after countersteer and then near zero after centering; grass settles near zero without damage. The dirt powered turn is approximately 6.4 degrees of slip at its endpoint. This supports recovery from a modest slide, not general moderate/large-angle drift recovery. Inputs were selected between paused segments in an open fixture.
- **VERIFIED — retained network evidence inspected, not a new network run:** [two-peer metrics](network-two-client.json) record p99 correction 0.4222843 m, maximum 5.1980734 m and one large correction. [Eight-peer metrics](network-eight-summary.json) record player 6 p99 3.0303395 m against the unchanged 3 m gate, maximum 5.6693463 m and four large corrections. All eight joined and were grounded; that does not cancel the failure. [Isolated-host log](network-eight-isolated.log) explicitly records `Host acknowledgements stalled; reconnect to resynchronize.`
- **INFERRED:** meter-scale corrections and prolonged execution stalls threaten predictable driving and smooth peer presentation. The logs establish the corrections/stalls; this reviewer did not visually watch multiplayer rubber-banding. Same-machine contention is a plausible contributor, not an established root cause or grounds to waive acceptance.

## Runtime / Operational Limitations

The initial rendered invocation completed scenarios but emitted sandbox user-log and certificate-store errors; it is retained as [failed environment evidence](astra-westjump-sandbox.log), not a clean pass. The permitted rerun above resolved those environment errors.

**UNVERIFIED:** uninterrupted human keyboard/gamepad driving, physical-controller ergonomics, enjoyment, audio, moderate/large drift recovery across speeds, remote-machine/EOS/Internet play, exported builds, sustained multiplayer recovery/reconnect success and exhaustive slides/landings. Earlier bounded playtests and current still captures cannot replace these. Native harness passes support the scoped runtime observations but do not establish human feel. The prior tunnel collision is excluded from this handling judgment; no TS-161 changes are recommended here.

## Recommendations

### 1. Resolve impaired eight-peer runtime acceptance

- **Problem:** eight-player local driving does not satisfy the existing correction gate; the isolated-host diagnostic additionally loses host acknowledgement progress.
- **Evidence:** p99 3.0303395 m exceeds 3 m; isolated-host execution raises the resynchronization error. Eight-peer frame-time p99 ranges 256.915–2,221.506 ms, with host p99 1,115.611 ms. Even the passing two-peer run records p99 75.249 ms and a 1,107.651 ms maximum.
- **Severity:** High.
- **Impact:** the exercised integrated result cannot support confidence in consistent responsive multiplayer handling; acknowledgement failure prevents normal continued operation in that diagnostic.
- **Suggested Improvement:** if authorized, distinguish local scheduling saturation from simulation/network faults using reproducible eight-peer runs with host/client timing and acknowledgement traces, including separate-machine execution where available. Correct the demonstrated cause or establish the supported execution arrangement, then rerun the unchanged gates and observe rendered clients. Preserve the failure evidence and thresholds; do not treat a near miss as a pass.
- **Scope:** In Scope for TS-160 multiplayer integration verification; any broader networking redesign requires separate explicit approval.
- **Corrective Work Type:** New Corrective Task, if the human authorizes investigation/correction.

### 2. Establish continuous player control and broader recovery feel

- **Problem:** evidence is insufficient to judge the main handling experience highly polished; current deliberate recovery traces exercise a modest slide with pauses between control decisions.
- **Evidence:** the retained powered dirt turn has about 6.4 degrees of endpoint slip; no uninterrupted physical-controller session was exercised. This is an evidence gap, not proof of a defective recovery model.
- **Severity:** Medium.
- **Impact:** control confidence and the usable recovery envelope remain uncertain for players performing longer or larger slides and transitions under continuous inputs.
- **Suggested Improvement:** if authorized, perform a continuous rendered dirt/grass session with throttle lift, countersteer, moderate/larger attainable slides, alternating small corrections and transitions at several speeds. Record inputs and observations, preserve TS-159 behavior, and change tuning only if the observed result warrants it.
- **Scope:** In Scope.
- **Corrective Work Type:** No Code Change Required to establish the missing evidence; authorize any resulting correction separately.

## Recommended Next Round

Prioritize the impaired eight-peer failure and acknowledgement stalls, then continuous human driving and the recovery envelope. Repeat affected verification before a separately authorized Story Round 2. No tuning, implementation, Jira creation or additional critique round was performed as part of this review.

**STOP — human decision required.** [Critique policy](../../critique.md) states: “After presenting any formal critique, STOP and wait for explicit human instruction.”
