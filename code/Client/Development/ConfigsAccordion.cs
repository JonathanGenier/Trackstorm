using Godot;

namespace Trackstorm.Client.Development;

/// <summary>Session-local category disclosure; filtering reveals matches without changing the user's open state.</summary>
internal sealed partial class ConfigsAccordion : VBoxContainer
{
    private readonly Button _header;
    private readonly string _title;
    private bool _expanded = true;

    internal ConfigsAccordion(string title, Action reset)
    {
        _title = title;
        _header = new Button { Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 40) };
        _header.AddThemeColorOverride("font_disabled_color", Colors.White);
        AddChild(_header);
        _header.Pressed += () => { _expanded = !_expanded; Reveal(false); };
        var inset = new MarginContainer();
        inset.AddThemeConstantOverride("margin_left", 12);
        AddChild(inset);
        Body = new VBoxContainer();
        inset.AddChild(Body);
        var resetButton = new Button { Text = "Reset to Defaults", SizeFlagsHorizontal = SizeFlags.ShrinkBegin, TooltipText = $"Stage defaults for all settings in {title}, including settings hidden by search." };
        DevToolsButtonPresentation.Configure(resetButton, "reset", DevToolsButtonPresentation.Treatment.Reset);
        resetButton.Pressed += reset;
        Body.AddChild(resetButton);
        Reveal(false);
    }

    /// <summary>Owns the category reset and settings together beneath the disclosure header.</summary>
    internal VBoxContainer Body { get; }

    /// <summary>Temporarily expands search results; clearing search restores the previous disclosure state.</summary>
    /// <param name="filtering">Whether a nonempty search is revealing matches.</param>
    internal void Reveal(bool filtering)
    {
        bool open = filtering || _expanded;
        Body.GetParent<Control>().Visible = open;
        _header.Text = $"{(open ? "▾" : "▸")} {_title}";
        _header.Disabled = filtering;
        _header.TooltipText = filtering ? "Matching settings are expanded. Clear search to restore your category layout." : $"{(open ? "Collapse" : "Expand")} {_title}";
    }
}
