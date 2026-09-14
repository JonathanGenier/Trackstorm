namespace Trackstorm.Client.Online;

/// <summary>Owned platform seam for deterministic lifecycle checks without native EOS or network access.</summary>
internal interface IEosPlatform : IDisposable
{
    /// <summary>Validates configuration and starts an owned platform; duplicate startup is ignored.</summary>
    /// <param name="configuration">Validated development environment and restricted client configuration.</param>
    void Start(EosConfiguration configuration);

    /// <summary>Pumps native work and delivers queued completions on the owner thread.</summary>
    void Tick();

    /// <summary>Starts one Connect login using the SDK-managed Device ID credential.</summary>
    /// <param name="completed">Completion receiving only identity or safe error information.</param>
    void Login(Action<OnlineProductUserId?, string?> completed);

    /// <summary>Clears local identity and ends the current login without deleting Device ID credentials.</summary>
    /// <param name="completed">Completion receiving only identity or safe error information.</param>
    void Logout(Action<string?> completed);

    /// <summary>Registers safe notifications for expired or lost authentication.</summary>
    /// <param name="lost">Callback receiving a safe explanation when authentication becomes unusable.</param>
    void WatchIdentityLoss(Action<string> lost);
}
