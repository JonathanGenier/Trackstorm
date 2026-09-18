namespace Trackstorm.Core.Items;

/// <summary>Immutable host pickup tuning. Both supported items remain eligible.</summary>
public sealed record ItemSpawnConfiguration
{
    /// <summary>Ten seconds at the authoritative 60 Hz clock.</summary>
    public int CooldownTicks { get; init; } = 600;
    /// <summary>Maximum three-dimensional distance from vehicle center to marker.</summary>
    public float PickupRadius { get; init; } = 3;
    /// <summary>Relative repair probability.</summary>
    public int WrenchWeight { get; init; } = 1;
    /// <summary>Relative damaging-item probability; must remain positive.</summary>
    public int MissileWeight { get; init; } = 1;
    /// <summary>Reproducible match selection seed.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Rejects invalid timing, range and item pools before play.</summary>
    public void Validate()
    {
        if (CooldownTicks is < 1 or > 216000 || !float.IsFinite(PickupRadius) || PickupRadius is <= 0 or > 10 ||
            WrenchWeight <= 0 || MissileWeight <= 0 || (long)WrenchWeight + MissileWeight > int.MaxValue)
        {
            throw new ArgumentException("Pickup tuning requires positive bounded cooldown, radius and both item weights.");
        }
    }

    /// <summary>Creates an independent seeded weighted selection stream.</summary>
    /// <returns>A host-only selector.</returns>
    public Func<HeldItem> CreateSelector()
    {
        Validate();
        var random = new ItemRandom(unchecked((ulong)Seed));
        return () => random.Next(this);
    }
}
