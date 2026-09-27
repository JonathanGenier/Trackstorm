using Godot;

namespace Trackstorm.Client.Hud;

/// <summary>Reusable content/lifecycle for either physical slot; the enclosing assembly owns the frame.</summary>
internal sealed partial class ItemHudSlot : Control
{
    private readonly Label _selection = new();
    private readonly Label _name = new();
    private readonly Label _resource = new();
    private readonly TextureRect _icon = new();
    private ItemHudSlotView? _view;
    private bool _selected;

    internal void Initialize(int number)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(136, 92);
        AddLabel(_selection, "Selection", new Rect2(8, 4, 120, 18), 12);
        AddLabel(_name, "ItemName", new Rect2(6, 65, 124, 19), 13);
        AddLabel(_resource, "ResourceValue", new Rect2(8, 70, 120, 22), 18);
        _selection.Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _icon.Name = "ItemIcon";
        _icon.Position = new Vector2(40, 24);
        _icon.Size = new Vector2(56, 38);
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
        _name.Position = new Vector2(6, view.Resource is null ? 65 : 43);
        _name.Modulate = view.Item == Core.Items.HeldItem.None ? new Color("aaa79e") : Colors.White;
        _icon.Texture = icon;
        _icon.Visible = icon is not null;
        _icon.Size = new Vector2(56, view.Resource is null ? 38 : 21);
        _resource.Visible = view.Resource is not null;
        _resource.Text = view.Resource?.Text ?? string.Empty;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_selected) DrawLine(new Vector2(26, 22), new Vector2(110, 22), new Color("dba455"), 1);
        if (_view?.Resource?.Fraction is not double fraction) return;
        DrawRect(new Rect2(14, 63, 108, 8), new Color("060708"));
        for (int cell = 0; cell < 5; cell++)
        {
            var rect = new Rect2(16 + cell * 21, 65, 19, 4);
            DrawRect(rect, new Color("302c28"));
            float filled = (float)Math.Clamp(fraction * 5 - cell, 0, 1);
            DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X * filled, rect.Size.Y)), new Color("d7b478"));
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
