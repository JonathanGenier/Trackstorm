using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructable native wall. Only the active host advances its rigid-body solver.</summary>
internal sealed partial class TombstoneWallBody : RigidBody3D
{
    private Node3D _wings = null!;
    private float _expansion = 1;
    private Vector3 _size;
    private bool _tipping;
    private Vector3? _groundContact;
    private readonly List<(MeshInstance3D Mesh, int Side, float Center, float Width)> _panels = [];
    internal ulong Identity { get; private set; }

    internal void Initialize(TombstoneState state, bool host, bool animate)
    {
        Identity = state.Id;
        Name = $"Tombstone_{state.Id}";
        Mass = state.WallMass;
        _size = VehicleBody.ToGodot(state.WallSize);
        CollisionLayer = 32;
        CollisionMask = 3 | 32;
        ContinuousCd = true;
        MaxContactsReported = 8;
        AxisLockAngularX = true;
        AxisLockAngularZ = true;
        LinearDamp = 0.2f;
        AngularDamp = 1.5f;
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.08f, Rough = false, Bounce = 0 };
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = VehicleBody.ToGodot(state.WallSize) } });
        _wings = new Node3D();
        AddChild(_wings);
        float centerWidth = Math.Min(TombstoneGeometry.Size.X, state.WallSize.X);
        float wingWidth = (state.WallSize.X - centerWidth) / 2;
        Plate(centerWidth, 0, state, new(0.26f, 0.3f, 0.34f));
        foreach (int side in new[] { -1, 1 })
        { _panels.Add((Plate(wingWidth, side * (centerWidth + wingWidth) / 2, state, new(0.38f, 0.42f, 0.46f)), side, centerWidth, wingWidth)); }
        _expansion = animate ? 0 : 1;
        Install(state, host);
    }

    private MeshInstance3D Plate(float width, float x, TombstoneState state, Color color)
    {
        var plate = new MeshInstance3D { Position = new(x, 0, 0),
            Mesh = new BoxMesh { Size = new(width - 0.015f, state.WallSize.Y, state.WallSize.Z) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Metallic = 0.65f, Roughness = 0.55f } };
        _wings.AddChild(plate);
        return plate;
    }

    internal VehiclePhysicsState Observe() => new(VehicleBody.ToCore(GlobalPosition),
        new(Quaternion.X, Quaternion.Y, Quaternion.Z, Quaternion.W), VehicleBody.ToCore(LinearVelocity), VehicleBody.ToCore(AngularVelocity));

    internal TombstoneObservation ObserveWall() => new(Observe(), _groundContact is { } normal ? VehicleBody.ToCore(normal) : null);

    internal void Install(TombstoneState state, bool host)
    {
        if (Freeze == host) { Freeze = !host; _groundContact = null; }
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
        if (TombstoneGround.Seat(state.GetSpaceState(), pose.Origin, pose.Basis.Z, _size) is not { } seat ||
            Math.Abs(seat.Origin.Y - pose.Origin.Y) > 0.6f || state.LinearVelocity.Dot(seat.Basis.Y) > 2) { return; }
        // Terrain owns pitch/roll; native contact torque may still change yaw. This
        // follows the ground as the wall slides without letting gravity topple it.
        state.Transform = seat;
        var velocity = state.LinearVelocity;
        velocity.Y = -(seat.Basis.Y.X * velocity.X + seat.Basis.Y.Z * velocity.Z) / seat.Basis.Y.Y;
        state.LinearVelocity = velocity;
        state.AngularVelocity = new(0, state.AngularVelocity.Y, 0);
    }

    public override void _Process(double delta)
    {
        _expansion = Math.Min(1, _expansion + (float)delta * 5);
        _wings.Scale = new(1, 0.6f + 0.4f * _expansion, 1);
        foreach (var panel in _panels)
        {
            panel.Mesh.Scale = new(Math.Max(0.001f, _expansion), 1, 1);
            panel.Mesh.Position = new(panel.Side * (panel.Center + panel.Width * _expansion) / 2, 0, 0);
        }
    }
}
