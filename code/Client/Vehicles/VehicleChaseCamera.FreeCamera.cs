using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

public sealed partial class VehicleChaseCamera
{
    private bool _freeCamera;
    private float _savedNear;
    private Vector3 _freeVelocity;

    private bool PresentFreeCamera(VehicleSnapshot state, float delta)
    {
        if (_initialized && (state.VehicleId != _vehicle || state.LifeId != _life || !state.CanInteract))
            InputSource?.EndFreeCamera();
        bool active = InputSource is { FreeCameraActive: true, CameraEnabled: true };
        if (!active)
        {
            if (_freeCamera) ResetFollow();
            return false;
        }
        if (!_initialized) { InputSource!.EndFreeCamera(); return false; }
        if (!_freeCamera)
        {
            _freeCamera = true;
            _savedNear = Near;
            Near = .025f;
            _freeVelocity = Vector3.Zero;
            AimReset?.Invoke();
            _lookFilter.Reset();
            _streaks.Hide();
        }
        WeaponAiming = false;
        var preferences = SettingsSource?.Current;
        float dt = Math.Clamp(delta, 0, .05f);
        Vector2 mouse = InputSource!.ConsumeCameraMotion();
        Vector2 stick = InputSource.CameraIntent.LimitLength();
        if (preferences?.InvertY == true) { mouse.Y = -mouse.Y; stick.Y = -stick.Y; }
        Vector2 gain = new((float)(preferences?.HorizontalLookSensitivity ?? 1), (float)(preferences?.VerticalLookSensitivity ?? 1));
        Vector2 look = (mouse * .003f + stick * (2.2f * (float)(preferences?.StickAimSensitivity ?? 1) * dt)) * gain;
        Vector3 angles = GlobalBasis.GetEuler();
        angles.X = Math.Clamp(angles.X - look.Y, -Mathf.DegToRad(89), Mathf.DegToRad(89));
        angles.Y = Mathf.Wrap(angles.Y - look.X, -Mathf.Pi, Mathf.Pi);
        angles.Z = 0;
        GlobalBasis = Basis.FromEuler(angles);
        Vector3 movement = InputSource.FreeCameraMovement;
        Vector3 desired = (GlobalBasis.X * movement.X + Vector3.Up * movement.Y + GlobalBasis.Z * movement.Z).LimitLength() * InputSource.FreeCameraSpeed;
        _freeVelocity = _freeVelocity.Lerp(desired, 1 - MathF.Exp(-18 * dt));
        if (desired == Vector3.Zero && _freeVelocity.LengthSquared() < .000001f) _freeVelocity = Vector3.Zero;
        GlobalPosition += _freeVelocity * dt;
        return true;
    }

    private void ResetFreeCamera()
    {
        InputSource?.EndFreeCamera();
        if (_freeCamera) Near = _savedNear;
        _freeCamera = false;
        _freeVelocity = Vector3.Zero;
    }
}
