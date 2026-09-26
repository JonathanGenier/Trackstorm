using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Networking;

/// <summary>Synchronous Godot collision observation seam usable by both host steps and prediction replay.</summary>
internal sealed partial class NetworkVehicleBody : StaticBody3D
{
    /// <summary>Current native support material, independent of simulation handling.</summary>
    internal SurfaceIdentity? DetectedSurface { get; private set; }

    private readonly Node3D _visual = new();
    private VehicleConfiguration _configuration = new();
    private VehiclePhysicsState _previous;
    private VehiclePhysicsState _current;
    private bool _initialized;
    private ShaderMaterial _damageMaterial = null!;
    private float _previousHP = 100;
    private float _flash;
    private GpuParticles3D _nitroTrail = null!;
    private ulong _life;
    private VehicleSnapshot? _feedbackState;
    private bool _lifeCorrectionPending;
    /// <summary>Host-assigned identity used only to attribute contact observations.</summary>
    internal ulong VehicleId { get; init; }
    /// <summary>Only authoritative forward steps may apply native prop impulses.</summary>
    internal bool PushProps { get; set; }
    /// <summary>Presentation-only correction memory.</summary>
    internal CorrectionSmoothing Smoothing { get; } = new();
    /// <summary>Displayed position for the local chase camera.</summary>
    internal Vector3 VisualPosition => _visual.GlobalPosition;
    /// <summary>Complete displayed pose after interpolation and correction smoothing.</summary>
    internal Transform3D VisualTransform => _visual.GlobalTransform;
    /// <summary>Visibility of the vehicle presentation, independent of collision state.</summary>
    internal bool IsPresented => _visual.Visible;

