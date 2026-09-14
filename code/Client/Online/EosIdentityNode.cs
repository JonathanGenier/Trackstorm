using Godot;

namespace Trackstorm.Client.Online;

/// <summary>Application-owned EOS identity; the multiplayer panel presents its status and controls.</summary>
public sealed partial class EosIdentityNode : Node
{
    private readonly EosIdentityService _identity = new(() => new EosSdkPlatform());
    private EosLobbyStatus? _failure;
    private bool _authenticationStarted;
    private bool _logoutRequested;

    /// <summary>Current authenticated membership coordinator, absent when EOS is unavailable.</summary>
    internal OnlineLobbyCoordinator? Coordinator { get; private set; }

    /// <summary>Single source for the multiplayer authentication presentation.</summary>
    internal EosLobbyStatus Status { get; private set; } = EosLobbyStatus.Initializing;

    /// <inheritdoc />
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Login();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _identity.Tick();
        if (_identity.State != OnlineIdentityState.LoggedIn)
        {
            Coordinator?.Dispose();
            Coordinator = null;
        }
        else if (Coordinator is null && !_logoutRequested && _failure is null)
        {
            try
            {
                Coordinator = new OnlineLobbyCoordinator(_identity.CreateLobbyProvider(), _identity.ProductUserId!);
            }
            catch (InvalidOperationException)
            {
                _identity.Stop();
                _failure = new EosLobbyStatus("EOS unavailable. Lobby services could not start; retry login.", "EOS lobby services unavailable.", CanRetry: true);
            }
        }

        Coordinator?.Tick();
        if (_logoutRequested && Coordinator?.Busy != true)
        {
            Coordinator?.Dispose();
            Coordinator = null;
            _identity.Logout();
            _logoutRequested = false;
        }

        Status = _failure ?? EosLobbyStatus.FromIdentity(_identity.State, Coordinator is not null, _authenticationStarted, _identity.Failure);
        if (_logoutRequested)
        {
            Status = new EosLobbyStatus("EOS: Closing lobby before sign-out…", "Wait for lobby cleanup.");
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        Coordinator?.Dispose();
        _identity.Dispose();
        // No SDK calls are permitted after this terminal application shutdown.
        EosProcessRuntime.Shutdown();
    }

    /// <summary>Loads editor/export configuration and starts an explicit login or retry.</summary>
    internal void Login()
    {
        if (_identity.State is OnlineIdentityState.LoggingIn or OnlineIdentityState.LoggedIn or OnlineIdentityState.LoggingOut || _logoutRequested)
        {
            return;
        }

        string path = System.Environment.GetEnvironmentVariable("TRACKSTORM_EOS_CONFIG") ??
            System.IO.Path.Combine(OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://") : System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "eos.development.local.json");
        EosConfiguration configuration;
        try
        {
            configuration = EosConfiguration.Load(path);
        }
        catch (InvalidOperationException)
        {
            _failure = new EosLobbyStatus($"EOS: Configuration missing/invalid. Create or correct {path} using eos.development.example.json, then retry login.", "EOS configuration is missing or invalid.", CanRetry: true);
            Status = _failure;
            return;
        }

        _failure = null;
        _authenticationStarted = false;
        Status = EosLobbyStatus.Initializing;
        _identity.Start(configuration);
        _authenticationStarted = _identity.State == OnlineIdentityState.Ready;
        _identity.Login();
    }

    /// <summary>Closes membership before ending the current identity, or cancels pending login.</summary>
    internal void Logout()
    {
        Coordinator?.Leave();
        _logoutRequested = true;
    }
}
