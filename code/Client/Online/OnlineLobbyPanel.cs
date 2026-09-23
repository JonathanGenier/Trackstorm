using Godot;

namespace Trackstorm.Client.Online;

/// <summary>Existing joined-lobby membership controls; discovery presentation belongs to the Play Menu.</summary>
internal sealed partial class OnlineLobbyPanel : VBoxContainer
{
    private readonly Label _identity = new() { Name = "EosState", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button _login = new() { Text = "EOS dev login / Retry" };
    private readonly Button _logout = new() { Text = "EOS logout" };
    private readonly LineEdit _name = new() { Text = "Lobby", PlaceholderText = "Lobby name (1–48 characters)", MaxLength = 96 };
    private readonly Button _rename = new() { Text = "Rename lobby (host)" };
    private readonly Button _leave = new() { Text = "Leave / Close online lobby" };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };

    internal Func<OnlineLobbyCoordinator?> Coordinator { get; set; } = () => null;
    internal Func<EosLobbyStatus> IdentityStatus { get; set; } = () => EosLobbyStatus.Unavailable;
    internal Action Login { get; set; } = () => { };
    internal Action Logout { get; set; } = () => { };
    internal Action? LeaveSession { get; set; }

    public override void _Ready()
    {
        AddChild(_identity);
        var identityControls = new HBoxContainer();
        identityControls.AddChild(_login);
        identityControls.AddChild(_logout);
        AddChild(identityControls);
        _login.Pressed += () => Login();
        _logout.Pressed += () => Logout();
        AddChild(_name);
        AddChild(_rename);
        AddChild(_leave);
        AddChild(_status);
        _rename.Pressed += () => Coordinator()?.Rename(_name.Text);
        _leave.Pressed += () =>
        {
            if (LeaveSession is not null) LeaveSession();
            else Coordinator()?.Leave();
        };
    }

    public override void _Process(double delta)
    {
        var coordinator = Coordinator();
        var identity = IdentityStatus();
        _identity.Text = identity.Text;
        _login.Visible = identity.CanRetry;
        _logout.Visible = identity.CanLogout;
        bool active = coordinator?.Active is not null;
        _name.Visible = _rename.Visible = active && coordinator!.IsHost;
        _rename.Disabled = coordinator?.Busy == true;
        _leave.Visible = coordinator?.CanLeave == true;
        _status.Text = coordinator is null ? string.Empty :
            (active ? $"{coordinator.Active!.Name} · {coordinator.Active.Members}/8 · {coordinator.Active.Access}\n" : string.Empty) + coordinator.Status;
    }
}
