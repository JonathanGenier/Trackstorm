# Terrain detail and environment presentation

Runtime presentation reuses [surface identity](surfaces.md), the existing map
materials and the [Configs authority](developer-options.md). It never writes
collision, terrain geometry, material identity or handling state. Blender remains
the production source for authored mesh assets; no additional authored mesh or
third-party asset is needed for these shaders and pooled effects.

## Discrete session environments

`environment.preset` is the single authoritative setting under **Sky / Environment**
in Developer Options > Configs. The named dropdown stages changes; Apply Settings
validates, commits and saves through the existing configuration path. Cancel and
Reset retain their normal semantics. Stable identities are ClearBlue=0, Night=1,
EmberSky=2, Apocalypse=3 and NeonSunset=4. Unknown/fractional identities reject the
complete transaction. New saved files need no schema migration; missing keys use
Clear Blue. The complete configuration wire layout is version 11 with 101 values.

The accepted configuration travels through normal reliable revision ordering,
admission, reconnect and host-migration checkpoints. Joined clients never use
local overrides. EnvironmentPresentation projects that accepted identity into the
arena's single WorldEnvironment and directional light; changes do not reload the
map. Practice starts in Clear Blue. There is no gameplay clock, cycle, interpolation
or separate settings authority.

Each package controls its sky gradient, static cloud treatment, sun/moon color,
energy and direction, ambient color/energy, reflections, fog and filmic exposure.
Night uses a dark blue star field and cool moon/fill lighting; Ember Sky uses warm
orange/red light; Apocalypse uses heavy muted cloud/fog; Neon Sunset combines an
orange horizon, pink band and blue upper sky. These are original runtime shaders,
not imported sky textures.

## Surface detail and vehicle feedback

The infield shader adds metre-scaled gravel, soil variation, erosion streaks,
fine cracks, grass mottling, wet-bank variation and localized muddy sheen. The
existing field and its thresholds are unchanged. The water plane adds animated
normal ripples and broken shoreline treatment without moving vertices.

Every practice/network vehicle owns a TireFeedback presentation node. At up to
20 Hz it resolves individual native wheel contacts using SurfaceIdentityResolver.
Remote cars use their displayed pose and confirmed lifecycle; this cosmetic state
is local and not network-authoritative. An independent authored-waterline sample
permits wet feedback where the tire footprint is immersed without wheel support.

| Material | Track / interaction |
| --- | --- |
| Asphalt | Dark rubber marks while sliding |
| Concrete | Lower-opacity dark rubber on the lighter surface while sliding |
| Dirt | Brown disturbed tracks and dust |
| Grass | Wider exposed-soil-colored scuffs and low debris |
| Mud | Dark moist tracks and falling mud spray |
| Deep Mud | Wider, darker, heavier wet track treatment |
| Water | Falling splash spray and expanding short-lived wakes; no tire marks |
| Rock / unavailable | No tire marks |

These are visual strips and particles, not physical ruts or grass removal. Each
vehicle retains a 32-ripple wake ring and four 24-particle emitters. All vehicles
in an arena share one `TireMarkBatch` with one draw batch and one fixed allocation
of 65,536 quad slots (262,144 mesh-instance vertices / 131,072 triangles at the
absolute ceiling). Its default active submission budget is 49,152 segments;
eight cars emitting four segments at 20 Hz for 60 seconds require at most 38,400.
The ring overwrites its oldest slot when full. No segment creates a node, material,
mesh or network message. Marks default to 60 seconds, with full intensity through
42 seconds and a smooth final 18-second fade. Expired quads collapse in the vertex
shader so they incur no transparent fragment overdraw. Once the last possible
expiry passes, the whole batch submits zero instances. Submission count includes
expired slots until that reset or recycling; it always remains below the budget.
The fixed allocation never grows with time, distance travelled or player count.
Budget edits retire the existing batch immediately without reallocating storage.

Wakes default to 0.9 seconds and never enter the persistent ring. Airborne/destroyed
cars, surface changes, missing support, teleports and life transitions break track
continuity. Sampling stops beyond 100 m by default; fragment distance fading uses
camera-relative fragment position. Marks are arena-owned and survive a vehicle's
removal until expiry; wakes/emitters are vehicle-owned. All resources end with
their respective scene owners. The mark clock rebases hourly to retain shader
precision; the short wake ring clears at its hourly clock boundary.

Once every segment in a track or wake batch expires, its visible instance count
returns to zero, eliminating transparent submissions while idle. Restarting a
batch exposes only newly written segments. Wheel sampling reuses one native ray
query and exclusion list per vehicle and fetches water terrain once per sampling
pass; there is no cached map ownership or new authoritative state.

