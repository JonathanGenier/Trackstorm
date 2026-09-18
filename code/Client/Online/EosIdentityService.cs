namespace Trackstorm.Client.Online;

/// <summary>Main-thread owner of one replaceable EOS platform; epoch/state guards reject stale completions.</summary>
internal sealed class EosIdentityService : IDisposable
{
    private readonly Func<IEosPlatform> _factory;
    private readonly TimeProvider _time;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private IEosPlatform? _platform;
    private long _epoch;
    private long _operationStarted;

    /// <summary>Creates the main-thread service with an injectable platform and monotonic clock.</summary>
    /// <param name="factory">Creates a new owned platform for each startup.</param>
    /// <param name="time">Monotonic clock for operation deadlines.</param>
    public EosIdentityService(Func<IEosPlatform> factory, TimeProvider? time = null)
    {
        _factory = factory;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Gets the current local authentication state.</summary>
    public OnlineIdentityState State { get; private set; }

    /// <summary>Gets the authenticated online identity, never a session player ID.</summary>
    public OnlineProductUserId? ProductUserId { get; private set; }

    /// <summary>Gets the safe actionable failure message, if any.</summary>
    public string? Failure { get; private set; }

    /// <summary>Gets the validated development environment and deployment label.</summary>
    public string EnvironmentLabel { get; private set; } = "not configured";

    /// <summary>Gets whether this service currently owns a platform.</summary>
    public bool PlatformInitialized => _platform is not null;

    /// <summary>Gets safe development state and a hashed identity fingerprint.</summary>
    public string Diagnostics => $"EOS platform: {PlatformInitialized}; Connect: {State}; identity: {ProductUserId?.ToString() ?? "none"}; {EnvironmentLabel}" + (Failure is null ? string.Empty : $"; {Failure}");

    /// <summary>Validates configuration and starts an owned platform; duplicate startup is ignored.</summary>
    /// <param name="configuration">Validated development environment and restricted client configuration.</param>
    public void Start(EosConfiguration configuration)
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(State == OnlineIdentityState.Disposed, this);
        if (_platform is not null)
        {
            return;
        }

        try
        {
            configuration.Validate();
            EnvironmentLabel = $"{configuration.Environment}/{configuration.DeploymentName}";
            var platform = _factory();
            _platform = platform;
            platform.Start(configuration);
            long epoch = ++_epoch;
            platform.WatchIdentityLoss(reason =>
            {
                if (epoch == _epoch && State is OnlineIdentityState.LoggedIn or OnlineIdentityState.LoggingIn)
                {
                    Fail(reason);
                }
            });
            State = OnlineIdentityState.Ready;
            Failure = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Fail(exception is InvalidOperationException ? exception.Message : "EOS runtime cannot load. Run setup-eos.ps1, use Windows x64, and install the Microsoft VC++ x64 runtime.");
        }
    }

    /// <summary>Starts one Connect login using the SDK-managed Device ID credential.</summary>
    public void Login()
    {
        CheckThread();
        if (State != OnlineIdentityState.Ready || _platform is null)
        {
            return;
        }

        State = OnlineIdentityState.LoggingIn;
        _operationStarted = _time.GetTimestamp();
        long epoch = _epoch;
        _platform.Login((identity, failure) =>
        {
            if (epoch != _epoch || State != OnlineIdentityState.LoggingIn)
            {
                return;
            }

            if (identity is null || failure is not null)
            {
                Fail(failure ?? "EOS Connect returned no identity. Check development client policy and deployment.");
                return;
            }

            ProductUserId = identity;
            State = OnlineIdentityState.LoggedIn;
        });
    }

    /// <summary>Clears local identity and ends the current login without deleting Device ID credentials.</summary>
    public void Logout()
    {
        CheckThread();
        if (State == OnlineIdentityState.LoggingIn)
        {
            Stop();
        }
        else if (State == OnlineIdentityState.LoggedIn && _platform is not null)
        {
            ProductUserId = null;
            State = OnlineIdentityState.LoggingOut;
            _operationStarted = _time.GetTimestamp();
            long epoch = _epoch;
            _platform.Logout(failure =>
            {
                if (epoch != _epoch || State != OnlineIdentityState.LoggingOut)
                {
                    return;
                }

                // Release the old platform even after successful logout: no old callbacks can cross a new login.
                Stop();
                if (failure is not null)
                {
                    Failure = failure;
                    State = OnlineIdentityState.Failed;
                }
            });
        }
    }

    /// <summary>Pumps native work and delivers queued completions on the owner thread.</summary>
    public void Tick()
    {
        CheckThread();
        _platform?.Tick();
        if (State is OnlineIdentityState.LoggingIn or OnlineIdentityState.LoggingOut && _time.GetElapsedTime(_operationStarted).TotalSeconds >= 60)
        {
            Fail("EOS Connect timed out after 60 seconds. Check Internet access, development credentials and client policy; then retry login.");
        }
    }

    /// <summary>Invalidates callbacks and releases the platform while allowing a later restart.</summary>
    public void Stop()
    {
        CheckThread();
        if (State == OnlineIdentityState.Disposed)
        {
            return;
        }

        ++_epoch;
        ProductUserId = null;
        var old = _platform;
        _platform = null;
        old?.Dispose();
        State = OnlineIdentityState.Stopped;
        Failure = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        State = OnlineIdentityState.Disposed;
    }

    /// <summary>Creates an authenticated fencing connection without retaining tokens on disk.</summary>
    /// <returns>An authenticated HTTPS adapter.</returns>
    internal ILeaseTransport CreateLeaseTransport()
    {
        if (State != OnlineIdentityState.LoggedIn || _platform is not EosSdkPlatform sdk)
        {
            throw new InvalidOperationException("EOS authentication is required.");
        }

        return new HttpLeaseTransport(LeaseEndpointConfiguration.Resolve(), sdk.CopyIdToken);
    }

    /// <summary>Creates the Client-only lobby adapter from the current authenticated native runtime.</summary>
    /// <returns>A provider tied to the current platform lifetime.</returns>
    internal IOnlineLobbyProvider CreateLobbyProvider() => State == OnlineIdentityState.LoggedIn && _platform is EosSdkPlatform sdk
        ? sdk.CreateLobbyProvider() : throw new InvalidOperationException("EOS authentication is required.");

    /// <summary>Creates gameplay transport on the current authenticated platform.</summary>
    /// <param name="coordinator">Active membership owner.</param>
    /// <param name="credential">Transient client access code.</param>
    /// <returns>Owned gameplay gateway.</returns>
    internal Networking.EosP2pTransport CreateTransport(OnlineLobbyCoordinator coordinator, string? credential)
    {
        if (State != OnlineIdentityState.LoggedIn || _platform is not EosSdkPlatform sdk)
        {
            throw new InvalidOperationException("EOS authentication is required.");
        }

        coordinator.LeaseFactory ??= CreateLeaseTransport;
        return sdk.CreateTransport(coordinator, credential);
    }

    private void Fail(string reason)
    {
        Stop();
        State = OnlineIdentityState.Failed;
        Failure = reason;
    }

    private void CheckThread()
    {
        if (_thread != System.Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("EOS identity operations must run on their owning thread.");
        }
    }
}
