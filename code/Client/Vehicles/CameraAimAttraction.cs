using System.Numerics;

namespace Trackstorm.Client.Vehicles;

/// <summary>Bounded local camera pull after acquisition; input can immediately break engagement.</summary>
internal static class CameraAimAttraction
{
    internal static float AcquisitionMargin(float apparentRadius, float cone) =>
        cone * (.5f + .5f * Math.Clamp(1 - apparentRadius / Math.Max(.0001f, cone * .5f), 0, 1));

    internal static Vector2 Motion(Vector2 previous, Vector2 current, float delta)
    {
        Vector2 motion = new(MathF.IEEERemainder(current.X - previous.X, MathF.Tau), current.Y - previous.Y);
        float maximum = Math.Max(0, delta) * MathF.PI;
        return motion.Length() > maximum ? Vector2.Normalize(motion) * maximum : motion;
    }

    // Integrate physical intent over a short window instead of dividing a single
    // mouse event by render delta. Vehicle/lens motion never enters this quantity.
    internal static Vector2 Gesture(Vector2 previous, Vector2 mouse, Vector2 stick, float delta) =>
        previous * MathF.Exp(-Math.Max(0, delta) / .12f) + mouse * .003f + stick * stick.Length() * (2.2f * Math.Max(0, delta));

    internal static bool Breakaway(Vector2 placement, Vector2 gesture, Vector2 movement, float cone) =>
        Vector2.Dot(movement, placement) < 0 &&
        (gesture.Length() > .045f || placement.Length() > cone * .65f);

    internal static Vector2 Pull(Vector2 error, float degreesPerSecond, float delta)
    {
        if (delta <= 0 || degreesPerSecond <= 0) { return Vector2.Zero; }
        Vector2 correction = error * (1 - MathF.Exp(-8 * delta));
        float maximum = degreesPerSecond * MathF.PI / 180 * delta;
        return correction.Length() > maximum ? Vector2.Normalize(correction) * maximum : correction;
    }
}
