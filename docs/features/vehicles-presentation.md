# Vehicles Presentation

Read the [shared system contract](vehicles.md) first. This reference contains only the selected subsystem; preserve its shared ownership and integration requirements.

## Current vehicle presentation

The production Car is an original Blender-authored armored coupe with enlarged off-road tires, narrow rounded wheel arches, window guards and a split rear deployment bay. The hood, doors, front fenders, rear quarters and working deck halves remain separate physical meshes through export, with formed edges and geometric panel gaps. This is an asset-authoring contract, without panel damage or customization behavior. Its editable master, import pipeline, lighting parts and attachment contracts are documented in the [vehicle asset notes](../../assets/vehicles/README.md). The existing `WastelandVehicle.tscn` resource path remains shared by startup, Lobby, practice, matches and Podium.

`VehicleVisual` instances the art under the unchanged practice/native and network presentation roots. `WheelPresentation` uses accepted signed velocity for four-wheel spin, the accepted steering angle for front carriers, and per-wheel compression for suspension travel. Chassis links and coil-over sleeves aim at explicit inboard carrier-local hub brackets, with rotated local-axis scaling and retained twin shocks; no force, ray, authority or network state is added. All four tires have the same 1.094 m outside tread diameter, with a common nominal 0.54 m radius driving spin and contact height. Authored wheel-center heights match. Tire centres are +/-1.38 m front and +/-1.30 m rear, with 0.526 m rubber width and slight fender overhang. Central coachwork is widened independently of wheelbase and shoulder height. The authored visual wheelbase is 3.351105 m, with the additional length carried through doors, cabin and quarter surfaces. Tires use connected closed rubber meshes with integrated tread. The closed deck meets the quarter shoulders, and bonnet returns support its panel seams. Hood and rear deck extend another 0.220/0.160 m beyond the earlier 0.025 m end refinements without moving axle stations. The exterior silhouette is defined by painted pillars and roof edges, with a separately batched internal roll cage tied to floor rails. Rear twin-shock upper anchors sit 0.120 m lower and 0.100 m inward, with connected braces and seats beneath the deck. Continuously rolled quarters and swept end corners retain independent body panels. Surface-fitted arch armor, mounted hood rails and connected bumper stays preserve panel readability. The collision hull length is 5.06 m and spawn exclusion diameter is 5.85 m. Physical ray spacing and the 2.601105 m handling wheelbase retain their established values; the resulting longitudinal/lateral visual support offsets approximate terrain-edge contact.

`CarDeployment.Deployed` is driven by confirmed selected inventory through `CarRackPresentation` in network gameplay. It opens the two deck halves before raising the rack, retracts the rack before closing, and supports mid-cycle reversal. See [held-item rack presentation](items.md#vehicle-rack-presentation) for switching, use and recovery. Offline driving practice has no item authority and keeps it stowed. Four rack-local weapon mounts remain stable throughout deployment. The raised rack origin is 1.34 m with sockets at 1.51 m, above the roof lamps. Independent trunk/rack configuration speeds default to 3× their original rates (0.24/0.2933 s); [Developer Options](developer-options.md#car-deployment) describes live tuning. Selected Nitro instead follows the fixed authoritative deployment timeline described in [Boost exhaust](boost-exhaust.md), so its readiness gate and mechanical pose agree regardless of cosmetic speed tuning. `CarLighting` owns per-instance lens emission and six forward/two reverse spotlights. Headlights, four roof lamps and red running lights stay on. Brake emission follows accepted opposing tire acceleration or handbrake; passive coast/gravity do not light it. Signed accepted velocity activates reverse emission and beams. Local and remote vehicles consume their existing movement snapshots; no simulation rule or additional wire state is introduced. Spotlights are unshadowed and distance-faded. Separate windshield, side and rear panes use reflective alpha glass so the simple cabin remains visible. Authoritative weapon behavior remains in the existing item system. The existing identification surface retains player color and damage-flash feedback. The [rack-mounted Boost jet](boost-exhaust.md) rises with selected Nitro, extends once raised, and ignites only during active use, without changing rack geometry or physics.

`check-car-articulation.ps1 -GodotPath <exe> [-Visual]` exercises native acceleration, steering, four-wheel rotation, landing compression/rebound, repeated deployment and reversal, stable mounts and separable lights. Rendered evidence and a per-tick trace are written to `.godot/ts259-round8/car`. The ordinary vehicle and network harnesses cover surrounding driving and presentation-root behavior. The physical hull remains the shared simplified sprung envelope, with raised bumper undersides and no solid wheel colliders; physics is not derived from decorative meshes.

The [Shield carriage](../../assets/items/shield/README.md) uses the rear rack socket pair
and extends aft of the production wrap guards. The selected shield follows the full interpolated
Car pose. A bolted saddle, twin boxed arms, hydraulic rams and four-jaw cradle carry
the shield. The folded stack appears at 0.42 scale once the deck clears and rides
the rack through its lift. After the production rack rises, it travels aft, reaches full size, pitches
upright, opens its two center halves, side wings and four horizontal hinges, and draws forward into its rear
position. Deselecting reverses that 1.15-second motion before allowing rack retraction;
an empty carriage also returns after world release. Three full-height physical
panels match the rear plate and short wraparound side wings. Original vehicle geometry,
wheel articulation and force laws remain unchanged. Shield/wall presentation is described
under [held items](items.md#shield-rear-armor-and-persistent-health).

A local early-use press waits for the exact selected shield to finish this path
before requesting world deployment. Readiness includes the confirmed selection
revision, capability and life, fully raised rack and completed mount motion;
an old item's raised carriage cannot release the new request. The existing Core
placement/consumption path remains authoritative.

Mounted armor skims authored driveable ground instead of acting as a rear skid on
slopes. The native adapter sweeps chassis and armor separately, excludes contacted
authored terrain bodies from the armor query only, then chooses the earlier blocking
result. The chassis retains its complete terrain response. Armor still collides
with vehicles and solid obstacles; weapon cover and deployed-wall collision remain
unchanged. Up to eight armor queries handle overlapping support bodies, retaining
contact if that bound is exhausted. The low visual edge may briefly enter terrain
at sharp slope transitions; no automatic shield lift or new suspension is applied.
Armor filtering uses the support body's authored identity, regardless of the
sweep normal: concave track edges can return lateral/downward separating axes.
This exemption includes all faces of that terrain body, while distinct barrier
and obstacle bodies keep their shield collision. The production track regression
uses the committed concave mesh; `check-shield-presentation.ps1 -ProductionTrack`
drives the actual west-bank join with two UDP peers.
