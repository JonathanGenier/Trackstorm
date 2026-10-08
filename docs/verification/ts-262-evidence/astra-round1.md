# Astra Runtime Critique — Story Round 1

TS-262 · `ts-262-jg` · 2026-10-07 (America/Toronto)

**Overall Score: 8.1 / 10**

**Quality Assessment: PASS (>=8.0)**

The scoped Machine Gun integration is coherent in operation: camera-directed fire reaches targets around and above the car, the existing mount follows accepted aim beneath a fixed rack, and interrupted or retired aim stops firing without a stale forward fallback. Close-side fire remains usable, and simultaneous shooters produce consistent remote outcomes. This is a judgment of the approved placeholder integration, not of a finished production weapon or overall combat balance. The unresolved earlier rendered camera-authority comparison lowers confidence and the camera score; the successful independent replay does not erase it.

## Scope and basis

Reviewed the repository routing, workflow, critique policy, weapon-aiming, camera and relevant held-item documentation, and the current TS-262 verification report. The approved scope consumes TS-261's ready accepted origin/direction while retaining existing spread, damage, ammo, cadence and sustained-use behavior. TS-234 is not a prerequisite. Production weapon/barrel/muzzle art and new firing, tracer or impact VFX belong to TS-234/235/236 and are excluded from this score. No new target, cursor or assistance system is expected.

This is runtime/experiential review, not an independent engineering review. Compilation and unit-test totals establish context but do not earn the score.

## Category Scores

| Relevant category | Score | Runtime judgment |
| --- | ---: | --- |
| Runtime Functionality | 8.6 | Forward, side, rear, elevated and adjacent-target fire caused native hits and damage. Self-blocked intent preserved ammo; release, expiry, suppression and switching retired firing as expected. |
| Multiplayer / Networking Experience | 8.3 | Three local UDP peers under impairment agreed on ordered firing outcomes; both shooters sustained fire and observer articulation settled to accepted aim. This is local network consistency, not WAN responsiveness. |
| Camera / Aim Coherence | 7.7 | Independent rendered ground/air settings coverage passed and inspected captures retain a usable central aim region. An earlier rendered camera-authority mismatch remains unexplained, so consistency is not fully established. |
| Visual / Motion Coherence within Scope | 8.1 | Inspected side, rear, elevated and close captures show a coherent raised mount and payload, with readable target feedback. Native observation checks confirm fixed rack, full deployed scale and shared pivot. Final art and VFX quality are excluded. |
| Runtime Stability | 8.2 | The independent full rendered run completed without warnings/errors through interruption, recovery, driving, landing, death and respawn. No long-session or performance certification is implied. |
| Runtime Integration | 8.1 | Firing, readiness, presentation, ammo and remote outcomes agree in exercised scenarios; retained cover and reconnect evidence supports surrounding integrations. The earlier camera comparison remains a material limitation. |

Overall is a holistic scoped judgment, not a rounded category average. Gameplay fun, physical-controller feel, audio quality and frame-time performance were not scored without adequate direct evidence.

## What Was Exercised

**VERIFIED — independent scripted native execution.** Ran the completed build with Godot 4.7.2 .NET:

```powershell
./check-weapon-aim.ps1 -GodotPath 'C:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Visual -Impaired -Oval
```

The production oval run passed **1,031 checks** through three real local UDP peers, with **589 native firing rays / 177 target hits**, **324 simultaneous sustained rounds**, and **470 identical ordered remote shot outcomes** at the observer checkpoint. It passed all **152 Camera preference cases**, then native driving, airborne shooter aim and landing, NOS-to-weapon switching, removal/new life, actual missile death and timed respawn. No warnings/errors were reported. Full unfiltered [independent log](astra-round1-oval-86025f50/aim.log) is retained.

The five directional firing probes intentionally use **zero spread** to isolate aim-origin/direction and native damage. The simultaneous sustained phase restores the **default six-degree half-angle spread**. These are distinct kinds of evidence; the near-perfect directional-probe hit rates do not establish default weapon accuracy. Native ray assertions checked accepted origin/readiness and the configured cone during actual fire.

