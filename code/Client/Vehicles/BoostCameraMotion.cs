namespace Trackstorm.Client.Vehicles;

/// <summary>Local speed sensation from measured road speed and the existing active Nitro state.</summary>
internal sealed class BoostCameraMotion
{
    private float _boost;
    private float _speed;

    internal float FovExpansion => 3 * _speed + 13 * _boost;
    internal float PullBack => 1.15f * _boost;
    internal float StreakStrength => _speed * (0.12f + 0.88f * _boost);

    internal void Advance(float seconds, bool active, float roadSpeed)
    {
        // Absolute observed m/s avoids coupling presentation to host handling/cap tuning.
        float speed = Math.Clamp((roadSpeed - 12) / 48, 0, 1);
        speed *= speed * (3 - 2 * speed);
        float target = active ? 0.45f + 0.55f * speed : 0;
        _boost += (target - _boost) * ChaseCameraMotion.Blend(target > _boost ? 7 : 3.5f, seconds);
        _speed += (speed - _speed) * ChaseCameraMotion.Blend(5, seconds);
    }

    internal void Reset() => _boost = _speed = 0;
}
