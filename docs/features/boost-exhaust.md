# Boost exhaust presentation

`BoostExhaust` is a reconstructable Client component shared by practice `VehicleBody` and the interpolated visual root of `NetworkVehicleBody`. It reads the current vehicle snapshot: only an interacting vehicle with active Nitro deploys and fires the jet. Inactive overspeed recovery produces no thrust VFX. It does not consume input, change propulsion, delay authoritative thrust, or introduce network messages.

## Hardware and lifecycle

The [Blender-authored rear jet](../../assets/vehicles/boost/README.md) references the actual production Car master for fit. The fixed armored collar mounts centrally behind the fascia/bumper, below and behind the rack sweep. A nested sleeve travels .35 m rearward; the inner barrel/nozzle travels .72 m, with extending hydraulic rods. The original Car and rack geometry remain unchanged. Nitro has no rack payload: selecting it retracts any previous payload while the independent chassis jet follows accepted active Boost state.

Deployment takes .24 seconds with smooth acceleration/deceleration. Full extension opens a 40 ms ignition envelope with a brief size/brightness overshoot. Three instances of the authored UV shell form a hot core, primary flame and outer wisps. `BoostFlame.gdshader` uses additive unshaded rendering in the production Compatibility renderer. Travelling noise tears, longitudinal pulses, shock-cell modulation and tip displacement keep sustained thrust moving. Observed speed stretches the plume; stable vehicle/layer phases avoid synchronized fleet pulses. The throat heats and cools independently. No screen texture, dynamic light, bloom or renderer change is required.

Flames hide and both emitters stop on the first render update after active state ends. Existing world-space smoke fades over at most 1.3 seconds; sparks last .22 seconds. The nozzle holds for .18 seconds then retracts over .48 seconds. A short cancelled tap never ignites before full deployment. Repeated input reverses the hardware smoothly. New life, loss of participation/visibility, large pose discontinuity and explicit network reseed clear historical particles and pose. Vehicle removal frees owned runtime resources; imported assets remain shared.

## Budgets and verification

Each vehicle has three shared-mesh flame instances (1,728 triangles each), one 96-particle smoke emitter and one 10-particle spark emitter. Hardware is batched by material under each moving root. No per-activation nodes are allocated. Smoke emission ends beyond 65 m from the active camera, sparks beyond 35 m, and flames beyond 120 m. Particle/flame shadows are disabled. These are presentation budgets, not gameplay settings. The particle material applies vertex color for lifetime alpha and particle billboarding for correct size/rotation.

`check-boost-exhaust.ps1 -GodotPath <exe>` renders the production vehicle/map/environment through deployment, ignition, sustained motion, release, rapid reuse, full rack sweep, 1/8/32-car views, reseed, new life and death. Captures are written under `.godot/ts225-vfx`; `--boost-loop` repeats its scene for observation and Space restarts the sequence. Scripted poses isolate presentation, not physical driving or 32 network connections. `check-nitro.ps1` supplies actual two-peer native driving, held/released/reused/depleted Nitro and local/remote presentation cutoff checks. `check-car-rack.ps1` verifies the Nitro rack exception and other inventory transitions. Visual observation remains necessary to judge motion and readability.

See [vehicle networking](vehicle-networking.md), [vehicles](vehicles.md) and [Nitro authority](items.md#sustained-nitro-resource) for unchanged source-state contracts.
