using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Input;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Frontend;

/// <summary>Disposable staging projection; membership, ownership and match entry remain session-owned.</summary>
internal sealed partial class JoinedLobby : Control
{
    private static readonly Vector2[] Feet = [new(620, 405), new(415, 432), new(817, 425), new(235, 459), new(1035, 454), new(132, 515), new(870, 527), new(1143, 526)];
    private readonly Dictionary<ulong, LobbyShowcase> _cars = new();
    private readonly Dictionary<InputAction, bool> _held = new();
    private readonly Control _canvas = new() { Size = new Vector2(1280, 720), MouseFilter = MouseFilterEnum.Ignore };
    private readonly SubViewport _view = new() { Size = new Vector2I(1280, 720), OwnWorld3D = true, TransparentBg = true };
    private readonly Node3D _world = new();
    private readonly Camera3D _camera = new() { Projection = Camera3D.ProjectionType.Orthogonal, Size = 12, Position = new Vector3(0, 13, 70), Current = true };
    private readonly Button _start = new() { Text = "Start" };
    private readonly Button _ready = new() { Text = "Ready" };
    private readonly Button _map = new() { Text = "Map Select" };
    private readonly Button _lobbySettings = new() { Text = "Lobby Settings" };
    private readonly Button _settings = new() { Text = "Settings" };
    private readonly Button _quit = new() { Text = "Quit to Main Menu" };
    private readonly Label _status = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Label _count = new();
    private readonly Label _currentMap = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextureRect _preview = new() { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore };
    private LobbyDialog _dialog = null!;
    private ulong _session;
    private ulong _epoch;
    private MatchMap? _shownMap;
    internal DevelopmentSession Session { get; init; } = null!;
    internal IReadOnlyDictionary<ulong, LobbyShowcase> Cars => _cars;
    internal Func<ulong, Node3D> CreateVehicle { get; set; } = id => Vehicles.VehicleVisual.Create(new StandardMaterial3D { AlbedoColor = new Color(new[] { "bb523b", "bc933e", "688590", "76724c", "88718d", "627e57", "996342", "787d88" }[(int)(id % 8)]), Metallic = 0.65f, Roughness = 0.5f });

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var letterbox = new ColorRect { Color = new Color("0e0d0c"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(letterbox);
        letterbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_canvas);
        _canvas.AddChild(new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Texture = GD.Load<Texture2D>("res://assets/frontend/lobby/CarnageCircus.png"), Size = new Vector2(1280, 720), MouseFilter = MouseFilterEnum.Ignore });
        var display = new SubViewportContainer { Size = new Vector2(1280, 720), MouseFilter = MouseFilterEnum.Ignore };
        _canvas.AddChild(display);
        display.AddChild(_view);
        _view.AddChild(_world);
        _world.AddChild(_camera);
        _camera.LookAt(new Vector3(0, 1, 0));
        _world.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("c1c5d0"), AmbientLightEnergy = 0.7f, TonemapMode = Godot.Environment.ToneMapper.Filmic } });
        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, -30, 0), LightColor = new Color("ffd5a0"), LightEnergy = 1.7f });
        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-25, 145, 0), LightColor = new Color("ff995d"), LightEnergy = 1.1f });
        var mapPanel = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        mapPanel.AddThemeStyleboxOverride("panel", Plate());
        Place(mapPanel, 958, 22, 300, 177);
        Place(new Label { Text = "CURRENT MAP", HorizontalAlignment = HorizontalAlignment.Center }, 968, 30, 280, 30, 23);
        Place(_preview, 979, 66, 258, 88);
        Place(_currentMap, 970, 159, 276, 30, 23);
        Place(_count, 24, 20, 280, 26, 17);
        Place(_status, 130, 569, 1020, 36, 19);
        _status.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _status.AddThemeConstantOverride("shadow_outline_size", 5);
        foreach (var button in BottomButtons) Place(button, 0, 614, 224, 65, 23);
        _start.Modulate = new Color("ffaaa0");
        _ready.Modulate = new Color("ffaaa0");
        _dialog = new LobbyDialog(this, _canvas);
        _start.Pressed += () => { if (CanAct) Session.StartFromLobby(); };
        _ready.Pressed += () => { if (CanAct && Session.Lobby is { Authority: null, State: { } state } lobby) lobby.Request(LobbyCommand.Ready, !state.Players.Single(player => player.Id == lobby.LocalPlayerId).Ready); };
        _map.Pressed += () => { if (CanHostAct) _dialog.ShowMaps(); };
        _lobbySettings.Pressed += () => { if (CanHostAct) _dialog.ShowSettings(); };
        _settings.Pressed += () => { if (CanNavigate) Session.OpenSettings(); };
        _quit.Pressed += () => { if (CanNavigate) Session.ReturnToMainMenu(); };
        Resized += Layout;
        Layout();
        Refresh();
    }

    private Button[] BottomButtons => [_start, _ready, _map, _lobbySettings, _settings, _quit];
    private bool CanNavigate => Session.Stage == ApplicationStage.Lobby && !Session.OverlayOpen();
    internal bool CanAct => CanNavigate && Session.Lobby is { Reconnecting: false, Failure.Length: 0 } && Session.Lobby.Migration?.Frozen != true;
    internal bool CanHostAct => CanAct && Session.Lobby?.Authority is not null;

    public override void _Process(double delta)
    {
        Refresh();
        SampleNavigation(Visible && CanNavigate);
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible || !CanNavigate) return;
        SampleNavigation(true);
        if (GetViewport().GuiGetFocusOwner() is LineEdit && @event is InputEventKey { Keycode: Key.Left or Key.Right or Key.Home or Key.End }) return;
        if (Session.NavigationInput is not null && (@event.IsAction("ui_accept") || @event.IsAction("ui_up") || @event.IsAction("ui_down") || @event.IsAction("ui_left") || @event.IsAction("ui_right") || @event.IsAction("ui_cancel"))) GetViewport().SetInputAsHandled();
    }

    internal void Refresh()
    {
        Visible = Session.Stage == ApplicationStage.Lobby;
        _view.RenderTargetUpdateMode = Visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        var state = Session.Lobby?.State;
        // Keep existing showcases through loading/migration; clear only on membership/session change.
        var players = state?.Players.Where(player => player.Connected).ToArray() ?? [];
        foreach (ulong id in _cars.Keys.Where(id => state?.Session != _session || players.All(player => player.Id != id)).ToArray())
        {
            _cars[id].Dispose();
            _cars.Remove(id);
        }
        if (state?.Session != _session || state?.AuthorityEpoch != _epoch || !CanHostAct) _dialog.Close();
        _session = state?.Session ?? 0;
        _epoch = state?.AuthorityEpoch ?? 0;
        if (!Visible || state is null) return;
        bool host = Session.Lobby!.Authority is not null;
        foreach (var player in players)
        {
            if (!_cars.TryGetValue(player.Id, out var car))
            {
                int slot = Enumerable.Range(0, 8).First(index => _cars.Values.All(value => value.Slot != index));
                car = new LobbyShowcase(_world, _canvas, CreateVehicle(player.Id), slot);
                _cars.Add(player.Id, car);
                ulong id = player.Id;
                car.Target.Pressed += () => { if (CanHostAct && id != Session.Lobby!.State!.CurrentHostId) _dialog.ShowParticipant(id); };
                _dialog.Raise();
            }
            Vector3 origin = _camera.ProjectRayOrigin(Feet[car.Slot]);
            Vector3 ray = _camera.ProjectRayNormal(Feet[car.Slot]);
            if (!origin.IsFinite() || !ray.IsFinite() || Math.Abs(ray.Y) < 0.0001f) continue;
            float scale = car.Slot >= 5 ? 1.02f : 0.85f;
            car.Root.Scale = Vector3.One * scale;
            car.Root.Position = origin + ray * (-origin.Y / ray.Y) + new Vector3(0, 0.9f * scale, 0);
            float side = Feet[car.Slot].X - 640;
            car.Root.RotationDegrees = new Vector3(0, 180 + Math.Sign(side) * 22 + side / 40, 0);
            Vector2 point = _camera.UnprojectPosition(car.Root.Position + new Vector3(0, 1.7f * scale, 0));
            if (!point.IsFinite()) continue;
            car.Target.Position = new Vector2(Math.Clamp(point.X - 74, 12, 1120), point.Y - 40 + (car.Slot >= 5 ? 26 : 0));
            car.Target.Text = player.Name + "\n" + (player.Id == state.CurrentHostId ? "★ HOST" : player.Ready ? "✓ READY" : "− NOT READY");
            car.Target.TooltipText = player.Name + (player.Id == Session.Lobby.LocalPlayerId ? " (you)" : "") + (host && player.Id != state.CurrentHostId ? " — select player" : "");
            car.Target.Disabled = !CanHostAct || player.Id == state.CurrentHostId || _dialog.Visible;
            var color = new Color(player.Id == state.CurrentHostId ? "ffcc70" : player.Ready ? "a3df80" : "ff8a79");
            car.Target.AddThemeColorOverride("font_color", color);
            car.Target.AddThemeColorOverride("font_disabled_color", color);
        }
        _dialog.ValidateTarget();
        _count.Text = $"{players.Length} / 8 CONNECTED";
        _start.Visible = _map.Visible = _lobbySettings.Visible = host;
        _ready.Visible = !host;
        foreach (var button in BottomButtons) button.Disabled = !CanAct || _dialog.Visible;
        _settings.Disabled = _quit.Disabled = !CanNavigate || _dialog.Visible;
        _start.Disabled |= !state.CanStart;
        _ready.Text = players.FirstOrDefault(player => player.Id == Session.Lobby.LocalPlayerId)?.Ready == true ? "Not Ready" : "Ready";
        Button[] visibleButtons = BottomButtons.Where(button => button.Visible).ToArray();
        float total = visibleButtons.Length * 224 + (visibleButtons.Length - 1) * 12;
        for (int i = 0; i < visibleButtons.Length; i++) visibleButtons[i].Position = new Vector2((1280 - total) / 2 + i * 236, 614);
        _currentMap.Text = state.Map == MatchMap.OldMap ? "OLD MAP" : "NEW MAP";
        if (_shownMap != state.Map)
        {
            _shownMap = state.Map;
            string path = $"res://assets/frontend/lobby/{state.Map}.png";
            _preview.Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        }
        _status.Text = Session.LobbyNotice.Length > 0 ? Session.LobbyNotice : !CanAct ? Session.Lobby.Migration?.Status ?? "Waiting for session…" : host ? state.CanStart ? "All players ready · Start when you're ready" : "Waiting for players to ready up" : "Ready up when you're prepared to enter the arena";
    }

    internal void RefreshVehicle(ulong id)
    {
        if (_cars.TryGetValue(id, out var car)) car.Replace(CreateVehicle(id));
    }

    private void SampleNavigation(bool dispatch)
    {
        if (Session.NavigationInput is not { } input) return;
        foreach (var action in new[] { InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight, InputAction.MenuAccept, InputAction.MenuCancel })
        {
            bool held = input.Enabled && input.Bindings.Strength(action, input.DeadZone) > 0.5f;
            bool previous = _held.GetValueOrDefault(action);
            _held[action] = held;
            if (!dispatch || !held || previous) continue;
            if (GetViewport().GuiGetFocusOwner() is LineEdit && action is InputAction.MenuLeft or InputAction.MenuRight) continue;
            if (action == InputAction.MenuCancel) { _dialog.Close(); continue; }
            Control[] controls = _dialog.Visible ? _dialog.Controls : _cars.Values.OrderBy(car => car.Slot).Select(car => car.Target).Concat(BottomButtons).Where(button => button.Visible && !button.Disabled).Cast<Control>().ToArray();
            MenuFocusNavigation.Navigate(action, controls, GetViewport().GuiGetFocusOwner());
        }
    }

    private void Layout()
    {
        float scale = Math.Min(Size.X / 1280, Size.Y / 720);
        _canvas.Scale = Vector2.One * scale;
        _canvas.Position = (Size - new Vector2(1280, 720) * scale) / 2;
    }

    private void Place(Control control, float x, float y, float width, float height, int fontSize = 20)
    {
        control.Position = new Vector2(x, y);
        control.Size = new Vector2(width, height);
        control.AddThemeFontSizeOverride("font_size", fontSize);
        control.AddThemeColorOverride("font_color", new Color("efdfbf"));
        control.AddThemeFontOverride("font", new SystemFont { FontNames = ["Bahnschrift", "Arial"], FontWeight = 600 });
        if (control is Button button) Style(button);
        _canvas.AddChild(control);
    }

    internal static StyleBoxTexture Plate() => new() { Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://assets/frontend/main-menu/Plate.png"), Region = new Rect2(0, 75, 2172, 540) }, ContentMarginLeft = 12, ContentMarginRight = 12 };

    internal static void Style(Button button)
    {
        foreach (string state in new[] { "normal", "disabled", "hover", "pressed", "focus" })
        {
            var plate = Plate();
            plate.ModulateColor = new Color(state is "hover" or "focus" or "pressed" ? "ffd19b" : "a69a8b");
            button.AddThemeStyleboxOverride(state, plate);
        }
        button.AddThemeColorOverride("font_color", new Color("efdfbf"));
        button.AddThemeColorOverride("font_disabled_color", new Color("928b80"));
    }
}
