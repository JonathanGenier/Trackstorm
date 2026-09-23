namespace Trackstorm.Core.Items;

/// <summary>Explicit SplitMix64 stream; its complete continuation is one unsigned word.</summary>
internal sealed class ItemRandom
{
    /// <summary>Starts at the supplied complete stream state.</summary>
    /// <param name="state">Saved state or initial seed.</param>
    internal ItemRandom(ulong state) => State = state;

    /// <summary>Complete portable stream continuation.</summary>
    internal ulong State { get; private set; }

    /// <summary>Advances exactly once for an accepted selection.</summary>
    /// <param name="configuration">Validated pool weights.</param>
    /// <returns>Selected supported item.</returns>
    internal HeldItem Next(ItemSpawnConfiguration configuration)
    {
        ulong value;
        unchecked
        {
            State += 0x9E3779B97F4A7C15UL;
            value = State;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
        }

        ulong draw = value % (ulong)configuration.Weights.Values.Sum(weight => (long)weight);
        foreach (var definition in ItemRegistry.All)
        {
            ulong weight = (ulong)configuration.Weights[definition.Identity];
            if (draw < weight)
            {
                return definition.Identity;
            }

            draw -= weight;
        }

        throw new InvalidOperationException("Validated item distribution has no selection.");
    }
}
