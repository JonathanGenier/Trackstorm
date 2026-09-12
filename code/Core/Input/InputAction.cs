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
    /// <summary>Drift control.</summary>
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
}
