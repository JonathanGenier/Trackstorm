using Godot;
using Trackstorm.Client.Development;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Development;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private DevelopmentSession? _lateClient;

    private async Task CheckSharedClients(string endpoint)
    {
        var panel = _devTools.Configs;
        panel.Session = () => _client;
        await Frames(20);
        Set("vehicle.mass", 1300);
        var before = _client!.Arena!.Driver.Configuration;
        Check(!panel.Apply() && panel.AwaitingConfirmation, "client Apply waits for host confirmation");
        Check(_client.Arena.Driver.Configuration == before, "client request creates no speculative gameplay state");
        await Until(() => !panel.AwaitingConfirmation && _host.DeveloperConfiguration.Vehicle.Mass == 1300 && _client.DeveloperConfiguration == _host.DeveloperConfiguration, "client edit converges through authority");
        Check(!panel.HasUnappliedChanges && ReadEditors() == _host.DeveloperConfiguration, "client editor shows accepted values");
        Set("vehicle.mass", -1);
        panel.Apply();
        await Until(() => !panel.AwaitingConfirmation, "host rejection feedback");
        Check(panel.HasUnappliedChanges && _host.DeveloperConfiguration.Vehicle.Mass == 1300, "host rejects unsafe client edit without changing gameplay");
        panel.Cancel();

        var viewport = new SubViewport { Size = new Vector2I(800, 600), OwnWorld3D = true };
        AddChild(viewport);
        _lateClient = new DevelopmentSession { Name = "LateConfigClient" };
        viewport.AddChild(_lateClient);
        _lateClient.Open(false, endpoint, "Late tuning peer");
        await Until(() => _lateClient.Arena?.Driver.IsActive == true && _lateClient.DeveloperConfiguration == _host.DeveloperConfiguration, "late client receives current session tuning and checkpoint");
        for (int i = 0; i < 6; i++)
        {
            var sender = i % 2 == 0 ? _client : _lateClient;
            Check(sender.ConfigureDeveloperOptions(new Dictionary<string, double> { ["damage.max_hp"] = 1500 + i * 100, ["items.missile_speed"] = 80 + i }, out _), "alternating client transaction submitted");
            await Until(() => sender.Lobby?.ConfigurationPending == false && _client.DeveloperConfiguration == _host.DeveloperConfiguration && _lateClient.DeveloperConfiguration == _host.DeveloperConfiguration && _host.DeveloperConfiguration.Damage.MaxHP == 1500 + i * 100, "three-peer configuration convergence");
            await Until(() => _client.Arena.Driver.LocalState?.Damage.MaxHP == 1500 + i * 100 && _lateClient.Arena!.Driver.LocalState?.Damage.MaxHP == 1500 + i * 100, "native gameplay consumes client-requested max HP");
        }
        await Frames(20);
        Set("vehicle.acceleration", 13);
        Check(_lateClient.ConfigureDeveloperOptions(new Dictionary<string, double> { ["vehicle.mass"] = 1400 }, out _), "second client edits while first has a draft");
        await Until(() => _client.DeveloperConfiguration.Vehicle.Mass == 1400, "external edit accepted");
        await Frames(20);
        Check(Descendants(panel).OfType<LineEdit>().Single(editor => editor.Name == "vehicle_mass").Text == "1400", "untouched editor fields converge while another field is dirty");
        Check(Descendants(panel).OfType<LineEdit>().Single(editor => editor.Name == "vehicle_acceleration").Text == "13", "explicit staged text remains a draft");
        panel.Cancel();
        var category = Descendants(panel).OfType<ConfigsAccordion>().Single(section => section.GetChildren().OfType<Button>().Single().Text.EndsWith("Vehicle", StringComparison.Ordinal));
        Descendants(category).OfType<Button>().Single(button => button.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => !panel.AwaitingConfirmation && _host.DeveloperConfiguration.Vehicle.Mass == GameplayConfiguration.HostedDefaults.Vehicle.Mass && _lateClient.DeveloperConfiguration == _host.DeveloperConfiguration, "client category reset converges");
        Press("Reset to Defaults");
        await Until(() => !panel.AwaitingConfirmation && _host.DeveloperConfiguration == GameplayConfiguration.HostedDefaults && _lateClient.DeveloperConfiguration == _host.DeveloperConfiguration, "client global reset converges");
        Check(_lateClient.ConfigureDeveloperOptions(new Dictionary<string, double> { ["vehicle.mass"] = 1100, ["items.machine_gun_damage"] = 12.5 }, out _), "export fixture edits");
        await Until(() => _host.DeveloperConfiguration.Vehicle.Mass == 1100 && _client.DeveloperConfiguration == _host.DeveloperConfiguration && _lateClient.DeveloperConfiguration == _host.DeveloperConfiguration, "export peers share confirmed values");
        var timestamp = DateTimeOffset.UtcNow;
        string Export(DevelopmentSession session) => ConfigurationChangesExport.Format(session.DeveloperConfiguration, GameVersion.Current.ToString(), timestamp);
        Check(Export(_host) == Export(_client) && Export(_host) == Export(_lateClient), "all three peers export identical canonical differences at a common timestamp");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "shared-configs.txt"), Export(_client));
        if (DisplayServer.GetName() == "headless")
        {
            // Headless acceptance drives the real dialog signals; native Windows presentation is inspected separately.
            var export = Descendants(panel).OfType<Button>().Single(button => button.Text == "Export Changes");
            Set("vehicle.mass", 1700);
            export.EmitSignal(BaseButton.SignalName.Pressed);
            var dialog = Descendants(panel).OfType<FileDialog>().Single();
            Check(dialog.UseNativeDialog && dialog.Exclusive && dialog.CurrentFile == "configs.txt", "Export requests native Save As with the required default filename");
            string path = System.IO.Path.Combine(_directory, "client-dialog-export.txt");
            dialog.Hide();
            dialog.EmitSignal(FileDialog.SignalName.FileSelected, path);
            string saved = System.IO.File.ReadAllText(path);
            Check(saved.Contains("vehicle.mass=1100", StringComparison.Ordinal) && !saved.Contains("1700", StringComparison.Ordinal), "dialog save callback writes confirmed values and excludes the pending draft");
            export.EmitSignal(BaseButton.SignalName.Pressed);
            dialog.Hide();
            dialog.EmitSignal(FileDialog.SignalName.Canceled);
            Check(System.IO.File.ReadAllText(path) == saved && HasStatus("Export cancelled"), "cancelled export leaves existing output unchanged");
            export.EmitSignal(BaseButton.SignalName.Pressed);
            dialog.Hide();
            dialog.EmitSignal(FileDialog.SignalName.FileSelected, _directory);
            Check(HasStatus("Could not save export"), "unwritable export destination gives retryable feedback");
            panel.Cancel();
        }
        await CheckImports();
        Check(!_host.DeveloperSettings!.Current.Equals(_host.DeveloperConfiguration), "session changes do not overwrite the local defaults owner");
        await Capture("client-shared-configs");
        _lateClient.Leave();
        await Until(() => _lateClient.LeaveComplete, "late client cleanup");
        _lateClient = null;
        viewport.QueueFree();
        panel.Session = () => _host;
        await Frames(20);
    }
}
