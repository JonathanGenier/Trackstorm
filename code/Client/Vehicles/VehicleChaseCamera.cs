using Godot;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Shared local chase presentation. Owns no vehicle commands or replicated state.</summary>
public sealed partial class VehicleChaseCamera : Camera3D
{
    private readonly ChaseCameraMotion _motion = new();
    private readonly CameraFreeLook _look = new();
    private readonly CameraObstruction _obstruction = new();
    private readonly BoostCameraMotion _boost = new();
    private readonly AerialCameraMotion _aerial = new();
    private CameraSpeedStreaks _streaks = null!;
    private float _baseFov;
    private bool _initialized;
    private ulong _vehicle;
    private ulong _life;
    private ulong _damageSequence;
    private float _heading;
    private Vector3 _anchor;
    private ulong _motionTick;
    private float _distanceScale = 1;
    private float _airSeconds;
    private bool _recoveringHeading;

    /// <summary>Horizontal chase distance behind the deployed weapon attachment in metres.</summary>
    [Export(PropertyHint.Range, "2,25,0.1")]
    public float FollowDistance { get; set; } = 6.4f;
    /// <summary>Lens height above the deployed weapon attachment in metres.</summary>
    [Export(PropertyHint.Range, "0,12,0.05")]
    public float CameraHeight { get; set; } = 1.25f;
    /// <summary>Downward viewing angle; the elevated boom keeps the weapon below the center cursor.</summary>
    [Export(PropertyHint.Range, "0,30,0.1")]
    public float ViewDownAngle { get; set; } = 3.5f;
    /// <summary>Position convergence rate per second.</summary>
    [Export(PropertyHint.Range, "0.1,30,0.1")]
    public float PositionDamping { get; set; } = 20;
    /// <summary>Rearward metres per forward acceleration in metres per second squared.</summary>
    [Export(PropertyHint.Range, "0,0.2,0.001")]
    public float LongitudinalInertia { get; set; } = 0.012f;
    /// <summary>Outside-turn metres per lateral acceleration.</summary>
    [Export(PropertyHint.Range, "0,0.1,0.001")]
    public float LateralInertia { get; set; } = 0.007f;
    /// <summary>Additional lateral weight from actual sideways velocity, including drift.</summary>
    [Export(PropertyHint.Range, "0,0.1,0.001")]
    public float SidewaysInertia { get; set; } = 0.004f;
    /// <summary>Maximum fore/aft inertia displacement in metres.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float MaximumLongitudinalInertia { get; set; } = 0.2f;
    /// <summary>Maximum lateral inertia displacement in metres.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MaximumLateralInertia { get; set; } = 0.12f;
    /// <summary>Bounded impact feedback gain.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CollisionShakeStrength { get; set; } = 0.55f;
    /// <summary>Bounded damage feedback gain.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float DamageShakeStrength { get; set; } = 0.85f;
    /// <summary>Shake envelope decay rate per second.</summary>
    [Export(PropertyHint.Range, "0.1,20,0.1")]
    public float ShakeDecay { get; set; } = 7;
    /// <summary>Minimum contact severity in metres per second for feedback.</summary>
    [Export(PropertyHint.Range, "0,10,0.1")]
    public float CollisionThreshold { get; set; } = 3;
    /// <summary>Maximum view-plane shake displacement in metres; actual impacts use a smaller envelope.</summary>
    [Export(PropertyHint.Range, "0,0.65,0.01")]
    public float MaximumShakeMetres { get; set; } = 0.65f;

    /// <summary>Presentation diagnostics for runtime checks.</summary>
    internal ChaseCameraMotion Motion => _motion;
    internal BoostCameraMotion BoostMotion => _boost;
    internal AerialCameraMotion AerialMotion => _aerial;
    internal bool RolloverFraming => _obstruction.Reframed;
    /// <summary>The existing local input owner; never a gameplay or replicated camera command.</summary>
    internal Input.PlayerInputAdapter? InputSource { get; set; }
    /// <summary>Local preferences supplied by composition; never replicated or read from disk here.</summary>
    internal Settings.PlayerSettingsController? SettingsSource { get; set; }
    /// <summary>Enables local aiming input preferences; chase framing and recentering stay unchanged.</summary>
    internal bool WeaponAiming { get; set; }
    /// <summary>Local near-target friction; scales only deliberate input and never steers the camera.</summary>
    internal Func<Vector2, Vector2, float, (float Mouse, float Stick)>? AimFriction { get; set; }

