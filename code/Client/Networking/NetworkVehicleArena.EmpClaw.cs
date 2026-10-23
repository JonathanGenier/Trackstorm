using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Networking;

internal sealed partial class NetworkVehicleArena
{
    private VehicleEmpSurface? _empSurface;

    private bool ContactEmpNose(ulong vehicle, N.Vector3 from, N.Vector3 to, float radius)
    {
        using var sphere = new SphereShape3D { Radius = radius };
        var excluded = new Godot.Collections.Array<Rid>();
        if (_bodies.TryGetValue(vehicle, out var body)) { excluded.Add(body.GetRid()); }
        using var query = new PhysicsShapeQueryParameters3D
        {
            Shape = sphere, Transform = new(Basis.Identity, VehicleBody.ToGodot(from)),
            Motion = VehicleBody.ToGodot(to - from), CollisionMask = 1 | 2 | 16 | 32, Exclude = excluded,
            Margin = 0,
        };
        var space = GetWorld3D().DirectSpaceState;
        if (space.IntersectShape(query, 1).Count > 0) { return true; }
        var sweep = space.CastMotion(query);
        if (sweep.Length == 2 && sweep[0] < 1) { return true; }
        query.Transform = new(Basis.Identity, VehicleBody.ToGodot(to));
        query.Motion = Vector3.Zero;
        return space.IntersectShape(query, 1).Count > 0;
    }

    private WeaponRayHit? RaycastEmpClaw(ulong owner, N.Vector3 origin, N.Vector3 end, IReadOnlyList<VehicleStepRequest> observations)
    {
        Vector3 start = VehicleBody.ToGodot(origin), finish = VehicleBody.ToGodot(end);
        float length = start.DistanceTo(finish);
        if (length <= .000001f) { return null; }
        // Preserve scenery/prop obstruction, but do not accept the movement hull's empty air.
        using var ray = PhysicsRayQueryParameters3D.Create(start, finish, 1 | 16);
        ray.HitFromInside = true;
        var world = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        WeaponRayHit? closest = world.Count == 0 ? null : new(Math.Clamp(start.DistanceTo(world["position"].AsVector3()) / length, 0, 1), 0)
        { Normal = VehicleBody.ToCore(world["normal"].AsVector3()) };
        _empSurface ??= new();
        foreach (var request in observations)
        {
            if (request.VehicleId == owner || !_bodies.TryGetValue(request.VehicleId, out var body) || body.CollisionLayer == 0) { continue; }
            // Use the solved tick consumed by Core's world-to-local conversion. The native
            // collision proxy still holds the preceding boundary while observations are staged.
            var pose = request.Observation.Physics;
            Transform3D frame = new(new Basis(VehicleBody.ToGodot(pose.Orientation)), VehicleBody.ToGodot(pose.Position));
            Transform3D inverse = frame.AffineInverse();
            var hit = _empSurface.Intersect(inverse * start, inverse * finish);
            if (hit.Count == 0) { continue; }
            float fraction = Math.Clamp(start.DistanceTo(frame * hit["position"].AsVector3()) / length, 0, 1);
            if (closest is not null && closest.Fraction <= fraction) { continue; }
            Vector3 normal = (frame.Basis * hit["normal"].AsVector3()).Normalized();
            if (normal.Dot(finish - start) > 0) { normal = -normal; }
            closest = new(fraction, body.VehicleId) { Normal = VehicleBody.ToCore(normal) };
        }
        return closest;
    }
}
