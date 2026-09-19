using Godot;
using Trackstorm.Client.Bootstrap;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.PostMatch;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Simulation;

namespace Trackstorm.Client.Verification;

/// <summary>Production Application Flow over two native UDP worlds; Finished is an explicit authoritative fixture.</summary>
public sealed partial class PostMatchIntegrationChecks : Node
{
    private SimulationBootstrap _bootstrap = null!;
    private DevelopmentSession _host = null!;
    private DevelopmentSession? _client;
    private string _output = string.Empty;
    private string _endpoint = string.Empty;
    private readonly List<string> _evidence = new();

    public override void _Ready() => CallDeferred(MethodName.Run);
    public override void _PhysicsProcess(double delta) => _client?.Advance(default);

    public async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            _output = OS.GetCmdlineUserArgs().Single(arg => arg.StartsWith("--post-match-output=", StringComparison.Ordinal))[20..];
            _bootstrap = new SimulationBootstrap { OnlineEnabled = false, StartupEnabled = true, VerificationOwnsExit = true, SettingsPath = System.IO.Path.Combine(_output, "settings.json") };
            _bootstrap.AddChild(new PlayerInput { Name = "PlayerInput" });
            AddChild(_bootstrap);
            await Until(() => _bootstrap.GetNodeOrNull<DevelopmentSession>("DevelopmentSession") is not null, "Actual startup reaches Main Menu", 2400);
            _host = _bootstrap.GetNode<DevelopmentSession>("DevelopmentSession");
            var shell = _bootstrap.FindChildren("*", "CanvasLayer", true, false).OfType<MenuShell>().Single();
            ulong video = shell.BackgroundInstanceId;
            using (var reservation = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
                _endpoint = $"127.0.0.1:{((System.Net.IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            _host.Open(true, _endpoint, "Podium host");
            var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
            AddChild(view);
            _client = new DevelopmentSession();
            view.AddChild(_client);
            _client.Open(false, _endpoint, "Podium challenger");
            await Until(() => _host.Lobby?.State?.Players.Count == 2 && _client.Lobby?.State?.Players.Count == 2, "Two production sessions admitted");
            Check(!_host.NavigatePostMatch(PostMatchDestination.Rematch), "Rematch rejected before Finished");
            await Start();

            for (int cycle = 0; cycle < 3; cycle++)
            {
                await FinishMatch();
                Check(!_client.NavigatePostMatch(PostMatchDestination.Rematch) && !_client.NavigatePostMatch(PostMatchDestination.Lobby) && !_client.NavigatePostMatch(PostMatchDestination.EndMatch), "Non-host cannot navigate the shared match");
                var context = _host.PostMatch!;
                var oldHost = _host.Arena!.Driver;
                var oldClient = _client.Arena!.Driver;
                Check(ReferenceEquals(context.Results, _host.GetNode<PodiumScene>("PodiumScene").Displayed!.Results), "Podium presents the exact detached Core result");
                Check(_client.PostMatch!.Results.Standings.SequenceEqual(context.Results.Standings), "Both peers display identical authoritative results");
                Check(!shell.MediaPlaying, "MenuShell stays suspended during Podium");
                if (cycle == 0)
                {
                    var world = _host.Arena!.Driver.Host!.World;
                    var boundary = world.State;
                    var active = new MatchState(boundary.Tick, boundary.Match!.Revision + 1, boundary.Match.KillTarget,
                        MatchPhase.Active, null, null, boundary.Match.Players.Select(row => new PlayerScore(row.Player, 0, 0, 0, 0)));
                    world.Restore(new SimulationState(boundary.Tick, boundary.LastInput, boundary.Vehicles, active));
                    await Until(() => _host.Stage == ApplicationStage.GameLoop,
                        "Locally restored authoritative checkpoint clears stale Podium (host fixture)");
                    Check(_client.Stage == ApplicationStage.Podium, "Ordinary phase packets cannot roll a remote Finished state backward");
                    world.Restore(boundary);
                    await Until(() => _host.Stage == ApplicationStage.Podium, "Restoring the original authoritative final checkpoint returns to Podium");
                    await Frames(2);
                    context = _host.PostMatch!;
                    foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(640, 360), new Vector2I(2560, 1080) })
                    {
                        GetWindow().Size = size;
                        await Frames(4);
                        var bounds = _host.GetNode<PodiumScene>("PodiumScene").Bounds;
                        Check(GetViewport().GetVisibleRect().Encloses(bounds), $"Podium fits {size}");
                        await Capture($"podium-{size.X}x{size.Y}");
                    }
                    GetWindow().Size = new Vector2I(1280, 720);
                    await Frames(3);
                    KeyEvent(Key.Escape);
                    await Frames(2);
                    Check(_host.OverlayOpen(), "ESC opens Game Menu above Podium");
                    KeyEvent(Key.Escape);
                    await Frames(2);
                    Check(!_host.OverlayOpen(), "ESC closes overlay and restores Podium navigation");
                    if (DisplayServer.GetName() != "headless") Check(Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Visible, "Podium releases native mouse capture");
                    // Native UI defaults cannot bypass a logical Accept remap.
                    var input = _bootstrap.GetNode<PlayerInput>("PlayerInput").Adapter;
                    using var key = new InputEventKey { PhysicalKeycode = Key.F8 };
                    input.Bindings.Replace(InputAction.MenuAccept, key);
                    var end = Buttons(_host).Single(button => button.Text == "Return to Lobby");
                    end.GrabFocus();
                    KeyEvent(Key.Enter);
                    Check(_host.Stage == ApplicationStage.Podium, "Unbound native Enter cannot bypass logical navigation");
                    input.Bindings.RestoreDefaults();
                }

