using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>Host placement/observation adapter and accepted-state reconstruction on every peer.</summary>
internal sealed partial class TombstoneWorld : Node3D
{
    private readonly Dictionary<ulong, TombstoneWallBody> _bodies = new();
    internal IReadOnlyDictionary<ulong, TombstoneWallBody> Bodies => _bodies;

    internal void Apply(ItemPublication publication, bool host, bool reseed = false)
    {
        var walls = publication.Tombstones.Where(s => !s.Attached).ToDictionary(s => s.Id);
        foreach (ulong id in _bodies.Keys.Except(walls.Keys).ToArray())
        {
            var body = _bodies[id];
            body.CollisionLayer = 0; body.CollisionMask = 0; body.Freeze = true;
            body.QueueFree(); _bodies.Remove(id);
        }
        foreach (var wall in walls.Values)
        {
            if (!_bodies.TryGetValue(wall.Id, out var body))
            {
                body = new(); AddChild(body);
                body.Initialize(wall, host, !reseed && publication.Events.Any(e => e.Item == HeldItem.Tombstone && e.Owner == wall.Owner && e.Position == wall.Position));
                _bodies.Add(wall.Id, body);
            }
            else { body.Install(wall, host); }
        }
    }

    internal VehiclePhysicsState? Observe(TombstoneState state) => _bodies.TryGetValue(state.Id, out var body) ? body.Observe() : null;

    internal VehiclePhysicsState? Place(VehiclePhysicsState vehicle, ItemConfiguration tuning, PhysicsBody3D owner)
    {
        // Vehicle sweeps observe the next pose without moving their native query proxy yet.
        // At speed, the old proxy overlaps the rear release point. Query the current chassis
        // instead, retaining self-clearance and restoring the proxy even on rejected placement.
        Transform3D previous = owner.GlobalTransform;
        try
        {
            owner.GlobalTransform = new(new Basis(VehicleBody.ToGodot(vehicle.Orientation)), VehicleBody.ToGodot(vehicle.Position));
            owner.ForceUpdateTransform();
            return FindPlacement(vehicle, tuning);
        }
        finally { owner.GlobalTransform = previous; owner.ForceUpdateTransform(); }
    }

    private VehiclePhysicsState? FindPlacement(VehiclePhysicsState vehicle, ItemConfiguration tuning)
    {
        var back = VehicleBody.ToGodot(System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, vehicle.Orientation));
        back.Y = 0;
        if (back.LengthSquared() < 0.01f) { return null; }
        back = back.Normalized();
        var rear = VehicleBody.ToGodot(vehicle.Position) + back * (VehicleDimensions.Length / 2 + tuning.TombstoneClearance + tuning.TombstoneDepth / 2);
        using var ray = PhysicsRayQueryParameters3D.Create(rear + Vector3.Up * 3, rear - Vector3.Up * 8, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not StaticBody3D) { return null; }
        Vector3 normal = hit["normal"].AsVector3().Normalized();
        if (normal.Y < 0.55f) { return null; }
        Vector3 right = Vector3.Up.Cross(back).Normalized();
        var basis = new Basis(right, Vector3.Up, back);
        // Stand upright above the highest corner of the local support plane. Remaining
        // terrain irregularities still go through the full expanded-box clearance query.
        float rise = (tuning.TombstoneWidth * Math.Abs(normal.Dot(right)) + tuning.TombstoneDepth * Math.Abs(normal.Dot(back))) / (2 * normal.Y);
        Vector3 center = hit["position"].AsVector3() + Vector3.Up * (rise + tuning.TombstoneHeight / 2 + 0.03f);
        using var shape = new BoxShape3D { Size = new(tuning.TombstoneWidth, tuning.TombstoneHeight, tuning.TombstoneDepth) };
        using var query = new PhysicsShapeQueryParameters3D { Shape = shape, CollisionMask = 3 | 32, Margin = 0.01f };
        for (int attempt = 0; attempt <= 10; attempt++)
        {
            query.Transform = new(basis, center + Vector3.Up * (attempt * 0.1f));
            if (GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count != 0) { continue; }
            var rotation = basis.GetRotationQuaternion();
            return new(VehicleBody.ToCore(query.Transform.Origin), new(rotation.X, rotation.Y, rotation.Z, rotation.W),
                new(vehicle.LinearVelocity.X, 0, vehicle.LinearVelocity.Z), System.Numerics.Vector3.Zero);
        }
        // Constrained rear space retains the selected shield and its capability for a later press.
        return null;
    }
}
