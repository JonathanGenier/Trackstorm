using Godot;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Settings;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Hud;

/// <summary>Resolution-safe component artwork with native live labels and read-only snapshot binding.</summary>
internal sealed partial class CombatHud : CanvasLayer
{
    private readonly Control _root = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly List<Control> _components = new();
    private readonly CircusScoreFeedback _scoreFeedback = new();
    private readonly List<Label> _scoreRows = new();
    private Panel _scorePanel = null!;
    private VBoxContainer _scoreCounter = null!;
    private Label _scoreTotal = null!;
    private Label _scoreMultiplier = null!;
    private Label _standing = null!;
    private Label _health = null!;
    private Label _speed = null!;
    private Label _unit = null!;
    private Label _nitro = null!;
    private Label _outOfBounds = null!;
    private Label _timer = null!;
    private readonly Control _itemAssembly = new() { Name = "ItemAssembly", MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(532, 188) };
    private readonly ItemHudSlot _firstSlot = new() { Name = "FirstSlot", Position = new Vector2(234, 72) };
    private readonly ItemHudSlot _secondSlot = new() { Name = "SecondSlot", Position = new Vector2(372, 72) };
    private ShaderMaterial _healthMaterial = null!;
    private ShaderMaterial _speedMaterial = null!;
    private readonly Dictionary<string, Texture2D> _itemIcons = new(StringComparer.Ordinal);

    private CombatHudView? _displayed;
    private double _milliseconds;

