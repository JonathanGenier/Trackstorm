namespace Trackstorm.Client.Bootstrap;

/// <summary>Explicit application startup states in presentation order.</summary>
internal enum StartupStage
{
    /// <summary>Minimal engine bootstrap before presentation begins.</summary>
    Preloader,
    /// <summary>Dedicated splash presentation.</summary>
    Splash,
    /// <summary>Persistent frontend shell with application loader.</summary>
    FrontendLoading,
    /// <summary>Interactive main-menu presentation.</summary>
    MainMenu,
    /// <summary>Recoverable required-initialization failure.</summary>
    Failed,
}
