using Godot;
using Trackstorm.Client.Bootstrap;
using Trackstorm.Client.Development;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Events;
using Trackstorm.Core.Items;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises F3 and live replicated history through the production bootstrap in Debug or exports.</summary>
public sealed partial class EventLogIntegrationChecks : Node
{
    private DevelopmentSession? _client;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta) => _client?.Advance(default);

    /// <summary>Runs the actual panel and two isolated native UDP worlds.</summary>
    public async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            var bootstrap = new SimulationBootstrap { OnlineEnabled = false, VerificationChild = true, SettingsPath = ProjectSettings.GlobalizePath("user://event-log-check-settings.json") };
            bootstrap.AddChild(new PlayerInput { Name = "PlayerInput" });
            AddChild(bootstrap);
            var host = bootstrap.GetNode<DevelopmentSession>("DevelopmentSession");
            var panel = bootstrap.GetNode<EventLogPanel>("EventLog");
            var frame = panel.GetChild<PanelContainer>(0);
            string endpoint;
            using (var reservation = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            {
                endpoint = $"127.0.0.1:{((System.Net.IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            }

            host.Open(true, endpoint, "Host tester");
            Tap();
            await Frames(3);
            Check(frame.Visible, "F3 opens in lobby");
            Check(bootstrap.GetNode<PlayerInput>("PlayerInput").Adapter.DiagnosticSuppressed, "panel suppresses local gameplay");
            Tap();
            await Frames(2);
            Check(!frame.Visible, "second F3 closes");
            var viewport = new SubViewport { Size = new Vector2I(800, 600), OwnWorld3D = true };
            AddChild(viewport);
            _client = new DevelopmentSession();
            viewport.AddChild(_client);
            _client.Open(false, endpoint, "Guest tester");
            await Until(() => _client.Lobby?.State?.Players.Count == 2);
            host.Lobby!.Request(LobbyCommand.Ready, true);
            _client.Lobby!.Request(LobbyCommand.Ready, true);
            await Until(() => host.Lobby.State!.CanStart);
            host.Lobby.Request(LobbyCommand.Start);
            await Until(() => _client.Arena is not null && host.Arena is not null);
            var authority = host.Arena!.Driver.Host!;
            Check(authority.GiveItem(0, HeldItem.Wrench), "normal item grant");
            var slot = authority.Items.Slots.Single(value => value.Vehicle == 1);
            Check(authority.UseItem(0, authority.SessionId, slot.Life, slot.Token), "normal item use");
            await Until(() => _client.Events.Entries.Any(entry => entry.Kind == "Used"));
            var use = host.Events.Entries.Single(entry => entry.Kind == "Used");
            Check(_client.Events.Entries.Single(entry => entry.Kind == "Used") == use, "same structured authoritative outcome on both peers");
            Tap();
            await Frames(3);
            var text = Descendants(panel).OfType<RichTextLabel>().Single();
            Check(text.Text.Contains("Used", StringComparison.Ordinal) && text.Text.Contains("[Item]", StringComparison.Ordinal), "live timestamped history rendered");
            ulong tick = authority.World.State.Tick;
            await Frames(12);
            Check(authority.World.State.Tick > tick, "simulation continues while F3 open");
            var follow = Descendants(panel).OfType<CheckButton>().Single();
            follow.ButtonPressed = false;
            await Frames(2);
            string frozen = text.Text;
            authority.GiveItem(0, HeldItem.Missile);
            await Frames(3);
            Check(text.Text == frozen, "freeze preserves inspected history while collection continues");
            follow.ButtonPressed = true;
            await Frames(3);
            Check(text.Text.Contains("Missile", StringComparison.Ordinal), "unfreeze displays collected outcomes");
            var filter = Descendants(panel).OfType<OptionButton>().Single();
            filter.Select((int)EventCategory.Item + 1);
            filter.EmitSignal(OptionButton.SignalName.ItemSelected, filter.Selected);
            await Frames(3);
            Check(!text.Text.Contains("[Session]", StringComparison.Ordinal) && text.Text.Contains("[Item]", StringComparison.Ordinal), "category filtering");
            if (DisplayServer.GetName() != "headless")
            {
                string? sizeArgument = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--event-log-size=", StringComparison.Ordinal));
                if (sizeArgument is not null)
                {
                    string[] dimensions = sizeArgument[17..].Split('x');
                    var size = new Vector2I(int.Parse(dimensions[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(dimensions[1], System.Globalization.CultureInfo.InvariantCulture));
                    DisplayServer.WindowSetSize(size);
                    await Frames(12);
                    Check(GetViewport().GetVisibleRect().Size == (Vector2)size, "requested visual verification size");
                    Check(frame.GetGlobalRect().End.X <= size.X && frame.GetGlobalRect().End.Y <= size.Y, "panel stays within viewport");
                }

                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng(ProjectSettings.GlobalizePath("user://event-log-check.png")) == Error.Ok, "screenshot saved");
            }

            Descendants(panel).OfType<Button>().Single(button => button.Text == "Close").EmitSignal(BaseButton.SignalName.Pressed);
            Check(!frame.Visible, "Close is navigation only");
            GD.Print("Event Log integration passed: F3, live replication, bounded journal presentation, freeze, filtering and input isolation.");
            _client.Leave();
            host.Leave();
            await Frames(90);
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
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
    }

    private static void Tap()
    {
        foreach (bool pressed in new[] { true, false })
        {
            using var input = new InputEventKey { Keycode = Key.F3, PhysicalKeycode = Key.F3, Pressed = pressed };
            Godot.Input.ParseInputEvent(input);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private async Task Until(Func<bool> predicate)
    {
        for (int i = 0; i < 900 && !predicate(); i++)
        {
            await Frames(1);
        }

        Check(predicate(), "Timed out waiting for production session transition.");
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
