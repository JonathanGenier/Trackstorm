# Arcade Trophy-Truck Vehicles and Damage

## Behavior and Local Arena

The local practice arena (`-- --local-practice`) uses the [banked oval](oval-map.md), with eight vehicles placed at its eight authored starting-grid transforms. The old yard and props remain in explicit content fixtures. The first vehicle accepts local input; the other seven are physical targets. The two-car/crate layout remains only in the focused `LegacyTestLayout` regression fixture. Default controls are W to accelerate, S to brake/reverse, A/D to steer and Space for the physical handbrake; saved bindings, steering inversion and dead zone continue to apply. The chase camera follows the player. The combat HUD shows current/max HP and preferred-unit speed. **Reset practice arena** and **Detonate nearby** are on the unified [Developer Options](developer-options.md) page; the separate Arena Tools UI is retired. Reset starts fresh vehicle lives at the active map's stable spawn slots; only legacy fixtures have props to reset. Detonate exercises the generic explosion path beside the player, independently of inventory acquisition.

Multiplayer host tuning updates existing vehicle/damage configuration through `VehicleAuthority.Retune`, preserving life, physics, attribution and cooldown memory. Health preserves its percentage when Max HP changes; reduced steering limits clamp steering memory. Host simulation, client prediction and native suspension/impulse observation use the accepted configuration. The [developer control mapping](developer-options.md#editable-control-to-runtime-mapping) identifies each live field, application boundary and gameplay-effect test.

Movement uses a grounded arcade model: player intent drives forces through two tire axles and four independent suspension supports. Acceleration, braking, lateral motion and yaw retain momentum. The handbrake applies rear braking and reduces rear lateral traction; it cannot select a drift angle or create a release boost. Sliding is diagnostic feedback derived from lateral speed and rear saturation, never a simulation mode.

Production practice, hosted sessions, prediction and Developer Options defaults share the same `VehicleConfiguration` asphalt baseline. Hosted damage remains 1000 HP with collision scale 5. Saved host overrides still apply; Reset to Defaults + Apply restores the complete baseline.

`VehicleConfiguration` owns tuning. Engine/brake forces use a 900 kg reference mass, so increasing mass makes acceleration, stopping and steering response slower. The production record has a 3000 kg chassis and forward engine demand of 51.42857 m/s² at reference mass before tire saturation; forward/reverse caps are 44.44/11 m/s (160 km/h forward). Drive opposing meaningful motion first brakes it; low-speed engagement is described below. Coasting uses 0.28/s rolling resistance: speed decays smoothly to about 57% after two seconds and 6% after ten, without changing powered acceleration or snapping to rest. Forward engine demand tapers continuously as `Acceleration * throttle * (1 - (forwardSpeed / ForwardSpeed)^4)` before tire saturation; at or above the cap it becomes zero. The remaining-speed bound prevents numerical overshoot without cutting external velocity. External impacts may exceed drive caps; absolute command bounds remain 65 m/s and 8 rad/s.

Engine demand uses a fixed-step exponential response (`ThrottleRiseTime` 0.65 seconds, `ThrottleFallTime` 0.12 seconds). Half a second of full pedal reaches roughly 54% demand; sustained pedal retains the existing full-power force and RWD traction limits. The response applies to tire propulsion, while service braking or disabled driving cuts demand immediately. Dedicated aerial axes apply bounded angular acceleration immediately without tire support, independently of ground pedals; neutral input preserves rotation. `VehicleState.Throttle` survives retuning, snapshot restoration and prediction; new lives start at zero. No RPM, gearbox or additional drivetrain model is introduced.

Wheel angle follows the speed-sensitive range and progressive response described below. Front/rear lateral contact velocities include yaw and front steering angle. Lateral demand and drive/brake demand share a smooth friction limit (`tanh` saturation, default friction coefficient 1.9 and gravity 11 m/s²). Tire forces change both velocity and yaw through the 2.601105 m physical wheelbase; baseline steering never directly sets body heading or target yaw; the dirt-only slide assist is described below. High entry speed therefore runs wider, and engine/brake demand while turning can consume lateral grip. Rear handbrake application rises at 1/s; full application retains a surface-scaled fraction of the global 50% rear lateral grip and adds 64.28571 m/s² rear braking at reference mass. All engine drive fades while the handbrake is held so throttle cannot cancel a locked rear axle. Releasing the input eases mechanical rear braking, drive suppression and lateral grip loss together at the 3/s rate. Moving handbrake does not reduce front tire capacity. Surface-specific rear release allows deliberate loose-surface rotation while front steering and progressive release preserve recovery. The lateral force response is 55.71429/s before reference-mass scaling. Mass, acceleration limits and tire saturation preserve momentum during steering transitions. Bounded yaw damping (0.65/s, configurable down to zero) dissipates energy without imposing a drift angle or overriding countersteering.

Longitudinal propulsion and lateral traction have distinct demands within each supported axle's tire budget. With the handbrake released, propulsion can use up to a configured 55% traction reserve per driven axle when lateral demand would otherwise consume almost the entire budget. This allocation never exceeds pedal demand or pure longitudinal tire capacity; lateral force is bounded by the remaining friction ellipse before the recovering rear grip fraction applies. It preserves normal straight-line drive and leaves braking/coasting allocation unchanged. Throttle therefore pulls along the vehicle's facing direction while sideways momentum and yaw continue, without waiting for the diagnostic sliding flag to clear or injecting a special drift velocity. Production drive is RWD (`FrontDriveShare = 0`). `RearDriveGrip` (1.6) increases rear longitudinal capacity independently of lateral grip; service braking uses `BrakeGrip` (2 at full pedal). Smooth tire saturation evaluates demand in this friction ellipse. Front contact velocity and accepted forces are transformed through the actual wheel angle, creating longitudinal tire scrub as well as lateral force and yaw. A missing axle has no tire budget. Engine demand remains bounded to the portable acceleration diagnostic limit under extreme surface overrides. The reserve can be tuned down to zero; surface grip, wheel support, mass scaling and speed caps still constrain the force.

Automatic torque/steering power drift is retired. Ordinary turns retain the same combined tire demand, momentum, surface traction and suspension owners; traction loss from physical saturation and deliberate handbrake inputs remains possible.

Four chassis-down wheel rays supply compression observations to Core. Tire forward/right axes are projected into the support plane. Yaw torque and damping act around the support normal, with pitch/roll damping confined to the perpendicular axes. Suspension forces and point-velocity damping act along the support normal, leaving the tangential component of gravity intact; gravity can therefore move a slow vehicle down a steep bank. No downforce or bank/racing-line assistance is needed for this baseline. Default full extension is 1.645 m and normal spring stiffness is 22/s². Compression damping is 12/s and rebound damping is 16/s. Static sag is 0.5 m, leaving useful droop and about 0.48 m of chassis clearance. Beyond 0.55 m compression, progressive resistance adds 3500 times the squared excess compression to the normalized spring acceleration. Compression and rebound damping grow by `1 + 8 * deepTravel²`, where deepTravel is the fraction beyond bump engagement. Each support remains unilateral and bounded to thirty times gravity before its quarter-mass contribution; extreme landings can still reach native chassis contact. Initial spring and damping rates remain compliant. Damping uses chassis point velocity, avoiding derivative kicks from discontinuous terrain samples. Core applies bounded individual spring forces and lever-arm torques. Clean landings with at least three supported wheels use the existing portable landing-intensity envelope to damp positive normal rebound after forces are integrated. The envelope decays at 3/s and uses `WheelReboundDamping`; ordinary ramp loading and genuine airborne input are unaffected. Actual bounded spring/damper support force determines the tire budget; missing supports contribute no force. When a pure fixture omits wheel observations, the fallback budget uses the normal component of gravity. Axle compression and longitudinal load transfer distribute front/rear grip. Pitch/roll spring targets also respond to the actual longitudinal/lateral tire forces (compliance 0.004, tilt bound 0.16 radians). Bumps and landings therefore affect both physical chassis motion and traction. The existing wheel meshes translate independently from these same compression observations; airborne zero support extends them to full droop. Ground attitude assistance scales with supported wheel count and only applies within the existing landable roll/pitch envelope; roof, side and severe bumper contact receive no immediate upright target. Vehicle bodies cannot supply wheel-ray suspension support. Rear wheel presentation slows to a lock with accepted handbrake application. During deliberate rear lock, the powered dirt steering reserve fades out with handbrake application. The front tires use actual lateral contact velocity and yaw rather than a steering demand derived from unsigned road speed; this prevents assistance from feeding rotation after broadside. The same gradual release restores the powered reserve. Ordinary dirt steering and power donuts are unchanged. This is a compact sprung-body/axle approximation, without a transmission, differential, or individual wheel angular dynamics.

Legacy `PowerSlip` snapshot memory decays at 2.5/s, survives restoration/retuning and clears on a new life. Ordinary powered driving cannot build new power-slip memory. Braking demand remains 96.42857 m/s² before mass and tire limits. Nitro adds an independent chassis-forward rocket force outside the tire traction budget and raises the effective forward speed limit; see [Nitro](items.md#sustained-nitro-resource).

## Ownership and Fixed-Step Physics

`Core.Simulation` owns one private `VehicleAuthority` per registered stable vehicle ID. Each owns a complete immutable `VehicleSnapshot`: identity and life generation, movement commands, support, steering, handbrake, slip, load and suspension state, observed numeric physics, health, attribution and collision cooldown. `Simulation.State.Vehicles` and `GetVehicle` expose the committed aggregates; Client has no independent movement/health lifecycle or gameplay clock. The pure `VehicleMovement` and `VehicleHealth` helpers operate privately within the authority's candidate evaluation and remain directly testable without Godot.

`Simulation.Step(input, requests)` requires exactly one `VehicleStepRequest` per registered vehicle at the next global tick. It evaluates complete candidates in vehicle-ID order before committing any state. Effects, strongest-contact damage, repair and movement are evaluated in that order; destruction immediately disables driving. If any candidate is invalid, neither vehicle nor the global tick changes. `Simulation.Restore` likewise validates the complete vehicle set and configured memory bounds before publishing. Preparing candidates uses small temporary objects to guarantee this atomicity; the eight-car prototype is not a large-scale performance benchmark.

Client `VehicleBody` is a Godot `RigidBody3D` observation/command adapter with native gravity/force damping disabled and native collision solving/continuous collision detection retained. During the input physics callback, `VehicleArena.Advance` captures all practice bodies through `PhysicsServer3D.BodyGetDirectState`, calls Core once, applies the complete batch, then publishes. Observations contain solved pose/velocities, a unit support normal or zero, and copied raw contacts with numeric velocities, normal, impulse and stable other-vehicle ID. A short downward support ray bridges tiny solver separation gaps without preserving grounding during a real upward launch. Core decides contact severity and damage. Native objects, queries, shapes, inertia, transforms and impulse application remain in Client. Camera smoothing, flashes and blast visuals use presentation time only.

The command boundary is explicitly **before native solving**. `VehicleSnapshot.ObservedPhysics` preserves the latest solved pose and velocities received by Core. `Movement` preserves that pose plus the new commanded velocities and deterministic handling state; `Effects` preserves the accepted one-shot native impulses. Client writes the movement commands, then applies those impulses exactly once; their resolved motion is observed on the following tick. `Movement.CommandSpeed` includes vertical commanded velocity and is only a physics diagnostic. Player-facing `VehicleSnapshot.Speed` uses observed horizontal motion, so jumps and downward gravity commands do not inflate road speed.

Reset is a queued new-life intent. Core increments `LifeId`, clears health/events/collision gates and handling memory, and advances the same global tick as every other vehicle. Client applies the accepted reset transform/velocity and clears presentation effects. Event sequence numbers restart within the new life; all movement and damage ticks remain global. Reset never rewinds ordered input.

## Terrain handling profiles

<a id="loose-surface-slide-recovery"></a>
[Read this section](vehicles-ground-handling.md#terrain-handling-profiles).

## Health, Collision Damage and Combat Hooks

<a id="static-environment-response"></a>
[Read this section](vehicles-collision.md#health-collision-damage-and-combat-hooks).

## Terrain landing recovery

[Read this section](vehicles-collision.md#terrain-landing-recovery).

## Current vehicle presentation

[Read this section](vehicles-presentation.md#current-vehicle-presentation).

## Player-controlled airborne rotation

[Read this section](vehicles-air-control.md#player-controlled-airborne-rotation).

## Serialization, Replay and Limits

`VehicleStateCodec` uses a 163-byte version-twelve little-endian layout: version/tick; thirteen physics floats; support/sliding flags; steering angle and handbrake floats; surface ID; front/rear slip, longitudinal/lateral acceleration and landing intensity floats; four wheel-compression floats and a two-byte remaining Oil duration, two-byte Nitro prediction budget, binary32 forward thrust and speed multiplier, binary32 power-slip memory and binary32 airborne thrust fraction; then seven binary32 air-control continuation values (elapsed airborne seconds, filtered pitch/yaw/roll input and three legacy stabilization floats, with X carrying the release-hold latch and Y/Z reserved). The final two binary32 values retain the latched crash timer and progressive engine demand. Two bits in the flags byte retain brake/reverse press continuation across prediction, reconnect and retuning. All values round-trip exactly. Readers reject older versions, reserved flags, invalid surfaces, nonfinite values and out-of-range handling state. Restoration also validates the wheel angle against configured tuning.

`VehicleSnapshotCodec` is the complete portable vehicle boundary. Its version-five envelope is a version byte followed by bounded UTF-8 JSON (64 KiB total maximum). Named fields carry vehicle/life identity, lifecycle and optional respawn deadline, the version-twelve movement payload, an observed-physics payload using the same 163-byte encoding with neutral handling state and Concrete as its neutral surface, validated damage/event/attribution state, and accepted effect requests. JSON represents the two binary payloads as base64. Decode validates nested payloads and cross-field identity/tick/pose/life invariants. Restore additionally checks health capacity and configured handling limits. No Godot objects are serialized.

To restore Core, register the same vehicle IDs/tuning and call `Simulation.Restore` with the global tick/input and all decoded vehicle aggregates. Damage is already committed; restoring does not replay its events or accepted damage. A native reconstruction driver must restore the matching environment and body pose, write the snapshot's movement commands, and apply its retained one-shot impulses once before the matching solve. Fresh observations supply support/contact data on each subsequent step; input alone cannot reconstruct external collisions. There is no second Client gameplay timer to recover.

Equivalent initial pure state, input frames and external observations produce equivalent Core results. Native full-body replay is checked within a numeric tolerance on the current Godot backend; cross-platform bit-identical native physics is not promised. The [network vehicle loop](vehicle-networking.md) uses these restore APIs for live prediction and reconciliation. A replay file format remains separate future work. [Network held-item combat](items.md) uses the host session; the local practice damage demonstration remains independent. The wasteland vehicle visual and shared [chase camera](camera.md) serve gameplay presentation; the camera has no obstruction avoidance. The vehicle uses one sprung collision body with independently observed wheel support and a compact axle traction model.

## Verification

`check-trophy-truck.ps1 -GodotPath <path>` exercises both production adapters with
powered dirt acceleration, rapid steering reversal under rear lock, release recovery,
four-wheel/side/roof/bumper drops, low/fast rear impacts, head-on and eccentric contacts,
and a lighter target. Per-tick numeric traces are written under `.godot/ts-197/trophy`.
The production Van is not yet integrated; current model validation covers the Car.

Pedals engage drive within one braking step of rest (`StopSpeed + Braking * ReferenceMass / Mass / TicksPerSecond`). This avoids repeated braking of tiny gravity-induced downhill motion while preserving braking at meaningful opposing speed. Tire grip, pedal demand, mass, handbrake and ground resistance still constrain propulsion; no map, slope or basin exception is involved.

Core NUnit coverage checks digital shaping, analog conditioning, mass/drive/braking response, progressive speed-sensitive wheel steering, combined tire demand, analog propulsion during slip, immediate handbrake-release drive with gradual lateral recovery, bounded supported tire forces, handbrake speed loss/recovery, individual spring forces, impulse retention, surfaces, malformed snapshots and deterministic replay/restoration. Existing damage, item, authority and networking tests remain applicable.

`check-input.ps1` checks native default mappings, analog precision, digital ramps, mouse preference round trips, camera intent, remapping, reserved RMB, focus and frame publication. `check-vehicle.ps1` exercises native acceleration, braking/reverse, low/fast corner entry, lane changes, slide recovery, prolonged handbrake use, short/long low/fast power-out slides with throttle before/on/after release, acceleration after braking and collision, suspension/ramp/landing, concrete/mud transitions, collisions, damage, explosions, HUD and settings suppression. It compares traces at 30 and 144 render FPS; `-Visual` saves rendered evidence. `check-network-vehicles.ps1` exercises separate host/client prediction and reconciliation. `check-oval.ps1` additionally exercises three sustained high-speed laps, normal support velocity, low-speed bank descent/start, a short handbrake/countersteer/throttle recovery, excessive-input spin and track-to-infield traversal using production defaults. Both practice and network collision adapters additionally exercise straight acceleration toward 44.44 m/s, ten-second lift-off, normal turns followed by neutral throttle, repeated bank crossings and a 12 cm test crest with compression/rebound and settling assertions. The infield uses its authored material profiles. Synthetic driving establishes repeatable behavior, not physical-controller ergonomics or human judgement of the Wreckfest feel target.

Authority restoration, epoch fencing, checkpoint cadence and migration limits are described in [host migration](host-migration.md).

[Feature index](README.md)

The [Event Log](event-log.md) records every committed positive hit from the Core step result, with exact applied amount, target, source/attacker and remaining HP. It does not infer damage from snapshots or raw native contacts.

[Circus combat scoring](matches.md#circus-combat-score) consumes those same positive applied outcomes inside the atomic simulation candidate, including nonlethal collision damage. The victim/life/damage-sequence identity prevents repeat awards; no separate Client contact listener awards points.

[Circus stunts](matches.md#circus-stunt-detection-and-banking) read the existing physical sliding, support, observed horizontal speed and pose after candidate damage/lifecycle evaluation. They never change movement commands, tire forces or native collision behavior.

[Oil hazards](items.md#persistent-oil) reduce lateral tire force without an entry yaw impulse. Supported contact refreshes the configurable recovery timer; remaining Oil ticks are movement memory, retained by retuning, codec restoration and prediction; death/new-life reset clears them. Input is never disabled by Oil.

## Material identity observations

[Read this section](vehicles-ground-handling.md#material-identity-observations).

## Nitro speed recovery

[Read this section](vehicles-ground-handling.md#nitro-speed-recovery).

## Interactive handling verification

[Read this section](vehicles-ground-handling.md#interactive-handling-verification).

## Dirt cornering and delayed crash recovery

<a id="continuous-brake-through-zero-and-reverse"></a>
<a id="progressive-braking-and-deliberate-slides"></a>
<a id="speed-sensitive-ground-steering"></a>
<a id="mass-and-force-calibration"></a>
[Read this section](vehicles-ground-handling.md#dirt-cornering-and-delayed-crash-recovery).

## Deliberate surface braking and stationary handbrake

[Read this section](vehicles-ground-handling.md#deliberate-surface-braking-and-stationary-handbrake).
