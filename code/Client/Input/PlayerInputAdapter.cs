using Trackstorm.Core.Input;

namespace Trackstorm.Client.Input;

/// <summary>Converts physical binding strengths to conditioned Core frames. Call Observe after events and Capture once per tick.</summary>
internal sealed class PlayerInputAdapter
{
    private static readonly (InputAction Action, InputButtons Button)[] DigitalActions =
    [
        (InputAction.Drift, InputButtons.Drift),
        (InputAction.Brake, InputButtons.Brake),
        (InputAction.AirRoll, InputButtons.AirRoll),
        (InputAction.AirControl, InputButtons.AirControl),
        (InputAction.UseItem, InputButtons.UseItem),
        (InputAction.SwitchItem, InputButtons.SwitchItem),
        (InputAction.Leaderboard, InputButtons.Leaderboard),
        (InputAction.MenuUp, InputButtons.MenuUp),
        (InputAction.MenuDown, InputButtons.MenuDown),
        (InputAction.MenuLeft, InputButtons.MenuLeft),
        (InputAction.MenuRight, InputButtons.MenuRight),
        (InputAction.MenuAccept, InputButtons.MenuAccept),
        (InputAction.MenuCancel, InputButtons.MenuCancel),
        (InputAction.Pause, InputButtons.Pause),
    ];

    private readonly InputFrameCapture _capture = new();
    private float _deadZone = 0.15f;
    private float _throttle;
    private float _brake;
    private float _steering;
    private bool _itemNeedsRelease;
    private bool _switchNeedsRelease;
    private bool _suppressPendingSwitch;
    private bool _enabled = true;
    private bool _gameplaySuppressed;
    private bool _diagnosticSuppressed;
    private Godot.Vector2 _cameraMotion;

    /// <summary>Creates an adapter around the Client-owned mapping.</summary>
    /// <param name="bindings">The single local player's bindings.</param>
    public PlayerInputAdapter(PlayerInputBindings bindings)
    {
        Bindings = bindings;
    }

    /// <summary>Notifies the native input owner when focus or UI ownership changes.</summary>
    internal event Action? ControlStateChanged;

    /// <summary>Digital intent rates; shaping happens before frame recording.</summary>
    public DrivingInputShaping Shaping { get; set; } = new();

    /// <summary>Fixed capture interval matching the native physics scheduler.</summary>
    public float CaptureInterval { get; set; } = 1f / 60;

    /// <summary>Local remappable camera intent; camera behavior is owned by presentation.</summary>
    public Godot.Vector2 CameraIntent => Enabled && !GameplaySuppressed && !DiagnosticSuppressed ? new(Bindings.Strength(InputAction.CameraRight, Math.Max(0.15f, DeadZone)) - Bindings.Strength(InputAction.CameraLeft, Math.Max(0.15f, DeadZone)), Bindings.Strength(InputAction.CameraDown, Math.Max(0.15f, DeadZone)) - Bindings.Strength(InputAction.CameraUp, Math.Max(0.15f, DeadZone))) : Godot.Vector2.Zero;

    /// <summary>Runtime remapping entry point.</summary>
    public PlayerInputBindings Bindings { get; }

    /// <summary>Inverts signed steering after resolving bindings and before quantization.</summary>
    public bool InvertSteering { get; set; }

    private float _aerialSensitivity = 1;
    /// <summary>Independent controller gain for aerial pitch, yaw and roll.</summary>
    public float AerialSensitivity
    {
        get => _aerialSensitivity;
        set
        {
            if (!float.IsFinite(value) || value is < 0.1f or > 3) { throw new ArgumentOutOfRangeException(nameof(value)); }
            _aerialSensitivity = value;
        }
    }

    private float _steeringSensitivity = 1;
    /// <summary>Ground-only player controller gain after deadzone and precision shaping.
    /// Aerial input retains independent authority.</summary>
    public float SteeringSensitivity
    {
        get => _steeringSensitivity;
        set
        {
            if (!float.IsFinite(value) || value is < 0.1f or > 3) { throw new ArgumentOutOfRangeException(nameof(value)); }
            _steeringSensitivity = value;
        }
    }

