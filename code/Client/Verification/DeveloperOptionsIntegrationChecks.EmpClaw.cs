using Godot;
using Trackstorm.Client.Development;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private async Task CheckEmpClawOptions(string phase)
    {
        Check(_host.DeveloperConfiguration == GameplayConfiguration.HostedDefaults,
            "EMP controls follow session-only configuration: fresh process restores canonical defaults.");
        var panel = _devTools.Configs;
        var search = Descendants(panel).OfType<LineEdit>().Single(editor => editor.Name == "ConfigSearch");
        search.Text = "EMP Claw";
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        foreach (string key in GameplayOptions.All.Where(option => option.Key.Contains("emp_claw", StringComparison.Ordinal)).Select(option => option.Key.Replace('.', '_')))
        {
            Check(Descendants(panel).OfType<LineEdit>().Any(editor => editor.Name == key && editor.IsVisibleInTree()), "EMP search exposes " + key);
        }
        await Frames(3);
        await Capture("emp-claw-controls");
        search.Text = string.Empty;
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        var local = Descendants(panel).OfType<ConfigsAccordion>().Single(section =>
            section.GetChildren().OfType<Button>().Single().Text.EndsWith("Local VFX", StringComparison.Ordinal) &&
            section.GetChildren().OfType<Button>().Single().Text.Contains("EMP Claw", StringComparison.Ordinal));
        var localEditors = Descendants(local.Body).OfType<SpinBox>().ToArray();
        Check(localEditors.Length == 6, "All six bounded EMP cosmetic controls are present.");
        var beforeLocal = _host.DeveloperConfiguration;
        localEditors[0].Value = 1.5;
        localEditors[1].Value = 4;
        localEditors[2].Value = .5;
        localEditors[3].Value = 1.5;
        localEditors[4].Value = .6;
        localEditors[5].Value = 1.8;
        Check(Items.EmpClawVfxSettings.Current == new Items.EmpClawVfxSettings
        { ShockwaveScale = 1.5f, TrailLength = 4, ElectricIntensity = .5f, ImpactIntensity = 1.5f, DeflationSeconds = .6f, CleanupSeconds = 1.8f }, "Native controls edit real cosmetic settings immediately.");
        Check(_host.DeveloperConfiguration == beforeLocal && !panel.HasUnappliedChanges, "Local EMP VFX leaves the authority draft untouched.");
        Descendants(local.Body).OfType<Button>().Single(button => button.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        Check(Items.EmpClawVfxSettings.Current == new Items.EmpClawVfxSettings(), "Local reset restores EMP cosmetics.");
        if (phase == "read") { return; }

        foreach (var item in ItemRegistry.All) { Set($"spawns.{item.Key}_weight", item.Identity == HeldItem.EmpClaw ? 3 : 0); }
        Set("items.emp_claw_deploy_seconds", 1.6);
        Set("items.emp_claw_stow_seconds", 1.1);
        Set("items.emp_claw_interval_ticks", 45);
        var flightEdits = new Dictionary<string, double>
        {
            ["items.emp_claw_launch_speed"] = 70, ["items.emp_claw_cruise_speed"] = 30,
            ["items.emp_claw_deceleration"] = 90, ["items.emp_claw_turn_rate"] = 80,
            ["items.emp_claw_response"] = 6, ["items.emp_claw_acquisition_degrees"] = 20,
            ["items.emp_claw_acquisition_range"] = 140, ["items.emp_claw_lifetime"] = 45,
            ["items.emp_claw_terrain_clearance"] = 1.5,
            ["items.emp_claw_attachment_seconds"] = 12,
            ["items.emp_claw_suppression"] = .5,
            ["items.emp_claw_attached_nose_radius"] = .25,
            ["items.emp_claw_stored_nose_radius"] = .1,
        };
        foreach (var edit in flightEdits) { Set(edit.Key, edit.Value); }
        Check(panel.HasUnappliedChanges, "EMP edits remain staged before Apply.");
        Press("Apply Settings");
        await Until(() => !panel.AwaitingConfirmation, "EMP Apply receives authority confirmation");
        var accepted = _host.DeveloperConfiguration;
        Check(accepted.Items.EmpClawDeploySeconds == 1.6f && accepted.Items.EmpClawStowSeconds == 1.1f &&
            accepted.Items.EmpClawIntervalTicks == 45, "All three EMP presentation controls reach their owning configuration.");
        foreach (var edit in flightEdits)
        { Check(Math.Abs(GameplayOptions.All.Single(option => option.Key == edit.Key).Read(accepted) - edit.Value) < .001, "Flight UI applies " + edit.Key); }
        Check(ItemRegistry.All.All(item => accepted.Spawns.Weights[item.Identity] == (item.Identity == HeldItem.EmpClaw ? 3 : 0)),
            "EMP-only item weights apply through the normal UI.");
        Set("items.emp_claw_interval_ticks", 0);
        Press("Apply Settings");
        Check(_host.DeveloperConfiguration == accepted && panel.HasUnappliedChanges, "Invalid EMP spacing cannot commit.");
        Press("Cancel");
        Check(!panel.HasUnappliedChanges && ReadEditors() == accepted, "Cancel restores accepted EMP values.");
        Set("items.emp_claw_lifetime", 61);
        Press("Apply Settings");
        Check(_host.DeveloperConfiguration == accepted && panel.HasUnappliedChanges, "UI cannot authorize a lifetime beyond sixty seconds.");
        Press("Cancel");
        var category = Descendants(panel).OfType<ConfigsAccordion>().Single(section =>
            section.GetChildren().OfType<Button>().Single().Text.EndsWith("EMP Claw presentation", StringComparison.Ordinal));
        Descendants(category.Body).OfType<Button>().Single(button => button.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => !panel.AwaitingConfirmation, "EMP category reset receives confirmation");
        Check(_host.DeveloperConfiguration.Items.EmpClawDeploySeconds == 1.2f &&
            _host.DeveloperConfiguration.Items.EmpClawStowSeconds == .82f &&
            _host.DeveloperConfiguration.Items.EmpClawIntervalTicks == 50, "EMP category reset restores all presentation defaults.");
        Check(_host.DeveloperConfiguration.Spawns == accepted.Spawns, "EMP presentation reset preserves the chosen pickup pool.");
        Check(_host.DeveloperConfiguration.Items.EmpClaw == accepted.Items.EmpClaw, "Presentation reset preserves independent flight tuning.");
        Check(!System.IO.File.Exists(System.IO.Path.Combine(_directory, "settings.json.developer.jsonl")),
            "Shared EMP tuning is session-only, following existing Developer Options behavior.");
    }
}
