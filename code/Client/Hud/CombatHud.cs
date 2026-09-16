using Godot;
using Trackstorm.Core.Items;
using Trackstorm.Core.Settings;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Hud;

/// <summary>Resolution-safe component artwork with native live labels and read-only snapshot binding.</summary>
internal sealed partial class CombatHud : CanvasLayer
{
    private readonly Control _root = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly List<Control> _components = new();
    private Label _standing = null!;
    private Label _health = null!;
    private Label _speed = null!;
    private Label _unit = null!;
    private Label _itemName = null!;
    private Label _timer = null!;
    private TextureRect _itemIcon = null!;
    private ShaderMaterial _healthMaterial = null!;
    private ShaderMaterial _speedMaterial = null!;
    private Texture2D _wrench = null!;
    private Texture2D _missile = null!;
    private CombatHudView? _displayed;

    /// <summary>Current existing gameplay snapshot; null hides the HUD outside a match.</summary>
    internal Func<VehicleSnapshot?> Vehicle { get; set; } = () => null;
    /// <summary>Confirmed inventory only; no predicted ownership.</summary>
    internal Func<ItemSlot?> Slot { get; set; } = () => null;
    /// <summary>Local preference service supplies presentation units.</summary>
    internal Func<SpeedUnit> Units { get; set; } = () => SpeedUnit.KilometresPerHour;
    /// <summary>Shared match standings position; practice has no match ranking.</summary>
    internal Func<string> Position { get; set; } = () => "--";
    /// <summary>Last projected values for native integration verification.</summary>
    internal CombatHudView? Displayed => _displayed;
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
        Texture2D steel = GD.Load<Texture2D>("res://assets/hud/Health.png");
        var health = Component("Health", new Vector2(440, 147), 0, steel);
        _healthMaterial = (ShaderMaterial)health.Material;
        _standing = Text(health, "Standing", new Rect2(32, 39, 68, 56), 43);
        _health = Text(health, "HealthValue", new Rect2(291, 88, 109, 26), 23);
        var speed = Component("Speed", new Vector2(250, 187.5f), 1, steel);
        _speedMaterial = (ShaderMaterial)speed.Material;
        _speed = Text(speed, "SpeedValue", new Rect2(73, 78, 108, 57), 49);
        _unit = Text(speed, "SpeedUnit", new Rect2(88, 137, 78, 20), 19);
        var item = Component("Item", new Vector2(112.5f, 150), 2, steel);
        _itemName = Text(item, "ItemName", new Rect2(17, 111, 84, 22), 18);
        _itemIcon = new TextureRect { Name = "ItemIcon", Position = new Vector2(25, 48), Size = new Vector2(70, 55), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        item.AddChild(_itemIcon);
        _wrench = GD.Load<Texture2D>("res://assets/hud/Wrench.svg");
        _missile = GD.Load<Texture2D>("res://assets/hud/Missile.svg");
        var timer = Component("Timer", new Vector2(220, 73.333f), 3, steel);
        _timer = Text(timer, "TimerValue", new Rect2(58, 14, 99, 36), 34);
        _standing.Text = "--";
        _timer.Text = "--:--";
        _root.Resized += Layout;
        Layout();
        Refresh();
    }

    /// <inheritdoc/>
    public override void _Process(double delta) => Refresh();

    /// <summary>Refreshes the actual labels and gauges from current providers, including setting-only changes.</summary>
    internal void Refresh()
    {
        VehicleSnapshot? state = Vehicle();
        Visible = state is not null;
        if (state is null)
        {
            _displayed = null;
            return;
        }

        CombatHudView view = CombatHudView.From(state, Slot(), Units()) with { Standing = Position() };
        if (view == _displayed)
        {
            return;
        }

        _displayed = view;
        _standing.Text = view.Standing;
        _health.Text = view.Health;
        _speed.Text = view.Speed;
        _unit.Text = view.Unit;
        _itemName.Text = view.ItemName;
        _itemIcon.Texture = view.Item switch { HeldItem.Wrench => _wrench, HeldItem.Missile => _missile, _ => null };
        _healthMaterial.SetShaderParameter("fill", view.HealthFill);
        _speedMaterial.SetShaderParameter("fill", view.SpeedFill);
    }

    private TextureRect Component(string name, Vector2 size, int kind, Texture2D steel)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/hud/Component.gdshader") };
        material.SetShaderParameter("component", kind);
        material.SetShaderParameter("steel", steel);
        var control = new TextureRect { Name = name, Size = size, Texture = GD.Load<Texture2D>($"res://assets/hud/{name}.png"), Material = material, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore };
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

    private void Layout()
    {
        Vector2 viewport = _root.Size;
        float scale = Math.Min(viewport.X / 1280, viewport.Y / 720);
        float margin = 16 * scale;
        foreach (Control component in _components)
        {
            component.Scale = Vector2.One * scale;
        }

        _components[0].Position = new Vector2(margin, viewport.Y - (147 * scale) - margin);
        _components[2].Position = new Vector2(viewport.X - (112.5f * scale) - margin, viewport.Y - (150 * scale) - margin);
        _components[1].Position = new Vector2(viewport.X - (354 * scale) - margin, viewport.Y - (187.5f * scale) - margin + (9 * scale));
        _components[3].Position = new Vector2((viewport.X - (220 * scale)) / 2, 12 * scale);
    }
}
