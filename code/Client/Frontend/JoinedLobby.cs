using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Input;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Frontend;

/// <summary>Reconstructable lobby stage. The session remains the sole membership and lifecycle owner.</summary>
internal sealed partial class JoinedLobby : Control
{
    private readonly Dictionary<ulong, LobbyShowcase> _cars = new();
    private readonly Dictionary<InputAction, bool> _held = new();
    private readonly Control _canvas = new() { Size = new Vector2(1280, 720), MouseFilter = MouseFilterEnum.Ignore };
    private readonly SubViewport _view = new() { Size = new Vector2I(1280, 720), OwnWorld3D = true, TransparentBg = false };
    private readonly Node3D _world = new();
    private readonly Camera3D _camera = new() { Projection = Camera3D.ProjectionType.Orthogonal, Size = 19, Position = new Vector3(0, 18, 30), Current = true };
    private readonly Button _start = new() { Text = "Start" };
    private readonly Button _ready = new() { Text = "Ready" };
    private readonly Button _settings = new() { Text = "Settings" };
    private readonly Button _quit = new() { Text = "Quit to Main Menu" };
    private readonly Button _kick = new() { Text = "Kick Player" };
    private readonly OptionButton _map = new();
    private readonly Label _status = new() { HorizontalAlignment = HorizontalAlignment.Center, ClipText = true };
    private readonly Label _count = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private ulong? _selected;
    private string _notice = string.Empty;
    internal DevelopmentSession Session { get; init; } = null!;
    internal IReadOnlyDictionary<ulong, LobbyShowcase> Cars => _cars;
    // Future selected-vehicle projection can replace the factory and refresh one existing showcase.
    internal Func<ulong, Node3D> CreateVehicle { get; set; } = _ => Vehicles.VehicleVisual.Create(new StandardMaterial3D { AlbedoColor = new Color("bb523b"), Metallic = 0.65f, Roughness = 0.5f });

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_canvas);
        var display = new SubViewportContainer { Size = new Vector2(1280, 720), MouseFilter = MouseFilterEnum.Ignore };
        _canvas.AddChild(display);
        display.AddChild(_view);
        _view.AddChild(_world);
        _world.AddChild(_camera);
        _camera.LookAt(new Vector3(0, 0.5f, 0));
        BuildStage();
        var backing = new ColorRect { Color = new Color(0.035f, 0.03f, 0.03f, 0.94f), Size = new Vector2(1280, 116), MouseFilter = MouseFilterEnum.Ignore };
        _canvas.AddChild(backing);
        var crest = new TextureRect { Texture = GD.Load<Texture2D>("res://assets/frontend/main-menu/Header.png"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Position = new Vector2(30, 12), Size = new Vector2(270, 90), MouseFilter = MouseFilterEnum.Ignore };
        _canvas.AddChild(crest);
        var title = new Label { Text = "C A R N A G E   C I R C U S", HorizontalAlignment = HorizontalAlignment.Center };
        Place(title, 320, 24, 870, 52, 36);
        title.AddThemeFontOverride("font", new SystemFont { FontNames = ["Impact", "Arial Black"], FontWeight = 800 });
        Place(_count, 475, 76, 540, 30, 18);
        Place(_status, 160, 585, 960, 30, 18);
        Place(_map, 36, 650, 210, 44, 20);
        _map.AddItem("Old Map", (int)MatchMap.OldMap);
        _map.AddItem("New Map", (int)MatchMap.NewMap);
        _map.ItemSelected += index => { if (!Blocked) Session.Lobby?.SelectMap((MatchMap)_map.GetItemId((int)index)); };
        Place(_start, 266, 650, 170, 44, 22);
        Place(_ready, 266, 650, 170, 44, 22);
        Place(_kick, 456, 650, 220, 44, 20);
        Place(_settings, 696, 650, 190, 44, 20);
        Place(_quit, 906, 650, 338, 44, 20);
        _start.Pressed += () => { if (!Blocked) _notice = Session.StartFromLobby() ? string.Empty : "Start unavailable. Wait for connected players, then retry."; };
        _ready.Pressed += () => { if (!Blocked && Session.Lobby is { Authority: null, State: { } state } lobby) lobby.Request(LobbyCommand.Ready, !state.Players.Single(player => player.Id == lobby.LocalPlayerId).Ready); };
        _settings.Pressed += () => { if (!Blocked) Session.OpenSettings(); };
        _quit.Pressed += () => { if (!Blocked) Session.ReturnToMainMenu(); };
        _kick.Pressed += () => { if (!Blocked && _selected is { } id) Session.Lobby?.Kick(id); };
        Resized += Layout;
        Layout();
        Refresh();
    }

    private bool Blocked => Session.OverlayOpen() || Session.Stage != ApplicationStage.Lobby;

    public override void _Process(double delta)
    {
        Refresh();
        SampleNavigation(Visible && !Blocked);
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible || Blocked) return;
        SampleNavigation(true);
        if (Session.NavigationInput is not null && (@event.IsAction("ui_accept") || @event.IsAction("ui_up") || @event.IsAction("ui_down") || @event.IsAction("ui_left") || @event.IsAction("ui_right"))) GetViewport().SetInputAsHandled();
    }

    internal void Refresh()
    {
        Visible = Session.Stage == ApplicationStage.Lobby;
        _view.RenderTargetUpdateMode = Visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        var state = Visible ? Session.Lobby?.State : null;
        var players = state?.Players.Where(player => player.Connected).OrderBy(player => player.Id).ToArray() ?? [];
        foreach (ulong id in _cars.Keys.Where(id => players.All(player => player.Id != id)).ToArray())
        {
            _cars[id].Dispose();
            _cars.Remove(id);
        }
        if (!Visible) { _selected = null; _notice = string.Empty; return; }
        bool host = Session.Lobby?.Authority is not null;
        bool locked = Blocked || Session.Lobby?.Reconnecting == true || Session.Lobby?.Migration?.Frozen == true || Session.Lobby?.Failure.Length > 0;
        if (_selected is { } selected && (!_cars.ContainsKey(selected) || selected == state!.CurrentHostId || !host)) _selected = null;
        for (int index = 0; index < players.Length; index++)
        {
            var player = players[index];
            if (!_cars.TryGetValue(player.Id, out var car))
            {
                car = new LobbyShowcase(_world, _canvas, CreateVehicle(player.Id));
                _cars.Add(player.Id, car);
                ulong id = player.Id;
                car.Target.Pressed += () => { if (!Blocked && Session.Lobby?.Authority is not null && id != Session.Lobby.State!.CurrentHostId) _selected = id; };
            }
            var position = new Vector3(-8.7f + index % 4 * 5.8f, 0.9f, index < 4 ? -4.2f : 5.2f);
            car.Root.Position = position;
            var point = _camera.UnprojectPosition(position + new Vector3(0, 2.5f, 0));
            car.Target.Position = point - new Vector2(100, 38);
            car.Target.AddThemeFontSizeOverride("font_size", _canvas.Scale.X < 0.75f ? 26 : 18);
            car.Target.Text = player.Name + (player.Id == Session.Lobby!.LocalPlayerId ? " · YOU" : "") + "\n" + (player.Id == state!.CurrentHostId ? "HOST" : player.Ready ? "READY" : "NOT READY");
            car.Target.TooltipText = player.Name + (host && player.Id != state.CurrentHostId ? " — select to kick" : "");
            car.Target.Disabled = locked || !host || player.Id == state.CurrentHostId;
            car.Target.Modulate = player.Id == _selected ? new Color("ffc876") : Colors.White;
        }
        _count.Text = $"{players.Length} / 8 CONNECTED  ·  PRE-MATCH STAGING";
        _start.Visible = host;
        _ready.Visible = !host;
        _start.Disabled = locked || players.Any(player => player.Id != state!.CurrentHostId && !player.Ready);
        _ready.Disabled = locked;
        _ready.Text = players.FirstOrDefault(player => player.Id == Session.Lobby!.LocalPlayerId)?.Ready == true ? "Not Ready" : "Ready";
        _kick.Visible = host;
        _kick.Disabled = locked || _selected is null;
        _settings.Disabled = _quit.Disabled = Blocked;
        _map.Disabled = locked || !host;
        _map.Select((int)state!.Map);
        _status.Text = _notice.Length > 0 ? _notice : locked ? Session.Lobby?.Migration?.Status ?? "Waiting for session…" : _selected is { } target ? $"Selected: {players.Single(player => player.Id == target).Name}" : host ? "Select a nameplate to kick · Waiting for everyone to ready up" : "Ready up when you're prepared to enter the arena";
    }

    internal void RefreshVehicle(ulong id)
    {
        if (_cars.TryGetValue(id, out var car)) car.Replace(CreateVehicle(id));
    }

    private void SampleNavigation(bool dispatch)
    {
        if (Session.NavigationInput is not { } input) return;
        foreach (var action in new[] { InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight, InputAction.MenuAccept })
        {
            bool held = input.Enabled && input.Bindings.Strength(action, input.DeadZone) > 0.5f;
            bool previous = _held.GetValueOrDefault(action);
            _held[action] = held;
            if (!dispatch || !held || previous) continue;
            Control[] controls = _cars.Values.Select(car => car.Target).Concat(new Button[] { _map, _start, _ready, _kick, _settings, _quit }).Where(button => button.Visible && !button.Disabled).Cast<Control>().ToArray();
            MenuFocusNavigation.Navigate(action, controls, GetViewport().GuiGetFocusOwner());
        }
    }

    private void Layout()
    {
        float scale = Math.Min(Size.X / 1280, Size.Y / 720);
        _canvas.Scale = Vector2.One * scale;
        _canvas.Position = (Size - new Vector2(1280, 720) * scale) / 2;
        foreach (var button in new Button[] { _map, _start, _ready, _kick, _settings, _quit }) button.AddThemeFontSizeOverride("font_size", scale < 0.75f ? 28 : 20);
        _status.AddThemeFontSizeOverride("font_size", scale < 0.75f ? 24 : 18);
    }

    private void Place(Control control, float x, float y, float width, float height, int fontSize)
    {
        control.Position = new Vector2(x, y);
        control.Size = new Vector2(width, height);
        control.AddThemeFontSizeOverride("font_size", fontSize);
        control.AddThemeColorOverride("font_color", new Color("efdfbf"));
        if (control is Button button) Style(button);
        _canvas.AddChild(control);
    }

    internal static void Style(Button button)
    {
        var texture = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://assets/frontend/main-menu/Plate.png"), Region = new Rect2(0, 75, 2172, 540) };
        foreach (string state in new[] { "normal", "disabled", "hover", "pressed", "focus" })
        {
            button.AddThemeStyleboxOverride(state, new StyleBoxTexture { Texture = texture, ModulateColor = new Color(state is "hover" or "focus" or "pressed" ? "ffd19b" : "a69a8b"), ContentMarginLeft = 12, ContentMarginRight = 12 });
        }
        button.AddThemeColorOverride("font_disabled_color", new Color("dbceb5"));
    }

    private void BuildStage()
    {
        _world.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("131419"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("d7c1a4"), AmbientLightEnergy = 0.3f, TonemapMode = Godot.Environment.ToneMapper.Filmic } });
        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightColor = new Color("ffe3b1"), LightEnergy = 0.8f, ShadowEnabled = true });
        var rust = GD.Load<Material>("res://assets/arena/materials/Rust.tres");
        var red = new StandardMaterial3D { AlbedoColor = new Color("751f23"), Roughness = 0.9f };
        var cream = new StandardMaterial3D { AlbedoColor = new Color("bba584"), Roughness = 0.9f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("28272a"), Metallic = 0.65f, Roughness = 0.7f };
        Box(new Vector3(0, -0.25f, 0), new Vector3(33, 0.5f, 27), GD.Load<Material>("res://assets/arena/materials/Asphalt.tres"));
        for (int index = 0; index < 16; index++)
        {
            Box(new Vector3(-15 + index * 2, 4, -12), new Vector3(2, 8, 0.3f), index % 2 == 0 ? red : cream);
            Box(new Vector3(-15 + index * 2, 8.2f, -10), new Vector3(2, 0.2f, 4), index % 2 == 0 ? red : cream);
        }
        for (int index = 0; index < 8; index++)
        {
            var position = new Vector3(-8.7f + index % 4 * 5.8f, 0, index < 4 ? -4.2f : 5.2f);
            Box(position, new Vector3(4.5f, 0.08f, 6), dark);
            Box(position + new Vector3(0, 0.06f, 3), new Vector3(4.5f, 0.08f, 0.16f), cream);
        }
        foreach (float x in new[] { -14f, 14f })
        {
            Box(new Vector3(x, 4, -9), new Vector3(0.65f, 8, 0.65f), rust);
            for (int i = 0; i < 16; i++)
            {
                var link = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.12f, OuterRadius = 0.20f, Rings = 8, RingSegments = 8 }, MaterialOverride = dark, Position = new Vector3(x, 8 - i * 0.35f, -8.5f), RotationDegrees = new Vector3(90, i % 2 * 90, 0) };
                _world.AddChild(link);
            }
            _world.AddChild(new SpotLight3D { Position = new Vector3(x, 9, 2), RotationDegrees = new Vector3(-65, x < 0 ? -25 : 25, 0), LightColor = new Color("ffcf86"), LightEnergy = 0.65f, SpotRange = 30, SpotAngle = 55 });
        }
        Box(new Vector3(0, 7.6f, -9), new Vector3(29, 0.5f, 0.6f), rust);
        var bulb = new StandardMaterial3D { AlbedoColor = new Color("ffe1a0"), EmissionEnabled = true, Emission = new Color("ffc46b"), EmissionEnergyMultiplier = 2 };
        for (int i = 0; i < 29; i++)
            _world.AddChild(new MeshInstance3D { Position = new Vector3(-14 + i, 7.6f, -8.6f), Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 8, Rings = 4 }, MaterialOverride = bulb });
    }

    private void Box(Vector3 position, Vector3 size, Material material) => _world.AddChild(new MeshInstance3D { Position = position, Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
}
