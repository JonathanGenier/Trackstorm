using System.Globalization;
using System.Reflection;

namespace Trackstorm.Core.Sessions;

/// <summary>Strict Release 0.0.1 build identity, derived from repository metadata at build time.</summary>
public sealed record GameVersion
{
    /// <summary>Creates a revision representable in normal .NET assembly version metadata.</summary>
    /// <param name="revision">Sequential Story revision, zero through 65534.</param>
    public GameVersion(int revision)
    {
        if (revision is < 0 or > 65534)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        Revision = revision;
    }

    /// <summary>The one source-derived runtime value; no runtime project-file access is needed.</summary>
    public static GameVersion Current { get; } = Parse(typeof(GameVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(value => value.Key == "TrackstormVersion").Value!);

    /// <summary>Sequential fourth component.</summary>
    public int Revision { get; }

    /// <summary>Parses only canonical four-component Release 0.0.1 values.</summary>
    /// <param name="value">Untrusted wire or build value.</param>
    /// <returns>Validated version.</returns>
    public static GameVersion Parse(string value) => TryParse(value, out var version) ? version! : throw new ArgumentException("Expected a canonical 0.0.1.N version (N = 0..65534).", nameof(value));

    /// <summary>Rejects absent, malformed, out-of-release and noncanonical versions.</summary>
    /// <param name="value">Untrusted version.</param>
    /// <param name="version">Validated result, otherwise null.</param>
    /// <returns>Whether the exact canonical format was supplied.</returns>
    public static bool TryParse(string? value, out GameVersion? version)
    {
        version = null;
        if (value is null || !value.StartsWith("0.0.1.", StringComparison.Ordinal) ||
            !int.TryParse(value.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int revision) || revision is < 0 or > 65534)
        {
            return false;
        }

        var parsed = new GameVersion(revision);
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
    public override string ToString() => "0.0.1." + Revision.ToString(CultureInfo.InvariantCulture);
}
