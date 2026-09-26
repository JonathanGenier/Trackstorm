using Godot;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Frontend;

/// <summary>Host-only transient drafts and confirmation. Every submission rechecks session authority.</summary>
internal sealed class LobbyDialog
{
    private readonly JoinedLobby _owner;
    private readonly Control _canvas;
    private readonly ColorRect _shade = new() { Color = new Color(0, 0, 0, 0.68f), Size = new Vector2(1280, 720), Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
    private readonly VBoxContainer _content = new() { Position = new Vector2(402, 237), Size = new Vector2(476, 280) };
    private readonly List<Control> _controls = new();
    private readonly Panel _backing = new() { Position = new Vector2(375, 210), Size = new Vector2(530, 335), MouseFilter = Control.MouseFilterEnum.Stop };
    private ulong? _target;
    private ulong _session;
    private ulong _epoch;
    private Action? _refresh;
    internal bool Visible => _shade.Visible;
    internal Control[] Controls => _controls.ToArray();

    internal LobbyDialog(JoinedLobby owner, Control canvas)
    {
        _owner = owner;
        _canvas = canvas;
        canvas.AddChild(_shade);
        _backing.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("191512"), BorderColor = new Color("92704e"), BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3 });
        _shade.AddChild(_backing);
        _shade.AddChild(_content);
        _content.AddThemeConstantOverride("separation", 12);
    }

    internal void Raise() => _canvas.MoveChild(_shade, _canvas.GetChildCount() - 1);
    internal void Close()
    {
        _shade.Visible = false;
        _target = null;
    }

    internal void ValidateTarget()
    {
        if (Visible && (!Authorized || _target is { } id && (_owner.Session.Lobby!.State!.Players.All(player => player.Id != id || !player.Connected) || id == _owner.Session.Lobby.State.CurrentHostId))) Close();
        if (Visible) _refresh?.Invoke();
    }

    private bool Authorized => _owner.CanHostAct && _owner.Session.Lobby!.State is { } state && state.Session == _session && state.AuthorityEpoch == _epoch;

    private void Begin(string title)
    {
        foreach (Node child in _content.GetChildren()) { _content.RemoveChild(child); child.QueueFree(); }
        _controls.Clear();
        _backing.Size = new Vector2(530, 335);
        _refresh = null;
        _target = null;
        _session = _owner.Session.Lobby!.State!.Session;
        _epoch = _owner.Session.Lobby.State.AuthorityEpoch;
        _shade.Visible = true;
        Raise();
        Label(title, 26);
    }

    private Label Label(string text, int size = 19)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color("efdfbf"));
        _content.AddChild(label);
        return label;
    }

    private void Button(string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 43) };
        JoinedLobby.Style(button);
        button.AddThemeFontSizeOverride("font_size", 21);
        button.Pressed += () => { if (Authorized) action(); else Close(); };
        _content.AddChild(button);
        _controls.Add(button);
    }

    private void FocusCancel() => (_controls.OfType<Button>().LastOrDefault(button => button.Text == "Cancel") ?? _controls.Last()).GrabFocus();

    internal void ShowParticipant(ulong id, bool confirm = false)
    {
        var player = _owner.Session.Lobby?.State?.Players.SingleOrDefault(player => player.Id == id && player.Connected);
        if (!_owner.CanHostAct || player is null || id == _owner.Session.Lobby!.State!.CurrentHostId) return;
        Begin(confirm ? "REMOVE PLAYER?" : "PLAYER SELECTED");
        _target = id;
        Label($"{player.Name} · Player {id}", 23);
        if (confirm) Label("This player will be removed from the lobby.");
        Button(confirm ? "Confirm Kick" : "Kick Player", () =>
        {
            ValidateTarget();
            if (!Visible) return;
            if (confirm) { _owner.Session.Lobby!.Kick(id); Close(); }
            else ShowParticipant(id, true);
        });
        Button("Cancel", Close);
        FocusCancel();
    }

    internal void ShowMaps()
    {
        Begin("MAP SELECT");
        Label("Current map: " + (_owner.Session.Lobby!.State!.Map == MatchMap.OldMap ? "Old Map" : "New Map"));
        Button("Old Map", () => { _owner.Session.Lobby!.SelectMap(MatchMap.OldMap); Close(); });
        Button("New Map", () => { _owner.Session.Lobby!.SelectMap(MatchMap.NewMap); Close(); });
        Button("Cancel", Close);
        FocusCancel();
    }

    internal void ShowSettings()
    {
        Begin("LOBBY SETTINGS");
        var config = _owner.Session.Lobby!.Authority!.Configuration.Configuration.Match;
        var mode = new OptionButton { CustomMinimumSize = new Vector2(0, 42) };
        mode.AddItem("First to Target", 0);
        mode.AddItem("Circus", 1);
        mode.Select((int)config.Mode);
        JoinedLobby.Style(mode);
        _content.AddChild(mode);
        _controls.Add(mode);
        var target = new HSlider { MinValue = 1, MaxValue = Math.Max(100, config.KillTarget), Step = 1, Value = config.KillTarget, CustomMinimumSize = new Vector2(0, 26) };
        var targetLabel = Label($"Kill target (First to Target only): {config.KillTarget}");
        target.ValueChanged += value => targetLabel.Text = $"Kill target (First to Target only): {value:0}";
        _content.AddChild(target);
        _controls.Add(target);
        Button("Apply", () =>
        {
            if (_owner.Session.ConfigureLobbyOptions(new Dictionary<string, double> { ["match.mode"] = mode.GetSelectedId(), ["match.kill_target"] = target.Value }, out string error)) Close();
            else targetLabel.Text = error;
        });
        Button("Cancel", Close);
        if (_owner.Session.OnlineCoordinator() is { IsHost: true })
        {
            _backing.Size = new Vector2(530, 400);
            Button("Rename lobby", ShowRename);
        }
        FocusCancel();
    }

    private void ShowRename()
    {
        var coordinator = _owner.Session.OnlineCoordinator();
        if (coordinator is not { IsHost: true, Active: not null }) return;
        string lobbyId = coordinator.Active.Id;
        Begin("RENAME LOBBY");
        var name = new LineEdit { Text = coordinator.Active.Name, PlaceholderText = "Lobby name", MaxLength = 48, CustomMinimumSize = new Vector2(0, 43) };
        _content.AddChild(name);
        _controls.Add(name);
        var status = Label("Only the lobby name will change.");
        Button("Save name", () =>
        {
            if (!ReferenceEquals(coordinator, _owner.Session.OnlineCoordinator()) || !coordinator.IsHost || coordinator.Active?.Id != lobbyId) { Close(); return; }
            coordinator.Rename(name.Text);
            status.Text = coordinator.Status;
        });
        Button("Back", ShowSettings);
        _refresh = () =>
        {
            if (!ReferenceEquals(coordinator, _owner.Session.OnlineCoordinator()) || !coordinator.IsHost || coordinator.Active?.Id != lobbyId) { Close(); return; }
            status.Text = coordinator.Status;
            ((Button)_controls[1]).Disabled = coordinator.Busy;
        };
        name.GrabFocus();
    }
}
