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
        foreach (string key in new[] { "spawns_emp_claw_weight", "items_emp_claw_deploy_seconds", "items_emp_claw_stow_seconds", "items_emp_claw_interval_ticks" })
        {
            Check(Descendants(panel).OfType<LineEdit>().Any(editor => editor.Name == key && editor.IsVisibleInTree()), "EMP search exposes " + key);
        }
        await Frames(3);
        await Capture("emp-claw-controls");
        search.Text = string.Empty;
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        if (phase == "read") { return; }

        foreach (var item in ItemRegistry.All) { Set($"spawns.{item.Key}_weight", item.Identity == HeldItem.EmpClaw ? 3 : 0); }
        Set("items.emp_claw_deploy_seconds", 1.6);
        Set("items.emp_claw_stow_seconds", 1.1);
        Set("items.emp_claw_interval_ticks", 45);
        Check(panel.HasUnappliedChanges, "EMP edits remain staged before Apply.");
        Press("Apply Settings");
        await Until(() => !panel.AwaitingConfirmation, "EMP Apply receives authority confirmation");
        var accepted = _host.DeveloperConfiguration;
        Check(accepted.Items.EmpClawDeploySeconds == 1.6f && accepted.Items.EmpClawStowSeconds == 1.1f &&
            accepted.Items.EmpClawIntervalTicks == 45, "All three EMP presentation controls reach their owning configuration.");
        Check(ItemRegistry.All.All(item => accepted.Spawns.Weights[item.Identity] == (item.Identity == HeldItem.EmpClaw ? 3 : 0)),
            "EMP-only item weights apply through the normal UI.");
        Set("items.emp_claw_interval_ticks", 0);
        Press("Apply Settings");
        Check(_host.DeveloperConfiguration == accepted && panel.HasUnappliedChanges, "Invalid EMP spacing cannot commit.");
        Press("Cancel");
        Check(!panel.HasUnappliedChanges && ReadEditors() == accepted, "Cancel restores accepted EMP values.");
        var category = Descendants(panel).OfType<ConfigsAccordion>().Single(section =>
            section.GetChildren().OfType<Button>().Single().Text.EndsWith("EMP Claw presentation", StringComparison.Ordinal));
        Descendants(category.Body).OfType<Button>().Single(button => button.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => !panel.AwaitingConfirmation, "EMP category reset receives confirmation");
        Check(_host.DeveloperConfiguration.Items.EmpClawDeploySeconds == 1.2f &&
            _host.DeveloperConfiguration.Items.EmpClawStowSeconds == .82f &&
            _host.DeveloperConfiguration.Items.EmpClawIntervalTicks == 50, "EMP category reset restores all presentation defaults.");
        Check(_host.DeveloperConfiguration.Spawns == accepted.Spawns, "EMP presentation reset preserves the chosen pickup pool.");
        Check(!System.IO.File.Exists(System.IO.Path.Combine(_directory, "settings.json.developer.jsonl")),
            "Shared EMP tuning is session-only, following existing Developer Options behavior.");
    }
}
