# Vehicles Collision

Read the [shared system contract](vehicles.md) first. This reference contains only the selected subsystem; preserve its shared ownership and integration requirements.

## Health, Collision Damage and Combat Hooks

The simulation's vehicle authority is the sole HP owner, using `VehicleHealth` to evaluate changes. Production practice and production hosted arenas configure MaxHP as 1000; the generic damage helper and focused legacy fixtures retain their 100 HP default. Damage carries a finite amount, source category, stable instigator identity, bounded context and an ordered global tick. Negative/zero requests cannot heal or overwrite attribution. Actual damage clamps HP to zero; repair clamps living vehicles to MaxHP. Reaching zero emits exactly one destroyed outcome and enters the authoritative Dead lifecycle. Further damage cannot duplicate it, and repair cannot revive a wreck. Production arenas automatically respawn after the configured tick delay; explicit development resets remain available. Inactive bodies are hidden and non-colliding, and Core suppresses driving, items and damage participation. The [lifecycle document](death-respawn.md) describes death, respawn and reset policy.

Static obstacle severity uses pre-solver normal approach speed, weighted continuously from harmless shallow incidence (normal fraction at most 0.2) to full crash severity (0.8). Tangential speed and solver friction impulses cannot damage a scrape. Other contacts retain the larger of relative normal closing speed and impulse divided by receiving mass, including the existing landing recovery rules. Each vehicle authority selects the strongest received contact and assigns the other vehicle's stable ID for attribution; walls/props use world identity zero. Core computes zero damage at or below the default 4 m/s threshold, then 5 HP per excess m/s in production (3 in the generic record), capped at 100 HP. The Core-owned 12-tick victim cooldown coalesces general vehicle/obstacle impacts; separate terrain body impacts use the contact continuation below.

### Static environment response

Rock tops can supply downward wheel-ray support at the ordinary support-normal
threshold while their side faces retain the anti-climb obstacle response. Chassis
landings on a rock top retain the existing inelastic lever-arm response with the
native chassis friction coefficient (0.15), rather than pinning the car without
rotation. Slow bodywork contact above a rock can leave an upright car high-centred.
The existing delayed crash-roll recovery also covers that case when fewer than two
wheels support it or driven-axle weighted suspension travel is below 0.1 m. The
condition requires speed below 1 m/s, an upward rock contact beneath the chassis,
and uses the existing crash delay/rate/ramp and serialized `CrashSeconds`. Tiny
flickering droop contacts cannot continually restart that delay. Useful wheel
support clears recovery and restores driving. Ordinary terrain/vehicle contacts
retain their existing first-wheel recovery behavior; no collision cooldown is added.

After applying tire drive and thrust, Core prevents fresh drive from increasing
inward velocity into an observed blocking rock side. It uses the existing
support-plane response normals, preserving the adapter's already-solved momentum.
The closest permitted drive velocity satisfies all blocking rock faces together;
sequential projections could push outward from an earlier face in a crevice.
Fresh throttle cannot repeatedly feed native bevel recovery and lift a stalled
chassis. Engine demand and natural tire slip continue; reverse/tangential escape
remains available. Gravity, suspension, landing recovery and supported rock tops
keep their ordinary motion. The constraint uses current contacts only, with no
timer or extra replicated state.

Both adapters retain native rock contact normals instead of substituting the
infrastructure union-ray normal, which can hit a different convex-rock facet.
After resolving actual impacts, they sample blocking rock faces at the solved
pose with a 1 cm skin covering the native 5 mm separation margin. This prevents
recovery gaps from alternately dropping contact and reapplying inward drive.
The sample uses a dedicated intact-rock query bit, excluding terrain and other
infrastructure from the extra narrow-phase work. Both adapters query the chassis
shape directly for each nearby rock's contact point/normal, without running a
second motion/recovery solve or creating an additional physics body.
The sample applies no displacement and carries zero impact velocity/impulse;
non-rock contacts keep their existing normal and response paths.

