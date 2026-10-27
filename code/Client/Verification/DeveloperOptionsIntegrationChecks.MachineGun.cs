using Godot;
using Trackstorm.Client.Development;
using Trackstorm.Client.Items;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Development;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private async Task CheckMachineGunOptions(string phase, string endpoint)
    {
        Check(_host.DeveloperConfiguration == GameplayConfiguration.HostedDefaults && MachineGunVfxSettings.Current == new MachineGunVfxSettings(),
            "Fresh process retains 800-round production gameplay and local VFX defaults.");
        var panel = _devTools.Configs;
        var search = Descendants(panel).OfType<LineEdit>().Single(editor => editor.Name == "ConfigSearch");
        search.Text = "Machine Gun";
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        foreach (var option in GameplayOptions.All.Where(o => o.Group == "Machine Gun"))
        {
            Check(Descendants(panel).OfType<LineEdit>().Any(editor => editor.Name == option.Key.Replace('.', '_') && editor.IsVisibleInTree()),
                "Machine Gun search exposes " + option.Key);
        }
        await Frames(3); await Capture("machine-gun-gameplay");
        var local = Descendants(panel).OfType<ConfigsAccordion>().Single(section =>
            section.GetChildren().OfType<Button>().Single().Text.EndsWith("Machine Gun · Local VFX", StringComparison.Ordinal));
        var editors = Descendants(local.Body).OfType<SpinBox>().ToArray();
        double[] values = [750, 1.5, 5, 1.3, .8, 3, .12, .5, 1.5];
        Check(editors.Length == values.Length, "All nine bounded Machine Gun visual controls are present.");
        for (int i = 0; i < values.Length; i++) { editors[i].Value = values[i]; }
        Check(MachineGunVfxSettings.Current == new MachineGunVfxSettings { Speed = 750, BulletLength = 1.5f, TracerLength = 5,
            Width = 1.3f, BulletBrightness = .8f, TracerBrightness = 3, MinimumFlightSeconds = .12f, FlashScale = .5f, SmokeIntensity = 1.5f },
            "All native cosmetic editor signals reach their real production values.");
        Check(_host.DeveloperConfiguration == GameplayConfiguration.HostedDefaults && !panel.HasUnappliedChanges,
            "Visual edits neither submit gameplay nor dirty the authority draft.");
        search.Text = "Machine Gun local"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        await Frames(3); await Capture("machine-gun-local-vfx");
        Descendants(local.Body).OfType<Button>().Single(b => b.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        Check(MachineGunVfxSettings.Current == new MachineGunVfxSettings() && editors[0].Value == 600,
            "Local category Reset restores settings and displayed values.");
        if (phase == "read") { return; }

        var viewport = new SubViewport { Size = new Vector2I(800, 600), OwnWorld3D = true };
        AddChild(viewport);
        _client = new DevelopmentSession { Name = "MachineGunTuningPeer" }; viewport.AddChild(_client);
        _client.Open(false, endpoint, "Machine Gun tuning peer");
        await Until(() => _client.Lobby?.State?.Players.Count(p => !p.PracticeCar) == 2, "Machine Gun tuning peer admitted over UDP");
        panel.Session = () => _client; await Frames(20);
        search.Text = "Machine Gun"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        var edits = new Dictionary<string, double> { ["items.machine_gun_capacity"] = 1000, ["items.machine_gun_fire_rate"] = 120,
            ["items.machine_gun_range"] = 250, ["items.machine_gun_spread"] = .7,
            ["items.machine_gun_ammo_per_round"] = 3, ["items.machine_gun_tracer_every"] = 4 };
        foreach (var edit in edits) { Set(edit.Key, edit.Value); }
        Check(panel.HasUnappliedChanges && _host.DeveloperConfiguration.Items.MachineGunCapacity == 500,
            "Gameplay edits remain staged until authoritative Apply.");
        Press("Apply Settings");
        await Until(() => !panel.AwaitingConfirmation && _host.DeveloperConfiguration.Items.MachineGunAmmoPerRound == 3 &&
            _client.DeveloperConfiguration == _host.DeveloperConfiguration, "Client-requested Machine Gun settings converge through host authority");
        foreach (var edit in edits)
        { Check(Math.Abs(GameplayOptions.All.Single(o => o.Key == edit.Key).Read(_host.DeveloperConfiguration) - edit.Value) < .001, "Applied " + edit.Key); }
        var accepted = _host.DeveloperConfiguration;
        Set("items.machine_gun_ammo_per_round", 0); Press("Apply Settings");
        await Until(() => !panel.AwaitingConfirmation, "Invalid Machine Gun usage is rejected");
        Check(_host.DeveloperConfiguration == accepted && panel.HasUnappliedChanges, "Invalid usage cannot partially change accepted gameplay.");
        Press("Cancel"); Check(!panel.HasUnappliedChanges, "Cancel restores accepted Machine Gun values.");
        var shared = Descendants(panel).OfType<ConfigsAccordion>().Single(section =>
            section.GetChildren().OfType<Button>().Single().Text.EndsWith(" Machine Gun", StringComparison.Ordinal));
        Descendants(shared.Body).OfType<Button>().Single(b => b.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => !panel.AwaitingConfirmation && _client.DeveloperConfiguration == _host.DeveloperConfiguration &&
            _host.DeveloperConfiguration.Items.MachineGunCapacity == 500, "Machine Gun category Reset is acknowledged by both peers");
        Check(_host.DeveloperConfiguration.Items == GameplayConfiguration.HostedDefaults.Items,
            "Shared Reset restores all production Machine Gun defaults.");
        if (OS.GetCmdlineUserArgs().Contains("--machine-gun-readability")) { await CheckMachineGunReadability(); }
    }
}
