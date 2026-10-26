using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Development;

internal sealed partial class DeveloperOptionsPanel
{
    private readonly List<(SpinBox Editor, Func<SalvoExplosionSettings, float> Read)> _salvoEditors = [];

    private void AddSalvoExplosionControls(VBoxContainer settings)
    {
        var section = new ConfigsAccordion("Salvo · Local explosion VFX", ResetSalvoExplosion);
        settings.AddChild(section); _localSections.Add(section);
        section.Body.AddChild(new Label { Text = "Local preview · next impact · session only · damage and flight trails unchanged",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var rows = ConfigurationRows(section.Body);
        var entries = new List<(Label Label, Control Editor, string Search)>();
        _sections.Add((section, entries));
        Add("Firework size scale", 1, 20, .5, s => s.FireworkScale, (s, v) => s with { FireworkScale = v });
        Add("Explosion smoke size scale", 1, 20, .5, s => s.SmokeScale, (s, v) => s with { SmokeScale = v });
        Add("Burst spacing (s)", .08, .3, .01, s => s.BurstSpacing, (s, v) => s with { BurstSpacing = v });
        Add("Explosion smoke lifetime (s)", 5, 30, 1, s => s.SmokeLifetime, (s, v) => s with { SmokeLifetime = v });
        Add("Explosion smoke opacity", .1, .8, .01, s => s.SmokeOpacity, (s, v) => s with { SmokeOpacity = v });
        Add("Firework brightness", .5, 3, .1, s => s.Brightness, (s, v) => s with { Brightness = v });

        void Add(string title, double minimum, double maximum, double step, Func<SalvoExplosionSettings, float> read,
            Func<SalvoExplosionSettings, float, SalvoExplosionSettings> write)
        {
            var label = ConfigurationLabel(title);
            var editor = new SpinBox { MinValue = minimum, MaxValue = maximum, Step = step,
                Value = read(SalvoExplosionSettings.Current), CustomMinimumSize = new(150, 36) };
            editor.ValueChanged += value => SalvoExplosionSettings.Current = write(SalvoExplosionSettings.Current, (float)value);
            rows.AddChild(label); rows.AddChild(editor); _salvoEditors.Add((editor, read));
            entries.Add((label, editor, "Salvo local explosion VFX " + title));
        }
    }

    private void ResetSalvoExplosion()
    {
        SalvoExplosionSettings.Current = new();
        foreach (var entry in _salvoEditors) { entry.Editor.SetValueNoSignal(entry.Read(SalvoExplosionSettings.Current)); }
    }
}
