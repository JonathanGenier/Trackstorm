using System.Globalization;
using Godot;
using Trackstorm.Client.Bootstrap;
using Trackstorm.Client.Development;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Production Settings/F1 controls, real UDP replication and separate-process persistence verification.</summary>
public sealed partial class DeveloperOptionsIntegrationChecks : Node
{
    private SimulationBootstrap _bootstrap = null!;
    private DevelopmentSession _host = null!;
    private DevelopmentSession? _client;
    private SettingsPanel _menu = null!;
    private DevToolsShell _devTools = null!;
    private int _assertions;
    private string _directory = string.Empty;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta) => _client?.Advance(default);

    /// <summary>Exercises actual UI commands and exits nonzero on any missing gameplay or persistence boundary.</summary>
    public async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            string[] args = OS.GetCmdlineUserArgs();
            _directory = args.Single(argument => argument.StartsWith("--dev-output=", StringComparison.Ordinal))[13..];
            string phase = args.Single(argument => argument.StartsWith("--phase=", StringComparison.Ordinal))[8..];
            _bootstrap = new SimulationBootstrap { OnlineEnabled = false, SettingsPath = System.IO.Path.Combine(_directory, "settings.json") };
            _bootstrap.AddChild(new PlayerInput { Name = "PlayerInput" });
            AddChild(_bootstrap);
            _host = _bootstrap.GetNode<DevelopmentSession>("DevelopmentSession");
            _menu = _bootstrap.GetNode<SettingsPanel>("PlayerSettings/SettingsPanel");
            _devTools = _bootstrap.GetNode<DevToolsShell>("DevTools");
            string endpoint;
            using (var reservation = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            {
                endpoint = $"127.0.0.1:{((System.Net.IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            }

            _host.Open(true, endpoint, "Developer host");
            await Until(() => _host.Lobby?.Authority is not null, "host authority");
            Tap(Key.F1);
            await Frames(20);
            Check(_devTools.IsOpen && _devTools.SelectedTab == DevToolsTab.Configs, "F1 opens unified DevTools on Configs");
            Check(!Descendants(_devTools.Configs).OfType<Label>().Any(label => label.Text.Contains("AuthorityEpoch:", StringComparison.Ordinal) || label.Text.Contains("Transport:", StringComparison.Ordinal)), "Configs does not duplicate read-only network diagnostics");
            if (phase == "read")
            {
                Check(_host.DeveloperConfiguration.Vehicle.Acceleration == 7, "full process restart restores host tuning");
                Press("FORCE START MATCH");
                await Until(() => _host.Arena?.Driver.Match?.Phase == MatchPhase.Active, "solo Force Start uses normal countdown and Active phase");
                Check(_host.Arena!.Driver.Configuration.Configuration.Vehicle.Acceleration == 7, "persisted values become authoritative arena configuration");
                Check(_host.Arena.Driver.Configuration.Configuration.Match.MinimumPlayers == 2, "Force Start does not change normal player rules");
            }
            else
            {
                Check(_host.DeveloperConfiguration == GameplayConfiguration.HostedDefaults, "host startup uses canonical production defaults");
                Check(_host.DeveloperConfiguration.Damage.MaxHP == 1000, "production multiplayer starts with 1000 HP");
                var viewport = new SubViewport { Size = new Vector2I(800, 600), OwnWorld3D = true };
                AddChild(viewport);
                _client = new DevelopmentSession { Name = "RemoteClient" };
                viewport.AddChild(_client);
                _client.Open(false, endpoint, "Joined player");
                await Until(() => _client.Lobby?.State?.Players.Count == 2, "real UDP client admission");
                string hostDiagnostics = DeveloperDiagnostics.Capture(_host);
                string clientDiagnostics = DeveloperDiagnostics.Capture(_client);
                Check(hostDiagnostics.Contains("Local PlayerId: 1; CurrentHostId: 1; role: HOST", StringComparison.Ordinal), "host diagnostic identity and role");
                Check(clientDiagnostics.Contains("Local PlayerId: 2; CurrentHostId: 1; role: CLIENT", StringComparison.Ordinal), "client diagnostic identity and role");
                foreach (string field in new[] { "AuthorityEpoch: 1", "match generation:", "Connection generation:", "Reconnect:", "migration:" })
                {
                    Check(hostDiagnostics.Contains(field, StringComparison.Ordinal) && clientDiagnostics.Contains(field, StringComparison.Ordinal), "both roles expose " + field);
                }

                _host.Lobby!.Request(LobbyCommand.Ready, true);
                _client.Lobby!.Request(LobbyCommand.Ready, true);
                await Until(() => _host.Lobby.State!.CanStart, "normal multiplayer readiness");
                Check(_host.Lobby.Request(LobbyCommand.Start), "normal Start");
                await Until(() => _client.Arena?.Driver.Match?.Phase == MatchPhase.Active, "normal Active match");
                await Frames(20);
                Check(_host.Arena!.Driver.Configuration.Configuration == GameplayConfiguration.HostedDefaults, "production arena uses the same hosted defaults");
                Check(!Descendants(_bootstrap).OfType<Button>().Any(button => button.Text == "Arena tools"), "separate Arena Tools retired");
                Check(Descendants(_devTools.Configs).OfType<Button>().Any(button => button.Text is "FORCE START MATCH" or "Give Wrench" or "Give Missile"), "host-authoritative developer actions remain in Configs");
                Check(!Descendants(_devTools.Configs).OfType<Button>().Any(button => button.Text is "Apply tuning" or "Reload current values" or "Save tuning / retry"), "obsolete tuning actions removed");
                var simulation = Descendants(_devTools.Configs).OfType<SpinBox>().ToArray();
                double[] impairment = [30, 5, 2, 10, 25];
                for (int i = 0; i < simulation.Length; i++)
                {
                    simulation[i].Value = impairment[i];
                }

                Press("Apply local network simulation");
                Check(Descendants(_devTools.Configs).OfType<Label>().Any(label => label.Text == "Local network simulation applied."), "real GNS accepts all five UI impairment controls");
                foreach (var option in GameplayOptions.All)
                {
                    double current = option.Read(_host.DeveloperConfiguration);
                    double value = option.Boolean ? 1 - current : option.Integral ? current + 1 : current * 1.05;
                    Set(option.Key, value);
                    Press("Apply Settings");
                    var accepted = _host.Arena!.Driver.Configuration;
                    Check(option.Read(accepted.Configuration) != current, "UI commits owning value: " + option.Key);
                    await Until(() => _client.Arena!.Driver.Configuration == accepted, "client effective value: " + option.Key);
                    Check(_client.Arena!.Driver.Configuration.Revision == accepted.Revision, "client revision: " + option.Key);
                }

                foreach (var control in simulation)
                {
                    control.Value = 0;
                }

                Press("Apply local network simulation");

                await CheckDraftActions();
                ulong revision = _host.Arena!.Driver.Configuration.Revision;
                Set("vehicle.mass", -1);
                Press("Apply Settings");
                Check(_host.Arena.Driver.Configuration.Revision == revision, "invalid UI edit cannot commit");
                Press("Discard Changes");
                Check(!_client.ConfigureDeveloperOptions(new Dictionary<string, double> { ["vehicle.mass"] = 200 }, out _), "joined client cannot mutate");
                Check(!_client.GiveDeveloperItem(HeldItem.Missile) && !_client.ForceDeveloperStart(), "joined client cannot invoke actions");
                Set("match.minimum_players", 2);
                Set("vehicle.acceleration", 7);
                Set("items.wrench_heal", 17);
                Set("items.missile_speed", 75);
                Press("Apply Settings");
                await Until(() => _client.Arena!.Driver.Configuration == _host.Arena.Driver.Configuration, "final tuned boundary");
                Set("vehicle.suspension_length", 0.9);
                Press("Apply Settings");
                await Frames(4);
                var suspensionState = _host.Arena.Driver.LocalState!;
                var hostBody = _host.Arena.Bodies[suspensionState.VehicleId];
                var extended = hostBody.Observe(suspensionState).Wheels;
                Set("vehicle.suspension_length", 0.1);
                Press("Apply Settings");
                var shortened = hostBody.Observe(suspensionState).Wheels;
                Check(extended != shortened, "UI suspension length changes native wheel ray support");
                Set("vehicle.suspension_length", 0.8);
                Press("Apply Settings");
                Press("Give Wrench");
                Check(_host.Arena.Driver.LocalItem?.Item == HeldItem.Wrench, "Give Wrench uses current host slot");
                Check(!_host.GiveDeveloperItem(HeldItem.Missile), "occupied slot cannot be overwritten");
                Check(_host.Arena.Driver.RequestItemUse(), "normal Wrench use");
                await Until(() => _host.Arena.Driver.LocalItem?.Item == HeldItem.None, "normal Wrench consumption");
                Press("Give Missile");
                Check(_host.Arena.Driver.LocalItem?.Item == HeldItem.Missile, "Give Missile uses current host slot");
                Check(_host.Arena.Driver.RequestItemUse(), "normal Missile use");
                await Until(() => _host.Arena.Driver.Host!.Items.Missiles.Count > 0, "real projectile launched");
                Check(Math.Abs(_host.Arena.Driver.Host!.Items.Missiles[0].Velocity.Length() - 75) < 0.01, "UI missile speed affects actual projectile");
                Check(_host.Arena.Driver.Configuration.Configuration.Damage.MaxHP == _host.Arena.Driver.LocalState!.Damage.MaxHP, "UI max HP affects live vehicle");
                Check(_client.Arena!.Driver.LocalState!.Damage.MaxHP == _host.Arena.Driver.LocalState.Damage.MaxHP, "client max HP matches authority");
                var accelerationEditor = Descendants(_devTools.Configs).OfType<LineEdit>().Single(editor => editor.Name == "vehicle_acceleration");
                accelerationEditor.GrabFocus();
                await Frames(3);
                await Capture("developer-options-values");
                foreach (bool pressed in new[] { true, false })
                {
                    using var button = new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown, Pressed = pressed };
                    Godot.Input.ParseInputEvent(button);
                    Godot.Input.FlushBufferedEvents();
                }

                Check(GetViewport().GuiGetFocusOwner() != accelerationEditor, "controller navigation exits numeric editor");
                Tap(Key.F1);
                Check(_devTools.IsOpen && _devTools.SelectedTab == DevToolsTab.Configs, "F1 keeps the shared shell open on Configs");
                Tap(Key.Escape);
                Check(!_devTools.IsOpen, "ESC closes DevTools");
                Tap(Key.Escape);
                Press("Settings");
                Press("Developer Options");
                Check(_menu.CurrentPage == MenuPage.Settings && _devTools.IsOpen && _devTools.SelectedTab == DevToolsTab.Configs, "Settings opens the same DevTools Configs surface");
                await Frames(3);
                await Capture("developer-options");
                _client.Leave();
                await Until(() => _client.LeaveComplete, "client cleanup");
                _client = null;
                viewport.QueueFree();
            }

            _host.Leave();
            await Until(() => _host.LeaveComplete, "host cleanup");
            _bootstrap.QueueFree();
            await Frames(4);
            GD.Print($"Developer Options integration passed: {phase}; {_assertions} assertions.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            _client?.Leave();
            _host?.Leave();
            GetTree().Quit(1);
        }
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private async Task CheckDraftActions()
    {
        string path = System.IO.Path.Combine(_directory, "settings.json.developer.jsonl");
        var before = _host.Arena!.Driver.Configuration;
        string persisted = System.IO.File.ReadAllText(path);
        Set("damage.max_hp", 2500);
        Set("vehicle.acceleration", 99);
        Press("Discard Changes");
        Check(_host.Arena.Driver.Configuration == before, "Discard does not mutate live configuration or revision");
        Check(ReadEditors() == before.Configuration, "Discard restores all current authoritative values");
        Check(System.IO.File.ReadAllText(path) == persisted, "Discard does not write persistence");

        Set("vehicle.mass", -1);
        Press("Reset to Defaults");
        await Frames(20);
        Check(ReadEditors() == GameplayConfiguration.HostedDefaults, "Reset stages the complete production defaults including 1000 HP");
        Check(_host.Arena.Driver.Configuration == before, "Reset does not mutate live configuration or revision");
        Check(_host.DeveloperSettings!.Current == before.Configuration, "Reset does not mutate the host persistence owner");
        Check(System.IO.File.ReadAllText(path) == persisted, "Reset does not write persistence");
        var hp = Descendants(_devTools.Configs).OfType<LineEdit>().Single(editor => editor.Name == "damage_max_hp");
        hp.EmitSignal(LineEdit.SignalName.TextSubmitted, hp.Text);
        Check(_host.Arena.Driver.Configuration == before, "submitting numeric text does not bypass Apply Settings");

        Press("Apply Settings");
        var reset = _host.Arena.Driver.Configuration;
        Check(reset.Configuration == GameplayConfiguration.HostedDefaults && reset.Revision == before.Revision + 1, "Reset plus Apply commits the default configuration once");
        await Until(() => _client!.Arena!.Driver.Configuration == reset, "Reset plus Apply synchronizes normally");
        Check(new DeveloperSettingsStore(path).LoadForHost() == GameplayConfiguration.HostedDefaults, "Reset plus Apply replaces persisted host tuning");

        persisted = System.IO.File.ReadAllText(path);
        var mass = Descendants(_devTools.Configs).OfType<LineEdit>().Single(editor => editor.Name == "vehicle_mass");
        mass.Text = "invalid";
        mass.EmitSignal(LineEdit.SignalName.TextChanged, mass.Text);
        Press("Apply Settings");
        Check(_host.Arena.Driver.Configuration == reset && HasStatus("Invalid value:"), "Apply reports malformed text without committing");
        Set("vehicle.mass", -1);
        Press("Apply Settings");
        Check(_host.Arena.Driver.Configuration == reset, "Apply still enforces authoritative configuration validation");
        Check(System.IO.File.ReadAllText(path) == persisted, "rejected Apply does not write persistence");
        Press("Discard Changes");

        // A directory at the temporary file path deterministically fails saving without changing permissions.
        System.IO.Directory.CreateDirectory(path + ".tmp");
        Set("vehicle.acceleration", 8);
        Press("Apply Settings");
        var accepted = _host.Arena.Driver.Configuration;
        Check(accepted.Configuration.Vehicle.Acceleration == 8 && accepted.Revision == reset.Revision + 1, "failed persistence retains accepted live tuning");
        Check(_host.DeveloperSettings.Current == accepted.Configuration && HasStatus("saving failed. Press Apply Settings to retry."), "save failure and Apply retry are clearly surfaced");
        Check(System.IO.File.ReadAllText(path) == persisted, "failed persistence preserves the prior file");
        System.IO.Directory.Delete(path + ".tmp");
        Press("Apply Settings");
        Check(_host.Arena.Driver.Configuration == accepted, "unchanged Apply retry does not advance revision");
        Check(new DeveloperSettingsStore(path).LoadForHost() == accepted.Configuration && HasStatus("Host tuning saved."), "unchanged Apply retries and persists successfully");
        await Until(() => _client!.Arena!.Driver.Configuration == accepted, "client retains the accepted retry boundary");
    }

    private GameplayConfiguration ReadEditors()
    {
        var values = new Dictionary<string, double>();
        foreach (var option in GameplayOptions.All)
        {
            var control = Descendants(_devTools.Configs).OfType<Control>().Single(control => control.Name == option.Key.Replace('.', '_'));
            values[option.Key] = control is CheckButton toggle ? (toggle.ButtonPressed ? 1 : 0) : double.Parse(((LineEdit)control).Text, CultureInfo.InvariantCulture);
        }

        Check(GameplayOptions.TryApply(GameplayConfiguration.HostedDefaults, values, out var configuration, out _), "staged editor values form a valid configuration");
        return configuration;
    }

    private bool HasStatus(string text) => Descendants(_devTools.Configs).OfType<Label>().Any(label => label.Text.Contains(text, StringComparison.Ordinal));

    private void Set(string key, double value)
    {
        var control = Descendants(_devTools.Configs).OfType<Control>().Single(control => control.Name == key.Replace('.', '_'));
        if (control is CheckButton toggle)
        {
            toggle.ButtonPressed = value == 1;
        }
        else
        {
            var editor = (LineEdit)control;
            editor.Text = value.ToString("G17", CultureInfo.InvariantCulture);
            editor.EmitSignal(LineEdit.SignalName.TextChanged, editor.Text);
        }
    }

    private void Press(string text) => Descendants(_bootstrap).OfType<Button>().Single(button => button.IsVisibleInTree() && button.Text == text).EmitSignal(BaseButton.SignalName.Pressed);

    private void Tap(Key key)
    {
        foreach (bool pressed in new[] { true, false })
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Godot.Input.ParseInputEvent(input);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private async Task Until(Func<bool> predicate, string message)
    {
        for (int i = 0; i < 900 && !predicate(); i++)
        {
            await Frames(1);
        }

        Check(predicate(), message);
    }

    private async Task Frames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng(System.IO.Path.Combine(_directory, name + ".png")) == Error.Ok, "rendered developer page");
        }
    }

    private void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }

        _assertions++;
    }
}