    /// <summary>Current existing gameplay snapshot; null hides the HUD outside a match.</summary>
    internal Func<VehicleSnapshot?> Vehicle { get; set; } = () => null;
    /// <summary>Confirmed inventory only; no predicted ownership.</summary>
    internal Func<ItemSlot?> Slot { get; set; } = () => null;
    /// <summary>Local preference service supplies presentation units.</summary>
    internal Func<SpeedUnit> Units { get; set; } = () => SpeedUnit.KilometresPerHour;
    /// <summary>Shared match standings position; practice has no match ranking.</summary>
    internal Func<string> Position { get; set; } = () => "--";
    /// <summary>Accepted authoritative match state; null keeps non-Circus practice presentation unchanged.</summary>
    internal Func<MatchState?> Match { get; set; } = () => null;
    /// <summary>Latest accepted authoritative world tick; never a presentation or prediction clock.</summary>
    internal Func<ulong> AuthoritativeTick { get; set; } = () => 0;
    /// <summary>Every accepted revision, preserving deltas when several arrive in one rendered frame.</summary>
    internal Func<IReadOnlyList<MatchState>> MatchUpdates { get; set; } = () => Array.Empty<MatchState>();
    /// <summary>Stable local participant identity used to select authoritative Circus state.</summary>
    internal Func<ulong> Player { get; set; } = () => 0;
    /// <summary>Last projected values for native integration verification.</summary>
    internal CombatHudView? Displayed => _displayed;
    /// <summary>Last authoritative Circus projection for native verification.</summary>
    internal CircusHudView? ScoreDisplayed { get; private set; }
    /// <summary>Rendered labels and materials are observable to native tests.</summary>
    internal string HealthText => _health.Text;
    /// <summary>Native material fraction for verification.</summary>
    internal double HealthFill => _healthMaterial.GetShaderParameter("fill").AsDouble();
    /// <summary>Native material fraction for verification.</summary>
    internal double SpeedFill => _speedMaterial.GetShaderParameter("fill").AsDouble();

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 1;
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Texture2D steel = Bootstrap.StartupController.LoadResource<Texture2D>("res://assets/hud/Health.png");
        var health = Component("Health", new Vector2(440, 147), 0, steel);
        _healthMaterial = (ShaderMaterial)health.Material;
        _standing = Text(health, "Standing", new Rect2(32, 39, 68, 56), 43);
        _health = Text(health, "HealthValue", new Rect2(291, 88, 109, 26), 23);
        _root.AddChild(_itemAssembly);
        var frame = new ItemHudFrame { Name = "ItemFrame", Position = new Vector2(220, 70), MouseFilter = Control.MouseFilterEnum.Ignore };
        _itemAssembly.AddChild(frame);
        var speed = Component("Speed", new Vector2(250, 187.5f), 1, steel);
        speed.Reparent(_itemAssembly, false);
        _components.Remove(speed);
        _speedMaterial = (ShaderMaterial)speed.Material;
        _speed = Text(speed, "SpeedValue", new Rect2(73, 78, 108, 57), 49);
        _unit = Text(speed, "SpeedUnit", new Rect2(88, 137, 78, 20), 19);
        _nitro = Text(speed, "NitroActive", new Rect2(20, -24, 220, 22), 18);
        _nitro.AddThemeColorOverride("font_color", new Color("ffd166"));
        _itemAssembly.AddChild(_firstSlot);
        _itemAssembly.AddChild(_secondSlot);
        _firstSlot.Initialize(1);
        _secondSlot.Initialize(2);
        foreach (var definition in ItemRegistry.All)
        {
            _itemIcons.Add(definition.PresentationKey, Bootstrap.StartupController.LoadResource<Texture2D>($"res://assets/hud/{definition.PresentationKey}.svg"));
        }
        var timer = Component("Timer", new Vector2(220, 73.333f), 3, steel);
        _timer = Text(timer, "TimerValue", new Rect2(58, 14, 99, 36), 34);
        _standing.Text = "--";
        _timer.Text = "--:--";
        _outOfBounds = Text(_root, "OutOfBounds", new Rect2(0, 0, 500, 70), 26);
        _outOfBounds.Text = "OUT OF BOUNDS\nARENA HAZARD • LOSING HEALTH";
        _outOfBounds.AddThemeColorOverride("font_color", new Color("ff6255"));
        _outOfBounds.Visible = false;
        BuildCircusScore();
        _root.Resized += Layout;
        Layout();
        Refresh();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _milliseconds += delta * 1000;
        Refresh();
    }

    /// <summary>Refreshes the actual labels and gauges from current providers, including setting-only changes.</summary>
    internal void Refresh()
    {
        VehicleSnapshot? state = Vehicle();
        Visible = state is not null;
        if (state is null)
        {
            _displayed = null;
            ScoreDisplayed = _scoreFeedback.Project(null, 0, _milliseconds);
            _scorePanel.Visible = false;
            _scoreCounter.Visible = false;
            return;
        }

        CombatHudView view = CombatHudView.From(state, Slot(), Units()) with { Standing = Position(), Timer = CombatHudView.FormatTimer(Match(), AuthoritativeTick()) };
        if (view != _displayed)
        {
            _displayed = view;
            _standing.Text = view.Standing;
            _timer.Text = view.Timer;
            _health.Text = view.Health;
            _speed.Text = view.Speed;
            _unit.Text = view.Unit;
            _firstSlot.Apply(view.FirstSlot, view.ActiveSlot == 0, 1, _itemIcons.GetValueOrDefault(view.FirstSlot.IconKey ?? string.Empty));
            _secondSlot.Apply(view.SecondSlot, view.ActiveSlot == 1, 2, _itemIcons.GetValueOrDefault(view.SecondSlot.IconKey ?? string.Empty));
            _healthMaterial.SetShaderParameter("fill", view.HealthFill);
            _speedMaterial.SetShaderParameter("fill", view.SpeedFill);
        }

        _outOfBounds.Visible = state.CanInteract && state.OutOfBounds;
        _nitro.Visible = state.Movement.Nitro.Active;
        _nitro.Text = "NITRO BOOST";
        ulong player = Player();
        CircusHudView? score = null;
        foreach (MatchState update in MatchUpdates())
        {
            score = _scoreFeedback.Project(update, player, _milliseconds);
        }

        RenderCircusScore(_scoreFeedback.Project(Match(), player, _milliseconds) ?? score);
    }

    private TextureRect Component(string name, Vector2 size, int kind, Texture2D steel)
    {
        var material = new ShaderMaterial { Shader = Bootstrap.StartupController.LoadResource<Shader>("res://assets/hud/Component.gdshader") };
        material.SetShaderParameter("component", kind);
        // The temporary Variant owns a native texture reference; release it after the material copies it.
        using var steelParameter = Variant.From(steel);
        material.SetShaderParameter("steel", steelParameter);
        var control = new TextureRect { Name = name, Size = size, Texture = Bootstrap.StartupController.LoadResource<Texture2D>($"res://assets/hud/{name}.png"), Material = material, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(control);
        _components.Add(control);
        return control;
    }

    private Label Text(Control parent, string name, Rect2 rect, int size)
    {
        var label = new Label { Name = name, Position = rect.Position, Size = rect.Size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color("f3efdf"));
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        parent.AddChild(label);
        return label;
    }

    private void BuildCircusScore()
    {
        _scorePanel = new Panel { Name = "CircusScore", Size = new Vector2(210, 62), MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.035f, 0.04f, 0.045f, 0.82f), BorderColor = new Color("b5342f") };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        _scorePanel.AddThemeStyleboxOverride("panel", style);
        _root.AddChild(_scorePanel);
        _scoreTotal = Text(_scorePanel, "ScoreTotal", new Rect2(12, 5, 186, 31), 22);
        _scoreTotal.HorizontalAlignment = HorizontalAlignment.Left;
        _scoreMultiplier = Text(_scorePanel, "ScoreMultiplier", new Rect2(12, 34, 186, 20), 15);
        _scoreMultiplier.HorizontalAlignment = HorizontalAlignment.Left;

        _scoreCounter = new VBoxContainer { Name = "CircusCounter", Size = new Vector2(250, 192), MouseFilter = Control.MouseFilterEnum.Ignore };
        _scoreCounter.AddThemeConstantOverride("separation", 4);
        _root.AddChild(_scoreCounter);
        for (int index = 0; index < Enum.GetValues<CircusScoreCategory>().Length; index++)
        {
            var label = new Label { CustomMinimumSize = new Vector2(250, 26), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 17);
            label.AddThemeColorOverride("font_shadow_color", Colors.Black);
            label.AddThemeConstantOverride("shadow_offset_y", 2);
            _scoreCounter.AddChild(label);
            _scoreRows.Add(label);
        }
    }

    private void RenderCircusScore(CircusHudView? score)
    {
        ScoreDisplayed = score;
        _scorePanel.Visible = score is not null;
        _scoreCounter.Visible = score?.Rows.Count > 0;
        if (score is null)
        {
            return;
        }

        _scoreTotal.Text = $"CIRCUS  {score.Total}";
        _scoreMultiplier.Text = $"K/D MULTIPLIER  {score.Multiplier}";
        for (int index = 0; index < _scoreRows.Count; index++)
        {
            CircusFeedbackRow? row = index < score.Rows.Count ? score.Rows[index] : null;
            Label label = _scoreRows[index];
            label.Visible = row is not null;
            if (row is null)
            {
                continue;
            }

            string status = row.Kind switch { CircusFeedbackKind.Pending => "PENDING", CircusFeedbackKind.Lost => "LOST", _ => "BANKED" };
            string sign = row.Kind == CircusFeedbackKind.Lost ? "−" : "+";
            label.Text = $"{row.Name}   {status} {sign}{CircusHudView.FormatPoints(row.Points)}";
            label.Modulate = new Color(1, 1, 1, row.Opacity);
            label.AddThemeColorOverride("font_color", row.Kind switch { CircusFeedbackKind.Pending => new Color("ffd166"), CircusFeedbackKind.Lost => new Color("ff6262"), _ => new Color("e8f3e8") });
        }
    }

    private void Layout()
    {
        Vector2 viewport = _root.Size;
        float scale = Math.Min(viewport.X / 1280, viewport.Y / 720);
        float margin = 16 * scale;
        _outOfBounds.Scale = Vector2.One * scale;
        _outOfBounds.Position = new Vector2((viewport.X - 500 * scale) / 2, 106 * scale);
        foreach (Control component in _components)
        {
            component.Scale = Vector2.One * scale;
        }

        _components[0].Position = new Vector2(margin, viewport.Y - (147 * scale) - margin);
        _itemAssembly.Scale = Vector2.One * scale;
        _itemAssembly.Position = new Vector2(viewport.X - (532 * scale) - margin, viewport.Y - (176 * scale) - margin);
        _components[1].Position = new Vector2((viewport.X - (220 * scale)) / 2, 12 * scale);
        _scorePanel.Scale = Vector2.One * scale;
        _scorePanel.Position = new Vector2(margin, 120 * scale);
        _scoreCounter.Scale = Vector2.One * scale;
        _scoreCounter.Position = new Vector2(margin, 194 * scale);
    }
}
