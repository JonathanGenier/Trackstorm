using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.PostMatch;

/// <summary>Dedicated reconstructable post-match scene. Requests navigation without owning session lifetime.</summary>
internal sealed partial class PodiumScene : CanvasLayer
{
    private readonly Control _root = new() { MouseFilter = Control.MouseFilterEnum.Stop };
    private readonly Control _content = new() { Size = new Vector2(1120, 660) };
    private readonly List<Button> _actions = new();
    private readonly List<Label[]> _rows = new();
    private readonly List<Label> _podiums = new();
    private readonly Dictionary<InputAction, bool> _held = new();
    private Label _winner = null!;
    private Label _status = null!;
    private Label _pageLabel = null!;
    private Button _previous = null!;
    private Button _next = null!;
    private PostMatchContext? _displayed;
    private int _page;
    private bool _wasBlocked;

    internal DevelopmentSession Session { get; init; } = null!;
    internal Rect2 Bounds => _content.GetGlobalRect();
    internal PostMatchContext? Displayed => _displayed;

    public override void _Ready()
    {
        Layer = 2;
        _root.Theme = MenuPresentation.CreateTheme();
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color("101214"), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_content);
        Text("MATCH COMPLETE", 0, 2, 1120, 36, 18, "d58f6e");
        _winner = Text(string.Empty, 0, 38, 1120, 50, 34, "ffe1a1");
        // Placement comes directly from the ordered Core handoff. The center plinth is rank one.
        foreach (var placement in new[] { (x: 380, y: 99, h: 138), (x: 20, y: 125, h: 112), (x: 740, y: 145, h: 92) })
        {
            var plate = new Panel { Position = new Vector2(placement.x, placement.y), Size = new Vector2(340, placement.h), MouseFilter = Control.MouseFilterEnum.Ignore };
            plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(_podiums.Count == 0 ? "492521" : "23272b"),
                BorderColor = new Color(_podiums.Count == 0 ? "d4a665" : "66625a"),
                BorderWidthTop = 3, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            _content.AddChild(plate);
            _podiums.Add(Text(string.Empty, placement.x + 10, placement.y + 8, 320, placement.h - 16, 23, "eee1c7"));
        }

        string[] headings = ["#", "PLAYER", "KILLS", "DEATHS", "WINS", "PING"];
        for (int column = 0; column < headings.Length; column++) Cell(headings[column], column, 249, 17);
        for (int row = 0; row < 8; row++)
        {
            var band = new ColorRect { Position = new Vector2(20, 278 + row * 29), Size = new Vector2(1080, 28), Color = new Color(row % 2 == 0 ? "202326" : "191c1f"), MouseFilter = Control.MouseFilterEnum.Ignore };
            _content.AddChild(band);
            _rows.Add(Enumerable.Range(0, 6).Select(column => Cell(string.Empty, column, 278 + row * 29, 19)).ToArray());
        }

        _previous = Button("Previous", 20, 515, 140, () => ChangePage(-1));
        _next = Button("Next", 960, 515, 140, () => ChangePage(1));
        _pageLabel = Text(string.Empty, 170, 520, 780, 28, 17, "a5a5a0");
        string[] labels = ["Rematch", "Return to Lobby", "End Match", "Main Menu", "Quit Game"];
        for (int index = 0; index < labels.Length; index++)
        {
            var destination = (PostMatchDestination)index;
            var button = Button(labels[index], 20 + index * 218, 566, 208, () => Session.NavigatePostMatch(destination));
            button.TooltipText = index switch
            {
                0 => "Host restarts with connected players through loading and synchronization.",
                1 or 2 => "Host ends the match for everyone and keeps the joined lobby.",
                3 => "Leave this session and return to the Main Menu.",
                _ => "Clean up the session and close Trackstorm.",
            };
            _actions.Add(button);
        }

