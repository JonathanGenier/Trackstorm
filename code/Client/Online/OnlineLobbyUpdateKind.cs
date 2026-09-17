namespace Trackstorm.Client.Online;

/// <summary>Identifies whether EOS confirmed membership or only refreshed lobby metadata.</summary>
internal enum OnlineLobbyUpdateKind
{
    /// <summary>Owner-controlled attributes or routing metadata changed; membership is not authoritative.</summary>
    Metadata,

    /// <summary>EOS reported a member join, remote departure, disconnect or promotion.</summary>
    Membership,

    /// <summary>EOS explicitly reported local departure or lobby closure.</summary>
    Closure,
}
