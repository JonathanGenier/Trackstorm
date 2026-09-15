using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Shared local chase presentation. Owns no vehicle commands or replicated state.</summary>
public sealed partial class VehicleChaseCamera : Camera3D
{
    private readonly ChaseCameraMotion _motion = new();
    private bool _initialized;
    private ulong _vehicle;
    private ulong _life;
    private ulong _damageSequence;
    private float _heading;
    private Vector3 _anchor;
    private ulong _motionTick;

    /// <summary>Horizontal chase distance in metres.</summary>
    [Export(PropertyHint.Range, "2,25,0.1")]
    public float FollowDistance { get; set; } = 11;
    /// <summary>Height above the damped anchor in metres.</summary>
    [Export(PropertyHint.Range, "1,12,0.1")]
    public float CameraHeight { get; set; } = 5;
    /// <summary>Position convergence rate per second.</summary>
    [Export(PropertyHint.Range, "0.1,30,0.1")]
    public float PositionDamping { get; set; } = 8;
    /// <summary>Rearward metres per forward acceleration in metres per second squared.</summary>
    [Export(PropertyHint.Range, "0,0.2,0.001")]
    public float LongitudinalInertia { get; set; } = 0.045f;
    /// <summary>Outside-turn metres per lateral acceleration.</summary>
    [Export(PropertyHint.Range, "0,0.1,0.001")]
    public float LateralInertia { get; set; } = 0.025f;
    /// <summary>Additional lateral weight from actual sideways velocity, including drift.</summary>
    [Export(PropertyHint.Range, "0,0.1,0.001")]
    public float SidewaysInertia { get; set; } = 0.015f;
    /// <summary>Maximum fore/aft inertia displacement in metres.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float MaximumLongitudinalInertia { get; set; } = 0.7f;
    /// <summary>Maximum lateral inertia displacement in metres.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MaximumLateralInertia { get; set; } = 0.4f;
    /// <summary>Bounded impact feedback gain.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CollisionShakeStrength { get; set; } = 0.55f;
    /// <summary>Bounded damage feedback gain.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float DamageShakeStrength { get; set; } = 0.65f;
    /// <summary>Shake envelope decay rate per second.</summary>
    [Export(PropertyHint.Range, "0.1,20,0.1")]
    public float ShakeDecay { get; set; } = 7;
    /// <summary>Minimum contact severity in metres per second for feedback.</summary>
    [Export(PropertyHint.Range, "0,10,0.1")]
    public float CollisionThreshold { get; set; } = 3;
    /// <summary>Maximum vertical shake displacement in metres.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.01")]
    public float MaximumShakeMetres { get; set; } = 0.12f;

    /// <summary>Presentation diagnostics for runtime checks.</summary>
    internal ChaseCameraMotion Motion => _motion;

    /// <inheritdoc/>
    public override void _Ready()
    {
        TopLevel = true;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
    }

    /// <summary>Coalesces existing native contacts into presentation feedback.</summary>
    /// <param name="observation">Native contact data.</param>
    /// <param name="mass">Mass for impulse normalization.</param>
    internal void ObserveCollision(VehicleObservation observation, float mass)
    {
        float severity = 0;
        foreach (VehicleContact contact in observation.Contacts)
        {
            severity = Math.Max(severity, Math.Max(-System.Numerics.Vector3.Dot(contact.RelativeVelocity, contact.Normal), contact.Impulse / mass));
        }

        _motion.Collision(severity, CollisionThreshold, CollisionShakeStrength);
    }

    /// <summary>Combines vehicle heading, measured positional inertia and accepted damage.</summary>
    /// <param name="pose">Interpolated displayed pose.</param>
    /// <param name="state">Aggregate for identity and damage.</param>
    /// <param name="delta">Elapsed presentation seconds.</param>
    internal void Follow(Transform3D pose, VehicleSnapshot state, float delta)
    {
        bool reset = !_initialized || state.VehicleId != _vehicle || state.LifeId != _life;
        Vector3 forward = -pose.Basis.Z;
        // Retain heading for invalid, near-vertical or overturned orientations; never inherit chassis pitch or roll.
        float heading = pose.Basis.IsFinite() && pose.Basis.Y.Y > 0.15f && new Vector2(forward.X, forward.Z).LengthSquared() > 0.1f
            ? MathF.Atan2(-forward.X, -forward.Z) : _heading;
        if (reset)
        {
            _motion.Reset(state.ObservedPhysics.LinearVelocity);
            _motionTick = state.Movement.Tick;
            _anchor = pose.Origin;
            _vehicle = state.VehicleId;
            _life = state.LifeId;
            _damageSequence = state.Damage.LastDamage?.Sequence ?? 0;
        }

        if (state.Damage.LastDamage is DamageEvent damage && damage.Sequence > _damageSequence)
        {
            _damageSequence = damage.Sequence;
            float strength = damage.Attribution.Source == "collision" ? CollisionShakeStrength : DamageShakeStrength;
            _motion.Impulse(strength * Math.Clamp(damage.Amount / 50, 0, 1));
        }

        if (state.Movement.Tick > _motionTick)
        {
            _motion.ObserveVelocity(state.ObservedPhysics.LinearVelocity, (state.Movement.Tick - _motionTick) / (float)Engine.PhysicsTicksPerSecond);
            _motionTick = state.Movement.Tick;
        }

        // The displayed pose already includes practice/network interpolation. Do not add yaw lag.
        _heading = heading;
        _motion.Advance(delta, _heading, LongitudinalInertia, LateralInertia, SidewaysInertia, MaximumLongitudinalInertia, MaximumLateralInertia, PositionDamping, ShakeDecay);
        // Horizontal position follows the interpolated vehicle, with only bounded local inertia.
        // Vertical damping absorbs bumps; neither inertia nor shake changes the heading or aim.
        _anchor = new Vector3(pose.Origin.X, Mathf.Lerp(_anchor.Y, pose.Origin.Y, ChaseCameraMotion.Blend(PositionDamping, delta)), pose.Origin.Z);
        Vector3 backward = new(MathF.Sin(_heading), 0, MathF.Cos(_heading));
        Vector3 right = new(MathF.Cos(_heading), 0, -MathF.Sin(_heading));
        float distance = Math.Max(2, FollowDistance);
        float height = Math.Max(1, CameraHeight);
        GlobalPosition = _anchor + (backward * (distance + _motion.Offset.Y)) + (right * _motion.Offset.X) + (Vector3.Up * (height + (_motion.ShakeOffset * MaximumShakeMetres)));
        GlobalBasis = Basis.FromEuler(new Vector3(-MathF.Atan2(height - 0.5f, distance), _heading, 0));
        _initialized = true;
    }
}
