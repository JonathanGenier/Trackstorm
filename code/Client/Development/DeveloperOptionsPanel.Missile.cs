using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Development;

internal sealed partial class DeveloperOptionsPanel
{
    private void AddMissileVfxControls(VBoxContainer settings)
    {
        var section = new ConfigsAccordion("Missile · Local flight VFX", ResetMissileVfx);
        section.Body.GetChildren().OfType<Button>().Single().TooltipText = "Restore local Missile VFX defaults immediately on this device.";
        settings.AddChild(section);
        _localSections.Add(section);
        section.Body.AddChild(new Label { Text = "Live local preview · applies immediately on this device · session only",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var rows = ConfigurationRows(section.Body);
        var entries = new List<(Label Label, Control Editor, string Search)>();
        _sections.Add((section, entries));
        Add("Orange flame length (m)", .2, 2.5, .05, s => s.FlameLength, (s, v) => s with { FlameLength = v });
        Add("Flame width (m)", .05, .5, .01, s => s.FlameWidth, (s, v) => s with { FlameWidth = v });
        Add("Crimson smoke lifetime (s)", .1, 1.2, .05, s => s.SmokeLifetime, (s, v) => s with { SmokeLifetime = v });
        Add("Smoke size (m)", .15, 1.2, .05, s => s.SmokeSize, (s, v) => s with { SmokeSize = v });
        Add("Smoke opacity", 0, .7, .01, s => s.SmokeOpacity, (s, v) => s with { SmokeOpacity = v });
        Add("Trail density (0 disables trail)", 0, 2, .1, s => s.Density, (s, v) => s with { Density = v });
        Add("Burning confetti lifetime (s)", .1, .8, .01, s => s.ConfettiLifetime, (s, v) => s with { ConfettiLifetime = v });
        Add("Confetti paper size (m)", .02, .15, .005, s => s.ConfettiSize, (s, v) => s with { ConfettiSize = v });
        Add("Red ember lifetime (s)", .1, .8, .05, s => s.EmberLifetime, (s, v) => s with { EmberLifetime = v });

        void Add(string title, double minimum, double maximum, double step, Func<MissileVfxSettings, float> read,
            Func<MissileVfxSettings, float, MissileVfxSettings> write)
        {
            var label = ConfigurationLabel(title);
            var editor = new SpinBox { MinValue = minimum, MaxValue = maximum, Step = step,
                Value = read(MissileVfxSettings.Current), CustomMinimumSize = new(150, 36) };
            editor.ValueChanged += value => MissileVfxSettings.Current = write(MissileVfxSettings.Current, (float)value);
            rows.AddChild(label); rows.AddChild(editor);
            _missileEditors.Add((editor, read));
            entries.Add((label, editor, "Missile local VFX " + title));
        }
    }

    private void ResetMissileVfx()
    {
        MissileVfxSettings.Current = new();
        foreach (var child in _missileEditors) { child.Editor.SetValueNoSignal(child.Read(MissileVfxSettings.Current)); }
    }

    private readonly List<(SpinBox Editor, Func<MissileVfxSettings, float> Read)> _missileEditors = new();
}
