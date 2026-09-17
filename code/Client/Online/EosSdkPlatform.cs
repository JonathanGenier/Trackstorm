using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;
using Trackstorm.Client.Networking;

namespace Trackstorm.Client.Online;

/// <summary>Official SDK adapter. Native callbacks enqueue work; consumers run only after EOS Tick returns.</summary>
internal sealed class EosSdkPlatform : IEosPlatform
{
    private readonly Queue<Action> _callbacks = new();
    private readonly EosPendingHandles _lobbyHandles = new();
    private PlatformInterface? _platform;
    private ConnectInterface? _connect;
    private ProductUserId? _user;
    private ulong _expirationNotification;
    private ulong _statusNotification;
    private bool _leased;
    private bool _disposed;
    private EosLobbyProvider? _lobbyProvider;
    private EosP2pTransport? _transport;

    /// <summary>Validates configuration and starts an owned platform; duplicate startup is ignored.</summary>
    /// <param name="configuration">Validated development environment and restricted client configuration.</param>
    public void Start(EosConfiguration configuration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_platform is not null)
        {
            return;
        }

        EosProcessRuntime.Acquire();
        _leased = true;
        var options = new Options
        {
            ProductId = configuration.ProductId,
            SandboxId = configuration.SandboxId,
            DeploymentId = configuration.DeploymentId,
            ClientCredentials = new ClientCredentials { ClientId = configuration.ClientId, ClientSecret = configuration.ClientSecret },
            Flags = PlatformFlags.DisableOverlay | PlatformFlags.DisableSocialOverlay,
            TickBudgetInMilliseconds = 2,
            TaskNetworkTimeoutSeconds = 30,
        };
        _platform = PlatformInterface.Create(ref options);
        if (_platform is null)
        {
            throw new InvalidOperationException("EOS platform creation failed. Check matching Product/Sandbox/Deployment and untrusted client credentials; see docs/eos-development.md.");
        }

