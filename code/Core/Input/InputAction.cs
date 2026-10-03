namespace Trackstorm.Core.Input;

/// <summary>Logical controls; directional actions resolve into the signed steering axis.</summary>
public enum InputAction
{
    /// <summary>Forward throttle.</summary>
    Accelerate,
    /// <summary>Brake or reverse, as decided by vehicle gameplay.</summary>
    Brake,
    /// <summary>Negative steering.</summary>
    SteerLeft,
    /// <summary>Positive steering.</summary>
    SteerRight,
    /// <summary>Physical handbrake; stable identifier retained for saved bindings.</summary>
    Drift,
    /// <summary>Use the equipped item.</summary>
    UseItem,
    /// <summary>Show leaderboard while held.</summary>
    Leaderboard,
    /// <summary>Navigate up.</summary>
    MenuUp,
    /// <summary>Navigate down.</summary>
    MenuDown,
    /// <summary>Navigate left.</summary>
    MenuLeft,
    /// <summary>Navigate right.</summary>
    MenuRight,
    /// <summary>Accept a menu choice.</summary>
    MenuAccept,
    /// <summary>Cancel a menu choice.</summary>
    MenuCancel,
    /// <summary>Request the pause menu.</summary>
    Pause,
    /// <summary>Local camera look left.</summary>
    CameraLeft,
    /// <summary>Local camera look right.</summary>
    CameraRight,
    /// <summary>Local camera look up.</summary>
    CameraUp,
    /// <summary>Local camera look down.</summary>
    CameraDown,
    /// <summary>Switch between the two held-item slots.</summary>
    SwitchItem,
    /// <summary>Hold to replace airborne yaw with roll.</summary>
    AirRoll,
    /// <summary>Hold to enable deliberate aerial input.</summary>
    AirControl,
    /// <summary>Nose down while air control is held.</summary>
    AirPitchDown,
    /// <summary>Nose up while air control is held.</summary>
    AirPitchUp,
    /// <summary>Aerial yaw left.</summary>
    AirYawLeft,
    /// <summary>Aerial yaw right.</summary>
    AirYawRight,
    /// <summary>Aerial roll left.</summary>
    AirRollLeft,
    /// <summary>Aerial roll right.</summary>
    AirRollRight,
}