`check-repeated-collisions.ps1` covers slow rock pressure, full throttle from rest
immediately against five rock shapes with measured vertical settling and sustained
longitudinal position/reversal checks, centred rock landings,
close contact and deliberately shallow embedding through both adapters, plus
sustained vehicle-pair momentum. `check-following-contact-network.ps1` exercises
two following pairs across four native UDP worlds with delay, jitter and loss.

Both native adapters call the pure `EnvironmentCollision` response. It removes inward velocity without restitution, applies exponential along-surface resistance at 0.18/s once per observation, progressively dissipates residual tangential crash motion as incidence becomes direct, and retains only bounded severity/lever-arm crash torque. Duplicate manifold contacts do not multiply drag or crash torque. A grounded side response lies in the support plane; obstacle bevels cannot convert along-road speed into a launch. Practice replaces the static solver's velocity contribution using the preceding command and retained effects while keeping the native solved pose. Network sweeps use the same projected normals and response before returning observations. There are no collision pose or heading snaps.

`EnvironmentContact` distinguishes tagged driveable terrain and legacy `SurfaceBody` support from untagged obstacle bevels. Non-terrain obstacle faces with upward normal below 0.95 remain blocking sides rather than wheel ramps; flat tops, tagged terrain, suspension and landing recovery retain their support path. Short contact rays select exposed surfaces at overlapping module joins. The oval additionally uses a continuous collision perimeter without internal end caps. Practice native restitution is zero; friction remains 0.15 for other native contacts. Severe eccentric crashes retain bounded rotation, while shallow rubbing adds no torque.

Current temporary Nitro remains unchanged. Its ordinary acceleration can overcome scrape resistance, which remains active while boosted. Direct boosted impacts use the same stopping and authoritative damage path. Fresh contacts require no additional replicated state or collision codec; host and prediction use the shared adapter and the existing snapshot boundary.

`check-environment-collisions.ps1 -GodotPath <exe>` exercises both adapters with sustained real-oval contact, box seams, shallow entry/exit, direct and oblique impacts, imported boulders and piers, repeated setup/impact runs, and ordinary/Nitro scrape-to-corner transitions. Per-tick evidence is written under `.godot/environment-collision-checks`. These synchronous adapter checks supplement actual transport harnesses; they are not multi-device evidence.

`check-tunnel-scrape.ps1 -GodotPath <exe> [-Visual] [-Case <prefix>]` repeatedly drops tilted vehicles against both tunnel inner banks at 3/12/25 m/s and three headings, through practice and hosted adapters. It records contact processing times, managed-GC pauses and motion, and checks bounded corrections and falling contact. An isolated native-query corpus compares collision/travel with the original query path and measures relative sweep cost; separate native checks cover masks, self-exclusion, rear-shield enablement, disposal and absence of phantom rigid-body support. Rendered fixed-step fixtures and input/observe playtests supplement these measurements; absolute timing remains dependent on machine load.

`check-environment-collision-network.ps1 -GodotPath <exe>` exercises two actual UDP peers in separate native worlds with 30 ms delay, 5 ms jitter and 2% loss. Both vehicles scrape and then strike a static wall; the check compares host/client HP and damage sequence after convergence. It is an extended local check, not Internet/EOS or separate-device evidence.

`DamageEffect` carries nonnegative requested damage, a world-space impulse and a world-space application offset. It is independent of missiles, inventory and item classes. The pure explosion helper linearly fades damage and impulse to zero at the radius, biases the outward direction upward, and uses up as the defined direction exactly at the center. Client queues effect intents for the next coordinated fixed step. Core evaluates damage and returns accepted impulses/outcomes; Client applies the impulses at their supplied offsets, allowing native inertia to create rotational response. The arena demonstration uses an 8-metre radius, 55 HP center damage and 15,000 N·s center impulse. Landing or secondary impacts can cause additional collision damage.

Damage feedback is Client-only: identification-panel flashes, a dark panel color, a visible expanding blast, HP/destruction text and [arena audio](audio.md) for engines, skids, collisions, damage, destruction and respawn. Audio consumes confirmed state and routes through the Vehicle/SFX hierarchy. Camera, feedback and UI cannot change HP or damage math.