        _status = Text(string.Empty, 20, 617, 1080, 30, 17, "c5b9a6");
        _root.Resized += Layout;
        Layout();
        Refresh();
    }

    public override void _Process(double delta)
    {
        Refresh();
        SampleNavigation(Visible && !Session.OverlayOpen());
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible || Session.OverlayOpen()) return;
        SampleNavigation(true);
        // Native UI defaults must not activate a button a second time or bypass remapping.
        if (Session.NavigationInput is not null && (@event.IsAction("ui_accept") || @event.IsAction("ui_up") || @event.IsAction("ui_down") || @event.IsAction("ui_left") || @event.IsAction("ui_right") ||
            new[] { InputAction.MenuAccept, InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight }.Any(action => @event.IsAction(Input.PlayerInputBindings.Name(action)))))
        {
            if (@event is InputEventKey { PhysicalKeycode: Key.Escape }) return;
            GetViewport().SetInputAsHandled();
        }
    }

    internal void Refresh()
    {
        Visible = Session.Stage == ApplicationStage.Podium;
        if (!Visible)
        {
            _displayed = null;
            return;
        }

        var context = Session.PostMatch!;
        bool changed = !ReferenceEquals(context, _displayed);
        if (changed) _page = 0;
        _displayed = context;
        var roster = Session.Lobby?.State;
        var results = context.Results;
        _winner.Text = results.Outcome.Winner is { } winner ? $"{context.Participant(winner, roster).Name} WINS" : "MATCH FINISHED";
        for (int index = 0; index < _podiums.Count; index++)
        {
            var standing = results.Standings.ElementAtOrDefault(index);
            _podiums[index].Text = standing is null ? "—" : $"{standing.Rank:00}\n{context.Participant(standing.PlayerId, roster).Name}\n{standing.Kills} KILLS  ·  {standing.Deaths} DEATHS";
        }

        for (int index = 0; index < _rows.Count; index++)
        {
            var row = results.Standings.ElementAtOrDefault(_page * 8 + index);
            var player = row is null ? null : context.Participant(row.PlayerId, roster);
            string[] values = row is null ? ["", "", "", "", "", ""] :
                [row.Rank.ToString(), player!.Name + (row.PlayerId == Session.Lobby?.LocalPlayerId ? " · YOU" : ""), row.Kills.ToString(), row.Deaths.ToString(), row.Wins.ToString(),
                 Hud.PingFormatter.Format(player.Connected && roster is not null ? Session.Lobby!.Latency.Get(roster, row.PlayerId) : null)];
            for (int column = 0; column < values.Length; column++)
            {
                _rows[index][column].Text = values[column];
                _rows[index][column].Modulate = new Color(1, 1, 1, player?.Connected == false ? 0.55f : 1);
            }
        }

        int pages = Math.Max(1, (results.Standings.Count + 7) / 8);
        _previous.Disabled = _page == 0;
        _next.Disabled = _page == pages - 1;
        _pageLabel.Text = $"FINAL STANDINGS  ·  {_page + 1} / {pages}";
        for (int index = 0; index < 3; index++) _actions[index].Disabled = !Session.CanManagePostMatch;
        _status.Text = Session.Lobby?.Migration?.Frozen == true ? Session.Lobby.Migration.Status
            : Session.Lobby?.Reconnecting == true ? Session.Lobby.ResumeStatus
            : Session.PostMatchStatus.Length > 0 ? Session.PostMatchStatus
            : Session.CanManagePostMatch ? "Host controls the next match. Return to Lobby / End Match keeps this lobby."
            : "Waiting for the host’s next match. You can leave or quit at any time.";
        bool blocked = Session.OverlayOpen();
        if (!blocked && (changed || _wasBlocked))
        {
            SampleNavigation(false);
            Focusable().FirstOrDefault()?.GrabFocus();
        }
        _wasBlocked = blocked;
    }

    private Control[] Focusable() => _actions.Concat(new[] { _previous, _next }).Where(button => !button.Disabled).Cast<Control>().ToArray();

    private void SampleNavigation(bool dispatch)
    {
        if (Session.NavigationInput is not { } input) return;
        foreach (var action in new[] { InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight, InputAction.MenuAccept })
        {
            bool held = input.Enabled && input.Bindings.Strength(action, input.DeadZone) > 0.5f;
            bool previous = _held.GetValueOrDefault(action);
            _held[action] = held;
            if (dispatch && held && !previous) MenuFocusNavigation.Navigate(action, Focusable(), GetViewport().GuiGetFocusOwner());
        }
    }

    private void ChangePage(int offset)
    {
        _page = Math.Clamp(_page + offset, 0, Math.Max(0, ((Session.PostMatch?.Results.Standings.Count ?? 0) - 1) / 8));
        Refresh();
    }

    private Button Button(string title, float x, float y, float width, Action action)
    {
        var button = new Button { Text = title, Position = new Vector2(x, y), Size = new Vector2(width, 42) };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += action;
        _content.AddChild(button);
        return button;
    }

    private Label Cell(string title, int column, float y, int size)
    {
        float[] positions = [30, 95, 620, 745, 870, 990];
        float[] widths = [55, 500, 110, 110, 100, 100];
        var label = Text(title, positions[column], y, widths[column], 28, size, "eee1d1");
        if (column == 1) label.HorizontalAlignment = HorizontalAlignment.Left;
        return label;
    }

    private Label Text(string text, float x, float y, float width, float height, int fontSize, string color)
    {
        var label = new Label { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, height),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(color));
        _content.AddChild(label);
        return label;
    }

    private void Layout()
    {
        float scale = Math.Min(_root.Size.X / 1200, _root.Size.Y / 720);
        _content.Scale = Vector2.One * scale;
        _content.Position = (_root.Size - _content.Size * scale) / 2;
    }
}
