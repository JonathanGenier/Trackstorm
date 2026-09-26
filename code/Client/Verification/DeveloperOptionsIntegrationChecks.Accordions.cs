using Godot;
using Trackstorm.Client.Development;
using Trackstorm.Core.Development;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private async Task CheckInitialAccordionState()
    {
        var sections = Descendants(_devTools.Configs).OfType<ConfigsAccordion>().ToArray();
        Check(sections.All(section => !section.Body.IsVisibleInTree()), "fresh Configs categories begin collapsed");
        await Capture("accordion-initial-collapsed");
        var search = Descendants(_devTools.Configs).OfType<LineEdit>().Single(editor => editor.Name == "ConfigSearch");
        search.Text = "salvo shots";
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        Check(sections.Count(section => section.Body.IsVisibleInTree()) == 1, "fresh collapsed Configs exposes matching search category");
        search.Text = string.Empty;
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        Check(sections.All(section => !section.Body.IsVisibleInTree()), "clearing initial search restores collapsed defaults");
        foreach (var section in sections) { section.GetChildren().OfType<Button>().Single().EmitSignal(BaseButton.SignalName.Pressed); }
        Check(sections.All(section => section.Body.IsVisibleInTree()), "all categories can be opened together for editing");
    }

    private async Task CheckAccordions()
    {
        var panel = _devTools.Configs;
        var sections = Descendants(panel).OfType<ConfigsAccordion>().ToArray();
        Button Header(ConfigsAccordion section) => section.GetChildren().OfType<Button>().Single();
        Button Reset(ConfigsAccordion section) => Descendants(section.Body).OfType<Button>().Single(button => button.Text == "Reset to Defaults");
        Control Editor(string key) => Descendants(panel).OfType<Control>().Single(control => control.Name == key.Replace('.', '_'));
        ConfigsAccordion Category(string name) => sections.Single(section => Header(section).Text[2..] == name);
        void Search(string query)
        {
            var search = (LineEdit)Editor("ConfigSearch");
            search.Text = query;
            search.EmitSignal(LineEdit.SignalName.TextChanged, query);
        }
        string Text(string key) => ((LineEdit)Editor(key)).Text;
        string path = System.IO.Path.Combine(_directory, "settings.json.developer.jsonl");
        var before = _host.Arena!.Driver.Configuration;
        string persisted = System.IO.File.ReadAllText(path);
        var names = GameplayOptions.All.Select(option => option.Group).Distinct()
            .Concat(TireEffectSettings.Options.Select(option => option.Group).Distinct()).Append("Local network simulation");
        Check(sections.Select(section => Header(section).Text[2..]).SequenceEqual(names), "every category remains in catalog order");
        foreach (var section in sections)
        {
            Check(section.Body.IsVisibleInTree() && Reset(section).Icon is not null, "opened category contains its own styled reset");
            Header(section).EmitSignal(BaseButton.SignalName.Pressed);
            Check(!section.Body.IsVisibleInTree() && Header(section).Text.StartsWith("▸", StringComparison.Ordinal), "header collapses category body and reset");
            Header(section).EmitSignal(BaseButton.SignalName.Pressed);
            Check(section.Body.IsVisibleInTree(), "header reopens category");
        }

        Set("items.salvo_count", 9);
        Set("items.machine_gun_damage", 12);
        Set("items.nitro_forward_thrust", 21000);
        Set("vehicle.mass", 1234);
        Set("tire.grass.duration", .5);
        var latency = Descendants(panel).OfType<SpinBox>().First();
        latency.Value = 25;
        var salvo = Category("Salvo");
        var gun = Category("Machine Gun");
        Header(salvo).EmitSignal(BaseButton.SignalName.Pressed);
        Header(gun).EmitSignal(BaseButton.SignalName.Pressed);
        Check(!Editor("items.salvo_count").IsVisibleInTree() && Text("items.salvo_count") == "9", "collapse preserves exact staged text");
        Header(gun).EmitSignal(BaseButton.SignalName.Pressed);
        Check(gun.Body.IsVisibleInTree() && !salvo.Body.IsVisibleInTree(), "categories toggle independently");
        Search("SALVO shots");
        Check(salvo.Body.IsVisibleInTree() && Editor("items.salvo_count").IsVisibleInTree() && !gun.IsVisibleInTree(), "search opens matching collapsed category and hides empty categories");
        Check(!Editor("items.salvo_interval_ticks").IsVisibleInTree(), "search hides nonmatching rows within the matched category");
        Reset(salvo).EmitSignal(BaseButton.SignalName.Pressed);
        foreach (var option in GameplayOptions.All.Where(option => option.Group == "Salvo"))
        {
            var editor = (LineEdit)Editor(option.Key);
            Check((option.Integral || option.DoublePrecision ? double.Parse(editor.Text, System.Globalization.CultureInfo.InvariantCulture) : float.Parse(editor.Text, System.Globalization.CultureInfo.InvariantCulture)) == option.Read(GameplayConfiguration.HostedDefaults), "filtered reset stages the entire Salvo category: " + option.Key);
            Check(editor.GetThemeColor("font_color") == new Color("69b7ff"), "reset immediately restores default blue: " + option.Key);
        }
        Check(Text("items.machine_gun_damage") == "12" && Text("items.nitro_forward_thrust") == "21000" && Text("vehicle.mass") == "1234" && Text("tire.grass.duration").StartsWith("0.5", StringComparison.Ordinal) && latency.Value == 25, "category reset retains every unrelated staged owner");
        Check(_host.Arena.Driver.Configuration == before && System.IO.File.ReadAllText(path) == persisted, "category reset leaves effective revision and persistence untouched");
        Check(panel.HasUnappliedChanges && HasStatus("Salvo defaults staged"), "category reset participates in existing unsaved feedback");
        await Frames(3);
        await Capture("accordion-filtered-reset");
        Search(string.Empty);
        Check(!salvo.Body.IsVisibleInTree() && gun.Body.IsVisibleInTree(), "clear search restores prior independent disclosure state");
        Header(salvo).EmitSignal(BaseButton.SignalName.Pressed);
        Check(salvo.Body.IsVisibleInTree() && gun.Body.IsVisibleInTree(), "multiple categories remain open together");
        Reset(salvo).EmitSignal(BaseButton.SignalName.Pressed);
        Check(Text("items.machine_gun_damage") == "12", "repeated already-default reset preserves unrelated edits");
        Check(panel.Apply(), "Apply accepts category defaults and unrelated edits together");
        var accepted = _host.Arena.Driver.Configuration;
        Check(accepted.Configuration.Items.SalvoCount == GameplayConfiguration.HostedDefaults.Items.SalvoCount && accepted.Configuration.Items.MachineGunDamage == 12 && accepted.Configuration.Vehicle.Mass == 1234, "combined staged values reach host authority");
        Check(accepted.Revision == before.Revision + 1, "combined category reset advances one revision");
        await Until(() => _client!.Arena!.Driver.Configuration == accepted, "combined category reset synchronizes over UDP");
        Check(new DeveloperSettingsStore(path).LoadForHost() == accepted.Configuration, "combined category reset persists accepted gameplay");

        Set("items.machine_gun_damage", 19);
        Reset(salvo).EmitSignal(BaseButton.SignalName.Pressed);
        panel.Cancel();
        Check(ReadEditors() == accepted.Configuration && !panel.HasUnappliedChanges, "Cancel discards category reset and unrelated edits");
        Set("vehicle.mass", 1400);
        Set("tire.grass.duration", .8);
        Set("tire.dirt.duration", .75);
        Search("tire grass duration");
        var grass = sections.Single(section => section.IsVisibleInTree());
        Reset(grass).EmitSignal(BaseButton.SignalName.Pressed);
        Check(Text("tire.grass.duration") == TireEffectSettings.Defaults["tire.grass.duration"].ToString("G", System.Globalization.CultureInfo.InvariantCulture) && Text("tire.dirt.duration").StartsWith("0.75", StringComparison.Ordinal) && Text("vehicle.mass") == "1400", "local category reset preserves unrelated local and gameplay edits");
        Search("local network");
        latency.Value = 40;
        Reset(Category("Local network simulation")).EmitSignal(BaseButton.SignalName.Pressed);
        Check(latency.Value == 0 && Text("vehicle.mass") == "1400" && Text("tire.dirt.duration").StartsWith("0.75", StringComparison.Ordinal), "network category reset is staged and isolated");
        panel.Cancel();
        Check(latency.Value == 25, "Cancel restores previously applied network impairment after category reset");
        Press("Reset to Defaults");
        Check(ReadEditors() == GameplayConfiguration.HostedDefaults && latency.Value == 0 && Text("tire.dirt.duration") == TireEffectSettings.Defaults["tire.dirt.duration"].ToString("G", System.Globalization.CultureInfo.InvariantCulture), "global reset while filtering still stages all categories");
        Check(_host.Arena.Driver.Configuration == accepted, "global reset stays unapplied");
        panel.Cancel();
        Search(string.Empty);
        latency.Value = 0;
        Check(panel.Apply(), "restore zero local impairment for subsequent checks");
        foreach (var section in sections) { Header(section).EmitSignal(BaseButton.SignalName.Pressed); }
        await Frames(3);
        await Capture("accordion-all-collapsed");
        _devTools.Open(DevToolsTab.Stats);
        _devTools.Open(DevToolsTab.Configs);
        Check(sections.All(section => !section.Body.IsVisibleInTree()), "tab switching retains collapsed categories");
        foreach (var section in sections) { Header(section).EmitSignal(BaseButton.SignalName.Pressed); }
    }
}
