namespace Trackstorm.Client.Vehicles;

/// <summary>Local framing envelope; brief support gaps do not request a wider view.</summary>
internal sealed class AerialCameraMotion
{
    internal float Amount { get; private set; }
    internal float Pullback { get; private set; }

    internal void Advance(float delta, bool grounded, float airborneSeconds, float strength, bool recoveringHeading = false)
    {
        // Stabilize rotation early without asking every small hop for full distance.
        float target = !grounded && airborneSeconds > .12f || recoveringHeading ? 1 : 0;
        Amount += (target - Amount) * ChaseCameraMotion.Blend(target > Amount ? 5 : 6, delta);
        float progress = Math.Clamp(airborneSeconds - .35f, 0, 1);
        float distance = 3.6f * strength * progress * progress * (3 - 2 * progress);
        // Landing stops expansion immediately. A backward landing retains only the
        // room already earned in flight while the heading catches up.
        if (grounded) distance = recoveringHeading ? Math.Min(Pullback, 3.6f * strength) : 0;
        Pullback += (distance - Pullback) * ChaseCameraMotion.Blend(distance > Pullback ? 4 : 6, delta);
    }

    internal void Reset() => Amount = Pullback = 0;
}
