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
vehicle has a fixed 512-segment track ring, a 32-ripple wake ring and four 24-particle
emitters. Marks fade over 20 seconds (or are overwritten sooner); wakes expire in
0.9 seconds. Airborne/destroyed cars, teleports and life transitions break track
continuity. Sampling stops beyond 100 m; track shader distance fading uses each
fragment rather than the world-origin distance. Density can be reduced or disabled
by a future local optimization policy; no new user setting or replicated authority
is introduced. Resources are released with the owning vehicle/arena.

## Verification routes

`check-terrain-effects.ps1 -GodotPath <exe> [-Visual]` drives the real native vehicle
on isolated authored material fixtures, checks every required response, wraps the
bounded buffer repeatedly and renders all five packages on the production map.
The existing surface, water, oval/infield and dressing checks cover imported map
identity and driving/collision. Configs checks exercise the named selector, normal
Apply, both UDP participants, map-instance retention and process-restart persistence.
Reconnect and migration checks carry nondefault environment identities through the
existing recovery paths. Actual results and limitations belong in Story evidence.
