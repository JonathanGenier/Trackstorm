using Godot;
using Trackstorm.Client.Input;
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
    private Vector3 _position;
    private float _mouse;
    private PlayerInput? _input;

    /// <summary>Horizontal chase distance in metres.</summary>
    [Export(PropertyHint.Range, "2,25,0.1")]
    public float FollowDistance { get; set; } = 11;
    /// <summary>Height above the damped anchor in metres.</summary>
    [Export(PropertyHint.Range, "1,12,0.1")]
    public float CameraHeight { get; set; } = 5;
    /// <summary>Position convergence rate per second.</summary>
    [Export(PropertyHint.Range, "0.1,30,0.1")]
    public float PositionDamping { get; set; } = 8;
    /// <summary>Heading, look and aim convergence rate per second.</summary>
    [Export(PropertyHint.Range, "0.1,30,0.1")]
    public float RotationDamping { get; set; } = 7;
    /// <summary>Normalized steering contribution multiplier.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SteeringInfluence { get; set; } = 1;
    /// <summary>Maximum steering anticipation in degrees.</summary>
    [Export(PropertyHint.Range, "0,20,0.1")]
    public float SteeringMaximumDegrees { get; set; } = 10;
    /// <summary>Mouse degrees per screen pixel.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float MouseSensitivity { get; set; } = 0.12f;
    /// <summary>Right stick degrees per second at full deflection.</summary>
    [Export(PropertyHint.Range, "0,180,1")]
    public float StickSensitivity { get; set; } = 65;
    /// <summary>Maximum combined steering and manual look in degrees.</summary>
    [Export(PropertyHint.Range, "0,45,0.1")]
    public float MaximumLookDegrees { get; set; } = 20;
    /// <summary>Seconds without manual input before recentering.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float RecenterDelay { get; set; } = 0.25f;
    /// <summary>Manual look recenter convergence rate per second.</summary>
    [Export(PropertyHint.Range, "0.1,20,0.1")]
    public float RecenterDamping { get; set; } = 4;
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
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _input = GetTree().GetFirstNodeInGroup("local_player_input") as PlayerInput;
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        // Unhandled motion leaves menus and settings in charge of their own pointer input.
        if (@event is InputEventMouseMotion mouse && InputActive())
        {
            _mouse += mouse.ScreenRelative.X;
        }
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

    /// <summary>Combines displayed pose, local look and accepted damage in render time.</summary>
    /// <param name="pose">Interpolated displayed pose.</param>
    /// <param name="state">Aggregate for identity and damage.</param>
    /// <param name="steering">Normalized local steering intent.</param>
    /// <param name="delta">Elapsed presentation seconds.</param>
    internal void Follow(Transform3D pose, VehicleSnapshot state, float steering, float delta)
    {
        bool reset = !_initialized || state.VehicleId != _vehicle || state.LifeId != _life;
        Vector3 forward = -pose.Basis.Z;
        // Retain heading near vertical and while overturned; never inherit chassis pitch or roll.
        float heading = pose.Basis.Y.Y > 0.15f && new Vector2(forward.X, forward.Z).LengthSquared() > 0.1f
            ? MathF.Atan2(-forward.X, -forward.Z) : _heading;
        if (reset)
        {
            _motion.Reset();
            _mouse = 0;
            _heading = heading;
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

        bool active = InputActive();
        float stick = active ? _input!.Adapter.CameraLookStrength() : 0;
        _motion.Advance(delta, steering * SteeringInfluence, active ? _mouse * MouseSensitivity : 0, stick * StickSensitivity, SteeringMaximumDegrees, MaximumLookDegrees, RotationDamping, RecenterDelay, RecenterDamping, ShakeDecay);
        _mouse = 0;
        _heading = Mathf.LerpAngle(_heading, heading, ChaseCameraMotion.Blend(RotationDamping, delta));
        _anchor = _anchor.Lerp(pose.Origin, ChaseCameraMotion.Blend(PositionDamping, delta));
        float yaw = _heading - Mathf.DegToRad(_motion.LookAngle);
        Vector3 desired = _anchor + (new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)) * Math.Max(2, FollowDistance)) + (Vector3.Up * Math.Max(1, CameraHeight));
        _position = reset ? desired : _position.Lerp(desired, ChaseCameraMotion.Blend(PositionDamping, delta));
        GlobalPosition = _position + (Vector3.Up * _motion.ShakeOffset * MaximumShakeMetres);
        Quaternion target = Basis.LookingAt((_anchor + (Vector3.Up * 0.5f)) - GlobalPosition, Vector3.Up).GetRotationQuaternion();
        Quaternion = reset ? target : Quaternion.Slerp(target, ChaseCameraMotion.Blend(RotationDamping, delta));
        _initialized = true;
    }

    private bool InputActive()
    {
        if (_input is null || !GodotObject.IsInstanceValid(_input))
        {
            _input = GetTree().GetFirstNodeInGroup("local_player_input") as PlayerInput;
        }

        return _input is not null && _input.Adapter.Enabled && !_input.Adapter.GameplaySuppressed && !GetTree().Paused;
    }
}
