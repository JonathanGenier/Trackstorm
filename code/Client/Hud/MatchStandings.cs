using Godot;

namespace Trackstorm.Client.Hud;

/// <summary>One centered, resolution-safe board for held standings and persistent match results.</summary>
internal sealed partial class MatchStandings : CanvasLayer
{
    private readonly Control _root = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly StandingsMetal _board = new() { Size = new Vector2(1040, 520), MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly List<Label[]> _rows = new();
    private Label _status = null!;
    private Label _footer = null!;
    private Button _return = null!;

    /// <summary>Reconstructable presentation from the session owner.</summary>
    internal Func<MatchStandingsView?> View { get; set; } = () => null;
    /// <summary>Existing session Return/Leave action, supplied by the session owner.</summary>
    internal Action LeaveResults { get; set; } = () => { };
    /// <summary>Host returns the group; a client leaves individually.</summary>
    internal Func<bool> IsHost { get; set; } = () => false;
    /// <summary>Last rendered projection for runtime verification.</summary>
    internal MatchStandingsView? Displayed { get; private set; }

    /// <summary>Rendered board footprint including its decorative edge, for composition checks.</summary>
    internal Rect2 Bounds => _board.GetGlobalRect().Grow(24 * _board.Scale.Y);

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 2;
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.43f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_board);
        Text("MATCH STANDINGS", 40, 28, 650, 47, 38);
        _status = Text("LIVE", 716, 34, 284, 32, 20, HorizontalAlignment.Right);
        string[] columns = { "#", "Player Name", "Kills", "Deaths", "Ping" };
        for (int index = 0; index < columns.Length; index++)
        {
            Cell(columns[index], index, 98, 20);
        }

        for (int row = 0; row < 8; row++)
        {
            _rows.Add(Enumerable.Range(0, 5).Select(column => Cell(string.Empty, column, 134 + (row * 40), 23)).ToArray());
        }

        _footer = Text(string.Empty, 42, 468, 720, 30, 18);
        _return = new Button { Name = "LeaveResults", Position = new Vector2(785, 466), Size = new Vector2(213, 34), FocusMode = Control.FocusModeEnum.None };
        _return.AddThemeFontSizeOverride("font_size", 16);
        _return.Pressed += () => LeaveResults();
        _board.AddChild(_return);
        _root.Resized += Layout;
        Layout();
        Refresh();
    }

    /// <inheritdoc/>
    public override void _Process(double delta) => Refresh();

    /// <summary>Applies current state without modifying gameplay or intercepting driving input.</summary>
    internal void Refresh()
    {
        Displayed = View();
        Visible = Displayed?.Visible == true;
        if (Displayed is not { } view)
        {
            return;
        }

        _status.Text = view.Finished ? "FINAL RESULTS" : "LIVE / FIRST TO TARGET";
        _return.Visible = view.Finished;
        _return.Text = IsHost() ? "RETURN TO LOBBY" : "LEAVE SESSION";
        _footer.Text = view.Finished ? $"WINNER  /  {view.WinnerName}" : "HOLD LEADERBOARD TO VIEW  /  RELEASE TO RETURN";
        for (int index = 0; index < _rows.Count; index++)
        {
            StandingsRow? row = index < view.Rows.Count ? view.Rows[index] : null;
            string marker = row?.Winner == true ? "  · WINNER" : row?.Rank == 1 ? "  · LEADER" : string.Empty;
            string[] values = row is null ? new[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty } : new[] { row.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Name + marker + (row.Local ? "  · YOU" : string.Empty), row.Kills.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Ping };
            for (int column = 0; column < values.Length; column++)
            {
                _rows[index][column].Text = values[column];
                _rows[index][column].AddThemeColorOverride("font_color", new Color(index == 0 ? "ffe1a1" : "ebe7df"));
            }
        }
    }

    private Label Cell(string text, int column, float y, int fontSize)
    {
        float[] positions = { 42, 110, 650, 770, 887 };
        float[] widths = { 52, 530, 100, 100, 112 };
        return Text(text, positions[column], y, widths[column], 32, fontSize, column == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Center);
    }

    private Label Text(string text, float x, float y, float width, float height, int fontSize, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, height), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color("ebe7df"));
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        _board.AddChild(label);
        return label;
    }

    private void Layout()
    {
        float scale = Math.Min(_root.Size.X / 1200, _root.Size.Y / 680);
        _board.Scale = Vector2.One * scale;
        _board.Position = (_root.Size - (_board.Size * scale)) / 2;
    }
}
