using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;
using Trackstorm.Core.Items;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Networking;

/// <summary>Synchronous Godot collision observation seam usable by both host steps and prediction replay.</summary>
internal sealed partial class NetworkVehicleBody : StaticBody3D
{
    private VehicleMotionQuery _motionQuery = null!;
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
    private BoostExhaust _boost = null!;
    private ulong _life;
    internal CarRackPresentation Rack { get; private set; } = null!;
    private VehicleSnapshot? _feedbackState;
    private bool _lifeCorrectionPending;
    private CollisionShape3D _rearCollision = null!;
    private MeshInstance3D _rearVisual = null!;
    internal bool HasRearShield { get; private set; }
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
        CollisionMask = 3 | 32;
        Quaternion initialRotation = GlobalBasis.GetRotationQuaternion().Normalized();
        _current = new(VehicleBody.ToCore(GlobalPosition), new Numerics.Quaternion(initialRotation.X, initialRotation.Y, initialRotation.Z, initialRotation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero);
        var chassis = VehicleVisual.CreateCollision();
        AddChild(chassis);
        AddChild(_visual);
        _rearCollision = new CollisionShape3D { Shape = new BoxShape3D { Size = VehicleBody.ToGodot(TombstoneGeometry.Size) },
            Position = VehicleBody.ToGodot(TombstoneGeometry.Center), Disabled = true };
        AddChild(_rearCollision);
        _motionQuery = new VehicleMotionQuery(this, chassis, _rearCollision);
        _rearVisual = new MeshInstance3D { Mesh = new BoxMesh { Size = VehicleBody.ToGodot(TombstoneGeometry.Size) },
            Position = VehicleBody.ToGodot(TombstoneGeometry.Center), Visible = false,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.36f, 0.4f), Metallic = 0.65f, Roughness = 0.7f } };
        _visual.AddChild(_rearVisual);
        _visual.TopLevel = true;
        AddChild(new TireFeedback { Source = () => _feedbackState is { } state ? (VisualTransform, state, _configuration) : null });
        Color paint = Color.FromHsv((VehicleId * 0.13f) % 1, 0.7f, 0.9f);
        _damageMaterial = new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>("res://assets/items/materials/DamageFlash.gdshader") };
        _damageMaterial.SetShaderParameter("paint", paint);
        var model = VehicleVisual.Create(_damageMaterial, () => _feedbackState is { } state ? (state.Movement, _configuration) : null);
        _visual.AddChild(model);
        _boost = new BoostExhaust { Source = () => _feedbackState };
        Rack = new CarRackPresentation { Boost = _boost };
        model.AddChild(Rack);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _flash = Math.Max(0, _flash - ((float)delta * 5));
        _damageMaterial.SetShaderParameter("flash", _flash);
    }

    public override void _ExitTree()
    {
        _motionQuery?.Dispose();
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

    /// <summary>Reconstructs temporary hardware from accepted state, independently of slot selection.</summary>
    internal void ObserveTombstones(VehicleSnapshot vehicle, IEnumerable<TombstoneState> states)
    {
        HasRearShield = vehicle.CanInteract && states.Any(s => s.Owner == VehicleId && s.Life == vehicle.LifeId && s.Stage == TombstoneStage.RearShield);
        _rearCollision.Disabled = !HasRearShield;
        _rearVisual.Visible = HasRearShield;
    }

    // Weapon intersection is decided in Core against current candidate state. Excluding this
    // reconstructable shape also prevents a same-step destroyed shield from masking the chassis.
    internal void SetShieldQueryEnabled(bool enabled) => _rearCollision.Disabled = !enabled || !HasRearShield;

    /// <summary>Resolves the preceding Core command through bounded native sweep/slide queries.</summary>
    /// <returns>Solved numeric physics/support/contact observations for the next Core step.</returns>
    /// <param name="snapshot">Complete pre-solver command boundary.</param>
    /// <param name="solveVehicles">Prediction resolves local contacts; host batching resolves both participants afterward.</param>
    internal VehicleObservation Observe(VehicleSnapshot snapshot, bool solveVehicles = true)
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
            bool collided = _motionQuery.Test(parameters, result);
            transform.Origin += collided ? result.GetTravel() : remaining;
            if (!collided)
            {
                break;
            }

            remaining = result.GetRemainder();
            for (int i = 0; i < result.GetCollisionCount(); i++)
            {
                Vector3 normal = EnvironmentContact.SupportFaceNormal(this, result.GetCollider(i), result.GetCollisionPoint(i), result.GetCollisionNormal(i).Normalized());
                if (EnvironmentContact.IsObstacle(result.GetCollider(i), normal))
                {
                    normal = EnvironmentContact.ExposedNormal(this, transform.Origin, result.GetCollisionPoint(i), normal);
                }
                var other = result.GetCollider(i) as NetworkVehicleBody;
                var wall = result.GetCollider(i) as Items.TombstoneWallBody;
                // A ground-aligned wall shares horizontal momentum in Core. Its
                // banked side face must not turn a sustained push into lift.
                if (wall is not null && Math.Abs(normal.Y) < _configuration.SupportNormalMinimum)
                { normal = new Vector3(normal.X, 0, normal.Z).Normalized(); }
                Vector3 point = result.GetCollisionPoint(i);
                Vector3 relative = other is null ? velocity + angular.Cross(point - transform.Origin) - result.GetColliderVelocity(i) :
                    incomingVelocity + incomingAngular.Cross(point - initialTransform.Origin) -
                    VehicleBody.ToGodot(other._current.LinearVelocity + Numerics.Vector3.Cross(other._current.AngularVelocity, VehicleBody.ToCore(point) - other._current.Position));
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
                contacts.Add(new VehicleContact(VehicleBody.ToCore(obstacle ? incomingVelocity : relative), VehicleBody.ToCore(normal), 0, other?.VehicleId ?? 0, result.GetCollider(i) is Node terrain && terrain.IsInGroup("landing_terrain") && normal.Y >= _configuration.SupportNormalMinimum, VehicleBody.ToCore(transform.AffineInverse() * result.GetCollisionPoint(i)), obstacle, Arenas.DestructibleEnvironment.RockId(result.GetCollider(i)), (result.GetCollider(i) as Items.TombstoneWallBody)?.Identity ?? 0));
                if (normal.Y >= _configuration.SupportNormalMinimum && !obstacle)
                {
                    support = normal;
                    surface = (result.GetCollider(i) as SurfaceBody)?.Surface ?? SurfaceType.Concrete;
                }

                if (TerrainCollision.IsBodyImpact(new Numerics.Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), contacts[^1], snapshot.Movement.Wheels))
                {
                    var incoming = new VehiclePhysicsState(VehicleBody.ToCore(transform.Origin),
                        new Numerics.Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W),
                        VehicleBody.ToCore(velocity), VehicleBody.ToCore(angular));
                    var resolved = TerrainCollision.Resolve(incoming, contacts[^1], _configuration);
                    velocity = VehicleBody.ToGodot(resolved.LinearVelocity);
                    angular = VehicleBody.ToGodot(resolved.AngularVelocity);
                }

                Vector3 geometryNormal = normal;
                if (obstacle)
                {
                    normal = VehicleBody.ToGodot(EnvironmentCollision.ResponseNormal(VehicleBody.ToCore(normal), VehicleBody.ToCore(initialSupport)));
                }
                // Tombstone's mass-sharing response owns its contact. Adding the
                // fixed-prop lever-arm kick pitches the car into the ground.
                else if (other is null && wall is null && normal.Y < _configuration.SupportNormalMinimum)
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

                if (other is null && velocity.Dot(normal) < 0)
                {
                    velocity = velocity.Slide(normal);
                }

                if (remaining.Dot(normal) < 0)
                {
                    remaining = remaining.Slide(normal);
                }
                if (remaining.Dot(geometryNormal) < 0)
                {
                    // The anti-climb response normal controls momentum, but the
                    // remaining sweep must also respect the actual surface plane.
                    // Otherwise falling against a sloped obstacle repeatedly sweeps
                    // downward into its widening face, exhausting all slide passes.
                    remaining = remaining.Slide(geometryNormal);
                }
            }

            if (remaining.LengthSquared() < 0.0000001f)
            {
                break;
            }
        }

        if (velocity.Y <= 1)
        {
            var excluded = new Godot.Collections.Array<Rid> { GetRid() };
            using var ray = PhysicsRayQueryParameters3D.Create(transform.Origin, transform.Origin + (Vector3.Down * (0.62f * VehicleDimensions.Scale)), CollisionMask, excluded);
            using var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
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
            foreach (var contact in contacts.Where(contact => !contact.StaticObstacle && contact.OtherVehicleId == 0))
            {
                Vector3 normal = VehicleBody.ToGodot(contact.Normal);
                if (velocity.Dot(normal) < 0) { velocity = velocity.Slide(normal); }
            }
        }

        float waterDepth = WaterObservation.Observe(this, transform);
        var observation = new VehicleObservation(new VehiclePhysicsState(VehicleBody.ToCore(transform.Origin), new Numerics.Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), VehicleBody.ToCore(velocity), VehicleBody.ToCore(angular)), VehicleBody.ToCore(support), contacts, surface, suspension.Wheels, VehicleBody.ToCore(suspension.TerrainNormal), waterDepth);
        if (solveVehicles)
        {
            // Prediction uses the latest remote proxy. The host solves both participants together
            // below, including stationary targets that did not themselves produce a sweep contact.
            foreach (var contact in contacts.Where(c => c.OtherVehicleId != 0).GroupBy(c => c.OtherVehicleId).Select(g => g.OrderBy(c => Numerics.Vector3.Dot(c.RelativeVelocity, c.Normal)).First()))
            {
                var otherBody = GetParent().GetChildren().OfType<NetworkVehicleBody>().FirstOrDefault(b => b.VehicleId == contact.OtherVehicleId);
                if (otherBody is null) { continue; }
                var point = observation.Physics.Position + Numerics.Vector3.Transform(contact.LocalPosition, observation.Physics.Orientation);
                var resolved = VehicleCollision.ResolvePair(observation.Physics, _configuration, otherBody._current, otherBody._configuration, contact.Normal, point);
                observation = WithPhysics(observation, resolved.First);
            }
        }
        return observation;
    }

    /// <summary>Observes one common host boundary, then resolves each vehicle pair once in stable identity order.</summary>
    internal static Dictionary<ulong, VehicleObservation> ObserveBatch(IReadOnlyDictionary<ulong, NetworkVehicleBody> bodies, IEnumerable<VehicleSnapshot> snapshots)
    {
        var observations = snapshots.ToDictionary(s => s.VehicleId, s => bodies[s.VehicleId].Observe(s, false));
        var pairs = new HashSet<(ulong, ulong)>();
        foreach (ulong id in observations.Keys.Order().ToArray())
        {
            foreach (var contact in observations[id].Contacts.Where(c => c.OtherVehicleId != 0).OrderBy(c => Numerics.Vector3.Dot(c.RelativeVelocity, c.Normal)))
            {
                ulong other = contact.OtherVehicleId;
                if (!observations.ContainsKey(other) || !pairs.Add((Math.Min(id, other), Math.Max(id, other)))) { continue; }
                var first = observations[id];
                var second = observations[other];
                var point = first.Physics.Position + Numerics.Vector3.Transform(contact.LocalPosition, first.Physics.Orientation);
                var resolved = VehicleCollision.ResolvePair(first.Physics, bodies[id]._configuration, second.Physics, bodies[other]._configuration, contact.Normal, point);
                observations[id] = WithPhysics(first, resolved.First);
                observations[other] = WithPhysics(second, resolved.Second);
            }
        }
        return observations;
    }

    private static VehicleObservation WithPhysics(VehicleObservation observation, VehiclePhysicsState physics) =>
        new(physics, observation.Support, observation.Contacts, observation.Surface, observation.Wheels, observation.TerrainSupport, observation.WaterDepth);

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
        _initialized = true;
        SetQueryPose(state);
    }

    /// <summary>Moves only the native collision proxy; used transiently for matching prediction ticks.</summary>
    internal void SetQueryPose(VehiclePhysicsState state)
    {
        _current = state;
        GlobalTransform = new Transform3D(new Basis(VehicleBody.ToGodot(state.Orientation)), VehicleBody.ToGodot(state.Position));
        ForceUpdateTransform();
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
        Rack.Reset();
        _boost.Reset();
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
        if (_life != state.LifeId || !state.CanInteract) { Rack.Reset(); }
        _feedbackState = state;

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
        CollisionMask = state.CanInteract ? 3u | 32u : 0u;
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
