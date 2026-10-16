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
        section.Body.AddChild(new Label { Text = "Local cosmetics · applies to the next repair · session only",
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var rows = ConfigurationRows(section.Body);
        var entries = new List<(Label Label, Control Editor, string Search)>();
        _sections.Add((section, entries));
        Add("WrenchDuration", "Three-contact duration (s)", .8, 1, .02, s => s.Duration, (s, v) => s with { Duration = v });
        Add("WrenchContact", "Contact hold fraction per beat", .25, .40, .01, s => s.ContactFraction, (s, v) => s with { ContactFraction = v });
        Add("WrenchSparks", "Spark intensity (0 disables)", 0, 2, .1, s => s.SparkIntensity, (s, v) => s with { SparkIntensity = v });

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
