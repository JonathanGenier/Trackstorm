namespace Trackstorm.Client.Statistics;

/// <summary>Ephemeral presentation refreshed from existing owners, never used to drive gameplay.</summary>
/// <param name="Players">Inspectable stable player/vehicle identities.</param>
/// <param name="Selected">Current selection or zero when none exists.</param>
/// <param name="Global">Session-wide categories.</param>
/// <param name="Player">Selected entity categories.</param>
internal sealed record StatisticView(IReadOnlyList<ulong> Players, ulong Selected, IReadOnlyList<StatisticSection> Global, IReadOnlyList<StatisticSection> Player);
