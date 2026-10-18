using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Development;

internal sealed partial class DeveloperOptionsPanel
{
    private readonly List<(SpinBox Editor, Func<WrenchRepairSettings, float> Read)> _wrenchEditors = [];

    private void AddWrenchControls(VBoxContainer settings)
    {
        var section = new ConfigsAccordion("Wrench · Local repair animation", ResetWrench);
        settings.AddChild(section);
        _localSections.Add(section);
        section.Body.AddChild(new Label { Text = "Session-local cosmetics · travel applies on next equip; repair/VFX on next use",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var rows = ConfigurationRows(section.Body);
        var entries = new List<(Label Label, Control Editor, string Search)>();
        _sections.Add((section, entries));
        Add("WrenchDuration", "Three-contact duration (s)", .8, 1, .02, s => s.Duration, (s, v) => s with { Duration = v });
        Add("WrenchContact", "Contact hold fraction per beat", .25, .40, .01, s => s.ContactFraction, (s, v) => s with { ContactFraction = v });
        Add("WrenchSparks", "Spark intensity (0 disables)", 0, 2, .1, s => s.SparkIntensity, (s, v) => s with { SparkIntensity = v });
        Add("WrenchDeploy", "Full deployment duration (s)", .85, .95, .01, s => s.DeploymentDuration, (s, v) => s with { DeploymentDuration = v });
        Add("WrenchRetract", "Full retraction duration (s)", .85, .95, .01, s => s.RetractionDuration, (s, v) => s with { RetractionDuration = v });
        Add("WrenchSpacing", "Contact lateral spacing (m)", .45, .55, .01, s => s.ContactSpacing, (s, v) => s with { ContactSpacing = v });
        Add("WrenchForward", "Contact forward offset (m)", -.02, .02, .01, s => s.ContactForward, (s, v) => s with { ContactForward = v });
        Add("WrenchHover", "Between-contact lift (m)", .18, .24, .01, s => s.HoverHeight, (s, v) => s with { HoverHeight = v });
        Add("WrenchVibration", "Drill upward vibration (m)", 0, .002, .0005, s => s.Vibration, (s, v) => s with { Vibration = v });
        Add("WrenchFrequency", "Vibration frequency (rad/s)", 120, 240, 10, s => s.VibrationFrequency, (s, v) => s with { VibrationFrequency = v });
        Add("WrenchDrill", "Drill rotation (rad/s)", 60, 100, 5, s => s.DrillSpeed, (s, v) => s with { DrillSpeed = v });
        Add("WrenchSparkLifetime", "Spark lifetime (s)", .10, .20, .01, s => s.SparkLifetime, (s, v) => s with { SparkLifetime = v });
        Add("WrenchSparkSize", "Spark size multiplier", .5, 1.5, .1, s => s.SparkSize, (s, v) => s with { SparkSize = v });
        Add("WrenchSparkSpeed", "Spark speed multiplier", .75, 1.25, .05, s => s.SparkSpeed, (s, v) => s with { SparkSpeed = v });

        void Add(string name, string title, double minimum, double maximum, double step, Func<WrenchRepairSettings, float> read,
            Func<WrenchRepairSettings, float, WrenchRepairSettings> write)
        {
            var label = ConfigurationLabel(title);
            var editor = new SpinBox { Name = name, MinValue = minimum, MaxValue = maximum, Step = step,
                Value = read(WrenchRepairSettings.Current), CustomMinimumSize = new(150, 36) };
            editor.ValueChanged += value => WrenchRepairSettings.Current = write(WrenchRepairSettings.Current, (float)value);
            rows.AddChild(label); rows.AddChild(editor);
            _wrenchEditors.Add((editor, read));
            entries.Add((label, editor, "Wrench local repair " + title));
        }
    }

    private void ResetWrench()
    {
        WrenchRepairSettings.Current = new();
        foreach (var child in _wrenchEditors) { child.Editor.SetValueNoSignal(child.Read(WrenchRepairSettings.Current)); }
    }
}