## Terrain landing recovery

Tire-down landings are safe at any impact speed, including suspension bottom-out. Core excludes a terrain underside contact before damage selection when its point is below the local origin by more than 0.15 m and the truck is within the terrain-relative landing envelope (50 degrees roll / 40 degrees pitch), or an actual wheel is supported with the chassis underside facing the terrain and the contact inside the longitudinal wheel footprint. The latter admits partial-wheel touchdowns outside the ordinary attitude envelope while preserving steep outboard bumper strikes. This immunity does not expire, depend on an airborne episode, or consume a damage cooldown. Roof, side and bumper/body contacts remain eligible, including during the same observation as a safe tire contact. Obstacles and other vehicles never receive terrain landing immunity.

`LandingState` retains airborne/recovery classification for feedback and a six-bit body-contact mask for damage continuation. A terrain body impact is eligible when it starts after separation or reaches a new body face. Multiple points on that face and continuing contact coalesce. Fresh impacts use the existing severity-to-HP curve immediately, even inside the general collision cooldown; three separate roof hits can therefore damage three times in one crash. Static obstacles and vehicle collisions retain their existing strongest-contact/cooldown policy. The complete aggregate and network codecs preserve the mask, preventing a restore from manufacturing a fresh hit.

`VehicleState.CrashSeconds` latches a significant non-wheel impact (severity above 4 m/s) or stranded body support. With no wheel touching, Core suppresses steering, pedals, handbrake, Nitro thrust and player airborne rotation, while retaining physical rotation. Unsupported crash bounces preserve angular velocity; body contact retains the existing gentle angular decay. One supported wheel clears the latch and immediately restores normal controls. New lives clear it; retuning, prediction, replay, reconnect and authority migration retain the same movement continuation. Ordinary flight without a preceding crash keeps the existing player air-control rules.

Both production adapters retain inelastic body-impact rotation through `TerrainCollision`: contact lever arms, effective rotational mass and bounded tangential friction convert incoming momentum into pitch/roll. Friction is resolved after the normal impulse to avoid adding kinetic energy. Native practice keeps its solver-resolved pose and replaces only the terrain body-contact velocity response; network sweeps integrate the same response. Tire suspension and mixed vehicle/obstacle contact retain their existing solving paths. There are no contact pose or heading snaps.

During a crash, body contact dissipates sliding/separating motion and angular energy. Assistance starts after 0.5 seconds and ramps over 0.35 seconds toward a 2 rad/s minimum roll/flip rate. It continues existing rotation through inversion; if nearly stationary it chooses the shortest tipping axis, with a stable roof tie-break. It stops adding rotation once the chassis approaches tire-down, allowing gravity and suspension to catch it. Assistance changes angular velocity only, with no teleport, orientation assignment or upward launch impulse. High-energy motion may complete multiple flips; the bounded assistance prevents a roof/side stall after that energy dissipates. The existing host crash tuning still applies.

Both adapters report explicit terrain identity, local contact position and terrain wheel-support normal. The oval road and infield terrain use the `landing_terrain` group; tunnel structures, containment, props and other vehicles are excluded. Only faces meeting the accepted suspension support-normal cutoff carry terrain identity. Material identifiers alone do not grant forgiveness.

`check-crash-recovery.ps1 -GodotPath <exe> [-Visual] [-Case <prefix>]` exercises both adapters with 13/25/45-metre tire drops, fast/slow nose impacts, rear, side, roof, resting-roof, awkward and repeated-flip crashes. It records per-tick contacts, HP, crash duration, wheel support and motion; conflicting held controls test crash suppression. Rendered mode captures sequences for direct review. `check-landing.ps1` covers both authored oval landing zones, banks, partial-wheel landings, secondary tumbles, obstacles and vehicles. Fixture setup poses and impulses are verification inputs, not gameplay recovery mechanisms.
