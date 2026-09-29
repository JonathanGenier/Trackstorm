using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Hud;

/// <summary>Live contents of one physical inventory well in the shared corner chassis.</summary>
internal sealed partial class ItemHudSlot : Control
{
    private readonly Label _number = new();
    private readonly Label _name = new();
    private readonly Label _resource = new();
    private readonly TextureRect _icon = new();
    private readonly Line2D _thrust = new()
    {
        Name = "BoostActive",
        Points = [new(40, 57), new(119, 57)],
        Width = 2,
        DefaultColor = new Color("66efff"),
        Visible = false,
    };
    private ItemHudSlotView? _view;
    private bool _selected;

    /// <summary>Physical selection, independent of item identity or remaining fuel.</summary>
    internal bool Selected => _selected;

    internal void Initialize(int number, Font font)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(159, 86);
        AddLabel(_number, "SlotNumber", new Rect2(9, 3, 18, 23), 20, font);
        AddLabel(_name, "ItemName", new Rect2(30, 4, 116, 20), 15, font);
        AddLabel(_resource, "ResourceValue", new Rect2(8, 55, 54, 29), 23, font);
        _number.Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _icon.Name = "ItemIcon";
        _icon.Position = new Vector2(34, 29);
        _icon.Size = new Vector2(91, 26);
        _icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _icon.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_icon);
        AddChild(_thrust);
    }

    /// <summary>Transient thrust feedback is local presentation, never resource authority.</summary>
    internal void SetBoosting(bool active) => _thrust.Visible = active && _view?.Item == HeldItem.Nitro;

    internal void Apply(ItemHudSlotView view, bool selected, Texture2D? icon)
    {
        if (_view == view && _selected == selected) return;
        _view = view;
        _selected = selected;
        _name.Text = view.Item == HeldItem.Nitro ? "BOOST" : view.Name;
        _name.Modulate = view.Item == HeldItem.None ? new Color("aaa79e") : Colors.White;
        _icon.Texture = icon;
        _icon.Visible = icon is not null;
        _resource.Visible = view.Resource is not null;
        _resource.Text = view.Resource?.Text ?? string.Empty;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_selected)
        {
            Vector2[] perimeter = [new(12, 0), new(146, 0), new(159, 14), new(159, 71), new(146, 85), new(12, 85), new(0, 71), new(0, 14), new(12, 0)];
            DrawPolyline(perimeter, new Color("664308"), 4, true);
            DrawPolyline(perimeter, new Color("ffcf22"), 2, true);
        }

        if (_view?.Resource?.Fraction is not double fraction) return;
        bool boost = _view.Item == HeldItem.Nitro;
        Color fill = new(boost ? "17d5ed" : "d8ae37");
        Color edge = new(boost ? "66efff" : "f4d46b");
        DrawRect(new Rect2(64, 60, 81, 21), new Color("090a0b"));
        for (int cell = 0; cell < 5; cell++)
        {
            var rect = new Rect2(66 + cell * 15.6f, 62, 13, 17);
            DrawRect(rect, new Color("272a2b"));
            float filled = (float)Math.Clamp(fraction * 5 - cell, 0, 1);
            if (filled <= 0) continue;
            var content = new Rect2(rect.Position, new Vector2(rect.Size.X * filled, rect.Size.Y));
            DrawRect(content, fill);
            DrawRect(new Rect2(content.Position, new Vector2(content.Size.X, 2)), edge);
        }
    }

    private void AddLabel(Label label, string name, Rect2 rect, int fontSize, Font font)
    {
        label.Name = name;
        label.Position = rect.Position;
        label.Size = rect.Size;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color("f3efdf"));
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.Size = rect.Size;
        AddChild(label);
    }
}
