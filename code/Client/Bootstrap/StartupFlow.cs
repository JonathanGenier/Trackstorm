namespace Trackstorm.Client.Bootstrap;

/// <summary>Validates deterministic startup transitions independently of Godot presentation.</summary>
internal sealed class StartupFlow
{
    /// <summary>Current validated startup state.</summary>
    internal StartupStage Stage { get; private set; } = StartupStage.Preloader;

    /// <summary>Advances from minimal preloading to the splash.</summary>
    internal void ShowSplash() => Transition(StartupStage.Preloader, StartupStage.Splash);

    /// <summary>Advances from the completed splash to the persistent frontend loader.</summary>
    internal void ShowFrontendLoader() => Transition(StartupStage.Splash, StartupStage.FrontendLoading);

    /// <summary>Advances successful initialization to the main menu.</summary>
    internal void Complete() => Transition(StartupStage.FrontendLoading, StartupStage.MainMenu);

    /// <summary>Stops required initialization in a recoverable failure state.</summary>
    internal void Fail() => Transition(StartupStage.FrontendLoading, StartupStage.Failed);

    /// <summary>Returns a failed initialization attempt to the loader.</summary>
    internal void Retry() => Transition(StartupStage.Failed, StartupStage.FrontendLoading);

    private void Transition(StartupStage expected, StartupStage next)
    {
        if (Stage != expected)
        {
            throw new InvalidOperationException($"Cannot transition startup from {Stage} to {next}; expected {expected}.");
        }

        Stage = next;
    }
}
