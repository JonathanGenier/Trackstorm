using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Online;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Frontend;

/// <summary>Fixed hanging shell populated from the existing coordinator's discovery projection.</summary>
internal sealed partial class HangingPlayMenu : Control
{
    private static readonly InputAction[] Actions = [InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight, InputAction.MenuAccept, InputAction.MenuCancel];
    private readonly Control _layout = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly Control _rig = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly Control _browser = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer _rows = new();
    private readonly ScrollContainer _scroll = new() { FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly LineEdit _search = new() { Name = "LobbySearch", PlaceholderText = "Search lobbies…", MaxLength = 48 };
    private readonly LineEdit _playerName = new() { Name = "OnlinePlayerName", Text = "Player", PlaceholderText = "Display name", MaxLength = 96, TooltipText = "Your player display name" };
    private readonly Button _filter = new() { Name = "LobbyFilter", Text = "All  ▾", TooltipText = "Filter lobbies" };
    private readonly Button _refresh = new() { Text = "Refresh" };
    private readonly Label _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Label _status = new() { Name = "PlayStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer _modal = new() { Name = "PlayDecision" };
    private readonly PlayMenuSelection _selection = new();
    private readonly Dictionary<InputAction, bool> _held = [];
    private LobbyRow[] _rendered = [];
    private string[] _choices = [];
    private string _page = "";
    private string? _lockedId;
    private bool _picker;
    private int _choice;
    private double _time;
    private float _motion = 2;
    private Action? _hoisted;
    private bool _wasInteractive;
    private InputAction? _repeatAction;
    private double _repeat;
    private OnlineLobbyCoordinator? _previous;
    private Button _host = null!;
    private Button _back = null!;
    private Button _login = null!;
    private Button _direct = null!;
    private Button _resume = null!;
    private bool _dismissedHint;
    private LineEdit? _code;
    private string _modalKey = "";

    internal Func<OnlineLobbyCoordinator?> Coordinator { get; set; } = () => null;
    internal Func<EosLobbyStatus> IdentityStatus { get; set; } = () => EosLobbyStatus.Unavailable;
    internal PlayerInputAdapter? NavigationInput { get; set; }
    internal Func<bool> Blocked { get; set; } = () => false;
    internal Action Back { get; set; } = () => { };
    internal Action Login { get; set; } = () => { };
    internal Action Direct { get; set; } = () => { };
    internal Action CancelAdmission { get; set; } = () => { };
    internal Action<string> PlayerNameChanged { get; set; } = _ => { };
    internal bool Settled => _motion >= 0.82f && _hoisted is null;
    internal bool Interactive => IsVisibleInTree() && Settled && !Blocked();
    internal IReadOnlyList<LobbyRow> VisibleRows => _rendered;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = MakeTheme();
        AddChild(_layout);
        _layout.AddChild(_rig);
        // Separate atlas regions retain the supplied pixels without rendering a flattened mockup.
        Picture("Fame.png", new Rect2(0, 0, 1672, 340), new Rect2(110, 12, 1140, 232));
        Add(_rig, new ColorRect { Color = new Color("151210"), MouseFilter = MouseFilterEnum.Ignore }, new Rect2(78, 355, 1205, 391));
        Picture("Fame.png", new Rect2(0, 350, 1672, 480), new Rect2(0, 328, 1360, 465));
        var flag = Picture("Flag.png", null, new Rect2(970, 200, 305, 173));
        var cloth = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/frontend/play-menu/Fabric.gdshader") };
        flag.Material = cloth;
        _host = ArtButton("Host_button.png", new Rect2(0, 95, 2172, 530), new Rect2(500, 235, 440, 114), "Host Game", () => OpenPage("host"));
        var free = ArtButton("Free_Play_Button.png", new Rect2(0, 60, 2172, 560), new Rect2(90, 240, 390, 105), "Free Play — Coming Soon", () => { });
        free.Disabled = true;
        free.Modulate = new Color(0.6f, 0.6f, 0.6f);
        _back = ArtButton("Back_button.png", new Rect2(0, 90, 2172, 540), new Rect2(35, 791, 280, 70), "Back", () => { if (Interactive) Back(); });
        _rig.AddChild(_browser);
        Place(_browser, new Rect2(82, 380, 1194, 345));
        Add(_browser, _search, new Rect2(0, 0, 650, 46));
        Add(_browser, _filter, new Rect2(675, 0, 325, 46));
        Add(_browser, _refresh, new Rect2(1020, 0, 174, 46));
        _filter.Pressed += TogglePicker;
        _refresh.Pressed += () => { _selection.Reset(); Coordinator()?.Refresh(); };
        _search.TextChanged += _ => { _selection.Reset(); UpdateRows(); };
        var headings = new HBoxContainer();
        Add(_browser, headings, new Rect2(0, 58, 1160, 35));
        AddCells(headings, ["LOBBY NAME", "GAME MODE", "PLAYERS", "BLOCKED", "PING"], true);
        Add(_browser, _scroll, new Rect2(0, 98, 1194, 210));
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows.AddThemeConstantOverride("separation", 3);
        _scroll.AddChild(_rows);
        Add(_browser, _empty, new Rect2(0, 98, 1194, 210));
        _empty.MouseFilter = MouseFilterEnum.Ignore;
        Add(_rig, _modal, new Rect2(135, 410, 1090, 295));
        _modal.AddThemeConstantOverride("separation", 12);
        Add(_rig, _status, new Rect2(85, 699, 1185, 45));
        _status.AddThemeFontSizeOverride("font_size", 21);
        _status.MaxLinesVisible = 2;
        _login = PlainButton("EOS dev login / Retry", () => Login());
        Add(_rig, _playerName, new Rect2(340, 810, 275, 46));
        _playerName.TextChanged += name => PlayerNameChanged(name);
        Add(_rig, _login, new Rect2(640, 810, 325, 46));
        _resume = PlainButton("Check previous session", () => { _dismissedHint = false; Coordinator()?.ResumeRetained(); });
        Add(_rig, _resume, new Rect2(640, 810, 325, 46));
        _direct = PlainButton("Direct-IP / LAN", () => Direct());
        Add(_rig, _direct, new Rect2(985, 810, 295, 46));
        Resized += Layout;
        Layout();
    }

    internal void BeginEntrance()
    {
        _motion = 0;
        _hoisted = null;
        _wasInteractive = false;
        _selection.Reset();
        _page = "";
        _lockedId = null;
        _modalKey = "";
        _dismissedHint = false;
        UpdateMotion();
    }

    internal void Hoist(Action completed)
    {
        if (_hoisted is not null) return;
        _hoisted = completed;
        _motion = 0;
        _selection.Reset();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (IsVisibleInTree()) _motion += (float)delta;
        if (_hoisted is not null && _motion >= 0.34f)
        {
            var completed = _hoisted;
            _hoisted = null;
            completed();
        }
        UpdateMotion();
        if (!IsVisibleInTree())
        {
            Sample(false, delta);
            _wasInteractive = false;
            _page = "";
            _picker = false;
            _lockedId = null;
            _code?.Clear();
            _modalKey = "!";
            return;
        }
        UpdatePresentation();
        bool interactive = Interactive;

        if (interactive && !_wasInteractive) Focusables().FirstOrDefault()?.GrabFocus();
        Sample(interactive && _wasInteractive, delta);
        _wasInteractive = interactive;
    }

    private void UpdatePresentation()
    {
        var coordinator = Coordinator();
        var identity = IdentityStatus();
        if (!ReferenceEquals(coordinator, _previous))
        {
            _previous = coordinator;
            _dismissedHint = false;
            _selection.Select(null);
            if (coordinator is not null) { coordinator.Browser.Search = ""; coordinator.Refresh(); }
        }
        bool retained = coordinator is { HasRetainedDecision: true } || (coordinator?.CanResumeRetained == true && !_dismissedHint);
        bool busy = coordinator?.Busy == true || coordinator?.Active is not null;
        string page = retained ? "retained:" + coordinator!.RetainedDecision : busy ? "admission" : _page;
        _browser.Visible = page.Length == 0;
        _modal.Visible = !_browser.Visible;
        _host.Disabled = !identity.Online || coordinator is null || busy || retained;
        _back.Disabled = busy || retained;
        _host.TooltipText = _host.Disabled ? identity.HostReason.Length > 0 ? identity.HostReason : coordinator?.Status ?? "Initializing online services…" : "Create a lobby";
        _login.Visible = identity.CanRetry && !retained;
        _resume.Visible = !retained && coordinator?.CanResumeRetained == true;
        if (_resume.Visible) _login.Visible = false;
        _playerName.Visible = !retained;
        _playerName.Editable = !busy;
        _direct.Visible = !retained && !busy;
        _refresh.Disabled = coordinator is null || busy || coordinator.Searching;
        _search.Editable = !busy;
        _filter.Disabled = busy;
        _status.Text = retained ? coordinator!.Status : coordinator is null ? identity.Text : coordinator.Status;
        _status.Visible = !retained;
        _status.TooltipText = _status.Text;
        if (page != _modalKey) BuildModal(page);
        if (_modal.GetNodeOrNull<Label>("RetainedStatus") is { } progress) progress.Text = coordinator?.Status ?? string.Empty;
        if (!_browser.Visible) _selection.Reset();
        UpdateRows();
    }

    private void UpdateRows()
    {
        LobbyRow[] source = Coordinator()?.Browser.Rows.ToArray() ?? [];
        string[] choices = PlayMenuSelection.Choices(source);
        if (!_choices.SequenceEqual(choices))
        {
            _choices = choices;
            if (!_choices.Contains(_selection.Filter)) { _selection.Filter = "All"; _selection.Reset(); }
            if (_picker) { _picker = false; _page = ""; _modalKey = "!"; }
        }
        _filter.Text = _selection.Filter + "  ▾";
        LobbyRow[] rows = _selection.Apply(source, _search.Text);
        _empty.Visible = rows.Length == 0;
        _empty.Text = Coordinator() is null ? "Online lobbies are unavailable. See connection status below."
            : Coordinator()?.Searching == true ? "Searching for lobbies…" : source.Length == 0 ? "No lobbies found. Refresh or host a game." : "No lobbies match your search and filter.";
        bool busy = Coordinator()?.Busy == true || Coordinator()?.Active is not null;
        if (_rendered.SequenceEqual(rows))
        {
            foreach (Button button in _rows.GetChildren().OfType<Button>()) button.Disabled = busy || !rows.First(row => row.Id == (string)button.GetMeta("lobby_id")).Joinable;
            return;
        }
        bool hadFocus = GetViewport().GuiGetFocusOwner() is { } rowFocus && _rows.IsAncestorOf(rowFocus);
        _selection.Reset();
        if (!rows.Any(row => row.Id == _selection.Selected)) _selection.Select(null);
        foreach (Node child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        foreach (LobbyRow row in rows)
        {
            var button = new Button { Name = "LobbyRow", CustomMinimumSize = new Vector2(0, 43), Disabled = busy || !row.Joinable, TooltipText = row.VersionMismatch.Length > 0 ? row.VersionMismatch : row.Joinable ? "Double-click or press Accept twice to join " + row.Name : "Lobby unavailable or full" };
            button.SetMeta("lobby_id", row.Id);
            var cells = new HBoxContainer { AnchorRight = 1, AnchorBottom = 1, OffsetLeft = 12, OffsetRight = -12, MouseFilter = MouseFilterEnum.Ignore };
            AddCells(cells, [row.Name, row.GameMode.Length > 0 ? row.GameMode : "Unknown", $"{row.Members} / {row.Capacity}", row.Access == LobbyAccess.Locked ? "Locked" : "Open", row.PingMilliseconds is int ping ? $"{ping} ms" : "—"], false);
            button.AddChild(cells);
            button.FocusEntered += () => _selection.Select(row.Id);
            button.FocusExited += _selection.Reset;
            button.Pressed += () => _selection.Select(row.Id);
            button.GuiInput += input =>
            {
                if (Interactive && input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse && _selection.MousePress(row, mouse.DoubleClick)) Join(row.Id);
            };
            _rows.AddChild(button);
            if (hadFocus && _selection.Selected == row.Id) button.GrabFocus();
        }
        _rendered = rows;
        if (hadFocus && _selection.Selected is null) _search.GrabFocus();
    }

    private void Join(string id)
    {
        if (!Interactive || !_browser.Visible || Coordinator() is not { Busy: false, Active: null } coordinator) return;
        LobbyRow? row = coordinator.Browser.Find(id)?.Row;
        if (row is null || !row.Joinable || !_rendered.Any(item => item.Id == id)) return;
        _selection.Reset();
        if (row.Access == LobbyAccess.Locked) { _lockedId = id; OpenPage("locked"); }
        else coordinator.Join(id);
    }

    private void OpenPage(string page)
    {
        if (!Interactive) return;
        _selection.Reset();
        _page = page;
        _modalKey = "!";
        UpdatePresentation();
    }

    private void BuildModal(string page)
    {
        bool focus = (GetViewport().GuiGetFocusOwner() is { } modalFocus && _modal.IsAncestorOf(modalFocus)) || page.Length > 0;
        foreach (Node child in _modal.GetChildren()) { _modal.RemoveChild(child); child.QueueFree(); }
        _code = null;
        _modalKey = page;
        _modal.AddThemeConstantOverride("separation", page == "filter" ? 2 : 12);
        if (page.Length == 0) return;
        if (page.StartsWith("retained:", StringComparison.Ordinal))
        {
            _picker = false;
            _page = "";
            _modal.AddChild(new Label { Text = "Reconnect to previous game?", HorizontalAlignment = HorizontalAlignment.Center });
            var coordinator = Coordinator()!;
            _modal.AddChild(new Label { Name = "RetainedStatus", Text = coordinator.Status, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center });
            if (coordinator.RetainedDecision == RetainedSessionDecision.Choose)
            {
                _modal.AddChild(PlainButton("Yes", () => coordinator.DecideRetained(true)));
                _modal.AddChild(PlainButton("No", () => coordinator.DecideRetained(false)));
            }
            else if (coordinator.RetainedDecision == RetainedSessionDecision.Failed)
            {
                _modal.AddChild(PlainButton("Retry previous session", coordinator.RetryRetained));
                _modal.AddChild(PlainButton("Back to browser", () => { _dismissedHint = true; coordinator.DismissRetainedFailure(); }));
            }
            else if (coordinator.CanResumeRetained)
                _modal.AddChild(PlainButton("Check previous session", coordinator.ResumeRetained));
            else _modal.AddChild(new Label { Text = "Validating / recovering previous session…", HorizontalAlignment = HorizontalAlignment.Center });
        }
        else if (page == "admission")
        {
            _modal.AddChild(new Label { Text = "JOINING / CREATING LOBBY", HorizontalAlignment = HorizontalAlignment.Center });
            _modal.AddChild(new Label { Text = "Waiting for authoritative admission…", HorizontalAlignment = HorizontalAlignment.Center });
            _modal.AddChild(PlainButton("Cancel joining", () => CancelAdmission()));
        }
        else if (page == "filter")
        {
            _modal.AddChild(new Label { Text = "FILTER LOBBIES", HorizontalAlignment = HorizontalAlignment.Center });
            foreach (string choice in _choices)
            {
                var option = PlainButton(choice, () => Pick(choice));
                option.FocusEntered += () => _choice = Array.IndexOf(_choices, choice);
                option.AddThemeFontSizeOverride("font_size", 21);
                _modal.AddChild(option);
            }
        }
        else if (page == "host")
        {
            _modal.AddChild(new Label { Text = "HOST GAME", HorizontalAlignment = HorizontalAlignment.Center });
            var name = new LineEdit { Text = "Lobby", PlaceholderText = "Lobby name", MaxLength = 48 };
            var locked = new CheckButton { Text = "Locked / Private" };
            var code = new LineEdit { PlaceholderText = "Access code (4–64 characters)", Secret = true, MaxLength = 64, Visible = false };
            locked.Toggled += enabled => code.Visible = enabled;
            _modal.AddChild(name);
            var access = new HBoxContainer(); access.AddChild(locked); access.AddChild(code); code.SizeFlagsHorizontal = SizeFlags.ExpandFill; _modal.AddChild(access);
            var actions = new HBoxContainer(); _modal.AddChild(actions);
            var create = PlainButton("Create lobby", () => { Coordinator()?.Create(name.Text, locked.ButtonPressed ? LobbyAccess.Locked : LobbyAccess.Public, code.Text); code.Clear(); });
            create.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.AddChild(create);
            actions.AddChild(PlainButton("Cancel", ClosePage));
        }
        else if (page == "locked")
        {
            _modal.AddChild(new Label { Text = "LOCKED LOBBY · Enter access code", HorizontalAlignment = HorizontalAlignment.Center });
            _code = new LineEdit { Secret = true, MaxLength = 64, PlaceholderText = "Access code" };
            _modal.AddChild(_code);
            _modal.AddChild(PlainButton("Join locked lobby", () => { if (_lockedId is not null) Coordinator()?.Join(_lockedId, _code.Text); _code.Clear(); }));
            _modal.AddChild(PlainButton("Cancel", ClosePage));
        }
        if (focus) Focusables().FirstOrDefault()?.GrabFocus();
    }

    private void ClosePage() { _page = ""; _picker = false; _lockedId = null; _code?.Clear(); _modalKey = "!"; UpdatePresentation(); _filter.GrabFocus(); }
    private void TogglePicker()
    {
        if (!Interactive) return;
        _picker = true;
        _choice = Math.Max(0, Array.IndexOf(_choices, _selection.Filter));
        OpenPage("filter");
        _modal.GetChildren().OfType<Button>().ElementAtOrDefault(_choice)?.GrabFocus();
    }
    private void Pick(string choice) { _selection.Filter = choice; _selection.Reset(); ClosePage(); }

    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree() || Blocked()) return;
        Sample(Interactive && _wasInteractive, 0);
        if (!Interactive) { GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventMouse) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit && input is InputEventKey { Keycode: Key.Left or Key.Right or Key.Home or Key.End }) return;
        if (Actions.Any(action => input.IsAction(PlayerInputBindings.Name(action))) || new[] { "ui_accept", "ui_cancel", "ui_up", "ui_down", "ui_left", "ui_right", "ui_focus_next", "ui_focus_prev" }.Any(action => input.IsAction(action)))
            GetViewport().SetInputAsHandled();
    }

    private void Sample(bool dispatch, double delta)
    {
        if (NavigationInput is not { } input) return;
        foreach (InputAction action in Actions)
        {
            bool held = input.Enabled && input.Bindings.Strength(action, input.DeadZone) > 0.5f;
            bool previous = _held.GetValueOrDefault(action);
            _held[action] = held;
            if (!dispatch) continue;
            if (held && !previous) { Navigate(action); _repeatAction = action is InputAction.MenuAccept or InputAction.MenuCancel ? null : action; _repeat = 0.4; }
            else if (held && _repeatAction == action) { _repeat -= delta; if (_repeat <= 0) { Navigate(action); _repeat = 0.12; } }
            else if (!held && _repeatAction == action) _repeatAction = null;
        }
        if (!dispatch) { _repeatAction = null; _selection.Reset(); }
    }

    private void Navigate(InputAction action)
    {
        if (!Interactive) return;
        if (action == InputAction.MenuCancel)
        {
            _selection.Reset();
            if (_page.Length > 0 && Coordinator()?.Busy != true && Coordinator()?.Active is null) ClosePage();
            else if (!_back.Disabled) Back();
            return;
        }
        Control? focused = GetViewport().GuiGetFocusOwner();
        if (focused is LineEdit && action is InputAction.MenuLeft or InputAction.MenuRight) return;
        if (_picker)
        {
            if (action == InputAction.MenuAccept) { Pick(_choices[_choice]); return; }
            int direction = action is InputAction.MenuUp or InputAction.MenuLeft ? -1 : 1;
            _choice = (_choice + direction + _choices.Length) % _choices.Length;
            _modal.GetChildren().OfType<Button>().ElementAt(_choice).GrabFocus();
            return;
        }
        if (action == InputAction.MenuAccept && focused is Button button && button.HasMeta("lobby_id"))
        {
            var row = _rendered.FirstOrDefault(row => row.Id == (string)button.GetMeta("lobby_id"));
            if (row is not null && _selection.Accept(row, _time)) Join(row.Id);
            return;
        }
        if (action != InputAction.MenuAccept) _selection.Reset();
        Settings.MenuFocusNavigation.Navigate(action, Focusables(), focused);
    }

    private Control[] Focusables() => Descendants(_modal.Visible ? _modal : _rig).Where(control => control.IsVisibleInTree() && control.FocusMode == FocusModeEnum.All && (control is not BaseButton button || !button.Disabled)).ToArray();
    private static IEnumerable<Control> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren()) { if (child is Control control) yield return control; foreach (Control nested in Descendants(child)) yield return nested; }
    }
    private Button PlainButton(string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += () => { if (Interactive && !button.Disabled) action(); };
        button.MouseEntered += () => { if (Interactive && !button.Disabled) button.GrabFocus(); };
        return button;
    }
    private Button ArtButton(string file, Rect2 region, Rect2 bounds, string text, Action action)
    {
        var button = PlainButton(text, action);
        var texture = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://assets/frontend/play-menu/" + file), Region = region };
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            button.AddThemeStyleboxOverride(state, new StyleBoxTexture { Texture = texture, ModulateColor = state == "disabled" ? new Color(0.45f, 0.45f, 0.45f) : state is "hover" or "focus" ? new Color(1, 0.55f, 0.42f) : Colors.White });
        button.AddThemeColorOverride("font_color", Colors.Transparent);
        button.AddThemeColorOverride("font_hover_color", Colors.Transparent);
        button.AddThemeColorOverride("font_pressed_color", Colors.Transparent);
        button.AddThemeColorOverride("font_focus_color", Colors.Transparent);
        button.AddThemeColorOverride("font_disabled_color", Colors.Transparent);
        button.TooltipText = text;
        Add(_rig, button, bounds);
        return button;
    }
    private TextureRect Picture(string file, Rect2? region, Rect2 bounds)
    {
        Texture2D texture = GD.Load<Texture2D>("res://assets/frontend/play-menu/" + file);
        if (region is Rect2 crop) texture = new AtlasTexture { Atlas = texture, Region = crop };
        var picture = new TextureRect { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore };
        Add(_rig, picture, bounds);
        return picture;
    }
    private static void Place(Control control, Rect2 rect) { control.Position = rect.Position; control.Size = rect.Size; }
    private static void Add(Control parent, Control child, Rect2 rect) { parent.AddChild(child); Place(child, rect); }
    private static void AddCells(HBoxContainer parent, string[] texts, bool heading)
    {
        float[] widths = [425, 265, 170, 175, 110];
        for (int i = 0; i < texts.Length; i++)
        {
            var label = new Label { Text = texts[i], CustomMinimumSize = new Vector2(widths[i], 0), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", heading ? 23 : 27);
            parent.AddChild(label);
        }
    }
    private static Theme MakeTheme()
    {
        var theme = new Theme { DefaultFontSize = 27 };
        foreach (string type in new[] { "Button", "LineEdit" })
        {
            foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled", "read_only" })
                theme.SetStylebox(state, type, new StyleBoxFlat { BgColor = new Color(state is "hover" or "focus" or "pressed" ? "70251b" : "211914"), BorderColor = new Color("a16b43"), BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 5, ContentMarginBottom = 5 });
            theme.SetColor("font_color", type, new Color("f1e4ce"));
        }
        theme.SetColor("font_color", "Label", new Color("f1e4ce"));
        return theme;
    }
    private void Layout()
    {
        float scale = Math.Min(Size.X / 1400, Size.Y / 900);
        _layout.Scale = Vector2.One * scale;
        _layout.Position = new Vector2((Size.X - 1360 * scale) / 2, 0);
    }
    private void UpdateMotion()
    {
        float y = _hoisted is not null ? -940 * MathF.Pow(Math.Clamp(_motion / 0.34f, 0, 1), 2)
            : _motion < 0.46f ? -940 * (1 - MathF.Pow(_motion / 0.46f, 2))
            : _motion < 0.82f ? -5 * MathF.Sin((_motion - 0.46f) / 0.36f * MathF.PI) : 0;
        _rig.Position = new Vector2(0, y);
        QueueRedraw();
    }
    public override void _Draw()
    {
        var texture = GD.Load<Texture2D>("res://assets/frontend/main-menu/Chain.png");
        DrawSetTransform(_layout.Position, 0, _layout.Scale);
        foreach (float x in new[] { 225f, 475f, 895f, 1145f })
            for (float y = 0; y < 183 + _rig.Position.Y; y += 40)
                DrawTextureRectRegion(texture, new Rect2(x, y, 27, Math.Min(40, 183 + _rig.Position.Y - y)), new Rect2(277, 92, 170, 258));
        DrawSetTransform(Vector2.Zero);
    }
}
