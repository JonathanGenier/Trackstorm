namespace Trackstorm.Core.Items;

/// <summary>Engine-independent item metadata. Presentation keys are resolved by Client adapters.</summary>
/// <param name="Identity">Stable inventory and checkpoint identity.</param>
/// <param name="Key">Stable behavior, tuning and event namespace.</param>
/// <param name="DisplayName">Player-facing name.</param>
/// <param name="DefaultWeight">Initial distribution weight.</param>
/// <param name="PresentationKey">Client HUD/use presentation lookup key.</param>
/// <param name="PickupAudio">Client pickup audio hook.</param>
/// <param name="UseAudio">Client use audio hook, absent until behavior is implemented.</param>
/// <param name="ImpactAudio">Optional impact audio hook.</param>
public sealed record ItemDefinition(HeldItem Identity, string Key, string DisplayName, int DefaultWeight,
    string PresentationKey, string PickupAudio, string? UseAudio, string? ImpactAudio)
{
    /// <summary>Combat-economy allocation shared with other items in this category.</summary>
    public required ItemCategory Category { get; init; }
    /// <summary>Whether this build implements authoritative use.</summary>
    public bool CanUse => Handler is not null || Sustained;

    /// <summary>Requires capability-bound held input across fixed steps.</summary>
    public bool Sustained { get; init; }

    /// <summary>Use-effect texture hook, resolved only after a committed outcome.</summary>
    public string? UseVfx { get; init; }

    /// <summary>Optional impact-effect texture hook.</summary>
    public string? ImpactVfx { get; init; }

    /// <summary>Continuous active-effect texture, reconstructed from authoritative vehicle state.</summary>
    public string? ActiveVfx { get; init; }

    internal IItemUseHandler? Handler { get; init; }
}
