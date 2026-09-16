namespace Trackstorm.Client.Online;

/// <summary>Client-only discovery data; no Ready, phase, or gameplay replica.</summary>
internal sealed record OnlineLobby(string Id, string Name, OnlineProductUserId Owner, ulong Session, LobbyAccess Access, int Members, int Capacity, string Protocol, bool Open, LobbyCredential? Credential)
{
    /// <summary>Indexed build/protocol compatibility bucket for this lobby schema.</summary>
    internal const string CurrentProtocol = "trackstorm-lobby-6";

    /// <summary>Online members available to the authenticated transport admission boundary.</summary>
    internal IReadOnlyList<OnlineProductUserId> MemberIds { get; init; } = Array.Empty<OnlineProductUserId>();

    /// <summary>Whether metadata satisfies the current Trackstorm schema and capacity.</summary>
    internal bool Compatible => Protocol == CurrentProtocol && Session is > 0 and < ulong.MaxValue && Capacity == 8 && Members is >= 1 and <= 8 && Name.Length > 0 && Name == LobbyName.Sanitize(Name) && Enum.IsDefined(Access) && (Access == LobbyAccess.Public ? Credential is null : Credential is not null);

    /// <summary>Whether compatible metadata indicates available admission capacity.</summary>
    internal bool Joinable => Compatible && Open && Members < Capacity;

    /// <summary>Whitelisted browser presentation without online identities or verification material.</summary>
    internal LobbyRow Row => new(Id, Name, Members, Capacity, Access, Joinable);

    /// <inheritdoc />
    public override string ToString() => $"{Name}: {Members}/{Capacity}, {Access}, open={Open}";
}
