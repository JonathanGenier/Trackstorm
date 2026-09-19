namespace Trackstorm.Client.Development;

/// <summary>Presentation-only feedback projected from the existing configuration draft and Apply result.</summary>
internal enum DeveloperOptionsFeedbackState
{
    /// <summary>No compact footer feedback is active.</summary>
    None,
    /// <summary>The existing draft differs from effective configuration.</summary>
    Unsaved,
    /// <summary>The authoritative Apply operation completed successfully.</summary>
    Applied,
    /// <summary>Cancel restored the existing effective values.</summary>
    Discarded,
}