    /// <inheritdoc/>
    public override void _Ready()
    {
        CollisionLayer = 2;
        CollisionMask = 3;
        AddChild(VehicleVisual.CreateCollision());
        AddChild(_visual);
        _visual.TopLevel = true;
        AddChild(new TireFeedback { Source = () => _feedbackState is { } state ? (VisualTransform, state, _configuration) : null });
        Color paint = Color.FromHsv((VehicleId * 0.13f) % 1, 0.7f, 0.9f);
        _damageMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/items/materials/DamageFlash.gdshader") };
        _damageMaterial.SetShaderParameter("paint", paint);
        _visual.AddChild(VehicleVisual.Create(_damageMaterial, () => _feedbackState is { } state ? (state.Movement, _configuration) : null));
        _nitroTrail = Items.ItemPresentation.Particles(Core.Items.ItemRegistry.Find(Core.Items.HeldItem.Nitro)!.ActiveVfx!, false, 0.18f);
        _nitroTrail.Amount = 64;
        ((StandardMaterial3D)((QuadMesh)_nitroTrail.DrawPass1).Material).AlbedoColor = new Color(0.15f, 0.65f, 1);
        ((ParticleProcessMaterial)_nitroTrail.ProcessMaterial).ScaleMax = 0.25f;
        _nitroTrail.Position = new Vector3(0, 0.6f, 1.9f);
        _nitroTrail.Emitting = false;
        _visual.AddChild(_nitroTrail);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        // These procedural resources are owned by this emitter, not shared imported assets.
        var process = _nitroTrail.ProcessMaterial;
        var mesh = _nitroTrail.DrawPass1 as QuadMesh;
        var material = mesh?.Material;
        _nitroTrail.ProcessMaterial = null;
        _nitroTrail.DrawPass1 = null;
        process?.Dispose();
        material?.Dispose();
        mesh?.Dispose();
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

    /// <summary>Uses the same accepted tuning as Core for suspension, inertia and impulse conversion.</summary>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    internal void ApplyConfiguration(VehicleConfiguration configuration) => _configuration = configuration;

    /// <summary>Resolves the preceding Core command through bounded native sweep/slide queries.</summary>
    /// <returns>Solved numeric physics/support/contact observations for the next Core step.</returns>
    /// <param name="snapshot">Complete pre-solver command boundary.</param>
    internal VehicleObservation Observe(VehicleSnapshot snapshot)
    {
        if (!snapshot.CanInteract)
        {
            DetectedSurface = null;
            return new VehicleObservation(snapshot.Movement.Physics, Numerics.Vector3.Zero);
        }

        VehiclePhysicsState state = snapshot.Movement.Physics;
        Vector3 velocity = VehicleBody.ToGodot(state.LinearVelocity);
        Vector3 angular = VehicleBody.ToGodot(state.AngularVelocity);
        foreach (var effect in snapshot.Effects)
        {
            velocity += VehicleBody.ToGodot(effect.Effect.Impulse) / _configuration.Mass;
            angular += VehicleBody.ToGodot(Numerics.Vector3.Cross(effect.Effect.Offset, effect.Effect.Impulse)) / (_configuration.Mass * _configuration.Wheelbase * _configuration.Wheelbase / 3);
        }

        if (velocity.Length() > _configuration.MaximumPhysicsSpeed)
        {
            velocity = velocity.Normalized() * _configuration.MaximumPhysicsSpeed;
        }

        Quaternion orientation = VehicleBody.ToGodot(state.Orientation);
        if (angular.LengthSquared() > 0.000001f)
        {
            orientation = (new Quaternion(angular.Normalized(), angular.Length() / 60) * orientation).Normalized();
        }

        var transform = new Transform3D(new Basis(orientation), VehicleBody.ToGodot(state.Position));
        Vector3 incomingVelocity = velocity;
        Vector3 incomingAngular = angular;
        Transform3D initialTransform = transform;
        Vector3 initialSupport = Vector3.Zero;
        bool sampledObstacleSupport = false;
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
                if (EnvironmentContact.IsObstacle(result.GetCollider(i), normal))
                {
                    normal = EnvironmentContact.ExposedNormal(this, transform.Origin, result.GetCollisionPoint(i), normal);
                }
                Vector3 relative = velocity - result.GetColliderVelocity(i);
                var other = result.GetCollider(i) as NetworkVehicleBody;
                if (PushProps && result.GetCollider(i) is RigidBody3D prop && !prop.Freeze && pushed.Add(prop.GetInstanceId()))
                {
                    float closing = Math.Max(0, -relative.Dot(normal));
                    prop.ApplyCentralImpulse(-normal * Math.Min(1800, closing * prop.Mass));
                }

                bool obstacle = EnvironmentContact.IsObstacle(result.GetCollider(i), normal);
                if (obstacle && !sampledObstacleSupport)
                {
                    initialSupport = WheelSuspension.Observe(this, initialTransform, _configuration).Normal;
                    sampledObstacleSupport = true;
                }
                contacts.Add(new VehicleContact(VehicleBody.ToCore(obstacle ? incomingVelocity : relative), VehicleBody.ToCore(normal), 0, other?.VehicleId ?? 0, result.GetCollider(i) is Node terrain && terrain.IsInGroup("landing_terrain") && normal.Y >= _configuration.SupportNormalMinimum, VehicleBody.ToCore(transform.AffineInverse() * result.GetCollisionPoint(i)), obstacle, Arenas.DestructibleEnvironment.RockId(result.GetCollider(i))));
                if (normal.Y >= _configuration.SupportNormalMinimum && !obstacle)
                {
                    support = normal;
                    surface = (result.GetCollider(i) as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
                }

                if (obstacle)
                {
                    normal = VehicleBody.ToGodot(EnvironmentCollision.ResponseNormal(VehicleBody.ToCore(normal), VehicleBody.ToCore(initialSupport)));
                }
                else if (normal.Y < _configuration.SupportNormalMinimum)
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
            using var ray = PhysicsRayQueryParameters3D.Create(transform.Origin, transform.Origin + (Vector3.Down * (0.62f * VehicleDimensions.Scale)), CollisionMask, new Godot.Collections.Array<Rid> { GetRid() });
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count > 0 && hit["normal"].AsVector3().Y >= _configuration.SupportNormalMinimum && !EnvironmentContact.IsObstacle(hit["collider"].AsGodotObject(), hit["normal"].AsVector3()))
            {
                support = hit["normal"].AsVector3().Normalized();
                surface = (hit["collider"].AsGodotObject() as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
            }
        }

        var suspension = WheelSuspension.Observe(this, transform, _configuration);
        DetectedSurface = suspension.Identity;
        if (!suspension.Normal.IsZeroApprox())
        {
            support = suspension.Normal;
            surface = suspension.Surface;
        }

        if (contacts.Any(contact => contact.StaticObstacle))
        {
            var incoming = new VehiclePhysicsState(VehicleBody.ToCore(transform.Origin), new Numerics.Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), VehicleBody.ToCore(incomingVelocity), VehicleBody.ToCore(incomingAngular));
            var resolved = EnvironmentCollision.Resolve(incoming, VehicleBody.ToCore(initialSupport), contacts, _configuration);
            velocity = VehicleBody.ToGodot(resolved.LinearVelocity);
            angular = VehicleBody.ToGodot(VehicleMovement.Limit(resolved.AngularVelocity + VehicleBody.ToCore(angular - incomingAngular), _configuration.MaximumAngularSpeed));
            // Retain independent support/ceiling and movable-body constraints from the same sweep.
            foreach (var contact in contacts.Where(contact => !contact.StaticObstacle))
            {
                Vector3 normal = VehicleBody.ToGodot(contact.Normal);
                if (velocity.Dot(normal) < 0) { velocity = velocity.Slide(normal); }
            }
        }

        return new VehicleObservation(new VehiclePhysicsState(VehicleBody.ToCore(transform.Origin), new Numerics.Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), VehicleBody.ToCore(velocity), VehicleBody.ToCore(angular)), VehicleBody.ToCore(support), contacts, surface, suspension.Wheels, VehicleBody.ToCore(suspension.TerrainNormal), WaterObservation.Observe(this, transform));
    }

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
        ConstantAngularVelocity = VehicleBody.ToGodot(state.AngularVelocity);
    }

    /// <summary>Applies the existing complete aggregate to native and presentation state.</summary>
    /// <param name="state">Current authority or movement-only prediction.</param>
    /// <param name="correction">Whether replacing the current prediction.</param>
    internal void Apply(VehicleSnapshot state, bool correction = false)
    {
        SynchronizeLifecycle(state);
        Apply(state.Movement.Physics, correction && !_lifeCorrectionPending);
        if (correction)
        {
            _lifeCorrectionPending = false;
        }
    }

    /// <summary>Discards native render and damage-feedback memory at a connection resync boundary.</summary>
    /// <param name="state">Fresh authoritative vehicle state.</param>
    internal void Reseed(VehicleSnapshot state)
    {
        _initialized = false;
        _flash = 0;
        _previousHP = state.Damage.CurrentHP;
        Smoothing.Reset();
        Apply(state);
        PresentRemote(state.Movement.Physics);
    }

    /// <summary>Reconstructs participation and resets visual memory when the authoritative life changes.</summary>
    /// <param name="state">Freshest accepted aggregate, never an old reliable outcome.</param>
    internal void SynchronizeLifecycle(VehicleSnapshot state)
    {
        _feedbackState = state;
        _nitroTrail.Emitting = state.CanInteract && state.Movement.Nitro.Active;
        if (_life != state.LifeId)
        {
            _life = state.LifeId;
            _initialized = false;
            _lifeCorrectionPending = true;
            Smoothing.Reset();
            _flash = 0;
            _previousHP = state.Damage.CurrentHP;
            Apply(state.Movement.Physics);
            PresentRemote(state.Movement.Physics);
        }

        CollisionLayer = state.CanInteract ? 2u : 0u;
        CollisionMask = state.CanInteract ? 3u : 0u;
        _visual.Visible = state.CanInteract;
        PresentDamage(state.Damage);
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