    /// <summary>Analog dead zone; digital keys/buttons remain full strength.</summary>
    public float DeadZone
    {
        get => _deadZone;
        set
        {
            _ = InputAxis.Normalize(0, value);
            _deadZone = value;
        }
    }

    /// <summary>Suppresses capture while focus is lost; observing suppression releases held controls.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => SetControlState(ref _enabled, value);
    }

    /// <summary>Suppresses gameplay while settings are open, independently of application focus.</summary>
    public bool GameplaySuppressed
    {
        get => _gameplaySuppressed;
        set => SetControlState(ref _gameplaySuppressed, value);
    }

    /// <summary>Independent read-only diagnostic overlay input gate; does not alter menu ownership.</summary>
    internal bool DiagnosticSuppressed
    {
        get => _diagnosticSuppressed;
        set => SetControlState(ref _diagnosticSuppressed, value);
    }

    internal bool CameraAvailable { get; set; }
    internal bool CameraEnabled => CameraAvailable && Enabled && !GameplaySuppressed && !DiagnosticSuppressed;
    internal bool MouseLookHeld => CameraEnabled && Godot.Input.IsMouseButtonPressed(Godot.MouseButton.Right);

    internal void ObserveCamera(Godot.InputEvent input)
    {
        if (input is Godot.InputEventMouseMotion motion && MouseLookHeld)
        {
            _cameraMotion += motion.ScreenRelative;
        }
    }

    internal Godot.Vector2 ConsumeCameraMotion()
    {
        var motion = CameraEnabled ? _cameraMotion : Godot.Vector2.Zero;
        _cameraMotion = Godot.Vector2.Zero;
        return motion;
    }

    internal void ResetCameraMotion() => _cameraMotion = Godot.Vector2.Zero;

    /// <summary>Samples aggregate digital state; preserves press/release transitions until capture.</summary>
    public void Observe()
    {
        InputButtons held = InputButtons.None;
        if (!Enabled || GameplaySuppressed || DiagnosticSuppressed)
        {
            _itemNeedsRelease = true;
            _switchNeedsRelease = true;
        }

        if (Enabled && !GameplaySuppressed && !DiagnosticSuppressed)
        {
            foreach ((InputAction action, InputButtons button) in DigitalActions)
            {
                float strength = Bindings.Strength(action, DeadZone);
                if (action == InputAction.SwitchItem)
                {
                    // E is contextual yaw while the modifier is held. Require release
                    // before a held yaw key may become a weapon-switch press.
                    if (Bindings.Strength(InputAction.AirControl, DeadZone) > 0.5f &&
                        strength > Bindings.Strength(action, DeadZone, excluding: InputAction.AirYawRight))
                    {
                        _switchNeedsRelease = true;
                        _suppressPendingSwitch = true;
                    }
                    _switchNeedsRelease &= strength > 0.5f;
                    if (_switchNeedsRelease) { continue; }
                }
                if (action == InputAction.UseItem)
                {
                    _itemNeedsRelease &= strength > 0.5f;
                    if (_itemNeedsRelease)
                    {
                        continue;
                    }
                }

                if (strength > (action == InputAction.Brake ? 0 : 0.5f))
                {
                    held |= button;
                }
            }
        }

        _capture.Observe(held);
    }

    /// <summary>Produces one logical frame with no device data. Opposite steering bindings cancel.</summary>
    /// <param name="tick">Caller-owned simulation tick.</param>
    /// <returns>Conditioned and quantized logical input.</returns>
    public InputFrame Capture(ulong tick)
    {
        Observe();
        bool active = Enabled && !GameplaySuppressed && !DiagnosticSuppressed;
        if (!active)
        {
            _throttle = _brake = _steering = 0;
        }
        else
        {
            float throttle = Bindings.Strength(InputAction.Accelerate, DeadZone, false);
            float brake = Bindings.Strength(InputAction.Brake, DeadZone, false);
            float steering = Bindings.Strength(InputAction.SteerRight, DeadZone, false) - Bindings.Strength(InputAction.SteerLeft, DeadZone, false);
            _throttle = DrivingInputShaping.Approach(_throttle, throttle, throttle > _throttle ? Shaping.ThrottleRise : Shaping.ThrottleRelease, CaptureInterval);
            _brake = DrivingInputShaping.Approach(_brake, brake, brake > _brake ? Shaping.BrakeRise : Shaping.BrakeRelease, CaptureInterval);
            float steeringRate = steering == 0 ? Shaping.SteeringReturn : steering * _steering < 0 ? Shaping.SteeringReversal : Shaping.SteeringRise;
            _steering = DrivingInputShaping.Approach(_steering, steering, steeringRate, CaptureInterval);
        }

        float analogSteering = active ? Bindings.Strength(InputAction.SteerRight, DeadZone, true) - Bindings.Strength(InputAction.SteerLeft, DeadZone, true) : 0;
        analogSteering = Math.Clamp(Shaping.ShapeControllerSteering(analogSteering) *
            SteeringSensitivity, -1, 1);
        // A deliberate stick correction takes ownership after the keys are released;
        // a long digital return tail must not mask the controller's finer target.
        if (analogSteering != 0 && Bindings.Strength(InputAction.SteerRight, DeadZone, false) == 0 && Bindings.Strength(InputAction.SteerLeft, DeadZone, false) == 0)
        {
            _steering = 0;
        }
        float analogThrottle = active ? Bindings.Strength(InputAction.Accelerate, DeadZone, true) : 0;
        float analogBrake = active ? Bindings.Strength(InputAction.Brake, DeadZone, true) : 0;
        if (analogThrottle > 0 && Bindings.Strength(InputAction.Accelerate, DeadZone, false) == 0) { _throttle = 0; }
        if (analogBrake > 0 && Bindings.Strength(InputAction.Brake, DeadZone, false) == 0) { _brake = 0; }
        InputFrame frame = _capture.Capture(
            tick,
            InputAxis.QuantizeSteering(InputAxis.Normalize(Math.Abs(analogSteering) > Math.Abs(_steering) ? analogSteering : _steering, inverted: InvertSteering)),
            InputAxis.QuantizePedal(active ? Math.Max(_throttle, analogThrottle) : 0),
            InputAxis.QuantizePedal(active ? Math.Max(_brake, analogBrake) : 0),
            AirAxis(InputAction.AirPitchUp, InputAction.AirPitchDown, active),
            AirAxis(InputAction.AirYawRight, InputAction.AirYawLeft, active, yaw: true),
            AirAxis(InputAction.AirRollRight, InputAction.AirRollLeft, active, roll: true));
        if (_switchNeedsRelease || _suppressPendingSwitch)
        {
            // A direction pressed just before Shift within this tick must not leak
            // its accumulated weapon-switch edge into the aerial context.
            frame = new InputFrame(frame.Tick, frame.Steering, frame.Accelerate, frame.Brake,
                frame.Held, frame.Pressed & ~InputButtons.SwitchItem, frame.Released,
                frame.AirPitch, frame.AirYaw, frame.AirRoll);
        }
        _suppressPendingSwitch = false;
        return active ? frame : new InputFrame(tick, 0, 0, 0, InputButtons.None, InputButtons.None, frame.Released);
    }

    private short AirAxis(InputAction positive, InputAction negative, bool active, bool yaw = false, bool roll = false)
    {
        if (!active || Bindings.Strength(InputAction.AirControl, DeadZone) <= 0.5f) { return 0; }
        bool rollHeld = Bindings.Strength(InputAction.AirRoll, DeadZone) > 0.5f;
        float digital = Bindings.Strength(positive, DeadZone, false) - Bindings.Strength(negative, DeadZone, false);
        float analog = Bindings.Strength(positive, DeadZone, true) - Bindings.Strength(negative, DeadZone, true);
        if (yaw && rollHeld) { analog = 0; }
        if (roll && rollHeld)
        {
            analog += Bindings.Strength(InputAction.AirYawRight, DeadZone, true) - Bindings.Strength(InputAction.AirYawLeft, DeadZone, true);
        }
        analog = Math.Clamp(analog * AerialSensitivity, -1, 1);
        float intent = Math.Abs(digital) >= Math.Abs(analog) ? digital : analog;
        return InputAxis.QuantizeSteering(InputAxis.Normalize(intent, inverted: (yaw || roll) && InvertSteering));
    }

    private void SetControlState(ref bool state, bool value)
    {
        if (state != value)
        {
            state = value;
            ResetCameraMotion();
            ControlStateChanged?.Invoke();
        }
    }
}
