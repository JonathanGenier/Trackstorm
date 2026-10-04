using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructable native wall. Only the active host advances its rigid-body solver.</summary>
internal sealed partial class ShieldWallBody : RigidBody3D
{
    internal ShieldVisual Visual { get; private set; } = null!;
    private Vector3 _size;
    private bool _tipping;
    private Vector3? _groundContact;
    internal ulong Identity { get; private set; }

    internal void Initialize(ShieldState state, bool host, bool animate, Transform3D? from = null, float fold = 0, float centerFold = 0, float horizontalFold = 0)
    {
        Identity = state.Id;
        Name = $"Shield_{state.Id}";
        Mass = state.WallMass;
        _size = VehicleBody.ToGodot(state.WallSize);
        CollisionLayer = 32;
        CollisionMask = 3 | 32;
        ContinuousCd = true;
        MaxContactsReported = 8;
        AxisLockAngularX = true;
        AxisLockAngularZ = true;
        LinearDamp = 2;
        AngularDamp = 1.5f;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.6f, Rough = false, Bounce = 0 };
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = VehicleBody.ToGodot(state.WallSize) } });
        Visual = new ShieldVisual();
        AddChild(Visual);
        Install(state, host);
        Visual.SetWorld(VehicleBody.ToGodot(state.WallSize), animate, from, fold, centerFold, horizontalFold);
    }

    internal VehiclePhysicsState Observe() => new(VehicleBody.ToCore(GlobalPosition),
        new(Quaternion.X, Quaternion.Y, Quaternion.Z, Quaternion.W), VehicleBody.ToCore(LinearVelocity), VehicleBody.ToCore(AngularVelocity));

    internal ShieldObservation ObserveWall() => new(Observe(), _groundContact is { } normal ? VehicleBody.ToCore(normal) : null);

    internal void Install(ShieldState state, bool host, bool reseed = false)
    {
        Visual.Observe(state, reseed);
        if (Freeze == host) { Freeze = !host; _groundContact = null; }
        if (_tipping != state.Tipping)
        {
            // Extra sliding resistance belongs to the standing wall. Preserve
            // the established fall/ground-contact response once it is knocked over.
            LinearDamp = state.Tipping ? 0.2f : 2;
            PhysicsMaterialOverride!.Friction = state.Tipping ? 0.08f : 0.6f;
        }
        _tipping = state.Tipping;
        // A collapsing wall must not become a ramp under the striking car or its
        // wheel rays. Keep native ground contact for authoritative side breakage.
        CollisionLayer = _tipping ? 0u : 32u;
        CollisionMask = _tipping ? 1u | 32u : 3u | 32u;
        AxisLockAngularX = !_tipping;
        AxisLockAngularZ = !_tipping;
        // The host has just observed this native body. Writing the same transform/velocity
        // back would discard pending vehicle impulses before the native solver consumes them.
        if (host && Observe() == new VehiclePhysicsState(state.Position, state.Orientation, state.LinearVelocity, state.AngularVelocity)) { return; }
        var transform = new Transform3D(new Basis(VehicleBody.ToGodot(state.Orientation)), VehicleBody.ToGodot(state.Position));
        if (GlobalTransform != transform) { _groundContact = null; }
        GlobalTransform = transform;
        LinearVelocity = VehicleBody.ToGodot(state.LinearVelocity);
        AngularVelocity = VehicleBody.ToGodot(state.AngularVelocity);
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (Freeze) { return; }
        _groundContact = null;
        for (int i = 0; i < state.GetContactCount(); i++)
        {
            if (state.GetContactColliderObject(i) is not StaticBody3D terrain || (terrain.CollisionLayer & 1) == 0) { continue; }
            var normal = state.GetContactLocalNormal(i).Normalized();
            if (normal.Y >= 0.55f) { _groundContact = normal; break; }
        }
        if (_tipping) { return; }
        var pose = state.Transform;
        if (ShieldGround.Seat(state.GetSpaceState(), pose.Origin, pose.Basis.Z, _size) is not { } seat ||
            Math.Abs(seat.Origin.Y - pose.Origin.Y) > 0.6f || state.LinearVelocity.Dot(seat.Basis.Y) > 2) { return; }
        // Terrain owns pitch/roll; native contact torque may still change yaw. This
        // follows the ground as the wall slides without letting gravity topple it.
        state.Transform = seat;
        var velocity = state.LinearVelocity;
        velocity.Y = -(seat.Basis.Y.X * velocity.X + seat.Basis.Y.Z * velocity.Z) / seat.Basis.Y.Y;
        state.LinearVelocity = velocity;
        state.AngularVelocity = new(0, state.AngularVelocity.Y, 0);
    }

}