**VERIFIED — firing lifecycle and clearance in that run.** Downward self-blocked intent refused firing without spending rounds. Release stopped both shooters. Aim expiry stopped held fire, fresh camera intent resumed it, suppression cleared presentation, and switching back did not replay prior sustained activation. Observer mount yaw/payload pitch, fixed rack, scale and pivot alignment passed for each firing direction. Packet blackout retired accepted aim and fresh delivery restored it. Camera/aim behavior while airborne was exercised separately; this is not evidence of a prolonged airborne dogfight.

**VERIFIED — direct visual inspection of saved native rendered frames.** Inspected prior prototype forward/side/rear/airborne/close captures and the prior oval simultaneous-fire frame. Also inspected this critique run's [side](astra-round1-oval-86025f50/mg-side.png), [rear](astra-round1-oval-86025f50/mg-rear.png), [close-side](astra-round1-oval-86025f50/mg-close-side.png), [simultaneous fire](astra-round1-oval-86025f50/mg-multiple-sustained.png), [self-clearance](astra-round1-oval-86025f50/05-self-clearance.png), [airborne shooter](astra-round1-oval-86025f50/07a-airborne-shooter.png) and [wide-FOV airborne view](astra-round1-oval-86025f50/settings-CameraFov-90-True-True.png). The target remains readable, the adjacent target receives visible impact feedback, and the rack/payload arrangement remains understandable across orientations. The placeholder label and box cannot communicate a production barrel axis; that approved art limitation is not treated as missing TS-262 work.

**INFERRED / corroborating recorded execution — not independently rerun in this critique.** Reviewed retained [Machine Gun](machine-gun-impaired.log), [rear-shield](rear-shield.log), [world-wall](world-wall.log) and [reconnect](reconnect.log) logs. They support native cover/damage ordering, exhaustion and exact partial-magazine recovery without replay. Reconnect recorded 299/800 rounds and phase 0.25 across three resyncs, including 125 seconds offline. The reported default-spread near-range hit coverage is substantially below zero-spread probes; this review does not claim unchanged hit probability or a proven balance result.

## Runtime / Operational Limitations

- **Unresolved rendered comparison:** the earlier [rendered oval log](oval-visual.log) failed `Authoritative articulation follows camera intent within network/pose tolerance: CameraDistance=1.15 air=False up=False`, after passing its firing stages. Its camera ray and local pivot-relative intent agreed immediately before that failure. The unchanged headless run passed, and this independent unchanged rendered run also passed the full matrix. The failed run remains **FAIL**. Cause, frequency, player-visible duration and attribution to production versus fixture timing are **UNVERIFIED**; neither a baseline-main reproduction nor a causal explanation exists. The passing replay narrows the evidence to an inconsistent observation, not a demonstrated fix or proof that the failure was harmless. This prevents a higher camera/integration confidence score, without overturning the directly successful scoped firing behavior.
- Input and target placement were scripted. Three sockets/arenas ran on one Windows machine with simulated 30 ms outbound delay, 5 ms jitter and 2% loss. Human competitive play, independent devices, real Internet/EOS behavior and physical-controller ergonomics are **UNVERIFIED**.
- I observed captured rendered frames and native runtime diagnostics, not continuous human-driven play or audio playback. Continuous animation smoothness, subjective input latency, sustained soundtrack/weapon mixing and frame-time behavior are **UNVERIFIED**.
- The existing pivot is the placeholder virtual muzzle. No production barrel/muzzle clearance or muzzle-origin artwork alignment was tested. Future TS-234 geometry requires its own validation; TS-235/236 feedback work is excluded.
- Rendered success does not establish all moving-target accuracy, maximum-player stress, prolonged-session behavior or multiplayer balance. Existing impact/tracer cues were sufficient to correlate observed actions with targets in these captures; their final polish was not rated.

## Round boundary

Round 1 ends here. No production/test changes, fixes, polish, Jira corrective work, commits or pushes were performed by this reviewer. The independent runtime artifacts originated in `.godot/aim-checks/86025f509a214e089247ae57c4bc7098`; representative frames and the full log are preserved in `astra-round1-oval-86025f50/` beside this report. A passing critique advises the human and does not authorize another round, acceptance or merge. Stop for explicit human direction as required by `docs/critique.md`.