    private float ShakeIntensity => (float)(SettingsSource?.Current.CameraShakeIntensity ?? 1);

    /// <summary>Clears presentation memory at a restored/reassigned display boundary, even for the same life.</summary>
    internal void ResetFollow()
    {
        _initialized = false;
        _boost.Reset();
        _aerial.Reset();
        if (IsNodeReady()) { Fov = _baseFov; _streaks.Reset(); _streaks.Hide(); }
        _look.Reset();
        InputSource?.ResetCameraMotion();
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        TopLevel = true;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _baseFov = Fov;
        var layer = new CanvasLayer { Layer = 0 };
        AddChild(layer);
        _streaks = new CameraSpeedStreaks { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(_streaks);
        _streaks.Hide();
    }

    /// <inheritdoc/>
    public override void _ExitTree() => _obstruction.Dispose();

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

        _motion.Collision(severity, CollisionThreshold, ShakeIntensity > 0 ? CollisionShakeStrength : 0);
    }

    /// <summary>Combines vehicle heading, measured positional inertia and accepted damage.</summary>
    /// <param name="pose">Interpolated displayed pose.</param>
    /// <param name="state">Aggregate for identity and damage.</param>
    /// <param name="delta">Elapsed presentation seconds.</param>
    /// <param name="followedBody">Native body excluded from presentation queries.</param>
    internal void Follow(Transform3D pose, VehicleSnapshot state, float delta, Rid followedBody = default)
    {
        bool reset = !_initialized || state.VehicleId != _vehicle || state.LifeId != _life;
        var preferences = SettingsSource?.Current;
        float inertia = (float)(preferences?.CameraInertia ?? .5) * 2;
        float distanceScale = (float)(preferences?.CameraDistance ?? 1);
        // Use the shared deployed attachment, not animated rack travel or accepted weapon
        // rotation. Camera input drives weapon intent; following its rotation would feed back.
        Basis pivotBasis = pose.Basis.IsFinite() ? pose.Basis : Basis.FromEuler(new Vector3(0, _heading, 0));
        Vector3 pivot = pose.Origin + pivotBasis * VehicleBody.ToGodot(WeaponAim.Pivot);
        Vector3 forward = -pose.Basis.Z;
        // Retain heading for invalid, near-vertical or overturned orientations; never inherit chassis pitch or roll.
        float heading = pose.Basis.IsFinite() && pose.Basis.Y.Y > 0.15f && new Vector2(forward.X, forward.Z).LengthSquared() > 0.1f
            ? MathF.Atan2(-forward.X, -forward.Z) : _heading;
        if (reset)
        {
            _look.Reset();
            _boost.Reset();
            _aerial.Reset();
            _distanceScale = distanceScale;
            _streaks.Reset();
            InputSource?.ResetCameraMotion();
            _motion.Reset(state.ObservedPhysics.LinearVelocity);
            _motionTick = state.Movement.Tick;
            _airSeconds = state.Movement.Grounded ? 0 : state.Movement.Air.Seconds;
            _recoveringHeading = false;
            _anchor = pivot;
            _vehicle = state.VehicleId;
            _life = state.LifeId;
            _damageSequence = state.Damage.LastDamage?.Sequence ?? 0;
        }

        if (state.Damage.LastDamage is DamageEvent damage && damage.Sequence > _damageSequence)
        {
            _damageSequence = damage.Sequence;
            float strength = damage.Attribution.Source == "collision" ? CollisionShakeStrength : DamageShakeStrength;
            if (ShakeIntensity > 0)
            {
                _motion.Impulse(strength * MathF.Sqrt(Math.Clamp(damage.Amount / 50, 0, 1)));
            }
        }

        if (state.Movement.Tick > _motionTick)
        {
            float elapsed = (state.Movement.Tick - _motionTick) / (float)Engine.PhysicsTicksPerSecond;
            _motion.ObserveVelocity(state.ObservedPhysics.LinearVelocity, elapsed);
            // The gameplay air-control timer resets during a crash. Presentation still
            // needs framing for an unsupported tumble, without counting repeated renders.
            _airSeconds = state.Movement.Grounded ? 0 : Math.Min(60, _airSeconds + elapsed);
            _motionTick = state.Movement.Tick;
        }

        // Keep a stable launch heading through flips instead of adopting the reversed
        // projection halfway through a rotation. Normal supported driving remains immediate.
        float airborneSeconds = Math.Max(_airSeconds, state.Movement.Air.Seconds);
        bool flight = !state.Movement.Grounded && airborneSeconds > .12f;
        if (flight) _recoveringHeading = true;
        if (reset || !flight)
        {
            if (!reset && _recoveringHeading)
            {
                float difference = Mathf.AngleDifference(_heading, heading);
                float step = 4.5f * Math.Max(0, delta);
                // A bounded catch-up reaches a moving ground heading too; an exponential
                // tail could retain aerial framing indefinitely through a sustained turn.
                _heading += Math.Clamp(difference, -step, step);
                _recoveringHeading = Math.Abs(Mathf.AngleDifference(_heading, heading)) > .001f;
            }
            else _heading = heading;
        }
        // Keep room and the stable pivot while recovering a backward landing. Closing
        // the boom before yaw catches up would push the chassis toward the screen edge.
        _aerial.Advance(reset ? 0 : delta, state.Movement.Grounded && !_recoveringHeading,
            _recoveringHeading ? Math.Max(.13f, airborneSeconds) : airborneSeconds, (float)(preferences?.CameraAerialPullback ?? 1));
        _distanceScale = Mathf.Lerp(_distanceScale, distanceScale, ChaseCameraMotion.Blend(8, delta));
        // Rotation of the rack around the chassis must not swing the entire aerial view.
        Vector3 levelPivot = pose.Origin + Basis.FromEuler(new Vector3(0, _heading, 0)) * VehicleBody.ToGodot(WeaponAim.Pivot);
        pivot = pivot.Lerp(levelPivot, _aerial.Amount);
        if (state.CanInteract)
        {
            _boost.Advance(reset ? 0 : delta, state.Movement.Nitro.Active, state.Speed);
        }
        else { _boost.Reset(); }
        Fov = Math.Clamp(_baseFov + _boost.FovExpansion, 1, 110);
        if (ShakeIntensity == 0)
        {
            _motion.ClearShake();
        }
        _motion.Advance(delta, _heading, LongitudinalInertia * inertia, LateralInertia * inertia, SidewaysInertia * inertia, MaximumLongitudinalInertia * inertia, MaximumLateralInertia * inertia, PositionDamping, ShakeDecay);
        // Horizontal position follows the interpolated vehicle, with only bounded local inertia.
        // Vertical damping absorbs bumps; neither inertia nor shake changes the heading or aim.
        float vertical = Mathf.Lerp(_anchor.Y, pivot.Y, ChaseCameraMotion.Blend(PositionDamping, delta));
        float maximumVerticalLag = Mathf.Lerp(.18f, .06f, _aerial.Amount) * inertia;
        _anchor = new Vector3(pivot.X, Math.Clamp(vertical, pivot.Y - maximumVerticalLag, pivot.Y + maximumVerticalLag), pivot.Z);
        Vector3 backward = new(MathF.Sin(_heading), 0, MathF.Cos(_heading));
        Vector3 right = new(MathF.Cos(_heading), 0, -MathF.Sin(_heading));
        float distance = Math.Max(2, FollowDistance) * _distanceScale + _aerial.Pullback;
        float height = Math.Max(0, CameraHeight);
        float basePitch = -Mathf.DegToRad(Math.Clamp(ViewDownAngle, 0, 30));
        Vector2 mouse = InputSource?.ConsumeCameraMotion() ?? Vector2.Zero;
        Vector2 stick = InputSource is { CameraEnabled: true } source ? source.CameraIntent.LimitLength() : Vector2.Zero;
        if (!reset)
        {
            var friction = WeaponAiming ? AimFriction?.Invoke(mouse, stick, delta) ?? (1f, 1f) : (1f, 1f);
            _look.Advance(new(mouse.X, mouse.Y), InputSource?.MouseLookHeld == true, new(stick.X, stick.Y), delta, basePitch, WeaponAiming,
                friction.Item1 * (WeaponAiming ? (float)(preferences?.MouseAimSensitivity ?? 1) : 1),
                friction.Item2 * (WeaponAiming ? (float)(preferences?.StickAimSensitivity ?? 1) : 1), (float)(preferences?.StickAimCurve ?? 2));
        }

        GlobalBasis = Basis.FromEuler(new Vector3(basePitch + _look.Pitch, _heading + _look.Yaw, 0));
        Basis orbit = Basis.FromEuler(new Vector3(_look.Pitch, _heading + _look.Yaw, 0));
        System.Numerics.Vector2 shake = _motion.ShakeOffset * Math.Clamp(MaximumShakeMetres, 0, 0.65f) * ShakeIntensity;
        Vector3 intent = _anchor + orbit * new Vector3(0, height, distance) + GlobalBasis.Z * _boost.PullBack
            + backward * _motion.Offset.Y + right * _motion.Offset.X;
        Vector3 desired = intent + GlobalBasis.X * shake.X + GlobalBasis.Y * shake.Y;
        // Enclose the actual near-plane corners, including wide aspect ratios. Sweep after
        // inertia and shake so neither can place the rendered camera inside a world solid.
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float aspect = viewport.X / Math.Max(1, viewport.Y);
        float half = Near * MathF.Tan(Mathf.DegToRad(Fov) * 0.5f);
        float planeRadius = MathF.Sqrt(Near * Near + half * half * (1 + (KeepAspect == KeepAspectEnum.Height ? aspect * aspect : 1 / (aspect * aspect))));
        GlobalPosition = _obstruction.Resolve(GetWorld3D().DirectSpaceState, pivot, desired, intent, Math.Max(0.25f, planeRadius + 0.05f), followedBody, delta, reset, pose.Basis.Y.Y < .65f);
        if (_obstruction.Reframed)
        {
            LookAt(pivot, Vector3.Up);
        }
        else if (_obstruction.Lift > 0.001f)
        {
            // Only the cramped-view lift changes pitch, keeping the car framed below the
            // raised lens. Orbit intent and the normal chase basis remain untouched.
            Vector3 offset = GlobalPosition - pivot;
            float horizontal = new Vector2(offset.X, offset.Z).Length();
            float pitchCorrection = MathF.Atan2(offset.Y, horizontal) - MathF.Atan2(offset.Y - _obstruction.Lift, horizontal);
            GlobalBasis = GlobalBasis.Rotated(GlobalBasis.X, -pitchCorrection);
        }
        // Use the final view, including rollover framing, to avoid false forward
        // flow while looking sideways. Normal chase/Boost wisps are unchanged.
        Vector3 velocity = VehicleBody.ToGodot(state.ObservedPhysics.LinearVelocity);
        velocity.Y = 0;
        Vector3 viewForward = -GlobalBasis.Z;
        viewForward.Y = 0;
        float alignment = velocity.LengthSquared() > 1 ? Math.Clamp((velocity.Normalized().Dot(viewForward.Normalized()) - 0.5f) * 2, 0, 1) : 0;
        _streaks.Present(delta, state.Speed, _boost.StreakStrength * alignment, Current && state.CanInteract);
        _initialized = true;
    }
}
