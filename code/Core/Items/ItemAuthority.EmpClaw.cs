using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    private EmpClawFlight CaptureEmpTarget(Simulation.Simulation world, ItemSlot slot, WeaponAimSolution aim,
        IReadOnlyList<VehicleStepRequest> requests, Func<ulong, Vector3, Vector3, WeaponRayHit?>? raycast, ulong deadline)
    {
        var intent = _aimIntents.GetValueOrDefault(slot.Vehicle);
        var victim = world.State.Vehicles.FirstOrDefault(v => v.VehicleId == intent?.Target && v.LifeId == intent.TargetLife && v.CanInteract);
        var request = requests.FirstOrDefault(r => r.VehicleId == victim?.VehicleId && !r.Reset.HasValue);
        bool eligible = victim is not null && request is not null && victim.VehicleId != slot.Vehicle &&
            EmpVisible(slot.Vehicle, aim.Origin, aim.Direction, victim.VehicleId, request.Observation.Physics.Position,
                300, Configuration.EmpClaw.AcquisitionDegrees, raycast);
        return new(slot.Life, eligible ? victim!.VehicleId : 0, eligible ? victim!.LifeId : 0, deadline);
    }

    private MissileState GuideEmp(Simulation.Simulation world, MissileState missile, IReadOnlyList<VehicleStepRequest> requests,
        Func<ulong, Vector3, Vector3, WeaponRayHit?>? raycast, Func<Vector3, Vector3, MissileTerrainSample?>? terrain)
    {
        var configuration = Configuration.EmpClaw;
        var flight = missile.EmpClaw!;
        Vector3 direction = Vector3.Normalize(missile.Velocity);
        if (flight.Target == 0)
        {
            // First detection wins. Simultaneous candidates use distance, then stable vehicle ID.
            var candidate = requests.Where(r => r.VehicleId != missile.Owner && !r.Reset.HasValue &&
                    world.GetVehicle(r.VehicleId).CanInteract)
                .OrderBy(r => Vector3.DistanceSquared(missile.Position, r.Observation.Physics.Position)).ThenBy(r => r.VehicleId)
                .FirstOrDefault(r => EmpVisible(missile.Owner, missile.Position, direction, r.VehicleId,
                    r.Observation.Physics.Position, configuration.AcquisitionRange, configuration.AcquisitionDegrees, raycast));
            if (candidate is not null)
            { flight = flight with { Target = candidate.VehicleId, TargetLife = world.GetVehicle(candidate.VehicleId).LifeId }; }
        }
        var victim = world.State.Vehicles.FirstOrDefault(v => v.VehicleId == flight.Target && v.LifeId == flight.TargetLife && v.CanInteract);
        var target = requests.FirstOrDefault(r => r.VehicleId == victim?.VehicleId && !r.Reset.HasValue);
        Vector3 desired = direction;
        if (target is not null)
        {
            Vector3 offset = target.Observation.Physics.Position - missile.Position;
            if (offset.LengthSquared() > .0001f) { desired = Vector3.Normalize(offset); }
        }
        float speed = Math.Clamp(missile.Velocity.Length() - configuration.Deceleration / 60, configuration.CruiseSpeed, configuration.LaunchSpeed);
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(direction, desired), -1, 1));
        float step = Math.Min(angle * (1 - MathF.Exp(-configuration.Response / 60)), configuration.TurnRate * MathF.PI / (180 * 60));
        Vector3 axis = Vector3.Cross(direction, desired);
        if (angle > .00001f && step > 0)
        {
            if (axis.LengthSquared() < .000001f)
            { axis = Vector3.Cross(direction, Math.Abs(direction.Y) < .9f ? Vector3.UnitY : Vector3.UnitX); }
            direction = Vector3.Transform(direction, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), step));
        }
        missile = missile with { Velocity = direction * speed, EmpClaw = flight };
        // Reuse the proven support/cliff/bank probes, with EMP-specific tuning. Standard Missile is unchanged.
        return missile with { Velocity = MissileFlight.Correct(missile, configuration.Terrain, terrain) };
    }

    private static bool EmpVisible(ulong owner, Vector3 origin, Vector3 direction, ulong target, Vector3 position,
        float range, float degrees, Func<ulong, Vector3, Vector3, WeaponRayHit?>? raycast)
    {
        Vector3 offset = position - origin;
        float distance = offset.Length();
        if (raycast is null || distance < .001f || distance > range ||
            Vector3.Dot(offset / distance, direction) < MathF.Cos(degrees * MathF.PI / 180)) { return false; }
        var hit = raycast(owner, origin, position);
        return hit is null || (hit.Vehicle == target && float.IsFinite(hit.Fraction) && hit.Fraction is >= 0 and <= 1);
    }

    private static EmpClawContact? EmpContact(Simulation.Simulation world, MissileState missile, Vector3 position,
        WeaponRayHit? hit, IReadOnlyList<VehicleStepRequest> requests)
    {
        if (hit is null || hit.Vehicle == 0 || hit.Vehicle == missile.Owner) { return null; }
        var victim = world.State.Vehicles.FirstOrDefault(v => v.VehicleId == hit.Vehicle && v.CanInteract);
        var request = requests.FirstOrDefault(r => r.VehicleId == hit.Vehicle && !r.Reset.HasValue);
        if (victim is null || request is null) { return null; }
        var pose = request.Observation.Physics;
        Vector3 normal = WeaponAim.IsDirection(hit.Normal) ? hit.Normal : -Vector3.Normalize(missile.Velocity);
        Quaternion inverse = Quaternion.Inverse(pose.Orientation);
        return new(victim.VehicleId, victim.LifeId, Vector3.Transform(position - pose.Position, inverse), Vector3.Transform(normal, inverse));
    }
}
