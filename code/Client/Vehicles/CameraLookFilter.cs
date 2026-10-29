using System.Numerics;

namespace Trackstorm.Client.Vehicles;

/// <summary>Short render-time input response, independent of physics ticks and wrapped orbit angles.</summary>
internal sealed class CameraLookFilter
{
    private Vector2 _mouseRemainder;
    private Vector2 _stickVelocity;

    internal void Reset() => (_mouseRemainder, _stickVelocity) = (Vector2.Zero, Vector2.Zero);

    internal Vector2 Advance(Vector2 mouseMovement, Vector2 stickVelocity, float delta, bool held)
    {
        if (!float.IsFinite(delta) || delta <= 0) return Vector2.Zero;
        // Preserve total mouse displacement, with an 11 ms response to uneven event packets.
        // Releasing ownership consumes the final drag and discards any deferred motion.
        _mouseRemainder += mouseMovement;
        Vector2 mouse = _mouseRemainder * (held ? 1 - MathF.Exp(-90 * delta) : 1);
        _mouseRemainder -= mouse;
        // Integrate the exponential velocity response analytically: the result is stable
        // across render rates and starts moving on the first frame, without angle lerping.
        Vector2 stick = stickVelocity * delta + (_stickVelocity - stickVelocity) * ((1 - MathF.Exp(-60 * delta)) / 60);
        _stickVelocity = Vector2.Lerp(_stickVelocity, stickVelocity, 1 - MathF.Exp(-60 * delta));
        if (stickVelocity == Vector2.Zero) { _stickVelocity = Vector2.Zero; stick = Vector2.Zero; }
        return mouse + stick;
    }
}
