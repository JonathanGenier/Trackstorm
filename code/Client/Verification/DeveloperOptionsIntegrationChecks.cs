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
            if (phase == "inspect")
            {
                GD.Print("Developer Options interactive inspection ready; isolated local host and settings.");
                return;
            }
            await CheckInitialAccordionState();
            if (phase == "read")
            {
                Check(_host.DeveloperConfiguration.Environment == EnvironmentPreset.NeonSunset, "process restart restores environment identity");
                Check(_host.DeveloperConfiguration.Vehicle.Acceleration == 7, "full process restart restores host tuning");
                Press("Force Start");
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
                _devTools.Configs.Session = () => _client;
                await Frames(2);
                Check(!Descendants(_devTools.Configs).OfType<LineEdit>().Any(editor => editor.IsVisibleInTree() && editor.Name != "ConfigSearch" && !editor.Name.ToString().StartsWith("tire_", StringComparison.Ordinal)), "joined client's Configs hides host-only settings");
                Check(Descendants(_devTools.Configs).OfType<LineEdit>().Any(editor => editor.IsVisibleInTree() && editor.Name == "tire_lifetime"), "joined client retains local tire graphics controls");
                var sharedBeforeLocalEdit = _host.DeveloperConfiguration;
                Set("tire.grass.duration", .5);
                Check(_devTools.Configs.Apply(), "Joined client can apply local surface graphics without host authority");
                await Frames(10);
                Check(_host.DeveloperConfiguration == sharedBeforeLocalEdit && _client.DeveloperConfiguration == sharedBeforeLocalEdit, "Local surface tuning leaves both synchronized gameplay configurations unchanged");
                _devTools.Configs.Session = () => _host;
                await Frames(20);
                string hostDiagnostics = DeveloperDiagnostics.Capture(_host);
                string clientDiagnostics = DeveloperDiagnostics.Capture(_client);
                Check(hostDiagnostics.Contains("Local PlayerId: 1; CurrentHostId: 1; role: HOST", StringComparison.Ordinal), "host diagnostic identity and role");
                Check(clientDiagnostics.Contains("Local PlayerId: 2; CurrentHostId: 1; role: CLIENT", StringComparison.Ordinal), "client diagnostic identity and role");
                foreach (string field in new[] { "AuthorityEpoch: 1", "match generation:", "Connection generation:", "Reconnect:", "migration:" })
                {
                    Check(hostDiagnostics.Contains(field, StringComparison.Ordinal) && clientDiagnostics.Contains(field, StringComparison.Ordinal), "both roles expose " + field);
                }

                Set("match.mode", 0);
                Press("Apply Settings");
                Check(_host.DeveloperConfiguration.Match.Mode == MatchMode.FirstToTarget, "Lobby config selects non-Circus mode");
                Set("match.mode", 1);
                Press("Apply Settings");
                Check(_host.DeveloperConfiguration.Match.Mode == MatchMode.Circus, "Lobby config selects Circus before match entry");
                _host.Lobby!.Request(LobbyCommand.Ready, true);
                _client.Lobby!.Request(LobbyCommand.Ready, true);
                await Until(() => _host.Lobby.State!.CanStart, "normal multiplayer readiness");
                Check(_host.Lobby.Request(LobbyCommand.Start), "normal Start");
                await Until(() => _client.Arena?.Driver.Match?.Phase == MatchPhase.Active, "normal Active match");
                await Frames(20);
                var expectedDefaults = GameplayConfiguration.HostedDefaults with { Spawns = GameplayConfiguration.HostedDefaults.Spawns with { Seed = _host.Arena!.Driver.Configuration.Configuration.Spawns.Seed } };
                Check(_host.Arena.Driver.Configuration.Configuration == expectedDefaults, "production arena retains hosted defaults with the fresh authoritative match seed");
                Check(!Descendants(_bootstrap).OfType<Button>().Any(button => button.Text == "Arena tools"), "separate Arena Tools retired");
                Check(!Descendants(_devTools.Configs).OfType<Button>().Any(button => button.Name == "ForceStart"), "Configs contains no duplicate Force Start action");
                Check(Descendants(_devTools).OfType<Button>().Single(button => button.Name == "ForceStart").IsVisibleInTree(), "Force Start is available from the DevTools shell");
                Check(!Descendants(_devTools.Configs).OfType<Button>().Any(button => button.Text.StartsWith("Give ", StringComparison.Ordinal)), "developer item-grant controls are absent");
                Check(!Descendants(_devTools.Configs).OfType<Button>().Any(button => button.Text is "Apply tuning" or "Reload current values" or "Save tuning / retry"), "obsolete tuning actions removed");
                var simulation = Descendants(_devTools.Configs).OfType<SpinBox>().ToArray();
                double[] impairment = [30, 5, 2, 10, 25];
                for (int i = 0; i < simulation.Length; i++)
                {
                    simulation[i].Value = impairment[i];
                }

                Press("Apply Settings");
                Check(Descendants(_devTools.Configs).OfType<Label>().Any(label => label.Text == "Host tuning saved."), "real GNS accepts all five UI impairment controls");
                foreach (var option in GameplayOptions.All)
                {
                    if (option.Key == "match.mode")
                    {
                        var previous = _host.Arena!.Driver.Configuration;
                        Set(option.Key, 0);
                        Press("Apply Settings");
                        Check(_host.Arena.Driver.Configuration == previous, "Active match rejects a scoring mode change");
                        Press("Cancel");
                        continue;
                    }
                    double current = option.Read(_host.DeveloperConfiguration);
                    double value = option.Boolean ? 1 - current : option.Integral ? current + 1 : current * 1.05;
                    // Shorten suspension and reduce the airborne fraction from its canonical maximum.
                    if (option.Key is "vehicle.suspension_length" or "items.nitro_airborne_thrust_scale" or "environment.piece_speed")
                    {
                        value = current * 0.95;
                    }

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

                Press("Apply Settings");

                var selector = Descendants(_devTools.Configs).OfType<OptionButton>().Single(button => button.Name == "environment_preset");
                Check(selector.ItemCount == 5, "five named environment presets in Configs");
                var originalMap = _host.Arena!.Map;
                foreach (var preset in Enum.GetValues<EnvironmentPreset>())
                {
                    Check(selector.GetItemText(selector.GetItemIndex((int)preset)) == Arenas.EnvironmentPresentation.DisplayName(preset), "stable readable preset name: " + preset);
                    Set("environment.preset", (int)preset);
                    Press("Apply Settings");
                    await Until(() => _client.Arena!.Driver.Configuration == _host.Arena.Driver.Configuration, "environment synchronized: " + preset);
                    await Frames(3);
                    Check(_host.Arena.GetChildren().OfType<Arenas.EnvironmentPresentation>().Single().Current == preset &&
                        _client.Arena!.GetChildren().OfType<Arenas.EnvironmentPresentation>().Single().Current == preset, "both runtimes present " + preset);
                    Check(ReferenceEquals(originalMap, _host.Arena.Map), "preset switching retains map instance");
                }
                await CheckAccordions();
                await CheckDraftActions();
                await CheckRedesign();
                var distributionSearch = Descendants(_devTools.Configs).OfType<LineEdit>().Single(editor => editor.Name == "ConfigSearch");
                distributionSearch.Text = "weight";
                distributionSearch.EmitSignal(LineEdit.SignalName.TextChanged, distributionSearch.Text);
                await Frames(3);
                await Capture("distribution-weights");
                distributionSearch.Text = "Oil";
                distributionSearch.EmitSignal(LineEdit.SignalName.TextChanged, distributionSearch.Text);
                await Frames(3);
                foreach (string key in new[] { "vehicle_oil_grip_reduction", "vehicle_oil_recovery_seconds", "items_oil_enemy_contacts", "items_oil_lifetime_seconds" })
                {
                    Check(Descendants(_devTools.Configs).OfType<LineEdit>().Any(editor => editor.Name == key && editor.IsVisibleInTree()), "Oil tuning visible in Configs: " + key);
                }
                await Capture("oil-configs");
                distributionSearch.Text = string.Empty;
                distributionSearch.EmitSignal(LineEdit.SignalName.TextChanged, distributionSearch.Text);
                ulong revision = _host.Arena!.Driver.Configuration.Revision;
                Set("vehicle.mass", -1);
                Press("Apply Settings");
                Check(_host.Arena.Driver.Configuration.Revision == revision, "invalid UI edit cannot commit");
                Press("Cancel");
                Check(!_client.ConfigureDeveloperOptions(new Dictionary<string, double> { ["vehicle.mass"] = 200 }, out _), "joined client cannot mutate");
                Check(!_client.GiveDeveloperItem(HeldItem.Missile) && !_client.ForceDeveloperStart(), "joined client cannot invoke actions");
                Set("match.minimum_players", 2);
                Set("environment.preset", (int)EnvironmentPreset.NeonSunset);
                Set("vehicle.acceleration", 7);
                Set("items.wrench_heal", 17);
                Set("items.missile_speed", 75);
                Press("Apply Settings");
                await Until(() => _client.Arena!.Driver.Configuration == _host.Arena.Driver.Configuration, "final tuned boundary");
                Set("vehicle.suspension_length", 1.8);
                Press("Apply Settings");
                await Frames(4);
                var suspensionState = _host.Arena.Driver.LocalState!;
                var hostBody = _host.Arena.Bodies[suspensionState.VehicleId];
                var extended = hostBody.Observe(suspensionState).Wheels;
                Set("vehicle.suspension_length", 0.1);
                Press("Apply Settings");
                var shortened = hostBody.Observe(suspensionState).Wheels;
                Check(extended != shortened, "UI suspension length changes native wheel ray support");
                Set("vehicle.suspension_length", GameplayConfiguration.HostedDefaults.Vehicle.SuspensionLength);
                Press("Apply Settings");
                Check(_host.GiveDeveloperItem(HeldItem.Wrench), "fixture grants wrench through existing authority");
                Check(_host.Arena.Driver.LocalItem?.Item == HeldItem.Wrench, "Give Wrench uses current host slot");
                Check(_host.GiveDeveloperItem(HeldItem.Wrench), "fixture fills the second inventory slot");
                Check(!_host.GiveDeveloperItem(HeldItem.Missile), "full inventory cannot be overwritten");
                Check(_host.Arena.Driver.RequestItemUse(), "normal Wrench use");
                await Until(() => _host.Arena.Driver.LocalItem?.Item == HeldItem.None, "normal Wrench consumption");
                Check(_host.GiveDeveloperItem(HeldItem.Missile), "fixture grants missile through existing authority");
                Check(_host.Arena.Driver.LocalItem?.Item == HeldItem.Missile, "Give Missile uses current host slot");
                Check(_host.Arena.Driver.RequestItemUse(), "normal Missile use");
                await Until(() => _host.Arena.Driver.Host!.Items.Missiles.Count > 0, "real projectile launched");
                Check(Math.Abs(_host.Arena.Driver.Host!.Items.Missiles[0].Velocity.Length() - 75) < 0.01, "UI missile speed affects actual projectile");
                Check(_host.Arena.Driver.Configuration.Configuration.Damage.MaxHP == _host.Arena.Driver.LocalState!.Damage.MaxHP, "UI max HP affects live vehicle");
                Check(_client.Arena!.Driver.LocalState!.Damage.MaxHP == _host.Arena.Driver.LocalState.Damage.MaxHP, "client max HP matches authority");
                var accelerationEditor = Descendants(_devTools.Configs).OfType<LineEdit>().Single(editor => editor.Name == "vehicle_acceleration");
                if (DisplayServer.GetName() != "headless")
                {
                    GetWindow().GrabFocus();
                    await Until(() => GetWindow().HasFocus(), "visual controller fixture owns window focus");
                }
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
        Press("Cancel");
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
        Press("Cancel");

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

    private async Task CheckRedesign()
    {
        var before = _host.Arena!.Driver.Configuration;
        var panel = _devTools.Configs;
        var mass = Descendants(panel).OfType<LineEdit>().Single(editor => editor.Name == "vehicle_mass");
        var search = Descendants(panel).OfType<LineEdit>().Single(editor => editor.Name == "ConfigSearch");
        var feedback = Descendants(panel.Footer).OfType<Label>().Single(label => label.Name == "ConfigFeedback");
        var latency = Descendants(panel).OfType<SpinBox>().First();
        latency.Value = 10;
        Check(panel.HasUnappliedChanges, "local network editor participates in pending-change protection");
        Check(feedback.IsVisibleInTree() && feedback.Text == "Unsaved changes" && feedback.GetThemeColor("font_color") == new Color("e6a23c"), "staged values show persistent amber Unsaved changes feedback");
        Press("Cancel");
        Check(latency.Value == 0 && !panel.HasUnappliedChanges, "Cancel restores effective local simulation");
        Check(feedback.IsVisibleInTree() && feedback.Text == "Changes discarded" && feedback.GetThemeColor("font_color") == new Color("ff6262"), "Cancel shows red Changes discarded feedback");
        latency.Value = 10;
        Press("Apply Settings");
        Check(!panel.HasUnappliedChanges && _host.Arena.Driver.Configuration == before, "local simulation Apply does not revise gameplay");
        Check(feedback.IsVisibleInTree() && feedback.Text == "Settings applied" && feedback.GetThemeColor("font_color") == new Color("46b85d"), "successful Apply shows green Settings applied feedback");
        Press("Reset to Defaults");
        Check(latency.Value == 0 && panel.HasUnappliedChanges, "Reset stages zero network impairment");
        Check(feedback.IsVisibleInTree() && feedback.Text == "Unsaved changes", "Reset replaces confirmation with persistent Unsaved changes feedback");
        Press("Cancel");
        Check(latency.Value == 10, "Cancel restores applied impairment after Reset");
        latency.Value = 0;
        Press("Apply Settings");
        void Search(string query)
        {
            search.Text = query;
            search.EmitSignal(LineEdit.SignalName.TextChanged, query);
        }

        Press("Reset to Defaults");
        foreach (var option in GameplayOptions.All)
        {
            var editor = Descendants(panel).OfType<Control>().Single(control => control.Name == option.Key.Replace('.', '_'));
            Check(editor.GetThemeColor("font_color") == new Color("69b7ff"), "canonical value is blue: " + option.Key);
        }

        Press("Cancel");
        Set("vehicle.mass", 1234);
        Check(panel.HasUnappliedChanges && mass.GetThemeColor("font_color") == new Color("ff7979"), "staged override is dirty and red");
        Check(feedback.IsVisibleInTree() && feedback.Text == "Unsaved changes", "gameplay draft shows Unsaved changes feedback");
        Search("vehicle mass");
        Check(mass.IsVisibleInTree(), "search matches category and label together");
        Check(Descendants(panel).OfType<LineEdit>().Count(editor => editor.IsVisibleInTree() && editor.GetParent() is GridContainer) == 2, "mass search includes mass and reference mass");
        Search("no such setting");
        Check(!mass.IsVisibleInTree() && panel.HasUnappliedChanges, "empty search preserves hidden draft");
        Check(_host.Arena.Driver.Configuration == before, "search and field changes do not mutate runtime or revision");
        Search("MiSsIlE");
        Check(Descendants(panel).OfType<LineEdit>().Any(editor => editor.IsVisibleInTree() && editor.Name == "items_missile_speed"), "search is case insensitive");
        foreach (var surface in new[] { ("Concrete", "concrete"), ("Dirt", "dirt"), ("Grass", "grass"), ("Mud", "mud"), ("Deep Mud", "deep_mud"), ("Water", "water") })
        {
            Search(surface.Item1);
            foreach (string component in new[] { "grip", "drag", "acceleration" })
            {
                Check(Descendants(panel).OfType<LineEdit>().Any(editor => editor.IsVisibleInTree() && editor.Name == $"vehicle_{surface.Item2}_{component}"), $"Searchable surface control: {surface.Item1} {component}");
            }
        }
        Search(string.Empty);
        Check(mass.Text == "1234", "clearing search retains staged value");
        foreach (var label in Descendants(panel).OfType<Label>().Where(label => label.GetParent() is GridContainer))
        {
            Check(label.GetThemeColor("font_color") == Colors.White, "setting labels remain white");
        }

        foreach (Key tab in new[] { Key.F2, Key.F3 })
        {
            Tap(tab);
            Check(!Descendants(panel.Footer).OfType<Button>().Any(button => button.IsVisibleInTree() && button.Text is "Reset to Defaults" or "Apply Settings" or "Cancel"), "configuration actions hidden on read-only tabs");
            Check(Descendants(panel.Footer).OfType<Button>().Any(button => button.IsVisibleInTree() && button.Text == "Close"), "shell Close remains visible on read-only tabs");
        }

        Press("Close");
        Check(_devTools.IsOpen && panel.HasUnappliedChanges, "closing from Logs still protects Configs edits");
        Tap(Key.F1);
        Check(Descendants(_devTools).OfType<Button>().Any(button => button.Text == "Stay" && button.IsVisibleInTree()), "shortcuts cannot bypass close decision");
        Press("Stay");
        Tap(Key.F1);
        Tap(Key.Escape);
        Press("Discard");
        Check(!_devTools.IsOpen && !panel.HasUnappliedChanges && _host.Arena.Driver.Configuration == before, "Discard closes without committing");
        Tap(Key.F1);
        Set("vehicle.mass", -1);
        Press("Close");
        Press("Apply");
        Check(_devTools.IsOpen && panel.HasUnappliedChanges && _host.Arena.Driver.Configuration == before, "invalid Apply keeps editor open without committing");
        Check(feedback.IsVisibleInTree() && feedback.Text == "Unsaved changes", "rejected Apply does not show success feedback");
        Tap(Key.Escape);
        Tap(Key.Escape);
        Check(_devTools.IsOpen && panel.HasUnappliedChanges, "Escape on decision means Stay");
        Set("vehicle.mass", 1234);
        Press("Close");
        Press("Apply");
        Check(!_devTools.IsOpen && !panel.HasUnappliedChanges, "successful Apply closes and clears pending changes");
        var applied = _host.Arena.Driver.Configuration;
        Check(applied.Configuration.Vehicle.Mass == 1234 && applied.Revision == before.Revision + 1, "close Apply commits exactly one authoritative revision");
        await Until(() => _client!.Arena!.Driver.Configuration == applied, "close Apply synchronizes client");
        Tap(Key.F1);
        Check(feedback.IsVisibleInTree() && feedback.Text == "Settings applied" && feedback.GetThemeColor("font_color") == new Color("46b85d"), "authoritative close Apply shows green success feedback when Configs reopens");
        Check(mass.GetThemeColor("font_color") == new Color("ff7979"), "accepted nondefault remains red after reopening");
        Press("Cancel");
        Check(mass.Text == "1234", "Cancel restores effective override rather than defaults");
        Check(feedback.IsVisibleInTree() && feedback.Text == "Changes discarded", "Cancel confirmation remains associated with the footer");
        Set("vehicle.mass", 1200);
        string temporary = System.IO.Path.Combine(_directory, "settings.json.developer.jsonl.tmp");
        System.IO.Directory.CreateDirectory(temporary);
        try
        {
            Press("Close");
            Press("Apply");
            Check(_devTools.IsOpen && HasStatus("saving failed"), "close Apply keeps persistence failure visible");
            Check(feedback.IsVisibleInTree() && feedback.Text == "Settings applied" && feedback.GetThemeColor("font_color") == new Color("46b85d"), "authoritative Apply shows green success feedback despite persistence failure");
        }
        finally
        {
            System.IO.Directory.Delete(temporary);
        }

        var accepted = _host.Arena.Driver.Configuration;
        Press("Apply Settings");
        Check(_host.Arena.Driver.Configuration == accepted && HasStatus("Host tuning saved"), "retry saves without another revision");
        Check(feedback.IsVisibleInTree() && feedback.Text == "Settings applied", "successful persistence retry shows Settings applied feedback");
        await Until(() => _client!.Arena!.Driver.Configuration == accepted, "save-failure retry retains synchronized values");
    }

    private GameplayConfiguration ReadEditors()
    {
        var values = new Dictionary<string, double>();
        foreach (var option in GameplayOptions.All)
        {
            var control = Descendants(_devTools.Configs).OfType<Control>().Single(control => control.Name == option.Key.Replace('.', '_'));
            values[option.Key] = control is OptionButton presets ? presets.GetSelectedId() : control is CheckButton toggle ? (toggle.ButtonPressed ? 1 : 0) : double.Parse(((LineEdit)control).Text, CultureInfo.InvariantCulture);
        }

        Check(GameplayOptions.TryApply(GameplayConfiguration.HostedDefaults, values, out var configuration, out _), "staged editor values form a valid configuration");
        return configuration;
    }

    private bool HasStatus(string text) => Descendants(_devTools.Configs).OfType<Label>().Any(label => label.Text.Contains(text, StringComparison.Ordinal));

    private void Set(string key, double value)
    {
        var control = Descendants(_devTools.Configs).OfType<Control>().Single(control => control.Name == key.Replace('.', '_'));
        if (control is OptionButton presets)
        {
            presets.Select(presets.GetItemIndex((int)value));
            presets.EmitSignal(OptionButton.SignalName.ItemSelected, presets.Selected);
        }
        else if (control is CheckButton toggle)
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

    private void Press(string text) => Descendants(text == "Reset to Defaults" ? _devTools.Configs.Footer : _bootstrap).OfType<Button>().Single(button => button.IsVisibleInTree() && button.Text == text).EmitSignal(BaseButton.SignalName.Pressed);

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
