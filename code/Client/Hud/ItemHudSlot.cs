using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Hud;

/// <summary>Reusable content/lifecycle for either physical slot; the enclosing assembly owns the frame.</summary>
internal sealed partial class ItemHudSlot : Control
{
    private readonly Label _selection = new();
    private readonly Label _name = new();
    private readonly Label _resource = new();
    private readonly Label _boost = new();
    private readonly TextureRect _icon = new();
    private ItemHudSlotView? _view;
    private bool _selected;

    internal void Initialize(int number)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(112, 80);
        AddLabel(_selection, "Selection", new Rect2(1, -12, 110, 14), 10);
        AddLabel(_name, "ItemName", new Rect2(0, 42, 112, 26), 18);
        AddLabel(_resource, "ResourceValue", new Rect2(0, 57, 112, 23), 20);
        AddLabel(_boost, "BoostIdentity", new Rect2(0, 25, 112, 12), 11);
        _boost.Text = "BOOST";
        _boost.AddThemeColorOverride("font_color", new Color("87e8ff"));
        _boost.Visible = false;
        _selection.Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _icon.Name = "ItemIcon";
        _icon.Position = new Vector2(26, 0);
        _icon.Size = new Vector2(60, 35);
        _icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _icon.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_icon);
    }

    internal void Apply(ItemHudSlotView view, bool selected, int number, Texture2D? icon)
    {
        if (_view == view && _selected == selected) return;
        _view = view;
        _selected = selected;
        _selection.Text = selected ? $"{number} • ACTIVE" : number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _selection.Modulate = selected ? new Color("ffd166") : new Color("aaa79e");
        _name.Text = view.Name;
        _name.Visible = view.Resource is null;
        _name.Modulate = view.Item == Core.Items.HeldItem.None ? new Color("aaa79e") : Colors.White;
        _icon.Texture = icon;
        _icon.Visible = icon is not null;
        _resource.Visible = view.Resource is not null;
        _resource.Text = view.Resource?.Text ?? string.Empty;
        bool boost = view.Item == HeldItem.Nitro;
        _boost.Visible = boost;
        _icon.Size = new Vector2(60, boost ? 26 : 35);
        _resource.AddThemeColorOverride("font_color", boost ? new Color("c5f5ff") : new Color("f3efdf"));
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_selected) DrawLine(new Vector2(16, 0), new Vector2(96, 0), new Color("dba455"), 1);
        if (_view?.Resource?.Fraction is not double fraction) return;
        if (_view.Item == HeldItem.Nitro)
        {
            DrawBoostMeter(fraction);
            return;
        }
        DrawRect(new Rect2(8, 37, 96, 20), new Color("030404"));
        DrawRect(new Rect2(9, 38, 94, 18), new Color("3b3933"), false, 1);
        for (int cell = 0; cell < 4; cell++)
        {
            var rect = new Rect2(11 + cell * 23, 40, 21, 14);
            DrawRect(rect, new Color("272317"));
            float filled = (float)Math.Clamp(fraction * 4 - cell, 0, 1);
            var content = new Rect2(rect.Position, new Vector2(rect.Size.X * filled, rect.Size.Y));
            DrawRect(content, new Color("d6ab32"));
            DrawRect(new Rect2(content.Position, new Vector2(content.Size.X, 3)), new Color("ecc34e"));
        }
    }

    private void DrawBoostMeter(double fraction)
    {
        // Ten tapered thrust vanes read as fuel flow, distinct from the shared four-cell magazine meter.
        DrawRect(new Rect2(7, 38, 98, 18), new Color("06151b"));
        DrawLine(new Vector2(8, 56), new Vector2(104, 56), new Color("458d9b"), 1);
        for (int vane = 0; vane < 10; vane++)
        {
            float x = 9 + vane * 9.6f;
            float fill = (float)Math.Clamp(fraction * 10 - vane, 0, 1);
            Vector2[] shape = [new(x + 2, 40), new(x + 9, 40), new(x + 6, 53), new(x, 53)];
            DrawColoredPolygon(shape, new Color("193943"));
            if (fill <= 0) continue;
            DrawColoredPolygon(shape, new Color(0.09f, 0.74f, 0.89f, fill));
            if (fill >= 0.5f) DrawLine(new Vector2(x + 3, 41), new Vector2(x + 7, 41), new Color("b3f7ff"), 1);
        }
    }

    private void AddLabel(Label label, string name, Rect2 rect, int fontSize)
    {
        label.Name = name;
        label.Position = rect.Position;
        label.Size = rect.Size;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color("f3efdf"));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 2);
        AddChild(label);
    }
}
