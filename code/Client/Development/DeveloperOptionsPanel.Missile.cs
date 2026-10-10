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
        Add("Smoke residue lifetime (s)", 1, 6, .1, s => s.SmokeLifetime, (s, v) => s with { SmokeLifetime = v });
        Add("Dense smoke wake length (m)", 6, 9.2, .1, s => s.SmokeTrailLength, (s, v) => s with { SmokeTrailLength = v });
        Add("Smoke size (m)", .15, 2, .05, s => s.SmokeSize, (s, v) => s with { SmokeSize = v });
        Add("Smoke opacity", 0, .7, .01, s => s.SmokeOpacity, (s, v) => s with { SmokeOpacity = v });
        Add("Trail density (0 disables trail)", 0, 2, .1, s => s.Density, (s, v) => s with { Density = v });
        Add("Burning confetti lifetime (s)", .1, .8, .01, s => s.ConfettiLifetime, (s, v) => s with { ConfettiLifetime = v });
        Add("Confetti paper size (m)", .02, .15, .005, s => s.ConfettiSize, (s, v) => s with { ConfettiSize = v });
        Add("Red ember lifetime (s)", .1, .8, .05, s => s.EmberLifetime, (s, v) => s with { EmberLifetime = v });
        AddMissileExplosionControls(settings);

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

    private readonly List<(SpinBox Editor, Func<MissileExplosionSettings, float> Read)> _explosionEditors = new();

    private void AddMissileExplosionControls(VBoxContainer settings)
    {
        var section = new ConfigsAccordion("Missile · Local explosion VFX", ResetMissileExplosion);
        settings.AddChild(section);
        _localSections.Add(section);
        section.Body.AddChild(new Label { Text = "Local cosmetics · applies to the next impact · no damage or impulse changes",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var rows = ConfigurationRows(section.Body);
        var entries = new List<(Label Label, Control Editor, string Search)>();
        _sections.Add((section, entries));
        Add("Explosion intensity", .2, 2, .1, s => s.Intensity, (s, v) => s with { Intensity = v });
        Add("Central fire scale (within blast)", .5, 1.5, .1, s => s.FireScale, (s, v) => s with { FireScale = v });
        Add("Cosmetic reach (car lengths)", 1, 3, .05, s => s.ReachCarLengths, (s, v) => s with { ReachCarLengths = v });
        Add("Firework density (0 disables)", 0, 2, .1, s => s.Density, (s, v) => s with { Density = v });
        Add("Firework duration (s)", .8, 2.4, .1, s => s.Duration, (s, v) => s with { Duration = v });
        Add("Impact smoke duration (s)", .8, 5, .1, s => s.SmokeDuration, (s, v) => s with { SmokeDuration = v });
        Add("Impact smoke opacity", 0, .6, .02, s => s.SmokeOpacity, (s, v) => s with { SmokeOpacity = v });

        void Add(string title, double minimum, double maximum, double step, Func<MissileExplosionSettings, float> read,
            Func<MissileExplosionSettings, float, MissileExplosionSettings> write)
        {
            var label = ConfigurationLabel(title);
            var editor = new SpinBox { MinValue = minimum, MaxValue = maximum, Step = step,
                Value = read(MissileExplosionSettings.Current), CustomMinimumSize = new(150, 36) };
            editor.ValueChanged += value => MissileExplosionSettings.Current = write(MissileExplosionSettings.Current, (float)value);
            rows.AddChild(label); rows.AddChild(editor);
            _explosionEditors.Add((editor, read));
            entries.Add((label, editor, "Missile local explosion VFX " + title));
        }
    }

    private void ResetMissileExplosion()
    {
        MissileExplosionSettings.Current = new();
        foreach (var child in _explosionEditors) { child.Editor.SetValueNoSignal(child.Read(MissileExplosionSettings.Current)); }
    }
}
