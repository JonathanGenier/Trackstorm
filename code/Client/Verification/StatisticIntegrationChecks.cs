using Godot;
using Trackstorm.Client.Bootstrap;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Settings;
using Trackstorm.Client.Statistics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises the real F2 surface and owners using two local UDP peers in Debug or an exported Release.</summary>
public sealed partial class StatisticIntegrationChecks : Node
{
    private SimulationBootstrap _bootstrap = null!;
    private DevelopmentSession _host = null!;
    private DevelopmentSession? _remote;
    private StatisticPanel _panel = null!;
    private Vehicles.VehicleArena? _practice;
    private int _assertions;
    private string _output = string.Empty;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        _remote?.Advance(default);
        if (_practice is not null)
        {
            _practice.Advance(new Core.Input.InputFrame(_practice.Simulation.State.Tick + 1, 0, 0, 0, 0, 0, 0));
        }
    }

    /// <summary>Runs against production composition and exits nonzero on failure.</summary>
    public async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            _output = OS.GetCmdlineUserArgs().Single(value => value.StartsWith("--statistics-output=", StringComparison.Ordinal))[20..];
            _bootstrap = new SimulationBootstrap { OnlineEnabled = false, SettingsPath = System.IO.Path.Combine(_output, "settings.json") };
            _bootstrap.AddChild(new PlayerInput { Name = "PlayerInput" });
            AddChild(_bootstrap);
            _panel = _bootstrap.GetNode<StatisticPanel>("StatisticPanel");
            var input = _bootstrap.GetNode<PlayerInput>("PlayerInput");
            _host = _bootstrap.GetNode<DevelopmentSession>("DevelopmentSession");
            var menu = _bootstrap.GetNode<SettingsPanel>("PlayerSettings/SettingsPanel");
            Tap(Key.F2);
            await Frames(3);
            Check(_panel.IsOpen && input.Adapter.GameplaySuppressed, "F2 opens and suppresses local gameplay");
            Tap(Key.F1);
            Tap(Key.P);
            Check(menu.CurrentPage == MenuPage.Closed, "underlying menu cannot receive diagnostics navigation");
            Check(Text().Contains("NO SESSION", StringComparison.Ordinal), "missing session is explicit");
            Check(Descendants(_panel).OfType<BaseButton>().All(button => button is OptionButton or Button { Text: "Close (F2)" }), "only selection and Close controls exist");
            Tap(Key.F2);
            Check(!_panel.IsOpen && !input.Adapter.GameplaySuppressed, "F2 closes and releases input");
            string endpoint;
            using (var socket = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            {
                endpoint = $"127.0.0.1:{((System.Net.IPEndPoint)socket.Client.LocalEndPoint!).Port}";
            }

            _host.Open(true, endpoint, "Host");
            _remote = new DevelopmentSession { Name = "StatisticRemote" };
            var remoteViewport = new SubViewport { OwnWorld3D = true, Size = new Vector2I(640, 360), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
            AddChild(remoteViewport);
            remoteViewport.AddChild(_remote);
            _remote.Open(false, endpoint, "Remote");
            await Until(() => _remote.Lobby?.State?.Players.Count == 2, "two admitted peers");
            _host.Lobby!.Request(LobbyCommand.Ready, true);
            _remote.Lobby!.Request(LobbyCommand.Ready, true);
            await Until(() => _host.Lobby.State?.CanStart == true, "both ready");
            Check(_host.Lobby.Request(LobbyCommand.Start), "normal arena entry");
            await Until(() => _host.Arena?.Driver.Match?.Phase == MatchPhase.Active && _remote.Arena?.Driver.Latest?.Vehicles.Count == 2, "active replicated arena");
            Tap(Key.F2);
            await Frames(18);
            Check(_panel.IsOpen && Text().Contains("Role: HOST", StringComparison.Ordinal), "host diagnostics");
            Check(Text().Contains("HP 1000/1000", StringComparison.Ordinal) && Text().Contains("Concrete", StringComparison.Ordinal), "real HP and surface");
            Check(_panel.View!.Players.Count == 2, "multiple entities available");
            Check(Text().Contains("Available spawns:", StringComparison.Ordinal), "spawn owner observed");
            ulong tick = _host.Arena!.Driver.Latest!.Tick;
            string before = Text();
            var configuration = _host.Arena.Driver.Configuration;
            await Frames(20);
            Check(_host.Arena.Driver.Latest.Tick > tick && Text() != before, "live values update while simulation continues");
            Check(_host.Arena.Driver.Configuration == configuration, "observation does not mutate configuration");
            Check(_host.Arena.Driver.Host!.GiveItem(0, HeldItem.Missile), "fixture grants through existing authority");
            await Frames(20);
            Check(Text().Contains("Held item: Missile", StringComparison.Ordinal), "live inventory update without reopening");
            var selector = Descendants(_panel).OfType<OptionButton>().Single();
            selector.Select(1);
            selector.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Check(_panel.View!.Selected == 2 && Text().Contains("Vehicle 2", StringComparison.Ordinal), "remote vehicle selection");
            var client = RuntimeStatistics.Capture(_remote, null, _remote.Lobby!.LocalPlayerId);
            Check(client.Global.Any(section => section.Text.Contains("Role: CLIENT", StringComparison.Ordinal)), "client role from owning session");
            Check(client.Player.Any(section => section.Text.Contains("m (local client)", StringComparison.Ordinal)), "actual client prediction metric");
            Check(!Descendants(_bootstrap).OfType<Button>().Any(button => button.Text.Contains("Arena tools", StringComparison.OrdinalIgnoreCase)), "Arena Tools remains retired");
            foreach (var size in new[] { new Vector2I(640, 360), new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                GetWindow().Size = size;
                await Frames(4);
                var viewport = GetViewport().GetVisibleRect();
                Check(_panel.Bounds.Position.DistanceTo(viewport.Position) < 1 && _panel.Bounds.Size.DistanceTo(viewport.Size) < 1, "full viewport at " + size);
                var close = Descendants(_panel).OfType<Button>().Single(button => button.Text == "Close (F2)");
                Check(viewport.Encloses(close.GetGlobalRect()), "Close remains in viewport");
                await Capture(size.X + "x" + size.Y);
                var tabs = Descendants(_panel).OfType<TabContainer>().Single();
                tabs.CurrentTab = 1;
                await Frames(3);
                Check(viewport.Encloses(selector.GetGlobalRect()), "player selector remains in viewport");
                await Capture(size.X + "x" + size.Y + "-player");
                tabs.CurrentTab = 0;
            }

            Descendants(_panel).OfType<Button>().Single(button => button.Text == "Close (F2)").EmitSignal(BaseButton.SignalName.Pressed);
            Check(!_panel.IsOpen && !input.Adapter.GameplaySuppressed, "Close button restores gameplay");
            Tap(Key.Escape);
            Check(menu.CurrentPage == MenuPage.Game, "normal menu still opens");
            Tap(Key.F2);
            await Frames(3);
            Check(_panel.IsOpen, "statistics can cover menu");
            Tap(Key.Escape);
            Check(!_panel.IsOpen && menu.CurrentPage == MenuPage.Game && input.Adapter.GameplaySuppressed, "closing restores existing menu suppression");
            menu.Close();
            Tap(Key.F2);
            _remote.Leave();
            await Until(() => _host.Lobby!.State!.Players.Count == 1, "remote departure");
            await Frames(20);
            Check(_panel.View!.Players.Count == 1 && _panel.View.Selected == 1, "departure safely selects available player");
            _host.Leave();
            await Frames(30);
            Check(Text().Contains("NO SESSION", StringComparison.Ordinal) && !Text().Contains("HP 1000", StringComparison.Ordinal), "teardown clears stale telemetry");
            _practice = new Vehicles.VehicleArena();
            AddChild(_practice);
            _panel.Capture = selected => RuntimeStatistics.Capture(null, _practice, selected);
            await Frames(30);
            Check(_panel.View!.Players.Count == 8 && Text().Contains("LOCAL PRACTICE", StringComparison.Ordinal), "all eight practice vehicles available");
            selector.Select(7);
            selector.EmitSignal(OptionButton.SignalName.ItemSelected, 7L);
            Check(_panel.View!.Selected == 8 && Text().Contains("Vehicle 8", StringComparison.Ordinal), "practice target selection");
            _practice.QueueFree();
            _practice = null;
            _panel.Close();
            _bootstrap.QueueFree();
            remoteViewport.QueueFree();
            _remote = null;
            await Frames(3);
            GD.Print($"Statistic integration passed: {_assertions} assertions.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
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

    private static void Tap(Key key)
    {
        foreach (bool pressed in new[] { true, false })
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Godot.Input.ParseInputEvent(input);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private string Text() => string.Join("\n", _panel.View!.Global.Concat(_panel.View.Player).Select(section => section.Text));

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task Until(Func<bool> predicate, string description)
    {
        for (int i = 0; i < 900 && !predicate(); i++)
        {
            await Frames(1);
        }

        Check(predicate(), description);
    }

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng(System.IO.Path.Combine(_output, name + ".png")) == Error.Ok, "screenshot saved");
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
