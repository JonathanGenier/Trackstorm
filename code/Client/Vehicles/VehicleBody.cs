using Godot;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Vehicles;

/// <summary>Native observation/command adapter around the single Core simulation; standard force integration is disabled.</summary>
public sealed partial class VehicleBody : RigidBody3D
{
    /// <summary>Current native support material, independent of simulation handling.</summary>
    internal SurfaceIdentity? DetectedSurface { get; private set; }

    private readonly List<VehicleEffectRequest> _effects = new();
    private readonly VehicleFeedback _feedback = new();
    private VehiclePhysicsState? _reset;
    private Trackstorm.Core.Simulation.Simulation _simulation = null!;

    /// <summary>Publishes the exact fixed-step snapshot before native collision solving.</summary>
    internal event Action<VehicleState>? Advanced;

    /// <summary>Shared engine-independent tuning.</summary>
    internal VehicleConfiguration Configuration { get; set; } = new();
    /// <summary>Authority-owned damage tuning.</summary>
    internal DamageConfiguration DamageConfiguration { get; set; } = new();
    /// <summary>Stable local identity, suitable for later authority mapping.</summary>
    internal ulong VehicleId { get; set; } = 1;
    /// <summary>Procedural body color.</summary>
    internal Color Paint { get; set; } = new("2fd4df");
    /// <summary>Optional deterministic input source used by replay and verification.</summary>
    internal Func<ulong, InputFrame>? InputSource { get; set; }
    /// <summary>Core-owned movement state, independent of visual interpolation.</summary>
    internal VehicleState State => Snapshot.Movement;
    /// <summary>Read-only aggregate owned by the single Core simulation.</summary>
    internal VehicleSnapshot Snapshot => _simulation.GetVehicle(VehicleId);
    /// <summary>Serializable Core health snapshot for HUD and future networking.</summary>
    internal VehicleDamageState DamageState => Snapshot.Damage;
    /// <summary>Observed presentation submissions, not proof of audible output.</summary>
    internal int FeedbackCueCount => _feedback.CueCount;

    /// <inheritdoc/>
    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.On;
        Configuration.Validate();
        if (Configuration.TicksPerSecond != Engine.PhysicsTicksPerSecond)
        {
            throw new InvalidOperationException("Native physics and Core vehicle tick rates must match.");
        }

