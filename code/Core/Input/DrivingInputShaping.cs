namespace Trackstorm.Core.Input;

/// <summary>Progressive digital intent, applied before recording frames. Analog samples bypass this helper.</summary>
public sealed record DrivingInputShaping
{
    /// <summary>Throttle rise per second.</summary>
    public float ThrottleRise { get; init; } = 10;
    /// <summary>Throttle release per second.</summary>
    public float ThrottleRelease { get; init; } = 14;
    /// <summary>Brake rise per second.</summary>
    public float BrakeRise { get; init; } = 18;
    /// <summary>Steering rise per second.</summary>
    public float SteeringRise { get; init; } = 20;
    /// <summary>Steering return per second.</summary>
    public float SteeringReturn { get; init; } = 24;
    /// <summary>Steering reversal per second.</summary>
    public float SteeringReversal { get; init; } = 30;

    /// <summary>Moves toward bounded intent without overshoot; finite rates and a fixed timestep are required.</summary>
    /// <param name="current">Previous shaped intent.</param>
    /// <param name="target">New logical intent.</param>
    /// <param name="rate">Maximum change per second.</param>
    /// <param name="dt">Fixed capture interval.</param>
    /// <returns>Bounded progressive intent.</returns>
    public static float Approach(float current, float target, float rate, float dt)
    {
        if (!float.IsFinite(current) || !float.IsFinite(target) || !float.IsFinite(rate) || !float.IsFinite(dt) || rate <= 0 || dt <= 0 || dt > 1)
        {
            throw new ArgumentException("Input shaping requires finite values and positive rate/interval.");
        }

        target = Math.Clamp(target, -1, 1);
        return Math.Clamp(current + Math.Clamp(target - current, -rate * dt, rate * dt), -1, 1);
    }
}
