namespace Trackstorm.Client.Vehicles;

/// <summary>Deterministic presentation math, driven exclusively by caller-supplied render time.</summary>
internal sealed class ChaseCameraMotion
{
    private float _manual;
    private float _idle;
    private float _phase;
    private float _collisionCooldown;

    /// <summary>Smoothed steering anticipation in degrees.</summary>
    internal float SteeringAngle { get; private set; }
    /// <summary>Final clamped horizontal offset in degrees.</summary>
    internal float LookAngle { get; private set; }
    /// <summary>Bounded nonnegative feedback envelope.</summary>
    internal float Shake { get; private set; }
    /// <summary>Zero-centred deterministic oscillation scaled by the envelope.</summary>
    internal float ShakeOffset => MathF.Sin(_phase * 43) * Shake;

    /// <summary>Frame-rate independent exponential interpolation weight.</summary>
    /// <param name="rate">Convergence rate per second.</param>
    /// <param name="delta">Elapsed seconds.</param>
    /// <returns>Interpolation weight in [0, 1].</returns>
    internal static float Blend(float rate, float delta) => 1 - MathF.Exp(-Math.Max(0, rate) * Math.Max(0, delta));

    /// <summary>Advances presentation controls without simulation state.</summary>
    /// <param name="delta">Elapsed seconds.</param>
    /// <param name="steering">Normalized steering intent.</param>
    /// <param name="mouseDegrees">Mouse displacement in degrees.</param>
    /// <param name="stickDegreesPerSecond">Signed analog look rate.</param>
    /// <param name="steeringMaximum">Steering limit in degrees.</param>
    /// <param name="maximum">Combined angle limit in degrees.</param>
    /// <param name="rotationDamping">Look convergence rate.</param>
    /// <param name="recenterDelay">Idle seconds before recentering.</param>
    /// <param name="recenterDamping">Recenter convergence rate.</param>
    /// <param name="shakeDecay">Feedback decay rate.</param>
    internal void Advance(float delta, float steering, float mouseDegrees, float stickDegreesPerSecond, float steeringMaximum, float maximum, float rotationDamping, float recenterDelay, float recenterDamping, float shakeDecay)
    {
        delta = Math.Max(0, delta);
        maximum = Math.Max(0, maximum);
        steeringMaximum = Math.Max(0, steeringMaximum);
        SteeringAngle += ((Math.Clamp(steering, -1, 1) * steeringMaximum) - SteeringAngle) * Blend(rotationDamping, delta);
        SteeringAngle = Math.Clamp(SteeringAngle, -steeringMaximum, steeringMaximum);
        bool manual = mouseDegrees != 0 || stickDegreesPerSecond != 0;
        _idle = manual ? 0 : _idle + delta;
        _manual = Math.Clamp(_manual + mouseDegrees + (stickDegreesPerSecond * delta), -maximum, maximum);
        if (!manual && _idle > recenterDelay)
        {
            _manual *= 1 - Blend(recenterDamping, Math.Min(delta, _idle - recenterDelay));
        }

        float target = Math.Clamp(SteeringAngle + _manual, -maximum, maximum);
        LookAngle += (target - LookAngle) * Blend(rotationDamping, delta);
        LookAngle = Math.Clamp(LookAngle, -maximum, maximum);
        Shake *= 1 - Blend(shakeDecay, delta);
        if (Shake < 0.0001f)
        {
            Shake = 0;
            _phase = 0;
        }
        else
        {
            _phase += delta;
        }

        _collisionCooldown = Math.Max(0, _collisionCooldown - delta);
    }

    /// <summary>Coalesces impulses without unbounded accumulation.</summary>
    /// <param name="strength">Normalized feedback gain.</param>
    internal void Impulse(float strength) => Shake = Math.Clamp(Math.Max(Shake, strength), 0, 1);

    /// <summary>Rejects brushes and coalesces repeated contacts in render time.</summary>
    /// <param name="severity">Normal closing speed or mass-normalized impulse.</param>
    /// <param name="threshold">Minimum meaningful severity.</param>
    /// <param name="strength">Normalized feedback gain.</param>
    internal void Collision(float severity, float threshold, float strength)
    {
        if (severity <= threshold || _collisionCooldown > 0)
        {
            return;
        }

        Impulse(Math.Clamp((severity - threshold) / 20, 0, 1) * strength);
        _collisionCooldown = 0.15f;
    }

    /// <summary>Clears all presentation memory on a new vehicle life.</summary>
    internal void Reset()
    {
        _manual = _idle = _phase = _collisionCooldown = 0;
        SteeringAngle = LookAngle = Shake = 0;
    }
}
