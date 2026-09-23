using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Online;

/// <summary>Client-only discovery data; no Ready, phase, or gameplay replica.</summary>
internal sealed record OnlineLobby(string Id, string Name, OnlineProductUserId Owner, ulong Session, LobbyAccess Access, int Members, int Capacity, string Protocol, bool Open, LobbyCredential? Credential)
{
    /// <summary>Indexed build/protocol compatibility bucket for this lobby schema.</summary>
    internal const string CurrentProtocol = "trackstorm-lobby-15";
    /// <summary>Agreed routing metadata; never sufficient by itself to install gameplay authority.</summary>
    internal OnlineProductUserId? GameplayHost { get; init; }
    /// <summary>Last agreed Trackstorm authority fence advertised for authenticated resume routing.</summary>
    internal ulong AuthorityEpoch { get; init; } = 1;
    /// <summary>Initial owner routing, overridden only by an agreed migration.</summary>
    internal OnlineProductUserId HostIdentity => GameplayHost ?? Owner;

    /// <summary>Exact hosted game version; absent native metadata is never inferred.</summary>
    internal string Version { get; init; } = GameVersion.Current.ToString();
    /// <summary>Optional advertised mode; missing metadata is displayed as unknown.</summary>
    internal string GameMode { get; init; } = string.Empty;

    /// <summary>Compatibility diagnostic shared by the browser and refreshed join checks.</summary>
    internal string VersionMismatch => GameVersion.Current.IsCompatible(Version) ? string.Empty : GameVersion.Current.MismatchMessage(Version);

    /// <summary>Minimal public discovery attributes, consumed directly by the EOS adapter.</summary>
    internal IReadOnlyDictionary<string, string> DiscoveryAttributes
    {
        get
        {
            var attributes = new Dictionary<string, string>
            {
                ["name"] = Name,
                ["session"] = Session.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["access"] = Access.ToString(),
                ["version"] = Version,
                ["mode"] = GameMode,
            };
            if (Credential is not null)
            {
                attributes["verifier"] = Credential.ExportVerifier();
            }

            return attributes;
        }
    }

    /// <summary>Online members available to the authenticated transport admission boundary.</summary>
    internal IReadOnlyList<OnlineProductUserId> MemberIds { get; init; } = Array.Empty<OnlineProductUserId>();

    /// <summary>Whether metadata satisfies the current Trackstorm schema and capacity.</summary>
    internal bool Compatible => Protocol == CurrentProtocol && Session is > 0 and < ulong.MaxValue && Capacity == 8 && Members is >= 1 and <= 8 && Name.Length > 0 && Name == LobbyName.Sanitize(Name) && Enum.IsDefined(Access) && (Access == LobbyAccess.Public ? Credential is null : Credential is not null);

    /// <summary>Whether compatible metadata indicates available admission capacity.</summary>
    internal bool Joinable => Compatible && VersionMismatch.Length == 0 && Open && Members < Capacity;

    /// <summary>Whitelisted browser presentation without online identities or verification material.</summary>
    internal LobbyRow Row => new(Id, Name, Members, Capacity, Access, Joinable, Version, VersionMismatch) { GameMode = GameMode };

    /// <inheritdoc />
    public override string ToString() => $"{Name}: {Members}/{Capacity}, {Access}, open={Open}";
}
