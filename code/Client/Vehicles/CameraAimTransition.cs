namespace Trackstorm.Client.Vehicles;

/// <summary>Critically damped local framing blend; reversals retain continuous position and velocity.</summary>
internal sealed class CameraAimTransition
{
    private float _velocity;
    internal float Amount { get; private set; }
    internal void Reset() => (Amount, _velocity) = (0, 0);

    internal void Advance(bool active, float delta)
    {
        if (!float.IsFinite(delta) || delta <= 0) return;
        float target = active ? 1 : 0;
        float offset = Amount - target;
        float change = _velocity + 16 * offset;
        float decay = MathF.Exp(-16 * delta);
        Amount = Math.Clamp(target + (offset + change * delta) * decay, 0, 1);
        _velocity = (_velocity - 16 * change * delta) * decay;
    }
}
