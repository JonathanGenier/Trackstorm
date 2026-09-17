namespace Trackstorm.Client.Audio;

/// <summary>Bounded per-emitter event gate; duplicate identities stay consumed after cooldown.</summary>
internal sealed class AudioCooldown
{
    private ulong _identity;
    private double _next;

    /// <summary>Consumes a new identity and allows playback only outside the cooldown.</summary>
    /// <param name="identity">Monotonic event identity.</param>
    /// <param name="seconds">Controllable presentation time in seconds.</param>
    /// <param name="cooldown">Minimum interval between audible events.</param>
    /// <returns>The selected presentation value.</returns>
    internal bool TryAccept(ulong identity, double seconds, double cooldown)
    {
        if (identity <= _identity || !double.IsFinite(seconds) || seconds < 0 || !double.IsFinite(cooldown) || cooldown < 0)
        {
            return false;
        }

        _identity = identity;
        if (seconds < _next)
        {
            return false;
        }

        _next = seconds + cooldown;
        return true;
    }

    /// <summary>Seeds the latest identity without replaying historical feedback.</summary>
    /// <param name="identity">Monotonic event identity.</param>
    internal void Seed(ulong identity)
    {
        _identity = identity;
        _next = 0;
    }
}
