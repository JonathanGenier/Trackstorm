namespace Trackstorm.Core.Matches;

/// <summary>Authoritative match lifecycle; Finished is terminal for this arena session.</summary>
public enum MatchPhase : byte
{
    /// <summary>Awaiting the configured participant count.</summary>
    Waiting,
    /// <summary>Counting fixed ticks before scoring opens.</summary>
    Countdown,
    /// <summary>Deaths can change scores.</summary>
    Active,
    /// <summary>One winner and immutable final scores.</summary>
    Finished,
}
