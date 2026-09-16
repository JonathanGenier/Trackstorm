using System.Globalization;
using Godot;
using Trackstorm.Client.Bootstrap;
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
            string endpoint;
            using (var reservation = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            {
                endpoint = $"127.0.0.1:{((System.Net.IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            }

            _host.Open(true, endpoint, "Developer host");
            await Until(() => _host.Lobby?.Authority is not null, "host authority");
            Tap(Key.F1);
            await Frames(20);
            Check(_menu.CurrentPage == MenuPage.DeveloperOptions, "F1 opens existing Developer Options page");
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
                var viewport = new SubViewport { Size = new Vector2I(800, 600), OwnWorld3D = true };
                AddChild(viewport);
                _client = new DevelopmentSession { Name = "RemoteClient" };
                viewport.AddChild(_client);
                _client.Open(false, endpoint, "Joined player");
                await Until(() => _client.Lobby?.State?.Players.Count == 2, "real UDP client admission");
                _host.Lobby!.Request(LobbyCommand.Ready, true);
                _client.Lobby!.Request(LobbyCommand.Ready, true);
                await Until(() => _host.Lobby.State!.CanStart, "normal multiplayer readiness");
                Check(_host.Lobby.Request(LobbyCommand.Start), "normal Start");
                await Until(() => _client.Arena?.Driver.Match?.Phase == MatchPhase.Active, "normal Active match");
                await Frames(20);
                Check(!Descendants(_bootstrap).OfType<Button>().Any(button => button.Text == "Arena tools"), "separate Arena Tools retired");
                var simulation = Descendants(_menu.DeveloperOptions).OfType<SpinBox>().ToArray();
                double[] impairment = [30, 5, 2, 10, 25];
                for (int i = 0; i < simulation.Length; i++)
                {
                    simulation[i].Value = impairment[i];
                }

                Press("Apply local network simulation");
                Check(Descendants(_menu.DeveloperOptions).OfType<Label>().Any(label => label.Text == "Local network simulation applied."), "real GNS accepts all five UI impairment controls");
                foreach (var option in GameplayOptions.All)
                {
                    double current = option.Read(_host.DeveloperConfiguration);
                    double value = option.Boolean ? 1 - current : option.Integral ? current + 1 : current * 1.05;
                    Set(option.Key, value);
                    Press("Apply tuning");
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

                ulong revision = _host.Arena!.Driver.Configuration.Revision;
                Set("vehicle.mass", -1);
                Press("Apply tuning");
                Check(_host.Arena.Driver.Configuration.Revision == revision, "invalid UI edit cannot commit");
                Press("Reload current values");
                Check(!_client.ConfigureDeveloperOptions(new Dictionary<string, double> { ["vehicle.mass"] = 200 }, out _), "joined client cannot mutate");
                Check(!_client.GiveDeveloperItem(HeldItem.Missile) && !_client.ForceDeveloperStart(), "joined client cannot invoke actions");
                Set("match.minimum_players", 2);
                Set("vehicle.acceleration", 7);
                Set("items.wrench_heal", 17);
                Set("items.missile_speed", 75);
                Press("Apply tuning");
                await Until(() => _client.Arena!.Driver.Configuration == _host.Arena.Driver.Configuration, "final tuned boundary");
                Set("vehicle.suspension_length", 0.9);
                Press("Apply tuning");
                await Frames(4);
                var suspensionState = _host.Arena.Driver.LocalState!;
                var hostBody = _host.Arena.Bodies[suspensionState.VehicleId];
                var extended = hostBody.Observe(suspensionState).Wheels;
                Set("vehicle.suspension_length", 0.1);
                Press("Apply tuning");
                var shortened = hostBody.Observe(suspensionState).Wheels;
                Check(extended != shortened, "UI suspension length changes native wheel ray support");
                Set("vehicle.suspension_length", 0.8);
                Press("Apply tuning");
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
                var accelerationEditor = Descendants(_menu.DeveloperOptions).OfType<LineEdit>().Single(editor => editor.Name == "vehicle_acceleration");
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
                Check(_menu.CurrentPage == MenuPage.Closed, "F1 closes same page");
                Tap(Key.Escape);
                Press("Settings");
                Press("Developer Options");
                Check(_menu.CurrentPage == MenuPage.DeveloperOptions, "Settings reopens same developer panel");
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

    private void Set(string key, double value)
    {
        var control = Descendants(_menu.DeveloperOptions).OfType<Control>().Single(control => control.Name == key.Replace('.', '_'));
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

    private void Press(string text) => Descendants(_menu).OfType<Button>().Single(button => button.IsVisibleInTree() && button.Text == text).EmitSignal(BaseButton.SignalName.Pressed);

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