        Mass = Configuration.Mass;
        CustomIntegrator = true;
        ContinuousCd = true;
        CanSleep = false;
        ContactMonitor = true;
        MaxContactsReported = 16;
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0, (-0.25f * VehicleDimensions.Scale) + VehicleDimensions.OriginShift, 0);
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.15f, Bounce = 0, Absorbent = true };
        AddChild(VehicleVisual.CreateCollision());
        var identification = new StandardMaterial3D { AlbedoColor = Paint, Roughness = 0.8f };
        AddChild(VehicleVisual.Create(identification, () => (State, Configuration)));
        _feedback.Initialize(identification, Paint);
        AddChild(_feedback);
        AddChild(new TireFeedback { Source = () => (GlobalTransform, Snapshot, Configuration) });
    }

    /// <summary>Converts a native vector at the engine boundary.</summary>
    /// <param name="value">Native vector.</param>
    /// <returns>Plain numeric data.</returns>
    internal static Numerics.Vector3 ToCore(Vector3 value) => new(value.X, value.Y, value.Z);

    /// <summary>Converts a Core vector for native application.</summary>
    /// <param name="value">Plain numeric data.</param>
    /// <returns>Native vector.</returns>
    internal static Vector3 ToGodot(Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    /// <summary>Converts a Core orientation for native application.</summary>
    /// <param name="value">Core orientation.</param>
    /// <returns>Native orientation.</returns>
    internal static Quaternion ToGodot(Numerics.Quaternion value) => new(value.X, value.Y, value.Z, value.W);

    /// <summary>Builds an original primitive visual without external asset dependencies.</summary>
    /// <param name="size">Box dimensions.</param>
    /// <param name="position">Local center.</param>
    /// <param name="color">Surface color.</param>
    /// <returns>Owned visual node.</returns>
    internal static MeshInstance3D Box(Vector3 size, Vector3 position, Color color) => new()
    {
        Mesh = new BoxMesh { Size = size },
        Position = position,
        MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.7f },
    };

    /// <summary>Connects this reconstructable native body to an already registered Core aggregate.</summary>
    /// <param name="simulation">Single simulation owner shared by the arena.</param>
    internal void Initialize(Trackstorm.Core.Simulation.Simulation simulation) => _simulation = simulation;

    /// <summary>Captures native observations during the arena's fixed callback, without deciding gameplay outcomes.</summary>
    /// <param name="input">Next ordered logical input from the coordinator.</param>
    /// <returns>Plain-data observations and unprocessed requests.</returns>
    internal VehicleStepRequest Capture(InputFrame input)
    {
        if (!Snapshot.CanInteract && !_reset.HasValue)
        {
            DetectedSurface = null;
            return new VehicleStepRequest(VehicleId, input, new VehicleObservation(Snapshot.Movement.Physics, Numerics.Vector3.Zero));
        }

        PhysicsDirectBodyState3D body = PhysicsServer3D.BodyGetDirectState(GetRid());
        Vector3 support = Vector3.Zero;
        SurfaceType surface = SurfaceType.Concrete;
        float closestSupport = float.PositiveInfinity;
        ulong supportId = ulong.MaxValue;
        var contacts = new List<VehicleContact>();
        Numerics.Vector3 incomingVelocity = State.Physics.LinearVelocity;
        Numerics.Vector3 incomingAngular = State.Physics.AngularVelocity;
        foreach (var effect in Snapshot.Effects)
        {
            incomingVelocity += effect.Effect.Impulse / Configuration.Mass;
            incomingAngular += Numerics.Vector3.Cross(effect.Effect.Offset, effect.Effect.Impulse) / (Configuration.Mass * Configuration.Wheelbase * Configuration.Wheelbase / 3);
        }
        for (int contact = 0; contact < body.GetContactCount(); contact++)
        {
            // Godot contact normals refer to the local body but are expressed in world space.
            Vector3 normal = body.GetContactLocalNormal(contact);
            if (EnvironmentContact.IsObstacle(body.GetContactColliderObject(contact), normal))
            {
                normal = EnvironmentContact.ExposedNormal(this, body.Transform.Origin, body.GetContactLocalPosition(contact), normal);
            }
            if (normal.Y >= Configuration.SupportNormalMinimum && !EnvironmentContact.IsObstacle(body.GetContactColliderObject(contact), normal))
            {
                support += normal;
                Vector3 offset = body.GetContactLocalPosition(contact) - body.Transform.Origin;
                float distance = (offset.X * offset.X) + (offset.Z * offset.Z);
                GodotObject collider = body.GetContactColliderObject(contact);
                ulong id = collider?.GetInstanceId() ?? 0;
                if (distance < closestSupport || (distance == closestSupport && id < supportId))
                {
                    closestSupport = distance;
                    supportId = id;
                    surface = collider is SurfaceBody ground ? ground.Surface : SurfaceType.Concrete;
                }
            }

            Vector3 relative = body.GetContactLocalVelocityAtPosition(contact) - body.GetContactColliderVelocityAtPosition(contact);
            var other = body.GetContactColliderObject(contact) as VehicleBody;
            bool obstacle = EnvironmentContact.IsObstacle(body.GetContactColliderObject(contact), normal);
            contacts.Add(new VehicleContact(obstacle ? incomingVelocity : ToCore(relative), ToCore(normal.Normalized()), body.GetContactImpulse(contact).Length(), other?.VehicleId ?? 0, body.GetContactColliderObject(contact) is Node terrain && terrain.IsInGroup("landing_terrain") && normal.Y >= Configuration.SupportNormalMinimum, ToCore(body.Transform.AffineInverse() * body.GetContactLocalPosition(contact)), obstacle, Arenas.DestructibleEnvironment.RockId(body.GetContactColliderObject(contact))));
        }

        // Prefer the center's surface while retaining native contact normals for existing slope handling.
        // Bridge tiny solver separation gaps, but never preserve ground control during a real upward launch.
        if (!support.IsZeroApprox() || body.LinearVelocity.Y <= 1)
        {
            using var ray = PhysicsRayQueryParameters3D.Create(body.Transform.Origin, body.Transform.Origin + (Vector3.Down * (0.6f * VehicleDimensions.Scale)), CollisionMask, new Godot.Collections.Array<Rid> { GetRid() });
            Godot.Collections.Dictionary hit = body.GetSpaceState().IntersectRay(ray);
            if (hit.Count > 0)
            {
                Vector3 normal = hit["normal"].AsVector3();
                if (normal.Y >= Configuration.SupportNormalMinimum && !EnvironmentContact.IsObstacle(hit["collider"].AsGodotObject(), normal))
                {
                    if (support.IsZeroApprox())
                    {
                        support = normal;
                    }

                    surface = hit["collider"].AsGodotObject() is SurfaceBody ground ? ground.Surface : SurfaceType.Concrete;
                }
            }
        }

        var suspension = WheelSuspension.Observe(this, body.Transform, Configuration);
        DetectedSurface = suspension.Identity;
        if (!suspension.Normal.IsZeroApprox())
        {
            support = suspension.Normal;
            surface = suspension.Surface;
        }

        var physics = Observe(body.Transform, body.LinearVelocity, body.AngularVelocity);
        if (contacts.Any(contact => contact.StaticObstacle) && !contacts.Any(contact => contact.OtherVehicleId != 0))
        {
            var incoming = new VehiclePhysicsState(physics.Position, physics.Orientation, incomingVelocity, incomingAngular);
            physics = EnvironmentCollision.Resolve(incoming, ToCore(support.IsZeroApprox() ? Vector3.Zero : support.Normalized()), contacts, Configuration);
            Numerics.Vector3 velocity = physics.LinearVelocity;
            foreach (var contact in contacts.Where(contact => !contact.StaticObstacle))
            {
                float closing = Math.Max(0, -Numerics.Vector3.Dot(velocity, contact.Normal));
                velocity += contact.Normal * closing;
            }
            physics = new(physics.Position, physics.Orientation, velocity, physics.AngularVelocity);
        }
        var observation = new VehicleObservation(physics, ToCore(support.IsZeroApprox() ? Vector3.Zero : support.Normalized()), contacts, surface, suspension.Wheels, ToCore(suspension.TerrainNormal), WaterObservation.Observe(this, body.Transform));
        return new VehicleStepRequest(VehicleId, InputSource?.Invoke(input.Tick) ?? input, observation, _effects, _reset);
    }

    /// <summary>Applies an accepted Core result at the native fixed boundary; no health or movement rules live here.</summary>
    /// <param name="result">Commands and presentation outcomes committed by Core.</param>
    internal void Apply(VehicleStepResult result)
    {
        PhysicsDirectBodyState3D body = PhysicsServer3D.BodyGetDirectState(GetRid());
        VehiclePhysicsState commands = result.Snapshot.Movement.Physics;
        CollisionLayer = result.Snapshot.CanInteract ? 1u : 0u;
        CollisionMask = result.Snapshot.CanInteract ? 1u : 0u;
        Freeze = !result.Snapshot.CanInteract;
        Visible = result.Snapshot.CanInteract;
        if (result.Reset)
        {
            body.Transform = new Transform3D(new Basis(ToGodot(commands.Orientation)), ToGodot(commands.Position));
            ResetPhysicsInterpolation();
            _feedback.Reset();
        }

        body.LinearVelocity = ToGodot(commands.LinearVelocity);
        body.AngularVelocity = ToGodot(commands.AngularVelocity);
        foreach (VehicleEffectRequest request in result.Effects)
        {
            body.ApplyImpulse(ToGodot(request.Effect.Impulse), ToGodot(request.Effect.Offset));
        }

        if (result.DamageEvents.Count > 0 || result.Effects.Any(request => request.Effect.Impulse != Numerics.Vector3.Zero))
        {
            // Coalesce one tick's confirmed damage/impulses into one visual flash.
            _feedback.Present(result.Snapshot.Damage.Destroyed);
        }

        _effects.Clear();
        _reset = null;
    }

    /// <summary>Publishes only after all bodies have received the same committed global step.</summary>
    internal void Publish() => Advanced?.Invoke(State);

    /// <summary>Queues a new-life request for the next coordinated Core/native fixed step.</summary>
    /// <param name="physics">Explicit new pose and velocities.</param>
    internal void ResetBody(VehiclePhysicsState physics)
    {
        _effects.Clear();
        _reset = physics;
    }

    /// <summary>Queues a generic Core effect for safe native integration and authoritative health mutation.</summary>
    /// <param name="effect">Validated damage and impulse payload.</param>
    /// <param name="context">Stable attribution from the local authority.</param>
    internal void ApplyEffect(DamageEffect effect, DamageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _effects.Add(new VehicleEffectRequest(effect, context));
    }

    private static VehiclePhysicsState Observe(Transform3D transform, Vector3 velocity, Vector3 angular)
    {
        Quaternion rotation = transform.Basis.GetRotationQuaternion().Normalized();
        return new VehiclePhysicsState(ToCore(transform.Origin), new Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), ToCore(velocity), ToCore(angular));
    }
}
