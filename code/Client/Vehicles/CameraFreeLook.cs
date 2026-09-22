using System.Numerics;

namespace Trackstorm.Client.Vehicles;

/// <summary>Local orbit offsets; mouse displacement and stick velocity use separate time semantics.</summary>
internal sealed class CameraFreeLook
{
    internal float Yaw { get; private set; }
    internal float Pitch { get; private set; }

    internal void Reset() => (Yaw, Pitch) = (0, 0);

    internal void Advance(Vector2 mouse, bool held, Vector2 stick, float delta, float basePitch)
    {
        if (!float.IsFinite(delta) || delta <= 0)
        {
            return;
        }

        // Mouse displacement was already RMB-gated at event time; retain a drag released between renders.
        if (held || stick != Vector2.Zero || mouse != Vector2.Zero)
        {
            Vector2 movement = mouse * 0.003f + (stick * (2.2f * delta));
            Yaw = MathF.IEEERemainder(Yaw - movement.X, MathF.Tau);
            Pitch = Math.Clamp(Pitch - movement.Y, -MathF.PI * 5 / 12 - basePitch, -MathF.PI / 36 - basePitch);
        }
        else
        {
            float retention = MathF.Exp(-6 * delta);
            Yaw *= retention;
            Pitch *= retention;
            if (Math.Abs(Yaw) < 0.00001f && Math.Abs(Pitch) < 0.00001f)
            {
                Reset();
            }
        }
    }
}
