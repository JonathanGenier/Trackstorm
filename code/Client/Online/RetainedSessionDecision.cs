namespace Trackstorm.Client.Online;

/// <summary>Menu progress for an authority-validated retained-session choice.</summary>
internal enum RetainedSessionDecision
{
    /// <summary>Normal session/browser flow.</summary>
    None,
    /// <summary>Resolving routing and asking current authority.</summary>
    Checking,
    /// <summary>Validated reservation awaiting a player choice.</summary>
    Choose,
    /// <summary>Existing resume/resync operation in progress.</summary>
    Reconnecting,
    /// <summary>Waiting for authoritative abandonment confirmation.</summary>
    Leaving,
    /// <summary>Recoverable failure; no release is claimed.</summary>
    Failed,
}
