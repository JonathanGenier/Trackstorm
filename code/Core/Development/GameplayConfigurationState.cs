namespace Trackstorm.Core.Development;

/// <summary>Detached authoritative configuration revision carried by replication and checkpoints.</summary>
public sealed record GameplayConfigurationState
{
    /// <summary>Creates a detached validated configuration revision.</summary>
    /// <param name="revision">Monotonic configuration revision.</param>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    public GameplayConfigurationState(ulong revision, GameplayConfiguration configuration)
    {
        configuration.Validate();
        Revision = revision;
        Configuration = configuration;
    }

    /// <summary>Monotonically increasing authority-owned revision.</summary>
    public ulong Revision { get; }
    /// <summary>Validated effective gameplay tuning.</summary>
    public GameplayConfiguration Configuration { get; }

    /// <summary>Equal revisions are accepted only for identical data; an older revision can never rewind state.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="current">Previously accepted configuration.</param>
    public bool CanReplace(GameplayConfigurationState current) =>
        Revision > current.Revision || (Revision == current.Revision && Configuration == current.Configuration);
}
