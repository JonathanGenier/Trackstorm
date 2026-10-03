# Camera-relative weapon aiming

The shared direct-fire foundation serves Machine Gun and standard Missile. Salvo keeps its separate mechanics. The production Car's deployed rack stays fixed: its child mount yaws and the child payload pitches. Existing placeholder weapon artwork is retained.

**Current integration boundary:** Machine Gun and Missile handlers still fire along their established forward direction. Connecting those handlers awaits the explicit TS-261/TS-262/TS-263 scope decision. The current square demonstrates the shared aiming foundation; it must not be presented as a verified live firing reticle or released in this unfinished state.

## Authority and lifecycle

`Core.Items.WeaponAim` solves chassis-relative yaw/pitch from a finite unit direction and authoritative vehicle pose. `ItemAuthority` owns transient intent and accepted solutions; `HostVehicleSession` enforces sender, generation and participation. Requests bind to current life, selected grant token, selection watermark and monotonic counter, never a claimed player or hit outcome.

The accepted contract contains owner/life/token/tick, yaw, pitch, origin/direction, clearance and readiness. Full yaw supports side/rear aim. Host tuning bounds angular rate and pitch. A tick advances articulation at most once, including retries of rejected gameplay batches. A conservative production-body ray test covers the pivot-to-exit path, so offsetting a muzzle cannot skip the owner. Self-intersecting requests clamp to a clear pose but remain not ready. The shared pivot is the deployed rack payload attachment at local `(0, 1.81, 1.845)` metres.

Readiness starts on accepted ownership/selection, independently of first camera input. A shared two-phase mechanical helper serves the existing Client rack animation and a conservative Core readiness gate. The gate integrates current trunk/rack speeds every tick, reserving initial deployment or full retraction/deployment on a capability replacement, even without a slot switch. Replacing Nitro additionally reserves its 0.24-second nozzle nesting. Capability history survives empty-slot and aim-only resets; life change, departure and recovery clear it. Client animation retains its existing smooth partial-cycle reversal rather than snapping to a conservative phase. Selection, depletion, invalid life, death, removal and suspension retire transient aim. Requests expire after 15 host ticks. No additional inventory, damage or checkpoint authority is introduced.

Aim preparation uses the exact current native observation batch passed to the item step, matching the existing weapon handlers' pre-command pose phase. If that item/world transaction throws, the host restores transient aim and readiness state before retry. Presentation remains downstream of a successful host step.

An accepted Proxy Mine placement retains ownership of the existing rack until placement and its 0.23-second arm return finish. Selecting or acquiring a direct-fire capability during that placement holds its readiness gate before the replacement path; it does not interrupt or redesign mine placement.

## Camera, assistance and HUD

Selected direct-fire items share the close, low [chase camera](camera.md), orbiting the fully extended rack attachment used by `WeaponAim.Pivot`. The elevated boom and shallow viewing angle leave the payload below the centered cursor. The deployed reference remains stable while stowed/deploying; accepted weapon rotation never feeds back into camera orientation. The shared camera can tilt from the level horizon to 85 degrees downward, equally with and without a weapon. RMB mouse movement and the remapped camera stick change desired aim. Releasing RMB or returning the stick to neutral uses the same smooth return as ordinary chase mode. Equipping a weapon introduces no separate camera pose or retained orbit. The shared weapon contract still supports its independently bounded chassis-relative yaw/pitch; camera controls remain constrained by the camera's view limits.

Mouse displacement remains direct and unsmoothed. Stick input uses the existing dead zone, then a radial response exponent. Controls settings persist independent sensitivity multipliers (0.25–3, default 1) and stick exponent (1–3, default 2). Focus, menu and diagnostic suppression retain their existing owners.

Only living, visible rivals near the view axis qualify. Native closest-collider visibility rejects cover and intervening cars. Small preference for the prior eligible candidate limits switching without persistent lock. Assistance scales only deliberate input toward it: default maximum friction is 8% mouse and 28% stick. Flicks, large stick turns and movement away bypass friction. Assistance never follows a target without input; ordinary camera recentering remains active. No directional magnetism, random wobble or homing is added.

The compact square stays exactly at the camera viewport center through rotation, tilt, recentering and resizing. It represents camera aim intent independently of accepted mount direction, limits and network latency; white means accepted readiness, orange means deployment/self-clearance restriction. When the center camera ray's closest native collider is another living, presented car, four short red/white corners replace the square and surround that car. They have no connecting edges or fill. This applies to adjacent cars too; projected bounds are clipped to the viewport. Looking away, missing a car or hitting intervening cover immediately returns to the centered square. Near-axis assist eligibility never triggers brackets by itself. Suppression, entry/resync, lost participation/local state and capability changes clear cues.

After camera follow, the same center ray resolves native geometry for HUD intersection and conversion to direction from the authoritative pivot, accounting for close-range parallax. World cover bounds the ray first; per-car queries transform that segment into each native chassis hull's collision pose, then transform the closest result back into its displayed pose. This follows remote interpolation and local correction smoothing without moving gameplay colliders. The HUD does not project the accepted weapon ray or claim perfect shot alignment. Mount presentation eases toward accepted angles at 30/s; smoothing has no firing authority.

## Replication and tuning

Item protocol version 17 adds replaceable aim kinds to the existing item path. Requests are exactly 56 bytes; publications carry generation, configuration revision, tick and at most eight solutions (29–557 bytes). Subtype bounds precede allocation. Existing connection/epoch fencing and host/generation/configuration/tick validation remain mandatory.

Requests/publications run at 20 Hz; authority advances at 60 Hz. Aim does not advance reliable item outcome revisions or resend complete world state. Empty sessions produce no continuing aim traffic. Retirement samples clear presentation, with a 300 ms local expiry covering lost samples/outages. Recovery starts without stale aim.

Six host-validated **Configs → Weapon aiming** controls expose turn rate, pitch limits, assistance cone and per-device friction through the existing catalog. Configuration version 38 carries them through live replication and recovery. Local sensitivities stay outside synchronization.

## Verification

Core tests cover directional reach, rate/clearance, ownership, ordering, expiry, restore and codec bounds. Pure driver tests cover sender/configuration/generation/tick guards and outage expiry. Camera tests exercise mouse/stick response at 30/60/144 Hz.

`check-weapon-aim.ps1 -GodotPath <exe> [-Visual] [-Impaired] [-Oval]` composes three real UDP peers, production Cars, native queries, input and Combat HUD. Default geometry is an isolated prototype platform; `-Oval` uses the production map. Impairment adds 30 ms outbound delay, 5 ms jitter and 2% loss. Scenarios include centered mouse/stick/recentering/resize presentation, exact car intersection versus near-axis misses and intervening cover, direction changes, elevated/fast-crossing targets, side-by-side proximity, driving, suppression, switching, NOS then high-speed direct-fire selection, removal and new life. Target trajectories are fixture-authored; shooter movement/NOS use native input/simulation. Captures/logs go under `.godot/aim-checks`.

The fixture also exercises a real missile-attributed death and ordinary timed respawn after active aiming. Nitro must remain selected to boost; the existing two-slot system cannot simultaneously select a direct-fire weapon. Synthetic stick input does not establish physical-controller ergonomics, and same-machine UDP does not establish multi-device Internet behavior. See the [current verification report](../verification/ts-261.md) for observed results and limitations.

Related owners: [items](items.md), [camera](camera.md), [input](input.md), [HUD](hud.md), [networking](vehicle-networking.md).
