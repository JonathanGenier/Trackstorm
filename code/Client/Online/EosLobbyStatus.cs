namespace Trackstorm.Client.Online;

/// <summary>Credential-free authentication presentation shared by the multiplayer controls.</summary>
/// <param name="Text">Connection state and safe recovery guidance.</param>
/// <param name="HostReason">Adjacent explanation when hosting is disabled.</param>
/// <param name="Online">Identity and coordinator are both ready.</param>
/// <param name="CanRetry">A new explicit login is permitted.</param>
/// <param name="CanLogout">The current identity operation can be ended.</param>
internal sealed record EosLobbyStatus(string Text, string HostReason, bool Online = false, bool CanRetry = false, bool CanLogout = false)
{
    /// <summary>Initial presentation before the application starts EOS.</summary>
    internal static EosLobbyStatus Initializing { get; } = new("EOS: Initializing…", "Initializing EOS…");

    /// <summary>Fallback for composition without an online identity owner.</summary>
    internal static EosLobbyStatus Unavailable { get; } = new("EOS unavailable. Direct-IP / LAN is available below.", "EOS unavailable.");

    /// <summary>Authenticated presentation, only used once coordinator creation succeeds.</summary>
    internal static EosLobbyStatus Connected { get; } = new("EOS: Online", string.Empty, Online: true, CanLogout: true);

    /// <summary>Maps service state without exposing configuration contents or identity credentials.</summary>
    /// <param name="state">Current identity lifecycle.</param>
    /// <param name="coordinatorReady">Whether the authenticated lobby adapter exists.</param>
    /// <param name="authenticationStarted">Distinguishes initialization failure from login failure.</param>
    /// <param name="failure">Safe service-generated recovery guidance.</param>
    /// <returns>The single state shown by the lobby panel.</returns>
    internal static EosLobbyStatus FromIdentity(OnlineIdentityState state, bool coordinatorReady, bool authenticationStarted, string? failure) => state switch
    {
        OnlineIdentityState.LoggingIn => new("EOS: Authenticating…", "Authenticating with EOS…", CanLogout: true),
        OnlineIdentityState.LoggedIn when coordinatorReady => Connected,
        OnlineIdentityState.LoggedIn or OnlineIdentityState.Ready => Initializing,
        OnlineIdentityState.LoggingOut => new("EOS: Signing out…", "Wait for EOS sign-out to finish."),
        OnlineIdentityState.Failed when authenticationStarted => new($"EOS: Authentication failed. {failure}", "Sign in to EOS before hosting.", CanRetry: true),
        OnlineIdentityState.Failed => new($"EOS unavailable. {failure}", "EOS unavailable. Retry login or use Direct-IP / LAN.", CanRetry: true),
        OnlineIdentityState.Stopped => new("EOS: Signed out", "Sign in to EOS before hosting.", CanRetry: true),
        _ => Unavailable,
    };
}
