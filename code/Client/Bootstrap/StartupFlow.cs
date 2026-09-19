namespace Trackstorm.Client.Bootstrap;

/// <summary>Validates deterministic startup transitions independently of Godot presentation.</summary>
internal sealed class StartupFlow
{
    /// <summary>Current validated startup state.</summary>
    internal StartupStage Stage { get; private set; } = StartupStage.Preloader;

    /// <summary>Phase that owns the current recoverable failure.</summary>
    internal StartupFailurePhase? FailurePhase { get; private set; }

    /// <summary>Advances from minimal preloading to the splash.</summary>
    internal void ShowSplash() => Transition(StartupStage.Preloader, StartupStage.Splash);

    /// <summary>Advances from the completed splash to the persistent frontend loader.</summary>
    internal void ShowFrontendLoader() => Transition(StartupStage.Splash, StartupStage.FrontendLoading);

    /// <summary>Advances successful initialization to the main menu.</summary>
    internal void Complete() => Transition(StartupStage.FrontendLoading, StartupStage.MainMenu);

    /// <summary>Stops the active required phase in a recoverable failure state.</summary>
    /// <param name="phase">Phase that must be replayed by Retry.</param>
    internal void Fail(StartupFailurePhase phase)
    {
        StartupStage expected = phase == StartupFailurePhase.FrontendDependencies
            ? StartupStage.Preloader
            : StartupStage.FrontendLoading;
        Transition(expected, StartupStage.Failed);
        FailurePhase = phase;
    }

    /// <summary>Returns a failure to the exact startup phase that owns it.</summary>
    /// <returns>The phase the caller must replay.</returns>
    internal StartupFailurePhase Retry()
    {
        if (Stage != StartupStage.Failed || FailurePhase is not StartupFailurePhase phase)
        {
            throw new InvalidOperationException("Cannot retry startup without an active recoverable failure.");
        }

        Stage = phase == StartupFailurePhase.FrontendDependencies
            ? StartupStage.Preloader
            : StartupStage.FrontendLoading;
        FailurePhase = null;
        return phase;
    }

    private void Transition(StartupStage expected, StartupStage next)
    {
        if (Stage != expected)
        {
            throw new InvalidOperationException($"Cannot transition startup from {Stage} to {next}; expected {expected}.");
        }

        Stage = next;
    }
}
