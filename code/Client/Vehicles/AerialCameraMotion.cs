namespace Trackstorm.Client.Vehicles;

/// <summary>Local framing envelope; brief support gaps do not request a wider view.</summary>
internal sealed class AerialCameraMotion
{
    internal float Amount { get; private set; }
    internal float Pullback { get; private set; }

    internal void Advance(float delta, bool grounded, float airborneSeconds, float strength)
    {
        float target = !grounded && airborneSeconds > .12f ? 1 : 0;
        Amount += (target - Amount) * ChaseCameraMotion.Blend(target > Amount ? 5 : 6, delta);
        Pullback += (3.6f * Amount * strength - Pullback) * ChaseCameraMotion.Blend(10, delta);
    }

    internal void Reset() => Amount = Pullback = 0;
}
