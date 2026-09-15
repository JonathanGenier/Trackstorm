using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Networking;

/// <summary>Synchronous Godot collision observation seam usable by both host steps and prediction replay.</summary>
internal sealed partial class NetworkVehicleBody : StaticBody3D
{
    private readonly VehicleConfiguration _configuration = new();
    private readonly List<Node3D> _wheels = new();
    private readonly Node3D _visual = new();
    private VehiclePhysicsState _previous;
    private VehiclePhysicsState _current;
    private bool _initialized;
    private ShaderMaterial _damageMaterial = null!;
    private float _previousHP = 100;
    private float _flash;
    /// <summary>Host-assigned identity used only to attribute contact observations.</summary>
    internal ulong VehicleId { get; init; }
    /// <summary>Only authoritative forward steps may apply native prop impulses.</summary>
    internal bool PushProps { get; init; }
    /// <summary>Presentation-only correction memory.</summary>
    internal CorrectionSmoothing Smoothing { get; } = new();
    /// <summary>Displayed position for the local chase camera.</summary>
    internal Vector3 VisualPosition => _visual.GlobalPosition;

    /// <inheritdoc/>
    public override void _Ready()
    {
        CollisionLayer = 2;
        CollisionMask = 3;
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2, 1, 3.6f) } });
        AddChild(_visual);
        _visual.TopLevel = true;
        Color paint = Color.FromHsv((VehicleId * 0.13f) % 1, 0.7f, 0.9f);
        _damageMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/items/materials/DamageFlash.gdshader") };
        _damageMaterial.SetShaderParameter("paint", paint);
        var chassis = VehicleBody.Box(new Vector3(2, 0.7f, 3.6f), Vector3.Zero, paint);
        chassis.MaterialOverride = _damageMaterial;
        _visual.AddChild(chassis);
        _visual.AddChild(VehicleBody.Box(new Vector3(1.5f, 0.55f, 1.65f), new Vector3(0, 0.55f, 0.15f), new Color("172435")));
        _visual.AddChild(VehicleBody.Box(new Vector3(1.5f, 0.12f, 0.1f), new Vector3(0, 0.12f, -1.82f), new Color("ecfbff")));
        foreach (float z in new[] { -1.15f, 1.15f })
        {
            foreach (float x in new[] { -1.02f, 1.02f })
            {
                var wheel = VehicleBody.Box(new Vector3(0.3f, 0.75f, 0.75f), new Vector3(x, -0.1f, z), new Color("10131a"));
                _visual.AddChild(wheel);
                _wheels.Add(wheel);
            }
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _flash = Math.Max(0, _flash - ((float)delta * 5));
        _damageMaterial.SetShaderParameter("flash", _flash);
    }

    /// <summary>Drives a shader parameter only from accepted health outcomes.</summary>
    /// <param name="damage">Host-owned HP state.</param>
    internal void PresentDamage(VehicleDamageState damage)
    {
        if (damage.CurrentHP < _previousHP)
        {
            _flash = 1;
        }

        _previousHP = damage.CurrentHP;
    }

    /// <summary>Resolves the preceding Core command through bounded native sweep/slide queries.</summary>
    /// <param name="snapshot">Complete pre-solver command boundary.</param>
    /// <returns>Solved numeric physics/support/contact observations for the next Core step.</returns>
    internal VehicleObservation Observe(VehicleSnapshot snapshot)
    {
        VehiclePhysicsState state = snapshot.Movement.Physics;
        Vector3 velocity = VehicleBody.ToGodot(state.LinearVelocity);
        Vector3 angular = VehicleBody.ToGodot(state.AngularVelocity);
        foreach (var effect in snapshot.Effects)
        {
            velocity += VehicleBody.ToGodot(effect.Effect.Impulse) / _configuration.Mass;
            angular += VehicleBody.ToGodot(Numerics.Vector3.Cross(effect.Effect.Offset, effect.Effect.Impulse)) / (_configuration.Mass * _configuration.Wheelbase * _configuration.Wheelbase / 3);
        }

        if (velocity.Length() > 65)
        {
            velocity = velocity.Normalized() * 65;
        }

        Quaternion orientation = VehicleBody.ToGodot(state.Orientation);
        if (angular.LengthSquared() > 0.000001f)
        {
            orientation = (new Quaternion(angular.Normalized(), angular.Length() / 60) * orientation).Normalized();
        }

        var transform = new Transform3D(new Basis(orientation), VehicleBody.ToGodot(state.Position));
        Vector3 remaining = velocity / 60;
        Vector3 support = Vector3.Zero;
        SurfaceType surface = SurfaceType.Concrete;
        var pushed = new HashSet<ulong>();
        var contacts = new List<VehicleContact>();
        using var parameters = new PhysicsTestMotionParameters3D { Margin = 0.005f, MaxCollisions = 4, RecoveryAsCollision = true };
        using var result = new PhysicsTestMotionResult3D();
        for (int slide = 0; slide < 4; slide++)
        {
            parameters.From = transform;
            parameters.Motion = remaining;
            bool collided = PhysicsServer3D.BodyTestMotion(GetRid(), parameters, result);
            transform.Origin += collided ? result.GetTravel() : remaining;
            if (!collided)
            {
                break;
            }

            remaining = result.GetRemainder();
            for (int i = 0; i < result.GetCollisionCount(); i++)
            {
                Vector3 normal = result.GetCollisionNormal(i).Normalized();
                Vector3 relative = velocity - result.GetColliderVelocity(i);
                var other = result.GetCollider(i) as NetworkVehicleBody;
                if (PushProps && result.GetCollider(i) is RigidBody3D prop && !prop.Freeze && pushed.Add(prop.GetInstanceId()))
                {
                    float closing = Math.Max(0, -relative.Dot(normal));
                    prop.ApplyCentralImpulse(-normal * Math.Min(1800, closing * prop.Mass));
                }

                contacts.Add(new VehicleContact(VehicleBody.ToCore(relative), VehicleBody.ToCore(normal), 0, other?.VehicleId ?? 0));
                if (normal.Y >= 0.55f)
                {
                    support = normal;
                    surface = (result.GetCollider(i) as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
                }

                if (normal.Y < 0.55f)
                {
                    float closing = Math.Max(0, -relative.Dot(normal));
                    Vector3 deltaVelocity = normal * closing;
                    Vector3 lever = result.GetCollisionPoint(i) - transform.Origin;
                    float inertiaPerMass = _configuration.Wheelbase * _configuration.Wheelbase / 3;
                    // Sweep bodies retain contact lever-arm rotation instead of silently discarding the impact torque.
                    angular += lever.Cross(deltaVelocity) / inertiaPerMass;
                    angular = VehicleBody.ToGodot(VehicleMovement.Limit(VehicleBody.ToCore(angular), _configuration.MaximumAngularSpeed));
                    velocity += deltaVelocity;
                }

                if (velocity.Dot(normal) < 0)
                {
                    velocity = velocity.Slide(normal);
                }

                if (remaining.Dot(normal) < 0)
                {
                    remaining = remaining.Slide(normal);
                }
            }

            if (remaining.LengthSquared() < 0.0000001f)
            {
                break;
            }
        }

        if (velocity.Y <= 1)
        {
            using var ray = PhysicsRayQueryParameters3D.Create(transform.Origin, transform.Origin + (Vector3.Down * 0.62f), CollisionMask, new Godot.Collections.Array<Rid> { GetRid() });
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count > 0 && hit["normal"].AsVector3().Y >= 0.55f)
            {
                support = hit["normal"].AsVector3().Normalized();
                surface = (hit["collider"].AsGodotObject() as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
            }
        }

        var suspension = WheelSuspension.Observe(this, transform, _configuration);
        if (!suspension.Normal.IsZeroApprox())
        {
            support = suspension.Normal;
            surface = suspension.Surface;
        }

        return new VehicleObservation(new VehiclePhysicsState(VehicleBody.ToCore(transform.Origin), new Numerics.Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), VehicleBody.ToCore(velocity), VehicleBody.ToCore(angular)), VehicleBody.ToCore(support), contacts, surface, suspension.Wheels);
    }

    /// <summary>Applies visible steering and independent spring travel from accepted state.</summary>
    /// <param name="state">Accepted or predicted handling.</param>
    internal void PresentHandling(VehicleState state) => WheelSuspension.Present(_wheels, state, _configuration.SuspensionLength, 0.375f);

    /// <summary>Reconstructs the collision proxy immediately; rendering retains its own correction offset.</summary>
    /// <param name="state">Predicted or authoritative physics.</param>
    /// <param name="correction">Whether this state replaces a prediction at the same visible instant.</param>
    internal void Apply(VehiclePhysicsState state, bool correction = false)
    {
        if (_initialized && correction)
        {
            Smoothing.Correct(_current.Position + Smoothing.Offset, state.Position, Smoothing.Rotation * _current.Orientation, state.Orientation);
        }

        _previous = _initialized && !correction ? _current : state;
        _current = state;
        _initialized = true;
        GlobalTransform = new Transform3D(new Basis(VehicleBody.ToGodot(state.Orientation)), VehicleBody.ToGodot(state.Position));
        ConstantLinearVelocity = VehicleBody.ToGodot(state.LinearVelocity);
    }

    /// <summary>Applies a render-time remote pose directly without feeding it into gameplay state.</summary>
    /// <param name="state">Buffered interpolation sample.</param>
    internal void PresentRemote(VehiclePhysicsState state)
    {
        _visual.GlobalTransform = new Transform3D(new Basis(VehicleBody.ToGodot(state.Orientation)), VehicleBody.ToGodot(state.Position));
    }

    /// <summary>Draws predicted/host state between fixed ticks with presentation-only correction decay.</summary>
    /// <param name="delta">Render delta.</param>
    internal void PresentLocal(float delta)
    {
        if (!_initialized)
        {
            return;
        }

        Smoothing.Advance(delta);
        float alpha = (float)Engine.GetPhysicsInterpolationFraction();
        Numerics.Vector3 position = Numerics.Vector3.Lerp(_previous.Position, _current.Position, alpha) + Smoothing.Offset;
        Numerics.Quaternion orientation = Smoothing.Rotation * Numerics.Quaternion.Slerp(_previous.Orientation, _current.Orientation, alpha);
        _visual.GlobalTransform = new Transform3D(new Basis(VehicleBody.ToGodot(orientation)), VehicleBody.ToGodot(position));
    }
}
