using Godot;

namespace Trackstorm.Client.Online;

/// <summary>Unified public/locked browser; only whitelisted row data reaches the UI.</summary>
internal sealed partial class OnlineLobbyPanel : VBoxContainer
{
    private readonly Label _identity = new() { Name = "EosState", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label _hostReason = new() { Name = "HostReason", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button _login = new() { Text = "EOS dev login / Retry" };
    private readonly Button _logout = new() { Text = "EOS logout" };
    private readonly LineEdit _search = new() { PlaceholderText = "Search lobbies", MaxLength = 48 };
    private readonly LineEdit _name = new() { PlaceholderText = "Lobby name (1–48 characters)", MaxLength = 96 };
    private readonly CheckButton _locked = new() { Text = "Locked / Private" };
    private readonly LineEdit _credential = new() { PlaceholderText = "Access code (4–64 characters)", Secret = true, MaxLength = 64 };
    private readonly LineEdit _joinCredential = new() { PlaceholderText = "Enter lobby access code", Secret = true, MaxLength = 64 };
    private readonly Button _submit = new() { Text = "Join locked lobby" };
    private readonly Button _refresh = new() { Text = "Refresh" };
    private readonly Button _host = new() { Text = "Host Game" };
    private readonly Button _rename = new() { Text = "Rename lobby (host)" };
    private readonly Button _leave = new() { Text = "Leave / Close online lobby" };
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer _rows = new();
    private LobbyRow[] _rendered = Array.Empty<LobbyRow>();
    private string? _selected;
    private OnlineLobbyCoordinator? _previous;

    /// <summary>Returns the current authenticated coordinator, or absence while offline.</summary>
    internal Func<OnlineLobbyCoordinator?> Coordinator { get; set; } = () => null;

    /// <summary>Authentication presentation from the application identity owner.</summary>
    internal Func<EosLobbyStatus> IdentityStatus { get; set; } = () => EosLobbyStatus.Unavailable;

    /// <summary>Requests explicit authentication or retry.</summary>
    internal Action Login { get; set; } = () => { };

    /// <summary>Requests logout after membership cleanup.</summary>
    internal Action Logout { get; set; } = () => { };
    /// <summary>Gameplay-aware leave action supplied by session composition.</summary>
    internal Action? LeaveSession { get; set; }

    /// <inheritdoc />
    public override void _Ready()
    {
        AddChild(_identity);
        var identityControls = new HBoxContainer();
        identityControls.AddChild(_login);
        identityControls.AddChild(_logout);
        AddChild(identityControls);
        _login.Pressed += () => Login();
        _logout.Pressed += () => Logout();
        AddChild(_search);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 160), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_rows);
        AddChild(scroll);
        AddChild(_refresh);
        AddChild(_joinCredential);
        AddChild(_submit);
        AddChild(_name);
        AddChild(_locked);
        AddChild(_credential);
        AddChild(_host);
        AddChild(_hostReason);
        AddChild(_rename);
        AddChild(_leave);
        AddChild(_status);
        _search.TextChanged += text =>
        {
            if (Coordinator() is { } coordinator)
            {
                coordinator.Browser.Search = text;
            }
        };
        _refresh.Pressed += () => Coordinator()?.Refresh();
        _host.Pressed += () =>
        {
            Coordinator()?.Create(_name.Text, _locked.ButtonPressed ? LobbyAccess.Locked : LobbyAccess.Public, _credential.Text);
            _credential.Clear();
        };
        _rename.Pressed += () => Coordinator()?.Rename(_name.Text);
        _leave.Pressed += () =>
        {
            if (LeaveSession is not null)
            {
                LeaveSession();
            }
            else
            {
                Coordinator()?.Leave();
            }
        };
        _submit.Pressed += () =>
        {
            if (_selected is not null)
            {
                Coordinator()?.Join(_selected, _joinCredential.Text);
                _joinCredential.Clear();
            }
        };
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        var coordinator = Coordinator();
        var identity = IdentityStatus();
        _identity.Text = identity.Text;
        _login.Visible = identity.CanRetry;
        _logout.Visible = identity.CanLogout;
        if (!ReferenceEquals(_previous, coordinator))
        {
            _selected = null;
            _credential.Clear();
            _joinCredential.Clear();
            _previous = coordinator;
            if (coordinator is not null)
            {
                coordinator.Browser.Search = _search.Text;
                coordinator.Refresh();
            }
        }

        bool active = coordinator?.Active is not null;
        bool busy = coordinator?.Busy == true;
        _search.Visible = !active;
        _rows.GetParent<Control>().Visible = !active;
        _refresh.Visible = !active;
        _refresh.Disabled = coordinator is null || busy;
        _host.Visible = !active;
        _host.Disabled = !identity.Online || coordinator is null || coordinator.CanLeave;
        _hostReason.Visible = _host.Visible && _host.Disabled;
        _hostReason.Text = coordinator?.CanLeave == true ? coordinator.Status : identity.HostReason.Length > 0 ? identity.HostReason : "Initializing EOS lobby services…";
        _locked.Visible = !active;
        _credential.Visible = !active && _locked.ButtonPressed;
        _name.Visible = !active || coordinator?.IsHost == true;
        _rename.Visible = active && coordinator?.IsHost == true;
        _rename.Disabled = busy;
        _leave.Visible = coordinator?.CanLeave == true;
        _joinCredential.Visible = _submit.Visible = !active && _selected is not null;
        _submit.Disabled = busy || coordinator is null;
        _status.Text = coordinator is null ? string.Empty :
            (active ? $"{coordinator.Active!.Name} · {coordinator.Active.Members}/8 · {coordinator.Active.Access}\n" : string.Empty) + coordinator.Status;
        var rows = coordinator?.Browser.Rows.ToArray() ?? Array.Empty<LobbyRow>();
        if (!_rendered.SequenceEqual(rows))
        {
            foreach (Node child in _rows.GetChildren())
            {
                _rows.RemoveChild(child);
                child.QueueFree();
            }

            foreach (var row in rows)
            {
                var line = new HBoxContainer();
                line.AddChild(new Label { Text = row.Name, TooltipText = row.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
                line.AddChild(new Label { Text = $"{row.Members}/{row.Capacity}", CustomMinimumSize = new Vector2(48, 0) });
                line.AddChild(new Label { Text = row.Access == LobbyAccess.Locked ? "LOCKED" : "Public", CustomMinimumSize = new Vector2(76, 0) });
                var button = new Button { Text = row.Joinable ? "Join" : "Unavailable", TooltipText = row.VersionMismatch.Length > 0 ? row.VersionMismatch : row.Name, CustomMinimumSize = new Vector2(100, 0), Disabled = !row.Joinable };
                button.Pressed += () =>
                {
                    if (row.Access == LobbyAccess.Locked)
                    {
                        _selected = row.Id;
                        _joinCredential.Clear();
                    }
                    else
                    {
                        _selected = null;
                        Coordinator()?.Join(row.Id);
                    }
                };
                line.AddChild(button);
                _rows.AddChild(line);
                if (row.VersionMismatch.Length > 0)
                {
                    _rows.AddChild(new Label { Text = row.VersionMismatch, AutowrapMode = TextServer.AutowrapMode.WordSmart });
                }
            }

            _rendered = rows;
        }
    }
}
