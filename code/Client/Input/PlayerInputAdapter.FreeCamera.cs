using Godot;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Input;

internal sealed partial class PlayerInputAdapter
{
    private bool _photoBoundary;
    private bool _freeToggleHeld;
    private bool _resumeNeedsRelease;
    internal bool FreeCameraActive { get; private set; }

    internal Vector3 FreeCameraMovement => CameraEnabled && FreeCameraActive
        ? new Vector3(Strength(InputAction.SteerRight) - Strength(InputAction.SteerLeft),
            Strength(InputAction.CameraRise) - Strength(InputAction.CameraLower),
            Strength(InputAction.CameraBackward) - Strength(InputAction.CameraForward)).LimitLength()
        : Vector3.Zero;
    internal float FreeCameraSpeed => Strength(InputAction.CameraSlow) > .5f ? .6f : Strength(InputAction.CameraFast) > .5f ? 40 : 6;
    private float Strength(InputAction action) => Bindings.Strength(action, Math.Max(.15f, DeadZone));

    private void ObserveFreeCamera()
    {
        bool held = Strength(InputAction.FreeCamera) > .5f;
        if (CameraEnabled && held && !_freeToggleHeld)
        {
            if (FreeCameraActive) EndFreeCamera();
            else
            {
                FreeCameraActive = true;
                _photoBoundary = true;
                _resumeNeedsRelease = true;
                _throttle = _brake = _steering = 0;
                ResetCameraEngagement();
                ResetCameraMotion();
                CameraAimRevision++;
            }
        }
        _freeToggleHeld = held;
        if (!FreeCameraActive && _resumeNeedsRelease)
        {
            _resumeNeedsRelease = new[] { InputAction.Accelerate, InputAction.Brake, InputAction.SteerLeft,
                InputAction.SteerRight, InputAction.UseItem, InputAction.SwitchItem, InputAction.DiscardItem,
                InputAction.Drift, InputAction.AirRoll, InputAction.CameraForward, InputAction.CameraBackward,
                InputAction.CameraRise, InputAction.CameraLower, InputAction.CameraLeft, InputAction.CameraRight,
                InputAction.CameraUp, InputAction.CameraDown, InputAction.AirPitchUp, InputAction.AirPitchDown,
                InputAction.AirYawLeft, InputAction.AirYawRight, InputAction.AirRollLeft, InputAction.AirRollRight }.Any(action => Strength(action) > 0)
                || Godot.Input.IsMouseButtonPressed(MouseButton.Right);
        }
    }

    internal void EndFreeCamera()
    {
        if (!FreeCameraActive) return;
        FreeCameraActive = false;
        _photoBoundary = true;
        _resumeNeedsRelease = true;
        _itemNeedsRelease = _switchNeedsRelease = _discardNeedsRelease = true;
        _throttle = _brake = _steering = 0;
        ResetCameraEngagement();
        ResetCameraMotion();
        CameraAimRevision++;
    }
}
