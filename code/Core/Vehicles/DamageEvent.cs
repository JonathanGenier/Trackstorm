namespace Trackstorm.Core.Vehicles;

/// <summary>Immutable serializable outcome; sequence is monotonic within one explicitly reset life.</summary>
public sealed record DamageEvent
{
    /// <summary>Creates a validated damage outcome.</summary>
    /// <param name="sequence">One-based damage sequence within the life.</param>
    /// <param name="tick">Authority fixed tick.</param>
    /// <param name="amount">Actual positive HP removed.</param>
    /// <param name="attribution">Original damage metadata.</param>
    /// <param name="destroyedTransition">Whether this event first reached zero HP.</param>
    public DamageEvent(ulong sequence, ulong tick, float amount, DamageContext attribution, bool destroyedTransition)
    {
        if (sequence == 0 || !float.IsFinite(amount) || amount <= 0)
        {
            throw new ArgumentException("Invalid damage event.");
        }

        ArgumentNullException.ThrowIfNull(attribution);
        Sequence = sequence;
        Tick = tick;
        Amount = amount;
        Attribution = attribution;
        DestroyedTransition = destroyedTransition;
    }

    /// <summary>Ordered outcome identity within this life.</summary>
    public ulong Sequence { get; }
    /// <summary>Authority tick when damage was applied.</summary>
    public ulong Tick { get; }
    /// <summary>Actual HP loss.</summary>
    public float Amount { get; }
    /// <summary>Attribution retained even after subsequent non-damaging contacts.</summary>
    public DamageContext Attribution { get; }
    /// <summary>One death candidate per life, for later kill processing.</summary>
    public bool DestroyedTransition { get; }
}
