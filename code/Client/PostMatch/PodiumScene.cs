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
    private readonly List<Panel> _plates = new();
    private readonly List<ColorRect> _bands = new();
    private readonly List<Label> _headings = new();
    private readonly Dictionary<InputAction, bool> _held = new();
    private Label _winner = null!;
    private Label _title = null!;
    private Label _status = null!;
    private Label _pageLabel = null!;
    private Button _previous = null!;
    private Button _next = null!;
    private PostMatchContext? _displayed;
    private int _page;
    private bool _wasBlocked;
    private Button? _selected;
    private bool Compact => _root.Size.X < 900 || _root.Size.Y < 700;
    private int RowsPerPage => Compact ? 4 : 8;

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
        _title = Text("MATCH COMPLETE", 0, 2, 1120, 36, 18, "d58f6e");
        _winner = Text(string.Empty, 0, 38, 1120, 50, 34, "ffe1a1");
        _winner.MouseFilter = Control.MouseFilterEnum.Pass;
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
            _plates.Add(plate);
            _podiums.Add(Text(string.Empty, placement.x + 10, placement.y + 8, 320, placement.h - 16, 23, "eee1c7"));
            _podiums[^1].MouseFilter = Control.MouseFilterEnum.Pass;
        }

        string[] headings = ["#", "PLAYER", "SCORE", "KILLS", "DEATHS", "WINS", "PING"];
        for (int column = 0; column < headings.Length; column++) _headings.Add(Cell(headings[column], column, 249, 17));
        for (int row = 0; row < 8; row++)
        {
            var band = new ColorRect { Position = new Vector2(20, 278 + row * 29), Size = new Vector2(1080, 28), Color = new Color(row % 2 == 0 ? "202326" : "191c1f"), MouseFilter = Control.MouseFilterEnum.Ignore };
            _content.AddChild(band);
            _bands.Add(band);
            _rows.Add(Enumerable.Range(0, 7).Select(column => Cell(string.Empty, column, 278 + row * 29, 19)).ToArray());
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
        SampleNavigation(!_wasBlocked);
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
        if (changed)
        {
            _page = 0;
            _selected = null;
        }
        _displayed = context;
        var roster = Session.Lobby?.State;
        var results = context.Results;
        _title.Text = results.Outcome.Winner is not null ? "MATCH COMPLETE · WINNER" : "MATCH COMPLETE";
        _winner.Text = results.Outcome.Winner is { } winner ? context.Participant(winner, roster).Name : "MATCH FINISHED";
        _winner.TooltipText = _winner.Text;
        int pages = Math.Max(1, (results.Standings.Count + RowsPerPage - 1) / RowsPerPage);
        _page = Math.Clamp(_page, 0, pages - 1);
        for (int index = 0; index < _podiums.Count; index++)
        {
            var standing = results.Standings.ElementAtOrDefault(index);
            _podiums[index].Text = standing is null ? "—" : $"{standing.Rank:00}\n{context.Participant(standing.PlayerId, roster).Name}\n{Hud.CircusHudView.FormatPoints(standing.CircusScore)} POINTS";
            _podiums[index].TooltipText = _podiums[index].Text;
        }

        for (int index = 0; index < _rows.Count; index++)
        {
            var row = index < RowsPerPage ? results.Standings.ElementAtOrDefault(_page * RowsPerPage + index) : null;
            var player = row is null ? null : context.Participant(row.PlayerId, roster);
            string[] values = row is null ? ["", "", "", "", "", "", ""] :
                [row.Rank.ToString(), player!.Name + (row.PlayerId == Session.Lobby?.LocalPlayerId ? " · YOU" : ""), Hud.CircusHudView.FormatPoints(row.CircusScore), row.Kills.ToString(), row.Deaths.ToString(), row.Wins.ToString(),
                 Hud.PingFormatter.Format(player.Connected && roster is not null ? Session.Lobby!.Latency.Get(roster, row.PlayerId) : null)];
            for (int column = 0; column < values.Length; column++)
            {
                _rows[index][column].Text = values[column];
                _rows[index][column].TooltipText = values[column];
                _rows[index][column].Modulate = new Color(1, 1, 1, player?.Connected == false ? 0.55f : 1);
            }
        }

        _previous.Disabled = _page == 0;
        _next.Disabled = _page == pages - 1;
        _previous.Visible = _next.Visible = pages > 1;
        _pageLabel.Text = pages > 1 ? $"FINAL STANDINGS  ·  {_page + 1} / {pages}" : "FINAL STANDINGS";
        for (int index = 0; index < 3; index++) _actions[index].Disabled = !Session.CanManagePostMatch;
        _status.Text = Session.Lobby?.Migration?.Frozen == true ? Session.Lobby.Migration.Status
            : Session.Lobby?.Reconnecting == true ? Session.Lobby.ResumeStatus
            : Session.PostMatchStatus.Length > 0 ? Session.PostMatchStatus
            : Session.CanManagePostMatch ? (Compact ? "Return to Lobby / End Match keeps this lobby." : "Host controls the next match. Return to Lobby / End Match keeps this lobby.")
            : "Waiting for the host’s next match. You can leave or quit at any time.";
        bool blocked = Session.OverlayOpen();
        Layout();
        if (!blocked && (changed || _wasBlocked))
        {
            SampleNavigation(false);
            var available = Focusable();
            (available.Contains(_selected) ? _selected : available.FirstOrDefault())?.GrabFocus();
        }
        _wasBlocked = blocked;
    }

    private Control[] Focusable() => _actions.Concat(new[] { _previous, _next }).Where(button => !button.Disabled && button.Visible).Cast<Control>().ToArray();

    private void SampleNavigation(bool dispatch)
    {
        if (Session.NavigationInput is not { } input) return;
        foreach (var action in new[] { InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight, InputAction.MenuAccept })
        {
            bool held = input.Enabled && input.Bindings.Strength(action, input.DeadZone) > 0.5f;
            bool previous = _held.GetValueOrDefault(action);
            _held[action] = held;
            if (dispatch && held && !previous) Navigate(action);
        }
    }

    private void Navigate(InputAction action)
    {
        var controls = Focusable();
        var focused = GetViewport().GuiGetFocusOwner();
        if (focused is not null && controls.Contains(focused) && action is InputAction.MenuUp or InputAction.MenuDown)
        {
            Vector2 origin = focused.GetGlobalRect().GetCenter();
            float direction = action == InputAction.MenuDown ? 1 : -1;
            var next = controls.Where(control => (control.GetGlobalRect().GetCenter().Y - origin.Y) * direction > 4)
                .OrderBy(control => Math.Abs(control.GetGlobalRect().GetCenter().Y - origin.Y))
                .ThenBy(control => Math.Abs(control.GetGlobalRect().GetCenter().X - origin.X)).FirstOrDefault();
            if (next is not null)
            {
                next.GrabFocus();
                return;
            }
        }
        MenuFocusNavigation.Navigate(action, controls, focused);
    }

    private void ChangePage(int offset)
    {
        _page = Math.Clamp(_page + offset, 0, Math.Max(0, ((Session.PostMatch?.Results.Standings.Count ?? 0) - 1) / RowsPerPage));
        Refresh();
        if (!Focusable().Contains(GetViewport().GuiGetFocusOwner()))
            (!_previous.Disabled && _previous.Visible ? _previous : !_next.Disabled && _next.Visible ? _next : _actions.FirstOrDefault(button => !button.Disabled))?.GrabFocus();
    }

    private Button Button(string title, float x, float y, float width, Action action)
    {
        var button = new Button { Text = title, Position = new Vector2(x, y), Size = new Vector2(width, 42) };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += action;
        button.FocusEntered += () => _selected = button;
        _content.AddChild(button);
        return button;
    }

    private Label Cell(string title, int column, float y, int size)
    {
        float[] positions = [20, 80, 470, 610, 720, 830, 940];
        float[] widths = [55, 380, 140, 110, 110, 110, 100];
        var label = Text(title, positions[column], y, widths[column], 28, size, "eee1d1");
        if (column == 1)
        {
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.MouseFilter = Control.MouseFilterEnum.Pass;
        }
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
        if (_status is null) return;
        bool compact = Compact;
        float width = Math.Min(1120, Math.Max(320, _root.Size.X - 32));
        int count = _displayed?.Results.Standings.Count ?? 0;
        int rows = Math.Min(RowsPerPage, Math.Max(0, count - _page * RowsPerPage));
        Place(_title, 0, 0, width, compact ? 20 : 26, compact ? 13 : 18);
        Place(_winner, 0, compact ? 20 : 28, width, compact ? 34 : 48, compact ? 24 : 34);
        float y = compact ? 58 : 88;
        int podiumCount = Math.Min(3, count);
        for (int index = 0; index < _plates.Count; index++)
        {
            bool shown = !compact && index < podiumCount;
            _plates[index].Visible = _podiums[index].Visible = shown;
            if (!shown) continue;
            float plateWidth = (width - (podiumCount - 1) * 16) / Math.Max(1, podiumCount);
            int slot = podiumCount > 1 ? (index == 0 ? 1 : index == 1 ? 0 : 2) : 0;
            float top = y + index * 12;
            Place(_plates[index], slot * (plateWidth + 16), top, plateWidth, 140 - index * 12);
            Place(_podiums[index], slot * (plateWidth + 16) + 10, top + 8, plateWidth - 20, 124 - index * 12, 21);
        }
        if (!compact && podiumCount > 0) y += 156;
        float rankWidth = compact ? 30 : 60;
        float statWidth = compact ? 58 : 100;
        float scoreWidth = compact ? 110 : 150;
        float nameWidth = width - rankWidth - scoreWidth - statWidth * 4;
        float[] columns = [0, rankWidth, rankWidth + nameWidth, width - statWidth * 4, width - statWidth * 3, width - statWidth * 2, width - statWidth];
        float[] widths = [rankWidth, nameWidth, scoreWidth, statWidth, statWidth, statWidth, statWidth];
        for (int column = 0; column < 7; column++)
            Place(_headings[column], columns[column] + 4, y, widths[column] - 8, 24, compact ? 12 : 17);
        y += 24;
        float rowHeight = compact ? 24 : 30;
        for (int row = 0; row < _rows.Count; row++)
        {
            _bands[row].Visible = row < rows;
            Place(_bands[row], 0, y + row * rowHeight, width, rowHeight - 1);
            for (int column = 0; column < 7; column++)
            {
                _rows[row][column].Visible = row < rows;
                Place(_rows[row][column], columns[column] + 4, y + row * rowHeight, widths[column] - 8, rowHeight, compact ? 15 : 19);
            }
        }
        y += rows * rowHeight + 6;
        Place(_previous, 0, y, compact ? 92 : 140, 38, compact ? 14 : 18);
        Place(_next, width - (compact ? 92 : 140), y, compact ? 92 : 140, 38, compact ? 14 : 18);
        Place(_pageLabel, compact ? 96 : 150, y, width - (compact ? 192 : 300), 38, compact ? 13 : 17);
        y += 44;
        for (int index = 0; index < _actions.Count; index++)
        {
            int slots = compact ? (index < 3 ? 3 : 2) : 5;
            int slot = compact && index >= 3 ? index - 3 : index;
            float buttonWidth = (width - (slots - 1) * 8) / slots;
            Place(_actions[index], slot * (buttonWidth + 8), y + (compact && index >= 3 ? 44 : 0), buttonWidth, compact ? 38 : 42, compact ? 15 : 20);
        }
        y += compact ? 88 : 50;
        Place(_status, 0, y, width, compact ? 36 : 44, compact ? 13 : 17);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.Size = new Vector2(width, y + (compact ? 36 : 44));
        _content.Position = (_root.Size - _content.Size) / 2;
    }

    private static void Place(Control control, float x, float y, float width, float height, int? fontSize = null)
    {
        if (fontSize is { } size) control.AddThemeFontSizeOverride("font_size", size);
        control.Position = new Vector2(x, y);
        control.Size = new Vector2(width, height);
    }
}
