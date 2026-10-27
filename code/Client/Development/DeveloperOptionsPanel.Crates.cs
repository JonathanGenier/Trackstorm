using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Development;

internal sealed partial class DeveloperOptionsPanel
{
    private SpinBox _crateSize = null!;

    private void AddCrateSmashControls(VBoxContainer settings)
    {
        var section = new ConfigsAccordion("Crates · Local smash VFX", ResetCrateSmash);
        settings.AddChild(section);
        _localSections.Add(section);
        section.Body.AddChild(new Label { Text = "Local preview · next pickup · session only. Respawn time is shared under Item spawns.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var rows = ConfigurationRows(section.Body);
        var label = ConfigurationLabel("Crate explosion size (0.5–1.5×)");
        _crateSize = new SpinBox { Name = "CrateSmashSize", MinValue = .5, MaxValue = 1.5, Step = .05,
            Value = CarnivalSmashSettings.Current.Size, CustomMinimumSize = new(150, 36),
            TooltipText = "1× is the compact default. Scales debris, gold shapes, sparks and flash together; existing bursts keep their size." };
        _crateSize.ValueChanged += value => CarnivalSmashSettings.Current = new() { Size = (float)value };
        rows.AddChild(label); rows.AddChild(_crateSize);
        _sections.Add((section, [(label, _crateSize, "Crates local smash explosion effect size scale")]));
    }

    private void ResetCrateSmash()
    {
        CarnivalSmashSettings.Current = new();
        _crateSize.SetValueNoSignal(CarnivalSmashSettings.Current.Size);
    }
}
