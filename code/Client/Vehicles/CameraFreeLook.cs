using System.Numerics;

namespace Trackstorm.Client.Vehicles;

/// <summary>Local orbit offsets; mouse displacement and stick velocity use separate time semantics.</summary>
internal sealed class CameraFreeLook
{
    internal float Yaw { get; private set; }
    internal float Pitch { get; private set; }

    internal void Reset() => (Yaw, Pitch) = (0, 0);

    internal void Advance(Vector2 mouse, bool held, Vector2 stick, float delta, float basePitch, bool weaponAiming = false, float mouseScale = 1, float stickScale = 1, float stickCurve = 2, float horizontalScale = 1, float verticalScale = 1, float recenterScale = 1)
    {
        if (!float.IsFinite(delta) || delta <= 0)
        {
            return;
        }

        // Mouse displacement was already RMB-gated at event time; retain a drag released between renders.
        if (held || stick != Vector2.Zero || mouse != Vector2.Zero)
        {
            Vector2 shapedStick = weaponAiming ? stick * MathF.Pow(stick.Length(), stickCurve - 1) : stick;
            Vector2 movement = mouse * (0.003f * mouseScale) + (shapedStick * (2.2f * delta * stickScale));
            movement *= new Vector2(horizontalScale, verticalScale);
            Yaw = MathF.IEEERemainder(Yaw - movement.X, MathF.Tau);
            Pitch = Math.Clamp(Pitch - movement.Y, -MathF.PI * 17 / 36 - basePitch, MathF.PI * 17 / 36 - basePitch);
        }
        else
        {
            float retention = MathF.Exp(-6 * recenterScale * delta);
            Yaw *= retention;
            Pitch *= retention;
            if (Math.Abs(Yaw) < 0.00001f && Math.Abs(Pitch) < 0.00001f)
            {
                Reset();
            }
        }
    }
}
