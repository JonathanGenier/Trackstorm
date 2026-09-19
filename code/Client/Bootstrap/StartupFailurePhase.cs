namespace Trackstorm.Client.Bootstrap;

/// <summary>Required startup phase that entered the recoverable failure state.</summary>
internal enum StartupFailurePhase
{
    /// <summary>Minimum Splash and MenuShell dependencies loaded by the Preloader.</summary>
    FrontendDependencies,
    /// <summary>MenuShell media playback and saved frontend settings setup.</summary>
    FrontendSetup,
    /// <summary>Reusable application resources and composed application systems.</summary>
    ApplicationInitialization,
}
