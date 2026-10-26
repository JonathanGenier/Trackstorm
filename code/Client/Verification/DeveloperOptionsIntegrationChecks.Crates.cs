using Godot;
using Trackstorm.Client.Development;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private async Task CheckCrateOptions()
    {
        var panel = _devTools.Configs;
        Check(CarnivalSmashSettings.Current.Size == 1 && _host.DeveloperConfiguration.Spawns.CooldownTicks == 600,
            "Fresh process restores compact VFX and ten-second authoritative crate respawn defaults");
        var search = Descendants(panel).OfType<LineEdit>().Single(editor => editor.Name == "ConfigSearch");
        search.Text = "crate"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        var size = Descendants(panel).OfType<SpinBox>().Single(editor => editor.Name == "CrateSmashSize");
        var cooldown = Descendants(panel).OfType<LineEdit>().Single(editor => editor.Name == "spawns_cooldown_ticks");
        Check(size.IsVisibleInTree() && cooldown.IsVisibleInTree(), "Crate search exposes cosmetic size and the existing shared respawn control");
        var authority = _host.DeveloperConfiguration;
        size.Value = 1.5;
        Check(CarnivalSmashSettings.Current.Size == 1.5f && _host.DeveloperConfiguration == authority && !panel.HasUnappliedChanges,
            "Local size editor changes no authority, pickup radius, cooldown or configuration draft");
        var effect = new CarnivalSmash { Token = 311, Resources = new CarnivalSmashResources() };
        AddChild(effect); effect.SetProcess(false); effect.Advance(.1f);
        Check(effect.Settings.Size == 1.5f && effect.GetChildren().OfType<MultiMeshInstance3D>().All(view =>
            ((ShaderMaterial)view.MaterialOverride).GetShaderParameter("effect_scale").AsSingle() == 1.5f),
            "All actual wood, gold and spark render batches capture the edited size");
        var local = Descendants(panel).OfType<ConfigsAccordion>().Single(section => section.Body.IsAncestorOf(size));
        Descendants(local.Body).OfType<Button>().Single(button => button.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        Check(CarnivalSmashSettings.Current.Size == 1 && size.Value == 1 && effect.Settings.Size == 1.5f,
            "Category reset affects future smashes without resizing or restarting active debris");
        effect.Advance(11.8f);
        Check(!effect.IsQueuedForDeletion(), "Maximum-size preview keeps the normal lifetime boundary");
        effect.Advance(.2f);
        Check(effect.IsQueuedForDeletion(), "Maximum-size preview still cleans up at twelve seconds");
        await Frames(2);
        Set("spawns.cooldown_ticks", 120);
        Check(panel.HasUnappliedChanges && _host.DeveloperConfiguration.Spawns.CooldownTicks == 600, "Crate respawn timing remains staged until Apply");
        panel.Apply();
        await Until(() => _host.DeveloperConfiguration.Spawns.CooldownTicks == 120, "Shared crate respawn editor applies two seconds through host validation");
        Check(_host.DeveloperConfiguration == authority with { Spawns = authority.Spawns with { CooldownTicks = 120 } },
            "Respawn edit changes only its existing authoritative field");
        Set("spawns.cooldown_ticks", 0); panel.Apply(); await Frames(3);
        Check(_host.DeveloperConfiguration.Spawns.CooldownTicks == 120 && panel.HasUnappliedChanges, "Invalid zero respawn is rejected without changing live state");
        Press("Cancel");
        await Capture("crate-controls");
    }
}
