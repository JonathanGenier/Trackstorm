using System.Numerics;

namespace Trackstorm.Client.Vehicles;

/// <summary>Local angular cursor magnetism; never rotates the camera or predicts a projectile intercept.</summary>
internal sealed class MagneticAimCursor
{
    private Vector2 _previousTarget;
    private Vector2 _intent;
    private float _age;
    internal Vector2 Offset { get; private set; }
    internal float Retention { get; private set; }
    internal bool Released { get; private set; }

    internal void Reset() { Offset = default; End(); }
    internal void End() { _age = 0; _intent = default; Retention = 0; Released = false; }
    internal void Begin(Vector2 target) { End(); _previousTarget = target; }

    internal void Advance(Vector2 target, Vector2 input, bool controller, float strength, float delta)
    {
        if (delta <= 0) return;
        _age += delta;
        // Opposing nudges cancel. Sustained directional travel progressively removes
        // resistance, even at low sensitivity; a reversal must earn a new gesture.
        // Stick velocity must eventually release even while moving toward a car
        // that shares the direction of the next target. Only initial acquisition
        // gets approach grace; the human-approved mouse classifier is unchanged.
        if ((!controller || _age < .18f) && Vector2.Dot(input, _previousTarget - Offset) > 0 && (_previousTarget - Offset).Length() > .005f)
            _intent *= MathF.Exp(-20 * delta);
        else
        {
            if (Vector2.Dot(input, _intent) < 0) _intent *= MathF.Exp(-20 * delta);
            _intent += input;
        }
        float threshold = controller ? .045f : .05f;
        float release = Math.Clamp((_intent.Length() - .008f) / threshold, 0, 1);
        release = release * release * (3 - 2 * release);
        Retention = (1 - MathF.Exp(-_age * (controller ? 12 : 8))) * (1 - release);
        Vector2 motion = new(MathF.IEEERemainder(target.X - _previousTarget.X, MathF.Tau), target.Y - _previousTarget.Y);
        _previousTarget = target;
        if (strength > 0)
        {
            // Camera-relative target movement includes camera input. Following only
            // the retained portion absorbs noise while deliberate input wins smoothly.
            Offset += motion * Retention;
            Offset += CameraAimAttraction.Pull(target - Offset, strength * (controller ? 1.5f : 1) * Retention, delta);
        }
        Released = release >= 1;
    }

    internal void Recover(float delta) => Offset *= MathF.Exp(-Math.Max(0, delta) * 4);

    // Only deliberate player travel can rearm a dismissed car. Target motion and
    // a single reverse nudge must not end a manually held lead between shots.
    internal static float ReturnTravel(float previous, Vector2 input, Vector2 toward, Vector2 departure, float delta)
    {
        if (Vector2.Dot(input, departure) < 0 && Vector2.Dot(input, toward) > 0)
            return previous + input.Length();
        return previous * MathF.Exp(-20 * Math.Max(0, delta));
    }
}
