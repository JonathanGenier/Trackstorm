using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Runs native motion queries with local chassis bounds, independently of the collision proxy's world AABB.</summary>
internal sealed class VehicleMotionQuery : IDisposable
{
    private readonly PhysicsBody3D _owner;
    private readonly CollisionShape3D[] _shapes;
    private readonly bool[] _disabled;
    private Rid _body;

    internal VehicleMotionQuery(PhysicsBody3D owner, params CollisionShape3D[] shapes)
    {
        _owner = owner;
        _shapes = shapes;
        _disabled = new bool[shapes.Length];
        _body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(_body, PhysicsServer3D.BodyMode.Static);
        PhysicsServer3D.BodySetCollisionLayer(_body, 0);
        PhysicsServer3D.BodySetCollisionMask(_body, 0);
        for (int i = 0; i < shapes.Length; i++)
        {
            _disabled[i] = shapes[i].Disabled;
            PhysicsServer3D.BodyAddShape(_body, shapes[i].Shape.GetRid(), shapes[i].Transform, _disabled[i]);
        }
        PhysicsServer3D.BodyAddCollisionException(_body, owner.GetRid());
        PhysicsServer3D.BodySetSpace(_body, owner.GetWorld3D().Space);
    }

    internal bool Test(PhysicsTestMotionParameters3D parameters, PhysicsTestMotionResult3D result)
    {
        // Godot Physics reconstructs local bounds from a body's cached world AABB.
        // Undoing a rotation on an AABB expands it; rotating it again for From
        // compounds that expansion and visits unrelated concave terrain triangles.
        // This query body stays at identity. From still carries the exact requested
        // pose, and the native recovery, sweep, margin and contact solver are unchanged.
        for (int i = 0; i < _shapes.Length; i++)
        {
            if (_disabled[i] == _shapes[i].Disabled) { continue; }
            _disabled[i] = _shapes[i].Disabled;
            PhysicsServer3D.BodySetShapeDisabled(_body, i, _disabled[i]);
        }
        PhysicsServer3D.BodySetCollisionMask(_body, _owner.CollisionMask);
        try { return PhysicsServer3D.BodyTestMotion(_body, parameters, result); }
        // It has no collision layer, and no mask outside this synchronous query:
        // neither other queries nor native rigid-body steps see a phantom chassis.
        finally { PhysicsServer3D.BodySetCollisionMask(_body, 0); }
    }

    public void Dispose()
    {
        if (!_body.IsValid) { return; }
        PhysicsServer3D.FreeRid(_body);
        _body = default;
    }
}
