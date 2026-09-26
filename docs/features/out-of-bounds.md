# Arena boundary and out-of-bounds damage

The [oval map](oval-map.md) has a finite visible concrete barrier and inward-curving catch fence. Its offline baker stores all 916 concrete outer-edge points and a below-map height of -30 m. Client `ActiveMap` extracts the immutable `ArenaBoundary` contract before creating practice or host authority. Old Map has no new boundary contract and retains its existing behavior.

Core evaluates the host-observed vehicle center with an inclusive X/Z polygon test. An airborne center remains in bounds while inside the polygon, regardless of height; outside the concrete footprint or below the authored floor begins exposure. This uses the actual authored perimeter rather than distance from origin. The one-millimetre edge tolerance avoids float noise. It is sampled at the existing fixed tick, not a swept test for arbitrary teleports.

Exposure is latched for the current living vehicle life: returning horizontally inside does not cancel it, and falling farther or below the world cannot evade it. At 60 Hz, `VehicleAuthority` applies the configured HP/s divided by 60 through `VehicleHealth.ApplyDamage`, with source `out-of-bounds`, instigator zero and context `arena-exterior`. The default is **100 HP/s**, approximately ten seconds from 1000 HP. Existing lethal damage, inventory cleanup, inactive physics, respawn deadlines and spawn selection remain authoritative. Death, respawn and explicit practice reset clear exposure. OOB does not itself credit damage points or kills; existing combat damage remains independently attributed. Existing repair/tuning rules still apply.

`VehicleSnapshot.OutOfBounds` carries this state in complete aggregate version 4 and world protocol version 11. Prediction retains the host flag and HP; it never classifies or damages. Entry and lifecycle transitions publish reliably, with periodic full snapshots recovering current state. Resume/migration checkpoints carry the same nested state; reconstruction uses the active map's boundary. Retuning and finished-match snapshot reconstruction preserve it.

Developer Options > Configs > **Arena boundary** exposes `vehicle.oob.damage`, accepting finite 0–10000 HP/s. Zero deliberately disables HP loss without disabling classification. The existing host-only transaction, file persistence, configuration revision and checkpoint paths own tuning. Configuration protocol version 24 carries 171 values. Exact build compatibility rejects mismatched game versions.

The affected player's HUD displays **OUT OF BOUNDS** and **ARENA HAZARD • LOSING HEALTH** from confirmed state, retaining ordinary HP gauges, body damage feedback and existing damage/death audio. The warning clears on death and respawn, and the entire HUD hides outside gameplay. At zero developer damage the hazard warning remains; the health gauge stays unchanged.

## Verification

`check-boundary.ps1 -GodotPath <exe> [-Visual]` exercises all section seams, finite upper clearance, concrete and airborne fence impacts through both production adapters, and actual ballistic trajectories from inside that clear the fence. It measures default damage over native ticks and follows below-map exposure through death/respawn. Rendered evidence is under `.godot/boundary-checks`.

`check-death-respawn.ps1 -GodotPath <exe> -OutOfBounds [-Impaired]` exercises two simultaneous remote OOB players across eight local native UDP peers, repeated deaths/respawns, replicated warnings, inventory and native participation cleanup. It uses a 60-second fixture transport timeout because eight isolated map worlds are constructed synchronously before pumping. This is not separate-device or authenticated EOS evidence.

Core tests cover translated/concave boundaries, edge inclusion, height/floor behavior, independent exposure, zero tuning, host permissions, default damage, attribution, prediction, serialization and checkpoint continuation. HUD runtime checks cover confirmed warning and death clearing. Historical commands, results and unverified areas belong in the Story verification report.

Exterior props, scoring, reactions and other exterior gameplay are intentionally absent.
