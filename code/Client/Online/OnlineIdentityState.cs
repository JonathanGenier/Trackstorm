namespace Trackstorm.Client.Online;

/// <summary>Observable local authentication lifecycle, independent of the gameplay session.</summary>
internal enum OnlineIdentityState
{
    /// <summary>No platform or local online identity is retained.</summary>
    Stopped,
    /// <summary>Platform initialized and ready for explicit login.</summary>
    Ready,
    /// <summary>One bounded authentication operation is pending.</summary>
    LoggingIn,
    /// <summary>Connect has supplied a valid local Product User ID.</summary>
    LoggedIn,
    /// <summary>Local identity is cleared while Connect discards its authentication state.</summary>
    LoggingOut,
    /// <summary>A safe actionable failure is available; explicit retry is required.</summary>
    Failed,
    /// <summary>The service is permanently closed.</summary>
    Disposed,
}
