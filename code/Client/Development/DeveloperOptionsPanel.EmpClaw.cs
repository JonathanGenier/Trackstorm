using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Development;

internal sealed partial class DeveloperOptionsPanel
{
    private readonly List<(SpinBox Editor, Func<EmpClawVfxSettings, float> Read)> _empEditors = [];

    private void AddEmpClawControls(VBoxContainer settings)
    {
        var section = new ConfigsAccordion("EMP Claw · Local VFX", ResetEmpClawVfx);
        settings.AddChild(section);
        _localSections.Add(section);
        section.Body.AddChild(new Label { Text = "Local cosmetics · immediate, session only. Release timings apply to the next removal.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var rows = ConfigurationRows(section.Body);
        var entries = new List<(Label Label, Control Editor, string Search)>();
        _sections.Add((section, entries));
        Add("Shockwave scale", .25, 2, .05, s => s.ShockwaveScale, (s, v) => s with { ShockwaveScale = v });
        Add("Electric flight trail length (m)", .5, 5, .1, s => s.TrailLength, (s, v) => s with { TrailLength = v });
        Add("Electric intensity (0 hides arcs)", 0, 2, .1, s => s.ElectricIntensity, (s, v) => s with { ElectricIntensity = v });
        Add("Impact / pop intensity (0 hides sparks)", 0, 2, .1, s => s.ImpactIntensity, (s, v) => s with { ImpactIntensity = v });
        Add("Nose deflation duration (s)", .1, .8, .05, s => s.DeflationSeconds, (s, v) => s with { DeflationSeconds = v });
        Add("Expired claw cleanup duration (s)", .8, 2, .1, s => s.CleanupSeconds, (s, v) => s with { CleanupSeconds = v });

        void Add(string title, double minimum, double maximum, double step, Func<EmpClawVfxSettings, float> read,
            Func<EmpClawVfxSettings, float, EmpClawVfxSettings> write)
        {
            var label = ConfigurationLabel(title);
            var editor = new SpinBox { MinValue = minimum, MaxValue = maximum, Step = step,
                Value = read(EmpClawVfxSettings.Current), CustomMinimumSize = new(150, 36) };
            editor.ValueChanged += value => EmpClawVfxSettings.Current = write(EmpClawVfxSettings.Current, (float)value);
            rows.AddChild(label); rows.AddChild(editor);
            _empEditors.Add((editor, read));
            entries.Add((label, editor, "EMP Claw local VFX " + title));
        }
    }

    private void ResetEmpClawVfx()
    {
        EmpClawVfxSettings.Current = new();
        foreach (var entry in _empEditors) entry.Editor.SetValueNoSignal(entry.Read(EmpClawVfxSettings.Current));
    }
}
