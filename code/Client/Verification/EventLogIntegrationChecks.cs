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
            GetTree().Root.Size = new Vector2I(1280, 720);
            var bootstrap = new SimulationBootstrap { OnlineEnabled = false, VerificationChild = true, SettingsPath = ProjectSettings.GlobalizePath("user://event-log-check-settings.json") };
            bootstrap.AddChild(new PlayerInput { Name = "PlayerInput" });
            AddChild(bootstrap);
            var host = bootstrap.GetNode<DevelopmentSession>("DevelopmentSession");
            var devTools = bootstrap.GetNode<DevToolsShell>("DevTools");
            var panel = devTools.Logs;
            string endpoint;
            using (var reservation = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            {
                endpoint = $"127.0.0.1:{((System.Net.IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            }

            host.Open(true, endpoint, "Host tester");
            Tap(Key.F3);
            await Frames(3);
            Check(devTools.IsOpen && devTools.SelectedTab == DevToolsTab.Logs, "F3 opens Logs in lobby");
            Check(Descendants(bootstrap).OfType<DevToolsShell>().Count() == 1, "only one DevTools shell exists");
            Check(Descendants(panel).OfType<BaseButton>().All(button => button is OptionButton or CheckButton), "Logs content remains read only");
            Check(bootstrap.GetNode<PlayerInput>("PlayerInput").Adapter.DiagnosticSuppressed, "panel suppresses local gameplay");
            Tap(Key.F3);
            await Frames(2);
            Check(devTools.IsOpen && devTools.SelectedTab == DevToolsTab.Logs, "repeated F3 keeps the shared shell open on Logs");
            Tap(Key.Escape);
            Check(!devTools.IsOpen, "ESC closes DevTools");
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
            await Until(() => host.Arena.Driver.Match?.Phase == Trackstorm.Core.Matches.MatchPhase.Active);
            Check(authority.GiveItem(0, HeldItem.Wrench), "normal item grant");
            var slot = authority.Items.Slots.Single(value => value.Vehicle == 1);
            Check(authority.UseItem(0, authority.SessionId, slot.Life, slot.Token), "normal item use");
            await Until(() => _client.Events.Entries.Any(entry => entry.Kind == "Used"));
            var use = host.Events.Entries.Single(entry => entry.Kind == "Used");
            Check(_client.Events.Entries.Single(entry => entry.Kind == "Used") == use, "same structured authoritative outcome on both peers");
            Tap(Key.F3);
            await Frames(3);
            var text = Descendants(panel).OfType<RichTextLabel>().Single();
            Check(text.GetParsedText().Contains("Used", StringComparison.Ordinal) && text.GetParsedText().Contains("[Item]", StringComparison.Ordinal), "live timestamped history rendered");
            ulong tick = authority.World.State.Tick;
            await Frames(12);
            Check(authority.World.State.Tick > tick, "simulation continues while F3 open");
            var follow = Descendants(panel).OfType<CheckButton>().Single();
            follow.ButtonPressed = false;
            await Frames(2);
            string frozen = text.GetParsedText();
            authority.GiveItem(0, HeldItem.Missile);
            await Frames(3);
            Check(text.GetParsedText() == frozen, "freeze preserves inspected history while collection continues");
            follow.ButtonPressed = true;
            await Frames(3);
            Check(text.GetParsedText().Contains("Missile", StringComparison.Ordinal), "unfreeze displays collected outcomes");
            var filter = Descendants(panel).OfType<OptionButton>().Single();
            filter.Select((int)EventCategory.Item + 1);
            filter.EmitSignal(OptionButton.SignalName.ItemSelected, filter.Selected);
            await Frames(3);
            Check(!text.GetParsedText().Contains("[Session]", StringComparison.Ordinal) && text.GetParsedText().Contains("[Item]", StringComparison.Ordinal), "category filtering");
            await CheckPresentation(panel, devTools, text, filter, follow);
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
                    Check(devTools.Bounds.End.X <= size.X && devTools.Bounds.End.Y <= size.Y, "shell stays within viewport");
                }

                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng(ProjectSettings.GlobalizePath("user://event-log-check.png")) == Error.Ok, "screenshot saved");
            }

            Descendants(devTools).OfType<Button>().Single(button => button.Text == "Close").EmitSignal(BaseButton.SignalName.Pressed);
            Check(!devTools.IsOpen, "Close is navigation only");
            await CheckActivityFeed(bootstrap, host);
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

    private static void Tap(Key key)
    {
        foreach (bool pressed in new[] { true, false })
        {
            using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
            Godot.Input.ParseInputEvent(input);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private async Task CheckPresentation(EventLogPanel panel, DevToolsShell shell, RichTextLabel text, OptionButton filter, CheckButton follow)
    {
        var originalSource = panel.Source;
        var stream = new EventStream { PlayerName = _ => "Missile" };
        stream.AdvanceTime(125104);
        stream.Record(EventCategory.Damage, "Applied", actor: 1, target: 2, cause: "Missile", amount: 27, hp: 73, maxHP: 100);
        stream.Record(EventCategory.Network, "[b]literal[/b]", local: true);
        for (int i = 0; i < 80; i++)
        {
            stream.Record(EventCategory.Item, "Used", actor: 1, cause: "Missile");
        }

        panel.Source = () => stream;
        filter.Select(0);
        filter.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
        await Frames(3);
        Check(text.GetParsedText() == string.Join('\n', stream.Entries.Select(EventLogFormatter.Format)), "styled rendering preserves exact timestamp, origin, sequence, names and literal markup");
        Check(!text.BbcodeEnabled && text.GetThemeColor("default_color") == new Color("eeeeee"), "literal readable message rendering");
        var first = stream.Entries[0];
        follow.ButtonPressed = false;
        text.GetVScrollBar().Value = 30;
        await Frames(2);
        string frozen = text.GetParsedText();
        double scroll = text.GetVScrollBar().Value;
        Check(scroll > 0, "frozen inspection scrolls away from the latest event");
        for (int i = 0; i < 1030; i++)
        {
            stream.Record(EventCategory.Item, "Used", actor: 1, cause: "Missile");
        }

        await Frames(3);
        Check(stream.Entries.Count == 1024 && text.GetParsedText() == frozen && text.GetVScrollBar().Value == scroll, "freeze retains presentation and scroll while bounded collection evicts oldest events");
        filter.Select((int)EventCategory.Damage + 1);
        filter.EmitSignal(OptionButton.SignalName.ItemSelected, filter.Selected);
        await Frames(2);
        Check(text.GetParsedText() == EventLogFormatter.Format(first), "filtering frozen history retains the inspected event even after journal eviction");
        Tap(Key.F2);
        Tap(Key.F3);
        await Frames(2);
        Check(shell.SelectedTab == DevToolsTab.Logs && !follow.ButtonPressed && text.GetParsedText() == EventLogFormatter.Format(first), "tab switching preserves frozen snapshot and filter");
        filter.Select(0);
        filter.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
        follow.ButtonPressed = true;
        await Frames(3);
        Check(text.GetParsedText() == string.Join('\n', stream.Entries.Select(EventLogFormatter.Format)), "unfreeze renders all and only retained events in stream order");
        Check(text.ScrollFollowing && Math.Abs(text.GetVScrollBar().Value - text.GetVScrollBar().MaxValue + text.GetVScrollBar().Page) < 1, "unfreeze resumes following the latest retained event");
        Check(!stream.Accept(first), "evicted authoritative identity remains rejected");
        stream.Record(EventCategory.Session, "Joined", actor: 1);
        await Frames(3);
        Check(text.GetParsedText().EndsWith(EventLogFormatter.Format(stream.Entries[^1]), StringComparison.Ordinal), "live rendering resumes after unfreeze");
        panel.Source = () => null;
        await Frames(2);
        Check(text.GetParsedText().Length == 0, "absent source clears stale history");
        panel.Source = originalSource;
        await Frames(3);
        Check(filter.ItemCount == Enum.GetValues<EventCategory>().Length + 1, "all structured categories retained");
        Rect2 content = panel.GetGlobalRect();
        Check(Math.Abs(content.Size.X - shell.Bounds.Size.X + 32) < 1 && text.Size.Y > 100 && content.Encloses(text.GetGlobalRect()), $"Logs fills the shared shell content width with an expanding history: shell={shell.Bounds}, content={content}, text={text.GetGlobalRect()}");
    }

    private async Task CheckActivityFeed(SimulationBootstrap bootstrap, DevelopmentSession host)
    {
        var feed = bootstrap.GetNode<Hud.ActivityFeed>("ActivityFeed");
        Label[] rows = Descendants(feed).OfType<Label>().ToArray();
        feed.Refresh(5);
        Check(!feed.Visible && rows.Length == 5, "empty feed has no persistent panel");
        Check(!Descendants(feed).Any(node => node is Panel or PanelContainer), "feed has no opaque panel");
        // Structured presentation fixtures use the production journal; the real leave below uses UDP authority.
        host.Events.Record(EventCategory.Session, "Joined", actor: 2);
        feed.Refresh(2);
        host.Events.Record(EventCategory.Network, "Reconnected", actor: 2);
        feed.Refresh(3);
        Check(rows.Count(row => row.Visible) == 1 && rows[0].Text == "Guest tester reconnected", "independent native row expiry");
        feed.Refresh(5);
        for (int index = 0; index < 6; index++)
        {
            host.Events.Record(EventCategory.Session, index == 0 ? "Joined" : "Left", actor: 2);
        }

        host.Events.Record(EventCategory.Developer, "Give Item", actor: 1);
        feed.Refresh(0);
        Check(rows.All(row => row.Visible && row.Text == "Guest tester left the game"), "sixth row evicts oldest and admin events stay hidden");
        feed.Refresh(5);
        if (DisplayServer.GetName() != "headless")
        {
            string output = ProjectSettings.GlobalizePath("res://.godot/activity-feed-checks");
            System.IO.Directory.CreateDirectory(output);
            foreach (Vector2I size in new[] { new Vector2I(640, 360), new Vector2I(1280, 720), new Vector2I(2560, 1080) })
            {
                DisplayServer.WindowSetSize(size);
                await Frames(12);
                feed.Refresh(5);
                host.Events.Record(EventCategory.Session, "Joined", actor: 1);
                host.Events.Record(EventCategory.Network, "Disconnected", actor: 2);
                host.Events.Record(EventCategory.Network, "Reconnected", actor: 2);
                host.Events.Record(EventCategory.Lifecycle, "Kill", actor: 1, target: 2, cause: "Missile");
                host.Events.Record(EventCategory.Lifecycle, "Dead", target: 2);
                await Frames(2);
                for (int index = 0; index < rows.Length; index++)
                {
                    Rect2 rect = rows[index].GetGlobalRect();
                    Check(rect.Position.Y >= 38 && rect.End.X <= size.X && rect.End.Y < size.Y / 2, "top-right feed fits viewport");
                    Check(index == 0 || rows[index - 1].GetGlobalRect().End.Y <= rect.Position.Y, "feed rows never overlap");
                    Check(rows[index].MouseFilter == Control.MouseFilterEnum.Ignore, "feed does not intercept input");
                }

                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var capture = GetViewport().GetTexture().GetImage();
                Check(capture.SavePng(System.IO.Path.Combine(output, $"feed-{size.X}x{size.Y}.png")) == Error.Ok, "feed screenshot saved");
            }
        }

        feed.Refresh(5);
        _client!.Leave();
        await Until(() => host.Lobby!.State!.Players.Any(player => !player.Connected));
        await Frames(3);
        Check(rows.Any(row => row.Visible && row.Text == "Guest tester disconnected"), "actual UDP arena disconnect appears automatically while player state is retained");
        feed.Refresh(5);
        Check(!feed.Visible, "all feed rows expire");
        GD.Print("Activity feed integration passed: production HUD, independent expiry, bounded bursts, filtering and UDP departure.");
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