        _connect = _platform.GetConnectInterface();
    }

    /// <summary>Registers safe notifications for expired or lost authentication.</summary>
    /// <param name="lost">Callback receiving a safe explanation when authentication becomes unusable.</param>
    public void WatchIdentityLoss(Action<string> lost)
    {
        var expiration = default(AddNotifyAuthExpirationOptions);
        _expirationNotification = _connect!.AddNotifyAuthExpiration(ref expiration, this, (ref AuthExpirationCallbackInfo info) =>
        {
            _callbacks.Enqueue(() => lost("EOS credentials are expiring. Log in again to renew the local identity."));
        });
        var status = default(AddNotifyLoginStatusChangedOptions);
        _statusNotification = _connect.AddNotifyLoginStatusChanged(ref status, this, (ref LoginStatusChangedCallbackInfo info) =>
        {
            if (info.CurrentStatus == LoginStatus.NotLoggedIn)
            {
                _callbacks.Enqueue(() => lost("EOS login ended. Check connectivity and log in again."));
            }
        });
        if (_expirationNotification == 0 || _statusNotification == 0)
        {
            throw new InvalidOperationException("EOS login notifications could not be registered. Restart the application.");
        }
    }

    /// <summary>Starts one Connect login using the SDK-managed Device ID credential.</summary>
    /// <param name="completed">Completion receiving only identity or safe error information.</param>
    public void Login(Action<OnlineProductUserId?, string?> completed)
    {
        var options = new CreateDeviceIdOptions { DeviceModel = "Windows Desktop" };
        _connect!.CreateDeviceId(ref options, this, (ref CreateDeviceIdCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            _callbacks.Enqueue(() =>
            {
                if (result is Result.Success or Result.DuplicateNotAllowed)
                {
                    Connect(completed);
                }
                else
                {
                    completed(null, Failure("Device ID creation", result));
                }
            });
        });
    }

    /// <summary>Clears local identity and ends the current login without deleting Device ID credentials.</summary>
    /// <param name="completed">Completion receiving only identity or safe error information.</param>
    public void Logout(Action<string?> completed)
    {
        var options = new LogoutOptions { LocalUserId = _user };
        _connect!.Logout(ref options, this, (ref LogoutCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            _callbacks.Enqueue(() => completed(result == Result.Success ? null : Failure("Logout", result)));
        });
        _user = null;
    }

    /// <summary>Pumps native work and delivers queued completions on the owner thread.</summary>
    public void Tick()
    {
        if (_disposed)
        {
            return;
        }

        _platform?.Tick();
        while (!_disposed && _callbacks.TryDequeue(out var callback))
        {
            callback();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport?.Dispose();
        _lobbyProvider?.Dispose();
        if (_expirationNotification != 0)
        {
            _connect!.RemoveNotifyAuthExpiration(_expirationNotification);
        }

        if (_statusNotification != 0)
        {
            _connect!.RemoveNotifyLoginStatusChanged(_statusNotification);
        }

        // Caller-owned lobby handles must be released while their platform is still valid.
        // Platform release then cancels their queued callbacks after the native objects are gone.
        _lobbyHandles.Dispose();
        _platform?.Release();
        _platform = null;
        _connect = null;
        _user = null;
        // Platform_Release ends native callback delivery; now remove canceled one-shot wrapper registrations.
        Helper.ReleaseTrackstormCallbacks(this);
        _callbacks.Clear();
        if (_leased)
        {
            EosProcessRuntime.Release();
            _leased = false;
        }
    }

    /// <summary>Copies a current Connect ID token only for authenticated HTTPS coordination.</summary>
    /// <returns>Current ID token, or no authenticated token.</returns>
    internal string? CopyIdToken()
    {
        if (_disposed || _platform is null || _user is null || _connect is null)
        {
            return null;
        }

        var options = new CopyIdTokenOptions { LocalUserId = _user };
        return _connect.CopyIdToken(ref options, out var token) == Result.Success ? token?.JsonWebToken.ToString() : null;
    }

    /// <summary>Creates coordination on this authenticated platform; no second runtime or identity is created.</summary>
    /// <returns>A provider tied to this platform's callback queue.</returns>
    internal IOnlineLobbyProvider CreateLobbyProvider()
    {
        if (_disposed || _user is null || _platform is null)
        {
            throw new InvalidOperationException("EOS authentication is required.");
        }

        _lobbyProvider?.Dispose();
        _lobbyProvider = new EosLobbyProvider(_platform.GetLobbyInterface(), _user, this, callback => _callbacks.Enqueue(callback), _lobbyHandles);
        return _lobbyProvider;
    }

    /// <summary>Replaces the packet gateway while retaining the authenticated platform and identity.</summary>
    /// <param name="coordinator">Active membership owner.</param>
    /// <param name="credential">Transient client access code.</param>
    /// <returns>Gateway released before its parent platform.</returns>
    internal EosP2pTransport CreateTransport(OnlineLobbyCoordinator coordinator, string? credential)
    {
        if (_disposed || _user is null || _platform is null)
        {
            throw new InvalidOperationException("EOS authentication is required.");
        }

        _transport?.Dispose();
        _transport = new EosP2pTransport(new EosP2pSdk(_platform.GetP2PInterface(), _user), coordinator.Identity, () => coordinator.Active, credential);
        return _transport;
    }

    private static string Failure(string operation, Result result) => $"EOS {operation} failed ({result}). Check Internet access, development deployment and Connect client-policy permissions; see docs/eos-development.md.";

    private void Connect(Action<OnlineProductUserId?, string?> completed)
    {
        var options = new LoginOptions
        {
            Credentials = new Credentials
            {
                Type = ExternalCredentialType.DeviceidAccessToken,
                Token = null,
            },
            // Display name is metadata only. EOS selects the identity using its OS-user credential.
            UserLoginInfo = new UserLoginInfo { DisplayName = "Trackstorm Tester" },
        };
        _connect!.Login(ref options, this, (ref LoginCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            ProductUserId user = info.LocalUserId;
            ContinuanceToken continuation = info.ContinuanceToken;
            _callbacks.Enqueue(() =>
            {
                if (result == Result.InvalidUser && continuation is not null)
                {
                    var create = new CreateUserOptions { ContinuanceToken = continuation };
                    _connect!.CreateUser(ref create, this, (ref CreateUserCallbackInfo created) =>
                    {
                        Result createResult = created.ResultCode;
                        ProductUserId createdUser = created.LocalUserId;
                        _callbacks.Enqueue(() => CompleteLogin(createResult, createdUser, completed));
                    });
                }
                else
                {
                    CompleteLogin(result, user, completed);
                }
            });
        });
    }

    private void CompleteLogin(Result result, ProductUserId? user, Action<OnlineProductUserId?, string?> completed)
    {
        if (result != Result.Success || user is null || !user.IsValid())
        {
            completed(null, Failure("Connect login", result));
            return;
        }

        _user = user;
        completed(new OnlineProductUserId(user.ToString()), null);
    }
}
