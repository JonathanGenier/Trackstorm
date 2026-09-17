namespace Trackstorm.Core.Events;

/// <summary>Detached immutable event. Numeric player IDs are session identities, never platform credentials.</summary>
public sealed record RuntimeEvent
{
    /// <summary>Monotonic stream identity; retained after replication.</summary>
    public ulong Sequence { get; init; }
    /// <summary>Original elapsed authority or local diagnostic time.</summary>
    public ulong Milliseconds { get; init; }
    /// <summary>Stable system used for filtering.</summary>
    public EventCategory Category { get; init; }
    /// <summary>Stable outcome type independent of wording.</summary>
    public string Kind { get; init; } = string.Empty;
    /// <summary>Responsible session player; zero denotes no player.</summary>
    public ulong Actor { get; init; }
    /// <summary>Affected session player; zero denotes no player.</summary>
    public ulong Target { get; init; }
    /// <summary>Sanitized actor name captured at publication.</summary>
    public string ActorName { get; init; } = string.Empty;
    /// <summary>Sanitized target name captured at publication.</summary>
    public string TargetName { get; init; } = string.Empty;
    /// <summary>Allowlisted gameplay cause, item or safe diagnostic reason.</summary>
    public string Cause { get; init; } = string.Empty;
    /// <summary>Allowlisted mechanic, marker, or setting key.</summary>
    public string Context { get; init; } = string.Empty;
    /// <summary>Exact applied damage, healing, or new numeric value.</summary>
    public double? Amount { get; init; }
    /// <summary>Health immediately following this application.</summary>
    public double? RemainingHP { get; init; }
    /// <summary>Health capacity at application time.</summary>
    public double? MaximumHP { get; init; }
    /// <summary>Previous numeric setting value.</summary>
    public double? PreviousValue { get; init; }
    /// <summary>Affected vehicle life generation.</summary>
    public ulong Life { get; init; }
    /// <summary>Authoritative gameplay tick when applicable.</summary>
    public ulong Tick { get; init; }
    /// <summary>True only for a local diagnostic, never a replicated outcome.</summary>
    public bool Local { get; init; }

    /// <summary>Rejects malformed wire data before it can enter a buffer or advance a watermark.</summary>
    public void Validate()
    {
        if ((Category is EventCategory.Damage or EventCategory.Healing && (Target == 0 || !Amount.HasValue || Amount <= 0)) ||
            (RemainingHP.HasValue && (RemainingHP < 0 || (MaximumHP.HasValue && RemainingHP > MaximumHP))) ||
            Sequence == 0 || !Enum.IsDefined(Category) || string.IsNullOrWhiteSpace(Kind) || Kind.Length > 64 ||
            ActorName is null || TargetName is null || Cause is null || Context is null ||
            ActorName.Length > 64 || TargetName.Length > 64 || Cause.Length > 64 || Context.Length > 128 ||
            new[] { Amount, RemainingHP, MaximumHP, PreviousValue }.Any(value => value.HasValue && !double.IsFinite(value.Value)))
        {
            throw new ArgumentException("Invalid runtime event.");
        }
    }
}
