using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Development;

internal sealed partial class DeveloperOptionsPanel
{
    private readonly List<(SpinBox Editor, Func<MachineGunVfxSettings, float> Read)> _machineGunEditors = [];

    private void AddMachineGunControls(VBoxContainer settings)
    {
        var section = new ConfigsAccordion("Machine Gun · Local VFX", ResetMachineGunVfx);
        settings.AddChild(section);
        _localSections.Add(section);
        section.Body.AddChild(new Label { Text = "Session-local cosmetics. Bullet edits affect new rounds; muzzle edits apply live. Fixed 512-flight / 64-per-shooter limits and 0.55 s lifetime remain enforced.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var rows = ConfigurationRows(section.Body);
        var entries = new List<(Label Label, Control Editor, string Search)>();
        _sections.Add((section, entries));
        Add("Speed", "Visible bullet speed (m/s)", 300, 1000, 25, s => s.Speed, (s, v) => s with { Speed = v });
        Add("BulletLength", "Ordinary bullet length (m)", .4, 2, .1, s => s.BulletLength, (s, v) => s with { BulletLength = v });
        Add("TracerLength", "Tracer length (m)", 1, 6, .1, s => s.TracerLength, (s, v) => s with { TracerLength = v });
        Add("Width", "Streak width multiplier", .5, 2, .1, s => s.Width, (s, v) => s with { Width = v });
        Add("Brightness", "Streak brightness multiplier", .25, 1.5, .05, s => s.Brightness, (s, v) => s with { Brightness = v });
        Add("Red", "Red share of tracers (%)", 0, 25, 1, s => s.RedPercent, (s, v) => s with { RedPercent = v });
        Add("Green", "Green share of tracers (%)", 0, 25, 1, s => s.GreenPercent, (s, v) => s with { GreenPercent = v });
        Add("Flash", "Muzzle flash scale (0 hides)", 0, 1.5, .1, s => s.FlashScale, (s, v) => s with { FlashScale = v });
        Add("Smoke", "Muzzle smoke intensity (0 hides)", 0, 2, .1, s => s.SmokeIntensity, (s, v) => s with { SmokeIntensity = v });

        void Add(string name, string title, double minimum, double maximum, double step, Func<MachineGunVfxSettings, float> read,
            Func<MachineGunVfxSettings, float, MachineGunVfxSettings> write)
        {
            var label = ConfigurationLabel(title);
            var editor = new SpinBox { Name = "MachineGun" + name, MinValue = minimum, MaxValue = maximum, Step = step,
                Value = read(MachineGunVfxSettings.Current), CustomMinimumSize = new(150, 36) };
            editor.ValueChanged += value => MachineGunVfxSettings.Current = write(MachineGunVfxSettings.Current, (float)value);
            rows.AddChild(label); rows.AddChild(editor);
            _machineGunEditors.Add((editor, read));
            entries.Add((label, editor, "Machine Gun local VFX " + title));
        }
    }

    private void ResetMachineGunVfx()
    {
        MachineGunVfxSettings.Current = new();
        foreach (var entry in _machineGunEditors) { entry.Editor.SetValueNoSignal(entry.Read(MachineGunVfxSettings.Current)); }
    }
}
