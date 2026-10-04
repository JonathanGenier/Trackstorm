using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Hud;

/// <summary>Live contents of one physical inventory well in the shared corner chassis.</summary>
internal sealed partial class ItemHudSlot : Control
{
    private readonly Label _number = new();
    private readonly Label _name = new();
    private readonly Label _resource = new();
    private readonly Label _resourceUnit = new();
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
        AddLabel(_resourceUnit, "ResourceUnit", new Rect2(8, 75, 54, 10), 9, font);
        _resourceUnit.Text = "HP";
        _resourceUnit.Visible = false;
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
        bool shield = view.Item == HeldItem.Shield;
        _resource.Size = new Vector2(54, shield ? 20 : 29);
        _resourceUnit.Visible = shield && view.Resource is not null;
        bool damagedShield = view.Item == HeldItem.Shield && view.Resource?.Fraction <= 0.25;
        _resource.Modulate = damagedShield ? new Color("ff7852") : Colors.White;
        _icon.Modulate = damagedShield ? new Color("ff9a73") : Colors.White;
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
        if (_view.Item == HeldItem.Shield)
        {
            DrawDurability(fraction);
            return;
        }
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

    // Three armored plates with pointed bases distinguish durability from fuel/magazine rails.
    private void DrawDurability(double fraction)
    {
        Color fill = new(fraction <= 0.25 ? "ee6945" : "a8c5b4");
        Color rim = new("dce8cf");
        for (int plate = 0; plate < 3; plate++)
        {
            float x = 65 + plate * 27;
            Vector2[] outline = [new(x, 59), new(x + 24, 59), new(x + 24, 74), new(x + 12, 82), new(x, 74)];
            DrawColoredPolygon(outline, new Color("1e2928"));
            float filled = (float)Math.Clamp(fraction * 3 - plate, 0, 1);
            if (filled > 0)
            {
                float bottom = 60 + 21 * filled;
                if (bottom <= 74)
                    DrawRect(new Rect2(x + 1, 60, 22, bottom - 60), fill);
                else
                {
                    float inset = (bottom - 74) * 11 / 7;
                    DrawColoredPolygon([new(x + 1, 60), new(x + 23, 60), new(x + 23, 74),
                        new(x + 23 - inset, bottom), new(x + 1 + inset, bottom), new(x + 1, 74)], fill);
                }
            }
            DrawPolyline([.. outline, outline[0]], rim, 1, true);
            if (filled < 1)
                DrawPolyline([new(x + 14, 62), new(x + 10, 68), new(x + 14, 70), new(x + 8, 77)], new Color("080d0d"), 2, true);
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
