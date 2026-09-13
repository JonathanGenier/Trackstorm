namespace Trackstorm.Core.Networking.Transport;

/// <summary>Current sampled diagnostics. Null means unavailable; quality is a fraction from zero to one.</summary>
public readonly record struct TransportStatistics(int? PingMilliseconds, float? IncomingQuality, float? OutgoingQuality)
{
    /// <summary>Gets estimated incoming packet loss, including late packets, as a fraction.</summary>
    public float? IncomingLoss => IncomingQuality is { } quality ? 1 - quality : null;

    /// <summary>Gets estimated outgoing packet loss, including late packets, as a fraction.</summary>
    public float? OutgoingLoss => OutgoingQuality is { } quality ? 1 - quality : null;

    /// <summary>Normalizes incomplete native samples without converting unknown values into zero loss.</summary>
    /// <param name="ping">Round-trip milliseconds, negative when unavailable.</param>
    /// <param name="incomingQuality">Incoming on-time delivery fraction, negative when unavailable.</param>
    /// <param name="outgoingQuality">Remote-reported on-time delivery fraction, negative when unavailable.</param>
    /// <returns>Normalized portable diagnostics.</returns>
    public static TransportStatistics FromSample(int ping, float incomingQuality, float outgoingQuality)
    {
        return new(ping >= 0 ? ping : null, Normalize(incomingQuality), Normalize(outgoingQuality));
    }

    private static float? Normalize(float value) => float.IsFinite(value) && value >= 0 && value <= 1 ? value : null;
}
