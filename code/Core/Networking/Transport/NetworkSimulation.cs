namespace Trackstorm.Core.Networking.Transport;

/// <summary>Validated outbound packet simulation settings, applied process-wide by native transports.</summary>
public sealed record NetworkSimulation
{
    /// <summary>Initializes bounded latency, average jitter, loss and reorder settings. Non-finite input is rejected.</summary>
    /// <param name="latencyMilliseconds">Added outbound delay, clamped to 0–5000 ms.</param>
    /// <param name="jitterMilliseconds">Average jitter, clamped to 0–1000 ms.</param>
    /// <param name="lossPercent">Loss percentage, clamped to 0–100.</param>
    /// <param name="reorderPercent">Reorder percentage, clamped to 0–100.</param>
    /// <param name="reorderMilliseconds">Extra reorder delay, clamped to 0–5000 ms.</param>
    public NetworkSimulation(int latencyMilliseconds = 0, int jitterMilliseconds = 0, float lossPercent = 0, float reorderPercent = 0, int reorderMilliseconds = 0)
    {
        if (!float.IsFinite(lossPercent) || !float.IsFinite(reorderPercent))
        {
            throw new ArgumentOutOfRangeException(nameof(lossPercent), "Simulation percentages must be finite.");
        }

        LatencyMilliseconds = Math.Clamp(latencyMilliseconds, 0, 5000);
        JitterMilliseconds = Math.Clamp(jitterMilliseconds, 0, 1000);
        LossPercent = Math.Clamp(lossPercent, 0, 100);
        ReorderPercent = Math.Clamp(reorderPercent, 0, 100);
        ReorderMilliseconds = Math.Clamp(reorderMilliseconds, 0, 5000);
    }

    /// <summary>Gets added one-way outbound latency.</summary>
    public int LatencyMilliseconds { get; }
    /// <summary>Gets average outbound jitter, capped natively at twice this value.</summary>
    public int JitterMilliseconds { get; }
    /// <summary>Gets the percentage of outbound packets discarded.</summary>
    public float LossPercent { get; }
    /// <summary>Gets the percentage of outbound packets delayed for reordering.</summary>
    public float ReorderPercent { get; }
    /// <summary>Gets additional delay for selected reordered packets.</summary>
    public int ReorderMilliseconds { get; }
}
