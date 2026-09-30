namespace Trackstorm.Core.Input;

/// <summary>Progressive digital intent, applied before recording frames. Analog steering uses a separate precision curve.</summary>
public sealed record DrivingInputShaping
{
    /// <summary>Approved airborne digital authority; ground precision must not slow aerial commands.</summary>
    public static DrivingInputShaping Aerial { get; } = new() { ThrottleRise = 10, ThrottleRelease = 14, BrakeRise = 18, BrakeRelease = 18, SteeringRise = 20, SteeringReturn = 24, SteeringReversal = 30, ControllerSteeringExponent = 1 };

    /// <summary>Rejects invalid digital response rates before configuration publication.</summary>
    public void Validate()
    {
        if (!float.IsFinite(ControllerSteeringExponent) || ControllerSteeringExponent is < 1 or > 4)
        {
            throw new ArgumentException("Controller steering exponent must be finite and between 1 and 4.");
        }
        if (new[] { ThrottleRise, ThrottleRelease, BrakeRise, BrakeRelease, SteeringRise, SteeringReturn, SteeringReversal }.Any(value => !float.IsFinite(value) || value is < 0.1f or > 60))
        {
            throw new ArgumentException("Input response rates must be finite and between 0.1 and 60 per second.");
        }
    }

    /// <summary>Ground stick precision exponent: one is linear, larger values soften small corrections without reducing full lock.</summary>
    public float ControllerSteeringExponent { get; init; } = 3.5f;

    /// <summary>Shapes a normalized analog steering sample while preserving sign, center and full articulation.</summary>
    /// <param name="value">Dead-zone-conditioned signed stick sample.</param>
    /// <returns>Continuous steering intent.</returns>
    public float ShapeControllerSteering(float value) => MathF.CopySign(MathF.Pow(Math.Clamp(Math.Abs(value), 0, 1), ControllerSteeringExponent), value);

    /// <summary>Throttle rise per second.</summary>
    public float ThrottleRise { get; init; } = 2.5f;
    /// <summary>Throttle release per second.</summary>
    public float ThrottleRelease { get; init; } = 4;
    /// <summary>Brake rise per second.</summary>
    public float BrakeRise { get; init; } = 3;
    /// <summary>Brake release per second.</summary>
    public float BrakeRelease { get; init; } = 10;
    /// <summary>Steering rise per second.</summary>
    public float SteeringRise { get; init; } = 0.45f;
    /// <summary>Steering return per second.</summary>
    public float SteeringReturn { get; init; } = 0.8f;
    /// <summary>Steering reversal per second.</summary>
    public float SteeringReversal { get; init; } = 1f;

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
