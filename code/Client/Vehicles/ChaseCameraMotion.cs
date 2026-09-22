using System.Numerics;

namespace Trackstorm.Client.Vehicles;

/// <summary>Deterministic presentation math, driven exclusively by caller-supplied render time.</summary>
internal sealed class ChaseCameraMotion
{
    private const float CollisionCooldownSeconds = 0.15f;
    private const float FullCollisionSpeed = 20;
    private const float ShakeFrequency = 43;
    private Vector3 _acceleration;
    private Vector3 _velocity;
    private float _phase;
    private float _collisionCooldown;

    /// <summary>Local lateral (X) and rearward (Y) displacement in metres.</summary>
    internal Vector2 Offset { get; private set; }
    /// <summary>Bounded nonnegative feedback envelope.</summary>
    internal float Shake { get; private set; }
    /// <summary>Bounded view-plane displacement; differing frequencies avoid a repetitive vertical bob.</summary>
    internal Vector2 ShakeOffset
    {
        get
        {
            var wave = new Vector2(0.6f * MathF.Sin(_phase * 31), MathF.Sin(_phase * ShakeFrequency));
            return wave / Math.Max(1, wave.Length()) * Shake;
        }
    }

    /// <summary>Frame-rate independent exponential interpolation weight.</summary>
    /// <param name="rate">Convergence rate per second.</param>
    /// <param name="delta">Elapsed seconds.</param>
    /// <returns>Interpolation weight in [0, 1].</returns>
    internal static float Blend(float rate, float delta) => 1 - MathF.Exp(-Math.Max(0, rate) * Math.Max(0, delta));

    /// <summary>Samples measured motion once per new physics snapshot, independent of render rate.</summary>
    /// <param name="velocity">Observed world velocity in metres per second.</param>
    /// <param name="seconds">Elapsed physics time between samples.</param>
    internal void ObserveVelocity(Vector3 velocity, float seconds)
    {
        if (seconds > 0)
        {
            _acceleration = (velocity - _velocity) / seconds;
            _velocity = velocity;
        }
    }

    /// <summary>Advances bounded positional inertia and feedback in presentation time.</summary>
    /// <param name="delta">Elapsed render seconds.</param>
    /// <param name="heading">Displayed vehicle heading in radians, or the last usable fallback.</param>
    /// <param name="longitudinalGain">Rearward metres per forward acceleration.</param>
    /// <param name="lateralGain">Outside-turn metres per lateral acceleration.</param>
    /// <param name="slipGain">Metres per sideways speed.</param>
    /// <param name="maximumLongitudinal">Maximum absolute fore/aft displacement.</param>
    /// <param name="maximumLateral">Maximum absolute sideways displacement.</param>
    /// <param name="damping">Offset convergence rate per second.</param>
    /// <param name="shakeDecay">Feedback envelope decay rate.</param>
    internal void Advance(float delta, float heading, float longitudinalGain, float lateralGain, float slipGain, float maximumLongitudinal, float maximumLateral, float damping, float shakeDecay)
    {
        delta = Math.Max(0, delta);
        var right = new Vector3(MathF.Cos(heading), 0, -MathF.Sin(heading));
        var backward = new Vector3(MathF.Sin(heading), 0, MathF.Cos(heading));
        float rearward = -Vector3.Dot(_acceleration, backward) * longitudinalGain;
        float lateral = -(Vector3.Dot(_acceleration, right) * lateralGain) - (Vector3.Dot(_velocity, right) * slipGain);
        var target = new Vector2(Math.Clamp(lateral, -maximumLateral, maximumLateral), Math.Clamp(rearward, -maximumLongitudinal, maximumLongitudinal));
        Offset = Vector2.Lerp(Offset, target, Blend(damping, delta));
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

    /// <summary>Disables pending feedback without resetting inertia or contact cooldown.</summary>
    internal void ClearShake() => _phase = Shake = 0;

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

        // A soft response curve makes useful mid-severity impacts readable at chase distance.
        Impulse(MathF.Sqrt(Math.Clamp((severity - threshold) / FullCollisionSpeed, 0, 1)) * strength);
        _collisionCooldown = CollisionCooldownSeconds;
    }

    /// <summary>Clears all presentation memory on a new vehicle life.</summary>
    /// <param name="velocity">Initial measured velocity, preventing a spawn impulse.</param>
    internal void Reset(Vector3 velocity = default)
    {
        _phase = _collisionCooldown = Shake = 0;
        _velocity = velocity;
        _acceleration = Vector3.Zero;
        Offset = Vector2.Zero;
    }
}
