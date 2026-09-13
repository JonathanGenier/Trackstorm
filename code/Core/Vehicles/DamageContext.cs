namespace Trackstorm.Core.Vehicles;

/// <summary>Serializable attribution supplied by the authority, without engine object references.</summary>
public sealed record DamageContext
{
    /// <summary>Constructs attribution for collision, missile, explosion or other damage.</summary>
    /// <param name="source">Stable source category.</param>
    /// <param name="instigatorId">Responsible vehicle/player identity; zero denotes the world.</param>
    /// <param name="context">Bounded effect or contact identifier for later attribution.</param>
    public DamageContext(string source, ulong instigatorId, string context)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > 64 || context is null || context.Length > 256)
        {
            throw new ArgumentException("Damage attribution must have a bounded source and context.");
        }

        Source = source;
        InstigatorId = instigatorId;
        Context = context;
    }

    /// <summary>Damage category, independent of item implementations.</summary>
    public string Source { get; }
    /// <summary>Responsible stable identity.</summary>
    public ulong InstigatorId { get; }
    /// <summary>Effect/contact identifier.</summary>
    public string Context { get; }
}
