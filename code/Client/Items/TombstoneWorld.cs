using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>Host placement/observation adapter and accepted-state reconstruction on every peer.</summary>
internal sealed partial class TombstoneWorld : Node3D
{
    private readonly Dictionary<ulong, TombstoneWallBody> _bodies = new();
    private readonly Dictionary<ulong, Transform3D> _mounted = new();
    private readonly List<TombstoneVisual> _debris = [];
    internal IReadOnlyDictionary<ulong, TombstoneWallBody> Bodies => _bodies;
    internal Func<ulong, (Transform3D Pose, float Fold, float CenterFold, float HorizontalFold)?>? MountedPose { get; set; }

    internal void Apply(ItemPublication publication, bool host, bool reseed = false)
    {
        var walls = publication.Tombstones.Where(s => !s.Attached).ToDictionary(s => s.Id);
        _debris.RemoveAll(v => !GodotObject.IsInstanceValid(v) || v.IsQueuedForDeletion());
        if (reseed)
        {
            foreach (var fragment in _debris) { fragment.QueueFree(); }
            _debris.Clear();
            _mounted.Clear();
        }
        foreach (ulong id in _bodies.Keys.Except(walls.Keys).ToArray())
        {
            var body = _bodies[id];
            body.CollisionLayer = 0; body.CollisionMask = 0; body.Freeze = true;
            if (!reseed && _debris.Count < 32)
            {
                body.Visual.Reparent(this);
                body.Visual.BreakApart();
                _debris.Add(body.Visual);
            }
            body.QueueFree(); _bodies.Remove(id);
        }
        foreach (var wall in walls.Values)
        {
            if (!_bodies.TryGetValue(wall.Id, out var body))
            {
                body = new(); AddChild(body);
                bool animate = !reseed && _mounted.ContainsKey(wall.Id);
                var release = animate ? MountedPose?.Invoke(wall.Owner) ?? (_mounted[wall.Id], 0f, 0f, 0f) : ((Transform3D Pose, float Fold, float CenterFold, float HorizontalFold)?)null;
                body.Initialize(wall, host, animate, release?.Pose, release?.Fold ?? 0, release?.CenterFold ?? 0, release?.HorizontalFold ?? 0);
                _bodies.Add(wall.Id, body);
            }
            else
            {
                body.Install(wall, host, reseed);
                if (reseed) { body.Visual.SetWorld(VehicleBody.ToGodot(wall.WallSize), false); }
            }
        }
        _mounted.Clear();
        foreach (var shield in publication.Tombstones.Where(s => s.Stage == TombstoneStage.RearShield))
        {
            var vehicle = publication.World.Vehicles.FirstOrDefault(v => v.State.VehicleId == shield.Owner)?.State;
            if (vehicle is null || vehicle.LifeId != shield.Life) { continue; }
            var pose = vehicle.Movement.Physics;
            var transform = new Transform3D(new Basis(VehicleBody.ToGodot(pose.Orientation)), VehicleBody.ToGodot(pose.Position));
            _mounted[shield.Id] = transform * new Transform3D(Basis.Identity, TombstoneVisual.MountedCenter);
        }
    }

    internal TombstoneObservation? Observe(TombstoneState state) => _bodies.TryGetValue(state.Id, out var body) ? body.ObserveWall() : null;

    internal VehiclePhysicsState? Place(VehiclePhysicsState vehicle, ItemConfiguration tuning, PhysicsBody3D owner)
    {
        // Vehicle sweeps observe the next pose without moving their native query proxy yet.
        // At speed, the old proxy overlaps the rear release point. Query the current chassis
        // instead, retaining self-clearance and restoring the proxy even on rejected placement.
        Transform3D previous = owner.GlobalTransform;
        try
        {
            // The selected armor is consumed by this transition. Its taller side/rear
            // panels must not obstruct their own replacement on sloping terrain.
            // Retain the chassis and every other vehicle's armor in the query.
            if (owner is Networking.NetworkVehicleBody car) { car.SetShieldQueryEnabled(false); }
            owner.GlobalTransform = new(new Basis(VehicleBody.ToGodot(vehicle.Orientation)), VehicleBody.ToGodot(vehicle.Position));
            owner.ForceUpdateTransform();
            return FindPlacement(vehicle, tuning);
        }
        finally
        {
            owner.GlobalTransform = previous; owner.ForceUpdateTransform();
            if (owner is Networking.NetworkVehicleBody car) { car.SetShieldQueryEnabled(true); }
        }
    }

    private VehiclePhysicsState? FindPlacement(VehiclePhysicsState vehicle, ItemConfiguration tuning)
    {
        var back = VehicleBody.ToGodot(System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, vehicle.Orientation));
        back.Y = 0;
        if (back.LengthSquared() < 0.01f) { return null; }
        back = back.Normalized();
        var rear = VehicleBody.ToGodot(vehicle.Position) + back * (VehicleDimensions.Length / 2 + tuning.TombstoneClearance + tuning.TombstoneDepth / 2);
        using var shape = new BoxShape3D { Size = new(tuning.TombstoneWidth, tuning.TombstoneHeight, tuning.TombstoneDepth) };
        if (TombstoneGround.Seat(GetWorld3D().DirectSpaceState, rear, back, shape.Size) is not { } seat) { return null; }
        using var query = new PhysicsShapeQueryParameters3D { Shape = shape, CollisionMask = 3 | 32, Margin = 0.01f };
        for (int attempt = 0; attempt <= 10; attempt++)
        {
            query.Transform = new(seat.Basis, seat.Origin + Vector3.Up * (attempt * 0.1f));
            if (GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count != 0) { continue; }
            var rotation = seat.Basis.GetRotationQuaternion();
            return new(VehicleBody.ToCore(query.Transform.Origin), new(rotation.X, rotation.Y, rotation.Z, rotation.W),
                new(vehicle.LinearVelocity.X, 0, vehicle.LinearVelocity.Z), System.Numerics.Vector3.Zero);
        }
        // Constrained rear space retains the selected shield and its capability for a later press.
        return null;
    }
}
