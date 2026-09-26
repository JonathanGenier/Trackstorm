using Godot;
using Trackstorm.Client.Development;
using Trackstorm.Core.Development;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private async Task CheckImports()
    {
        if (DisplayServer.GetName() != "headless") return;
        var client = _client ?? throw new InvalidOperationException("Import check requires a client");
        var lateClient = _lateClient ?? throw new InvalidOperationException("Import check requires a late client");
        var panel = _devTools.Configs;
        var import = Descendants(panel).OfType<Button>().Single(button => button.Text == "Import Configs");
        var defaults = GameplayConfiguration.HostedDefaults;
        var source = defaults with { Vehicle = defaults.Vehicle with { Mass = 1250.125f }, Damage = defaults.Damage with { MaxHP = 2100 } };
        string path = System.IO.Path.Combine(_directory, "import-configs.txt");
        System.IO.File.WriteAllText(path, ConfigurationChangesExport.Format(source, "0.1.70", DateTimeOffset.UtcNow));
        FileDialog Open()
        {
            import.EmitSignal(BaseButton.SignalName.Pressed);
            var dialog = Descendants(panel).OfType<FileDialog>().Single(dialog => dialog.FileMode == FileDialog.FileModeEnum.OpenFile);
            Check(dialog.UseNativeDialog && dialog.Exclusive && dialog.CurrentFile == "configs.txt", "Import requests the native file picker");
            dialog.Hide();
            return dialog;
        }
        void Load(string file) => Open().EmitSignal(FileDialog.SignalName.FileSelected, file);
        var before = _host.DeveloperConfiguration;
        Set("vehicle.mass", 1700);
        Open().EmitSignal(FileDialog.SignalName.Canceled);
        Check(ReadEditors().Vehicle.Mass == 1700 && _host.DeveloperConfiguration == before, "cancelled import retains draft and session");
        string invalid = System.IO.Path.Combine(_directory, "invalid-import.txt");
        System.IO.File.WriteAllText(invalid, ConfigurationChangesExport.Format(source, GameVersion.Current.ToString(), DateTimeOffset.UtcNow) + "\n[Vehicle]\nvehicle.mass=-1\n");
        Load(invalid);
        Check(ReadEditors().Vehicle.Mass == 1700 && _host.DeveloperConfiguration == before && HasStatus("repeated category"), "malformed import preserves draft and gameplay atomically");
        Load(path + ".missing");
        Check(HasStatus("Could not read config export") && ReadEditors().Vehicle.Mass == 1700, "missing file leaves existing draft intact");
        System.IO.File.WriteAllBytes(invalid, [0xff, 0xfe, 0xff]);
        Load(invalid);
        Check(HasStatus("Could not read config export"), "invalid UTF-8 rejected safely");
        System.IO.File.WriteAllText(invalid, new string(' ', ConfigurationChangesImport.MaximumLength + 1));
        Load(invalid);
        Check(HasStatus("exceeds 64 KiB"), "oversized file rejected without applying values");
        panel.Cancel();
        Load(path);
        Check(_host.DeveloperConfiguration == before && client.DeveloperConfiguration == before, "import only stages values before Apply");
        Check(ReadEditors().Vehicle.Mass == source.Vehicle.Mass && ReadEditors().Damage.MaxHP == 2100, "exported values round trip into actual controls");
        Check(ReadEditors().Items.MachineGunDamage == before.Items.MachineGunDamage, "unlisted nondefault gameplay value stays unchanged");
        Check(lateClient.ConfigureDeveloperOptions(new Dictionary<string, double> { ["vehicle.acceleration"] = 22 }, out _), "another peer edits an unlisted field before Apply");
        await Until(() => client.DeveloperConfiguration.Vehicle.Acceleration == 22, "external unlisted value synchronizes");
        await Frames(20);
        Check(ReadEditors().Vehicle.Acceleration == 22 && ReadEditors().Vehicle.Mass == source.Vehicle.Mass, "imported draft survives while unlisted fields converge");
        Check(!panel.Apply() && panel.AwaitingConfirmation, "client import Apply waits for host acknowledgement");
        await Until(() => !panel.AwaitingConfirmation && _host.DeveloperConfiguration.Vehicle.Mass == source.Vehicle.Mass && client.DeveloperConfiguration == _host.DeveloperConfiguration && lateClient.DeveloperConfiguration == _host.DeveloperConfiguration, "client import converges on all three peers");
        await Until(() => client.Arena!.Driver.LocalState?.Damage.MaxHP == 2100 && lateClient.Arena!.Driver.LocalState?.Damage.MaxHP == 2100, "imported values reach native gameplay owners");
        Check(_host.DeveloperConfiguration.Vehicle.Acceleration == 22 && _host.DeveloperConfiguration.Items.MachineGunDamage == before.Items.MachineGunDamage, "Apply does not revert any unlisted field");
        panel.Session = () => _host;
        await Frames(20);
        System.IO.File.WriteAllText(path, ConfigurationChangesExport.Format(defaults with { Vehicle = defaults.Vehicle with { Mass = 1100 } }, GameVersion.Current.ToString(), DateTimeOffset.UtcNow));
        Load(path);
        Check(panel.Apply(), "host imports through the same Apply authority");
        await Until(() => client.DeveloperConfiguration == _host.DeveloperConfiguration && lateClient.DeveloperConfiguration == _host.DeveloperConfiguration, "host import converges on clients");
        Check(_host.DeveloperConfiguration.Damage.MaxHP == 2100, "host import also preserves unlisted values");
        var stamp = DateTimeOffset.UtcNow;
        Check(ConfigurationChangesExport.Format(_host.DeveloperConfiguration, GameVersion.Current.ToString(), stamp) == ConfigurationChangesExport.Format(client.DeveloperConfiguration, GameVersion.Current.ToString(), stamp), "host/client re-exports agree after import");
        panel.Session = () => client;
        await Frames(20);
        var accepted = _host.DeveloperConfiguration;
        Check(GameplayOptions.TryApply(defaults, new Dictionary<string, double> { ["match.mode"] = 0, ["vehicle.mass"] = 1400 }, out var restricted, out _), "valid file can still contain a live-match restricted change");
        System.IO.File.WriteAllText(path, ConfigurationChangesExport.Format(restricted, GameVersion.Current.ToString(), DateTimeOffset.UtcNow));
        Load(path);
        panel.Apply();
        await Until(() => !panel.AwaitingConfirmation, "host responds to restricted imported transaction");
        Check(panel.HasUnappliedChanges && _host.DeveloperConfiguration == accepted && client.DeveloperConfiguration == accepted, "host rejects imported mode change atomically and keeps the draft visible");
        panel.Cancel();
        panel.Session = () => _host;
        await Frames(20);
        var pendingDialog = Open();
        panel.Session = () => client;
        pendingDialog.EmitSignal(FileDialog.SignalName.FileSelected, path);
        Check(HasStatus("Session changed"), "file selected for another session owner is rejected");
        await Frames(20);
    }
}