                if (cycle < 2)
                {
                    Press(_host, "Rematch");
                    Check(_host.Stage == ApplicationStage.MatchLoader && _host.Arena is null && _host.PostMatch is null, "Rematch immediately enters Loader with no arena/results");
                    Check(oldHost.Host is null && oldHost.Match is null && oldHost.EntryContext is null, "Rematch disposes host match state");
                    Check(!_host.NavigatePostMatch(PostMatchDestination.Rematch), "Rapid duplicate rematch rejected");
                    await Until(() => _client.Stage == ApplicationStage.MatchLoader, "Remote sees Loader before new Game Loop");
                    await Until(() => _host.Arena?.Driver.EntryReady == true && _client.Arena?.Driver.EntryReady == true, "Both peers finish fresh Loader/Sync");
                    Check(oldClient.Match is null && oldClient.Prediction is null, "Old client prediction and results disposed");
                    Check(_host.Lobby!.State!.Match == context.Roster.Match + 1, "Rematch advances generation exactly once");
                    Check(_host.Arena!.Driver.Match is { Winner: null } fresh && fresh.Phase != MatchPhase.Finished && fresh.Players.All(row => row.Kills == 0 && row.Deaths == 0 && row.Wins == 0 && row.ProcessedLife == 0), "Fresh mode has no winner, totals or consumed-life history");
                    Check(_host.FinalResults is null && _client.FinalResults is null, "Neither peer leaks old results");
                    await Until(() => _host.Arena?.Driver.Match?.Phase == MatchPhase.Active && _client.Arena?.Driver.Match?.Phase == MatchPhase.Active, "Fresh countdown reaches Active");
                }
            }

            var navigation = _bootstrap.GetNode<PlayerInput>("PlayerInput").Adapter;
            using (var remappedAccept = new InputEventKey { PhysicalKeycode = Key.F8 }) navigation.Bindings.Replace(InputAction.MenuAccept, remappedAccept);
            Buttons(_host).Single(button => button.Text == "Return to Lobby").GrabFocus();
            KeyEvent(Key.F8);
            navigation.Bindings.RestoreDefaults();
            await Until(() => _host.Stage == ApplicationStage.Lobby && _client.Stage == ApplicationStage.Lobby, "Return to Lobby reaches joined state on both peers");
            Check(_host.Lobby!.State!.Players.All(player => !player.Ready) && _host.Arena is null && _client.Arena is null, "Return clears readiness and all match runtime");
            await Start();
            await FinishMatch();
            Press(_host, "End Match");
            await Until(() => _host.Stage == ApplicationStage.Lobby && _client.Stage == ApplicationStage.Lobby, "End Match ends shared match through authoritative Return");
            await Start();
            await FinishMatch();
            ulong departed = _client.Lobby!.LocalPlayerId;
            var frozen = _host.PostMatch!.Results;
            Press(_client, "Main Menu");
            await Until(() => _client.Stage == ApplicationStage.MainMenu && _client.LeaveComplete, "Client Main Menu waits for reliable Leave cleanup");
            await Until(() => _host.Lobby!.State!.Players.Any(player => player.Id == departed && !player.Connected), "Finished retains disconnected participant reservation");
            Check(ReferenceEquals(frozen, _host.PostMatch!.Results) && !_host.PostMatch.Participant(departed, _host.Lobby!.State).Connected, "Retained Podium results stay frozen and offline participant dims");
            Press(_host, "Main Menu");
            await Until(() => _host.Stage == ApplicationStage.MainMenu && _host.LeaveComplete && shell.MediaPlaying, "Main Menu restores MenuShell after cleanup");
            Check(shell.BackgroundInstanceId == video && _host.Arena is null && _host.PostMatch is null, "Same MenuShell resumes with no stale match state");
            _client = null;
            view.QueueFree();
            await Frames(3);
            _host.Open(true, _endpoint, "Quit host");
            _host.ForceDeveloperStart();
            await Until(() => _host.Arena?.Driver.Match?.Phase == MatchPhase.Active, "Solo verification match reaches Active");
            await FinishMatch(false);
            Press(_host, "Quit Game");
            await Until(() => _host.LeaveComplete && _host.Arena is null, "Podium Quit uses production controlled cleanup");
            await Frames(3);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            GD.Print($"Post-match integration passed: {_evidence.Count} assertions.");
            // Cleanup has been observed; let the production bootstrap perform the actual exit.
            _bootstrap.VerificationOwnsExit = false;
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            _client?.Leave();
            _host?.Leave();
            GetTree().Quit(1);
        }
    }

    private async Task Start()
    {
        _host.Lobby!.Request(LobbyCommand.Ready, true);
        _client!.Lobby!.Request(LobbyCommand.Ready, true);
        await Until(() => _host.Lobby.State!.CanStart, "Connected lobby readied");
        Check(_host.Lobby.Request(LobbyCommand.Start), "Normal lobby Start accepted");
        await Until(() => _host.Arena?.Driver.Match?.Phase == MatchPhase.Active && _client.Arena?.Driver.Match?.Phase == MatchPhase.Active, "Initial Loader/Sync and countdown complete");
    }

    private async Task FinishMatch(bool withClient = true)
    {
        var world = _host.Arena!.Driver.Host!.World;
        var state = world.State;
        var match = state.Match!;
        ulong winner = _host.Lobby!.LocalPlayerId;
        ulong victim = match.Players.Select(row => row.Player).FirstOrDefault(id => id != winner, winner);
        var final = new MatchState(state.Tick, match.Revision + 1, match.KillTarget, MatchPhase.Finished, null, winner,
            match.Players.Select(row => new PlayerScore(row.Player, row.Player == winner ? match.KillTarget : 0, row.Player == victim ? match.KillTarget : 0, row.Player == winner ? 1 : 0, row.Player == victim ? (ulong)match.KillTarget : 0)));
        world.Restore(new SimulationState(state.Tick, state.LastInput, state.Vehicles, final));
        await Until(() => _host.Stage == ApplicationStage.Podium && (!withClient || _client?.Stage == ApplicationStage.Podium), "Authoritative Finished transitions to dedicated Podium");
        await Frames(2);
    }

    private static IEnumerable<Button> Buttons(Node node) => node.FindChildren("*", "Button", true, false).OfType<Button>();
    private void Press(DevelopmentSession session, string title)
    {
        session._Process(0);
        var button = Buttons(session).Single(button => button.Text == title && button.IsVisibleInTree());
        Check(!button.Disabled, $"Enabled Podium action: {title}");
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private static void KeyEvent(Key code)
    {
        foreach (bool down in new[] { true, false })
        {
            using var key = new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = down };
            Godot.Input.ParseInputEvent(key);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(_output, name + ".png")) == Error.Ok, "Captured " + name);
    }

    private async Task Until(Func<bool> condition, string message, int limit = 1200)
    {
        for (int frame = 0; frame < limit && !condition(); frame++) await Frames(1);
        Check(condition(), message);
    }

    private async Task Frames(int count)
    {
        for (int frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _evidence.Add(message);
    }
}
