using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>Stateless host terrain/collision adapter; Core alone commits mine state and outcomes.</summary>
internal sealed partial class ProxyMinePhysics : StaticBody3D
{
    private readonly CylinderShape3D _shape = new() { Radius = ProxyMineState.Radius, Height = ProxyMineState.HalfHeight * 2 };

    public override void _Ready()
    {
        // Query-only body: replicas and vehicles never originate mine outcomes or push this fixture.
        CollisionLayer = 0;
        CollisionMask = 3;
        AddChild(new CollisionShape3D { Shape = _shape });
    }

    internal ProxyMineState? Place(ItemSlot slot, VehiclePhysicsState pose)
    {
        Vector3 behind = VehicleBody.ToGodot(pose.Position + System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ * 4.5f, pose.Orientation));
        using var ray = PhysicsRayQueryParameters3D.Create(behind + Vector3.Up * 2, behind - Vector3.Up * 6, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not StaticBody3D) { return null; }
        Vector3 centre = hit["position"].AsVector3();
        Vector3 normal = hit["normal"].AsVector3().Normalized();
        if (normal.Y < 0.55f) { return null; }
        var points = new List<Vector3> { centre };
        Vector3 averageNormal = normal;
        Vector3 tangent = normal.Cross(Vector3.Forward).Normalized();
        Vector3 bitangent = normal.Cross(tangent);
        for (int i = 0; i < 8; i++)
        {
            Vector3 point = centre + ProxyMineState.Radius * (tangent * Mathf.Cos(i * Mathf.Tau / 8) + bitangent * Mathf.Sin(i * Mathf.Tau / 8));
            ray.From = point + Vector3.Up;
            ray.To = point - Vector3.Up;
            var sample = GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (sample.Count == 0 || sample["collider"].AsGodotObject() is not StaticBody3D || sample["normal"].AsVector3().Y < 0.55f) { return null; }
            points.Add(sample["position"].AsVector3());
            averageNormal += sample["normal"].AsVector3();
        }
        normal = averageNormal.Normalized();
        float high = points.Max(point => (point - centre).Dot(normal));
        float low = points.Min(point => (point - centre).Dot(normal));
        // A small irregular footprint is supported; cliffs and unsupported discontinuities retain the item.
        if (high - low > 0.22f) { return null; }
        centre += normal * (high + ProxyMineState.HalfHeight + 0.008f);
        var transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, normal)), centre);
        using var query = new PhysicsShapeQueryParameters3D { Shape = _shape, Transform = transform, CollisionMask = 3, Margin = 0.001f };
        // Triangle crests between footprint samples can protrude into a rigid base. Find the
        // first clear seated height within twelve centimetres; this is installation, never attraction.
        int clearance = 0;
        while (GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count > 0)
        {
            if (++clearance > 12) { return null; }
            centre += normal * 0.01f;
            query.Transform = new Transform3D(transform.Basis, centre);
        }
        return new(slot.Token, slot.Vehicle, VehicleBody.ToCore(centre), System.Numerics.Vector3.Zero, VehicleBody.ToCore(normal), 30);
    }

    internal ProxyMineMotion Move(ProxyMineState previous, ProxyMineState candidate)
    {
        var transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, VehicleBody.ToGodot(previous.Normal))), VehicleBody.ToGodot(previous.Position));
        using var overlap = new PhysicsShapeQueryParameters3D { Shape = _shape, Transform = transform, CollisionMask = 2, Margin = 0.015f };
        var touching = GetWorld3D().DirectSpaceState.IntersectShape(overlap, 8)
            .Select(hit => hit["collider"].AsGodotObject()).OfType<NetworkVehicleBody>().OrderBy(body => body.VehicleId).FirstOrDefault();
        if (touching is not null) { return new(candidate with { Position = previous.Position }, touching.VehicleId); }
        if (previous.SeatingTicks > 0) { return new(candidate); }
        Vector3 velocity = VehicleBody.ToGodot(candidate.Velocity);
        Vector3 remaining = VehicleBody.ToGodot(candidate.Position - previous.Position);
        Vector3 support = VehicleBody.ToGodot(previous.Normal);
        using var parameters = new PhysicsTestMotionParameters3D { Margin = 0.005f, MaxCollisions = 8, RecoveryAsCollision = true };
        using var result = new PhysicsTestMotionResult3D();
        ulong contact = 0;
        for (int slide = 0; slide < 4; slide++)
        {
            parameters.From = transform;
            parameters.Motion = remaining;
            bool collided = PhysicsServer3D.BodyTestMotion(GetRid(), parameters, result);
            transform.Origin += collided ? result.GetTravel() : remaining;
            if (!collided) { break; }
            remaining = result.GetRemainder();
            for (int i = 0; i < result.GetCollisionCount(); i++)
            {
                if (result.GetCollider(i) is NetworkVehicleBody vehicle && (contact == 0 || vehicle.VehicleId < contact)) { contact = vehicle.VehicleId; }
                Vector3 normal = result.GetCollisionNormal(i).Normalized();
                if (normal.Y >= 0.55f) { support = normal; }
                if (velocity.Dot(normal) < 0) { velocity = velocity.Slide(normal); }
                if (remaining.Dot(normal) < 0) { remaining = remaining.Slide(normal); }
            }
            if (contact != 0 || remaining.LengthSquared() < 0.0000001f) { break; }
        }
        // Gradual orientation follows support without changing force-driven position or velocity.
        var normalResult = VehicleBody.ToGodot(previous.Normal).Slerp(support, 0.2f).Normalized();
        return new(candidate with { Position = VehicleBody.ToCore(transform.Origin), Velocity = VehicleBody.ToCore(velocity), Normal = VehicleBody.ToCore(normalResult) }, contact);
    }
}