## Local Configs tuning

**Developer Options > Configs > Tire marks · Local graphics** and the seven
**Tire effects** surface groups use the existing shell's search, staged Apply,
Cancel, Reset, dirty-close protection and default colors. They are available to
hosts, joined clients and local practice. These values only control local graphics;
they use `PlayerSettingsController` and `player-settings.json` (`tireEffects`),
never `GameplayOptions`, host tuning revisions, migration checkpoints or wire
messages. Existing session-owned environment/gameplay settings keep their normal
host-only authority. Files without the new keys use defaults; invalid saved keys
default independently. Invalid staged values reject the entire local transaction.
Apply reports a local save failure and supports retry without losing accepted tuning.

| Stable key | Default; accepted range | Runtime effect |
| --- | --- | --- |
| `tire.lifetime` | 60 s; 1–180 | Base duration for newly emitted persistent marks |
| `tire.fade` | 0.3; 0.01–1 | Fraction of total lifetime spent fading; fade delay is the remaining fraction |
| `tire.width` | 1; 0.1–3 | Multiplies strip and wake width |
| `tire.intensity` | 1; 0–2 | Multiplies mark/wake/spray alpha, clamped to valid alpha |
| `tire.spacing` | 0.35 m; 0.1–2 | Minimum distance between successive strip endpoints |
| `tire.speed` | 0.8 m/s; 0–30 | Base mark/wake speed threshold |
| `tire.spray_speed` | 2 m/s; 0–30 | Base dust/debris/splash speed threshold |
| `tire.slip` | 0.35; 0–1 | Front/rear slip threshold for the corresponding hard-surface tires; confirmed drifting is also required |
| `tire.budget` | 49,152; integer 256–65,536 | Arena-wide submitted segment cap; oldest-first recycling |
| `tire.distance` | 100 m; 20–250 | Emission culling and shader distance fade |
| `tire.quality` | 1; 0–1 | Local sampling/spacing/spray density; zero stops emission while existing marks fade |

For each `surface` in `asphalt`, `concrete`, `dirt`, `grass`, `mud`,
`deep_mud`, and `water`, `tire.<surface>.*` exposes:

| Suffix | Default; accepted range | Runtime effect |
| --- | --- | --- |
| `duration` | 1; 0.05–2 (persistent surfaces) | Multiplies base duration: dirt/grass/mud/deep-mud disturbance can each recover independently |
| `duration` (Water) | 0.9 s; 0.1–3 | Absolute wake duration, independent of persistent mark lifetime |
| `width` | 1; 0.1–3 | Multiplies the distinct surface's authored width |
| `intensity` | 1; 0–2 | Multiplies the distinct surface's opacity and spray alpha |
| `fade` | 1; 0.1–3 | Multiplies global fade fraction, capped to the full duration |
| `density` | 1; 0–1 | Multiplies spacing/emission/spray density; zero disables that surface |
| `speed` | 1; 0–10 | Multiplies the base emission speed threshold |

Changes affect subsequent emissions; existing marks retain their born-time style,
duration and fade. Distance changes affect existing visibility immediately.
Lower quality reduces sampling to at most 10 Hz and increases spacing; spacing
saturates at 6 m so a nonzero setting cannot accidentally suppress every strip.
Maximum segment length is an 8 m continuity/teleport guard, not a style control.
The fixed four wheels, maximum 20 Hz contact cadence, wake/particle capacities,
support-normal rejection, surface lift, segment overlap, shader tread/noise pattern,
material RGB/roughness and particle gravity/spread are structural or authored
identity choices. They remain fixed to preserve contact safety and distinct TS-82
art direction; width/alpha/duration/density provide the applicable runtime controls.
There is no separate cleanup threshold: expiry and the active budget fully define
retirement. No terrain vertices, collision, handling or surface identities change.

## Verification routes

`check-terrain-effects.ps1 -GodotPath <exe> [-Visual]` drives the real native vehicle
on isolated authored material fixtures, checks every required response, wraps the
bounded buffer repeatedly and renders all five packages on the production map.
It also verifies expired-batch retirement/restart and records 120 rendered overview
frame intervals per preset, draw calls, primitives and reported video memory.
These include scheduling/vsync and are not isolated GPU timings or a hardware-wide
performance guarantee.
The existing surface, water, oval/infield and dressing checks cover imported map
identity and driving/collision. Configs checks exercise the named selector, normal
Apply, both UDP participants, map-instance retention and process-restart persistence.
Reconnect and migration checks carry nondefault environment identities through the
existing recovery paths. Actual results and limitations belong in Story evidence.
