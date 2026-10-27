# Vehicles Ground Handling

Read the [shared system contract](vehicles.md) first. This reference contains only the selected subsystem; preserve its shared ownership and integration requirements.

## Terrain handling profiles

Core retains stable `SurfaceType` values Concrete = 0 and Mud = 1, and adds Asphalt = 2, Dirt = 3, Grass = 4 and DeepMud = 5. `SurfaceHandling` maps the existing [material identities](surfaces.md) into these portable profiles. `WheelSuspension` supplies center-selected diagnostic identity and individual wheel materials to practice, host and prediction; detection is not duplicated. Unauthored fixtures retain their explicit `SurfaceBody` profile (otherwise Concrete). Rock uses the configured Asphalt profile. [Water](water.md) adds profile 6, immersion observations and deep-water damage through this same authority.

`VehicleConfiguration` owns immutable grip, drag and acceleration multipliers. Asphalt uses an independently tunable grip multiplier with neutral drag/drive. Normal asphalt, dirt and grass purchase is deliberately strong for arcade control; concrete and soft-ground profiles retain their existing tuning:

| Surface | Grip | Drag | Drive |
| --- | ---: | ---: | ---: |
| Asphalt | 3 | 1 | 1 |
| Concrete | 1.5 | 1 | 0.98 |
| Dirt | 2 | 1.15 | 0.95 |
| Grass | 1.9 | 1.4 | 0.9 |
| Mud | 0.6 | 2.5 | 0.85 |
| Deep Mud | 0.5 | 5 | 0.8 |

Grip scales the asphalt tire coefficient (1.9), giving effective coefficients of 5.7 on Asphalt and 3.8 on Dirt, 3.61 on Grass, 2.85 on Concrete and 0.95 in Deep Mud. These increase supported tire authority, without adding downforce, gravity or suspension changes. Deliberate braking/rear lock and decaying legacy power-slip memory can reduce lateral purchase. Velocity-dependent rolling resistance creates bogging without a static force that prevents every start. Supported-axle grip still bounds climbing; not every grade below the support-normal cutoff is climbable on every material. The defaults are gameplay tuning, not a claim to measured soil properties. [Configs](developer-options.md) exposes asphalt grip and all existing editable profiles through the existing host transaction; deliberate overrides can change their ordering. Multipliers remain finite and bounded 0–100, including zero to disable a contribution.

Core receives surface and wheel observations through `VehicleObservation` on the fixed boundary. Grip scales the combined tire budget, acceleration scales forward/reverse engine demand, and drag scales rolling resistance. Under power, additional resistance is `CoastDrag * max(0, drag - 1)`; coasting uses `CoastDrag * drag`. Surface transitions change force rates without resetting momentum, steering or handbrake recovery. All paths use `VehicleMovement.Step` with identical configuration.

`VehicleState.CurrentSurface` retains the last supported surface in flight for feedback, while airborne tires exert no driving or cornering forces. Wheel rays provide physical spring response upon reacquiring terrain. A center ray selects the diagnostic surface when available, with a deterministic supported-wheel fallback. Each wheel material scales its share of the tire budget. Compression divides the existing axle load between its supported left/right wheels; missing materials use the aggregate fallback. Rear-wheel materials weight engine demand, and all contacts weight rolling resistance. Unequal left/right longitudinal forces produce yaw through the track-width lever arm, so partial grass contact can unsettle the vehicle without an arbitrary spin impulse. Native wheel materials are fresh observations, not retained handling memory; movement snapshots retain compression for presentation.

### Loose-surface slide recovery

Dirt and Grass add a bounded arcade assist to the existing axle model. A smooth squared lateral-speed ratio supplies half authority near a 22-degree slide, with a 2 m/s speed floor; the diagnostic sliding flag never switches physics modes. Supported front Dirt/Grass contacts reserve up to `DirtSteeringReserve` (0.95) / `GrassSteeringReserve` (0.65) of lateral force allocation for the filtered front-wheel direction. The blended force remains inside the existing combined tire budget and does not change braking demand. Grass reservation also fades with actual wheel commitment, restoring passive front correction as the wheel centers. This keeps countersteering readable when ordinary lateral demand is saturated.

