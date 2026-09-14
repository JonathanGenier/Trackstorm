using Godot;

namespace Trackstorm.Client.Online;

/// <summary>Opt-in development identity controls; application ownership is independent of Direct-IP sessions.</summary>
public sealed partial class EosIdentityNode : CanvasLayer
{
    private readonly EosIdentityService _identity = new(() => new EosSdkPlatform());
    private Label _status = null!;
    private string? _configurationError;
    private bool _logoutRequested;

    /// <summary>Current authenticated membership coordinator, absent when EOS is unavailable.</summary>
    internal OnlineLobbyCoordinator? Coordinator { get; private set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 20;
        var box = new VBoxContainer { Position = new Vector2(12, 12), CustomMinimumSize = new Vector2(240, 0) };
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_status);
        var controls = new HBoxContainer();
        var login = new Button { Text = "EOS dev login" };
        var logout = new Button { Text = "EOS logout" };
        login.Pressed += Login;
        logout.Pressed += () =>
        {
            Coordinator?.Leave();
            _logoutRequested = true;
        };
        controls.AddChild(login);
        controls.AddChild(logout);
        box.AddChild(controls);
        AddChild(box);
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
        else if (Coordinator is null && !_logoutRequested)
        {
            Coordinator = new OnlineLobbyCoordinator(_identity.CreateLobbyProvider(), _identity.ProductUserId!);
        }

        Coordinator?.Tick();
        if (_logoutRequested && Coordinator?.Busy != true)
        {
            Coordinator?.Dispose();
            Coordinator = null;
            _identity.Logout();
            _logoutRequested = false;
        }

        _status.Text = $"SDK initialized: {EosProcessRuntime.Initialized}\n" + (_configurationError ?? _identity.Diagnostics);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        Coordinator?.Dispose();
        _identity.Dispose();
        // The bootstrap is the application root. No SDK calls are permitted after this terminal shutdown.
        EosProcessRuntime.Shutdown();
    }

    private void Login()
    {
        try
        {
            string path = System.Environment.GetEnvironmentVariable("TRACKSTORM_EOS_CONFIG") ??
                System.IO.Path.Combine(OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://") : System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "eos.development.local.json");
            var configuration = EosConfiguration.Load(path);
            _configurationError = null;
            _identity.Start(configuration);
            _identity.Login();
        }
        catch (InvalidOperationException exception)
        {
            _configurationError = exception.Message;
        }
    }
}
