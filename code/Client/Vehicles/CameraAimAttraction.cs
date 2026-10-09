using System.Numerics;

namespace Trackstorm.Client.Vehicles;

/// <summary>Bounded local camera pull after acquisition; input can immediately break engagement.</summary>
internal static class CameraAimAttraction
{
    internal static Vector2 Motion(Vector2 previous, Vector2 current, float delta)
    {
        Vector2 motion = new(MathF.IEEERemainder(current.X - previous.X, MathF.Tau), current.Y - previous.Y);
        float maximum = Math.Max(0, delta) * MathF.PI;
        return motion.Length() > maximum ? Vector2.Normalize(motion) * maximum : motion;
    }

    internal static bool Breakaway(Vector2 error, Vector2 mouse, Vector2 stick, float delta) =>
        (mouse.Length() / Math.Max(.001f, delta) > 180 &&
            (Vector2.Dot(mouse, error) <= 0 || Vector2.Dot(mouse * .003f - error, mouse) > 0)) ||
        (stick.Length() > .65f && (Vector2.Dot(stick, error) <= 0 || error.Length() < .01f));

    internal static Vector2 Pull(Vector2 error, float degreesPerSecond, float delta)
    {
        if (delta <= 0 || degreesPerSecond <= 0) { return Vector2.Zero; }
        Vector2 correction = error * (1 - MathF.Exp(-8 * delta));
        float maximum = degreesPerSecond * MathF.PI / 180 * delta;
        return correction.Length() > maximum ? Vector2.Normalize(correction) * maximum : correction;
    }
}