`DirtRecovery` (4/s) and `GrassRecovery` (3/s) progressively bring slide yaw toward the filtered wheel's turning direction. The requested rate is bounded by available dirt traction and road speed; each tick's correction is limited by supported front traction and the existing axle inertia. The handbrake reduces this assist to one quarter while yaw follows the requested turn, preserving deliberate rotation. Countersteering retains full recovery authority to arrest wrong-way yaw. The front allocation reserve fades with handbrake application; fully locked rear rotation uses actual front contact velocity. Countersteering yaw recovery remains bounded by front purchase. It never sets heading or linear velocity, adds propulsion, resets handling memory or uses a target drift angle. Missing front support, flight, disabled driving and Water immersion cannot enable it. Mixed contacts weight the assist by each supported front material; Oil's reduced traction also bounds it.

The reserved front Dirt demand includes the front contact velocity caused by chassis yaw and projects it into the steered tire frame. Once that contact follows the wheel direction, the reserve no longer adds torque from forward speed alone. Lateral correction still uses the existing supported tire budget; power-slip and handbrake mechanisms remain independent.

Asphalt uses strong passive tire purchase without the loose-surface yaw assist. Dirt drag and acceleration retain their existing defaults. Straight dirt acceleration is unchanged, preserving kicker approach performance. Existing state and prediction restoration need no extra memory. [Configs](developer-options.md#progressive-handling-controls) owns both tuning controls and their ordinary persistence/replication path.

## Material identity observations

The native adapters expose the current [material identity](surfaces.md) through the shared wheel-query path. Core maps this identity to handling; local Stats still reports the raw material separately from the committed handling profile.

`check-terrain-handling.ps1 -GodotPath <path> [-Visual]` compares eight-second acceleration, steering, handbrake recovery and asphalt return through both production adapters, plus standing starts on 20-degree Asphalt/Concrete/Dirt, 15-degree Grass, 12-degree Mud and 10-degree Deep Mud fixtures. The infield suite exercises actual mapped slopes and basin recovery. These are sampled grades, not a universal climb-angle guarantee.

[Water interaction](water.md) uses the existing handling and health/lifecycle owners; it has no parallel damage or respawn system.

## Nitro speed recovery

[Sustained Nitro](items.md#sustained-nitro-resource) supplies boost intent from authoritative inventory each held step. Movement immediately removes rocket thrust and restores the ordinary drive cap on release or exhaustion, then uses `OverspeedDeceleration` (default 3 m/s²) to remove horizontal speed above the normal limit gradually. The serialized inactive recovery continuation survives prediction and restoration; it clears when speed returns to the normal range. Direction, vertical motion, steering and ordinary brake/coast forces remain under the same vehicle owner. Canonical handling is never overwritten.

## Interactive handling verification

`check-world-collisions.ps1` exercises the actual map's steep tunnel dirt face,
bank neck and side slope, pillar, retaining wing, perimeter and intact rock through
practice and hosted adapters at 3/12/35 m/s initial speed with front/corner/side
orientations, pressure, reverse and re-contact. It records penetration, motion and
contact processing cost under `.godot/world-collision-checks/`. Run the scene with
`-- surface=bank-face adapter=network speed=12 angle=0 steer=1` for the sustained
steering repro. `original-terrain` reconstructs the original dense collider only
inside this verification fixture for a matched baseline comparison.

`check-world-collision-network.ps1` exercises two real UDP worlds on the actual
tunnel banks, with 30 ms latency, 5 ms jitter and 2% loss, at those three speeds.
It checks ordinary reverse escape, host-owned damage convergence and native step
cost. These are separate physics worlds in one process; the separate-process
`check-network-vehicles.ps1` remains the integration check.

The interactive scene accepts `--world-collision-playtest` for isolated output
under `.godot/ts-274/playtest` and a camera low enough to inspect the underpass.

The explicit verification scene `scenes/verification/handling_playtest.tscn` uses the production practice adapter on the real oval/infield map. Add `-- --handling-flat` for an isolated surface fixture. Atomically replace `.godot/ts-160/playtest/input.json` with a unique `id`, `frames` (1–600), normalized `steer`, `throttle`, `brake`, and optional `handbrake`. Add `-- --handling-network` to exercise the production hosted/prediction collision adapter in the same rendered scene. `--handling-host-seed` reads the existing host-local tuning without writing it; `--handling-round8` selects the preceding asphalt/brake defaults for comparison. Commands may select `keyboard` or `analog` to exercise actual synthetic native steering/pedal capture. Traces include recorded axes, observed versus commanded velocity and terrain-contact normals for launch diagnosis. Optional `spawn` (three coordinates), `yaw` (radians), and `speed` initialize a fixture; optional `surface` selects a flat-fixture material. Flat fixtures accept `grade` in degrees (±45); optional `pitch`/`roll` in radians and `verticalSpeed` set controlled initial drop poses. `airRoll` selects the existing air-roll input. Traces include orientation, vertical velocity, compression, uprightness and native contact count. The scene pauses between bounded input segments for observation, preserves commanded velocities on resume, and writes per-tick `trace.json` and rendered `view.png`. Run only one instance per workspace. This scene is never loaded by production gameplay. Segment-based observation does not establish physical-controller ergonomics or continuous human play.

[Destructible environment](destructible-environment.md) adds match-owned staged rocks and cleared soft cover. Both native adapters consume the same Core state; version-three resume checkpoints and nested migration retain damage, stages, movement continuation and plant bits without replaying impacts. New matches restore authored state.

The existing static-response coefficients are live host controls under Configs → Collision:
wall resistance, direct-crash dissipation, eccentric rotation and maximum per-contact
angular change. Production defaults remain 0.18/s, 0.95, 0.08 and 1.2 rad/s. Both native
adapters read the accepted `VehicleConfiguration`; no response adds restitution or changes
separate damage rules. See [control bounds and fixed invariants](developer-options.md#collision-and-destruction-tuning).

[Out-of-bounds damage](out-of-bounds.md) uses the authored oval perimeter, existing health/lifecycle authority and confirmed HUD feedback. Its life-scoped state survives tuning and checkpoint recovery; prediction never originates it.

## Dirt cornering and delayed crash recovery

`DirtCornering` scales directed front traction at low/medium Dirt speeds. Full assistance extends through `DirtCornerFullSpeed` (8 m/s) and fades to zero at `DirtCornerFadeSpeed` (28 m/s). Steering commitment increases the supported tire budget by up to `DirtCornerGrip` (20%), turning travel direction along with the nose. The former `DirtCornerPowerSlip` target is retired. Engine demand and full wheel range are unchanged by this assistance. Automatic powered slip is retired; supported tire budgets remain the force limit. This boost applies only to supported Dirt wheels, without using the center diagnostic label. The existing bounded yaw recovery targets the filtered wheel direction, while restored rear grip straightens the exit. Dedicated rear-lock drift remains the stronger sustained rotation option.

Gravity defaults to 11 m/s², with suspension length adjusted to preserve static ride height and extension damping of 16. Spring support scales with the fourth power of chassis/support alignment: an almost sideways ray cannot become a vertical catapult. Bad-attitude terrain/chassis contacts remove upward separation speed, damp tangential scraping at 2/s and retain rolling motion with 0.65/s angular damping, while raw contact velocity, direction and impulse remain available to authority. This does not change collision damage.

The existing crash latch, first-wheel control restoration, delayed minimum rolling rate and stranded-rock handling are described in [terrain landing recovery](vehicles.md#terrain-landing-recovery). Contact dissipates energy; unsupported bounces retain inertia. Faster rotation is never replaced by the recovery rate. No new automatic righting or aerial recovery is added.

The Configs catalog also exposes deep-travel damping gain, landing-envelope decay, front brake share, dirt assistance speed bounds/gains, crash scraping/rolling damping and recovery ramp duration. Fixed-step frequency, geometry, contact classification, unilateral force ceilings and numerical safety bounds remain structural invariants. Retired air-delay and stabilization overrides have no effect.

### Continuous brake-through-zero and reverse

A brake hold during forward travel applies service braking until longitudinal speed is within one available braking step plus `StopSpeed`, then transitions directly into reverse propulsion on the same hold. There is no release/repress requirement or sticky neutral. This threshold includes pedal strength and reference/body mass, preventing native downhill gravity creep from trapping a continuous hold in braking. A new press from rest still tolerates `ReverseEngagementSpeed` slope creep. Physical release marks `ReleaseTail`, so decaying keyboard pressure cannot initiate reverse; a genuine new press takes ownership immediately. `BrakeMode` remains portable across prediction, restore and retuning with the existing version-twelve movement layout. The handbrake remains the separate stop/hold and deliberate grip-release control.

### Progressive braking and deliberate slides

Service braking, pedal buildup, handbrake application/release, surface modifiers, tire force budgets and suspension remain under their existing owners. Ordinary throttle plus steering no longer manufactures `PowerSlip`: neither the old torque/steering target nor the dirt powered-slip/corner boost is evaluated. Physical combined tire demand can still saturate naturally, and handbrake rear release remains deliberate. Legacy saved power-slip memory decays through the retained recovery path instead of snapping momentum; new lives start at zero and never rebuild it from powered steering.

### Speed-sensitive ground steering

Full wheel range remains 0.9 radians below 8 m/s road speed. Between 8 and 35 m/s, a smooth cubic envelope reduces requested range to 25% (0.225 radians), then holds that range. Speed includes contact-plane lateral travel and reverse movement. The range changes the wheel target only: existing wheel filtering/rate bounds retain continuity, and no body direction or velocity is scripted. Initial steering rate remains 0.95 rad/s with a 0.3 s filter; a wheel opposing recorded steering intent can return at 2.8 rad/s. Keyboard release/counter rates are separately tunable through the input catalog. Low-speed full articulation, tight maneuvers, handbrake rotation and the existing physical handling model remain available. Air-control inputs retain their separate existing interpretation.

### Mass and force calibration

The default 3000 kg body retains the 900 kg reference-force convention. Engine,
reverse, service brake, handbrake and lateral response defaults scale together
by 3000/1400, preserving the prior per-mass drive and steering response. Fixed
external impulses now produce smaller velocity changes and collisions retain
the larger real mass. Springs and gravity already operate per unit mass; this
calibration does not add downforce or change intentional jump trajectories.
Ground controller steering uses the [input precision curve](input.md); maximum
wheel angle, keyboard shaping and active airborne controls remain independent.

## Deliberate surface braking and stationary handbrake

Each wheel resolves an immutable `SurfaceBraking` response separately from ordinary grip/drive/drag. Normal traction remains Asphalt 3 > Dirt 2 > Grass 1.9. Brake response never changes TS-268 input shaping, wheel filtering or speed-sensitive steering limits. Other materials retain their existing moving-brake response and grip/drag/drive profiles. Static holding uses the shared supported-friction rule, with water immersion excluded.

| Material | Braking effectiveness | Rear lateral grip at full service brake | Multiplier on moving handbrake rear grip |
| --- | ---: | ---: | ---: |
| Asphalt | 1 | 0.9 | 1 |
| Dirt | 0.8 | 0.55 | 0.45 |
| Grass | 0.6 | 0.2 | 0.16 |

Braking effectiveness scales both opposing longitudinal demand and its friction budget, including rear-lock braking. It does not reduce forward/reverse propulsion. Service-brake rear lateral purchase interpolates with accepted pedal strength; front purchase retains the square root of that fraction to preserve steering authority. Moving handbrake rear purchase multiplies the global `HandbrakeGrip` and interpolates with its existing application/release memory. At the default global 0.5, fully applied handbrake retains 0.5 / 0.225 / 0.08 rear purchase. These values are runtime-tuned gameplay defaults, not measured tire coefficients or enforced ratios. Supported wheel materials determine split braking torque and dirt corner capacity; changing the center diagnostic label alone cannot change those forces.

Below `HandbrakeHoldSpeed` (0.5 m/s contact-plane speed), a held handbrake with at least two supported wheels uses static tire friction instead of deliberate grip release. Engine drive is interrupted immediately. After gravity, a bounded opposing impulse removes tangential creep and yaw, limited by available support load, material purchase, Oil reduction and mechanical handbrake strength. Suspension motion, pose integration and collision impulses remain physical; there is no anchored position or new continuation state. No wheel support, disabled driving or water immersion disables the hold. Insufficient or zero configured traction cannot hold a slope. Releasing the button immediately removes static holding while the ordinary moving-brake envelope releases progressively.

The terrain harness compares normal driving, service braking, deliberate handbrake entry/countersteer/recovery, twenty repeated material changes during sustained driving, and ten-second settled holds at ±20 degrees facing uphill/downhill and across the slope. Both practice and host/prediction adapters execute these cases. Core tests also cover zero traction, unsupported/moving cases, split braking torque, configuration persistence and deterministic restoration. These fixtures complement actual production-map transition and bank checks.
[Shield rear armor](items.md#shield-rear-armor-and-persistent-health) extends the network body's collision envelope behind the bumper. Before the vehicle step, item authority routes actual plate contacts to the persistent shield pool using this vehicle's unchanged collision tuning. Intercepted vehicle-pair manifold points cannot also damage the protected car; unrelated contacts retain ordinary damage. Core also resolves weapon interception against the same oriented envelope. Native collision response remains physical, while shield HP and removal remain item authority.
