namespace Trackstorm.Core.Matches;

/// <summary>Reusable lifecycle after Application Flow has completed loading and synchronization.</summary>
public enum GameLoopPhase : byte
{
    /// <summary>Consumes the completed loading handoff; gameplay is disabled.</summary>
    Initialization,
    /// <summary>Waits for the authoritative fixed-tick deadline.</summary>
    Countdown,
    /// <summary>Normal gameplay participation is enabled.</summary>
    Active,
    /// <summary>The game mode's final outcome is immutable.</summary>
    Finished,
}
