using System.Globalization;
using System.Reflection;

namespace Trackstorm.Core.Sessions;

/// <summary>Strict pre-1.0 MAJOR.RELEASE.PR build identity, derived from repository metadata at build time.</summary>
public sealed record GameVersion
{
    /// <summary>Creates a revision representable in normal .NET assembly version metadata.</summary>
    /// <param name="release">Release milestone, zero through 65534.</param>
    /// <param name="revision">Sequential Story revision, zero through 65534.</param>
    public GameVersion(int release, int revision)
    {
        if (revision is < 0 or > 65534)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        if (release is < 0 or > 65534)
        {
            throw new ArgumentOutOfRangeException(nameof(release));
        }

        Release = release;
        Revision = revision;
    }

    /// <summary>The one source-derived runtime value; no runtime project-file access is needed.</summary>
    public static GameVersion Current { get; } = Parse(typeof(GameVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(value => value.Key == "TrackstormVersion").Value!);

    /// <summary>Active release milestone.</summary>
    public int Release { get; }

    /// <summary>Sequential third component.</summary>
    public int Revision { get; }

    /// <summary>Parses only canonical three-component MAJOR.RELEASE.PR values.</summary>
    /// <param name="value">Untrusted wire or build value.</param>
    /// <returns>Validated version.</returns>
    public static GameVersion Parse(string value) => TryParse(value, out var version) ? version! : throw new ArgumentException("Expected canonical 0.RELEASE.PR (RELEASE = 0..65534; PR = 0..65534).", nameof(value));

    /// <summary>Rejects absent, malformed, out-of-release and noncanonical versions.</summary>
    /// <param name="value">Untrusted version.</param>
    /// <param name="version">Validated result, otherwise null.</param>
    /// <returns>Whether the exact canonical format was supplied.</returns>
    public static bool TryParse(string? value, out GameVersion? version)
    {
        version = null;
        string[] parts = value?.Split('.') ?? [];
        if (parts.Length != 3 || parts[0] != "0" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int release) || release is < 0 or > 65534 ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int revision) || revision is < 0 or > 65534)
        {
            return false;
        }

        var parsed = new GameVersion(release, revision);
        if (parsed.ToString() != value)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    /// <summary>Provider-neutral exact-build admission rule used for fresh and returning players.</summary>
    /// <param name="remote">Advertised or claimed runtime version.</param>
    /// <returns>Whether this runtime can participate.</returns>
    public bool IsCompatible(string? remote) => TryParse(remote, out var version) && this == version;

    /// <summary>Safe player-facing diagnostic; malformed remote text is never echoed.</summary>
    /// <param name="lobby">Hosted version from metadata or rejection.</param>
    /// <returns>Local and hosted versions where valid.</returns>
    public string MismatchMessage(string? lobby) => $"Game version mismatch. Lobby: {(TryParse(lobby, out var version) ? version!.ToString() : "unknown/invalid")} — Your version: {this}.";

    /// <inheritdoc />
    public override string ToString() => "0." + Release.ToString(CultureInfo.InvariantCulture) + "." + Revision.ToString(CultureInfo.InvariantCulture);
}
