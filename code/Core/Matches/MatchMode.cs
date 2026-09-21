namespace Trackstorm.Core.Matches;

/// <summary>Match-scoped scoring policy; both modes use the configured kill-target completion rule.</summary>
public enum MatchMode : byte
{
    /// <summary>Existing kills/deaths and first-to-target behavior without Circus awards.</summary>
    FirstToTarget,
    /// <summary>Combat and stunt points alongside the configured match completion rule.</summary>
    Circus,
}
