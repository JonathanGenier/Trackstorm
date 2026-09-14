using Godot;

namespace Trackstorm.Client.Online;

/// <summary>Opt-in development identity controls; application ownership is independent of Direct-IP sessions.</summary>
public sealed partial class EosIdentityNode : CanvasLayer
{
    private readonly EosIdentityService _identity = new(() => new EosSdkPlatform());
    private Label _status = null!;
    private string? _configurationError;

    /// <inheritdoc />
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 20;
        var box = new VBoxContainer { Position = new Vector2(12, 90), CustomMinimumSize = new Vector2(600, 0) };
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_status);
        var controls = new HBoxContainer();
        var login = new Button { Text = "EOS dev login" };
        var logout = new Button { Text = "EOS logout" };
        login.Pressed += Login;
        logout.Pressed += _identity.Logout;
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
        _status.Text = $"SDK initialized: {EosProcessRuntime.Initialized}\n" + (_configurationError ?? _identity.Diagnostics);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
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
