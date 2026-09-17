namespace Trackstorm.Client.Settings;

/// <summary>Local navigation only; no authoritative match or preference state.</summary>
internal enum MenuPage
{
    /// <summary>Closed navigation page.</summary>
    Closed,
    /// <summary>Game navigation page.</summary>
    Game,
    /// <summary>Settings navigation page.</summary>
    Settings,
    /// <summary>Audio navigation page.</summary>
    Audio,
    /// <summary>Video navigation page.</summary>
    Video,
    /// <summary>Gameplay navigation page.</summary>
    Gameplay,
    /// <summary>Interface navigation page.</summary>
    Interface,
    /// <summary>Controls navigation page.</summary>
    Controls,
    /// <summary>DeveloperOptions navigation page.</summary>
    DeveloperOptions,
}
