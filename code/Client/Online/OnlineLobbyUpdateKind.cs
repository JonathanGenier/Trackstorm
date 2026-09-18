namespace Trackstorm.Client.Online;

/// <summary>Identifies the authority an EOS notification has over local lobby state.</summary>
internal enum OnlineLobbyUpdateKind
{
    /// <summary>Owner-controlled attributes or routing metadata changed; membership is not authoritative.</summary>
    Metadata,

    /// <summary>EOS lobby ownership changed; admitted membership and gameplay authority are not affected.</summary>
    Ownership,

    /// <summary>EOS explicitly reported one member joining.</summary>
    Joined,

    /// <summary>EOS explicitly reported one member leaving, being kicked or disconnecting.</summary>
    Departed,

    /// <summary>EOS explicitly reported local departure or lobby closure.</summary>
    Closure,
}
