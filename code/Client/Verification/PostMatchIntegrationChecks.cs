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

/// <summary>Production Application Flow over two native UDP worlds; a lethal Core outcome completes seeded Circus state.</summary>
public sealed partial class PostMatchIntegrationChecks : Node
{
    private SimulationBootstrap _bootstrap = null!;
    private DevelopmentSession _host = null!;
    private DevelopmentSession? _client;
    private string _output = string.Empty;
    private string _endpoint = string.Empty;
    private bool _collectDuringLoading;
    private readonly List<string> _evidence = new();

    public override void _Ready() => CallDeferred(MethodName.Run);
    public override void _PhysicsProcess(double delta)
    {
        _client?.Advance(default);
        if (_collectDuringLoading && _host?.LoadingMatch == true) GC.Collect();
    }

    public async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            _collectDuringLoading = OS.GetCmdlineUserArgs().Contains("--post-match-gc");
            _output = OS.GetCmdlineUserArgs().Single(arg => arg.StartsWith("--post-match-output=", StringComparison.Ordinal))[20..];
            if (OS.GetCmdlineUserArgs().Contains("--post-match-resources-only"))
            {
                await CheckWarmResourceHandoff();
                System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
                GD.Print($"Post-match integration passed: {_evidence.Count} resource assertions.");
                GetTree().Quit();
                return;
            }
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

            int cycles = int.Parse(OS.GetCmdlineUserArgs().Single(arg => arg.StartsWith("--post-match-cycles=", StringComparison.Ordinal))[20..], System.Globalization.CultureInfo.InvariantCulture);
            for (int cycle = 0; cycle < cycles; cycle++)
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
                    world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, world.State.Vehicles, boundary.Match));
                    await Until(() => _host.Stage == ApplicationStage.Podium, "Restoring the original authoritative final checkpoint returns to Podium");
                    await Frames(2);
                    context = _host.PostMatch!;
                    foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(640, 360), new Vector2I(2560, 1080) })
                    {
                        GetWindow().Size = size;
                        await Frames(4);
                        var bounds = _host.GetNode<PodiumScene>("PodiumScene").Bounds;
                        Check(GetViewport().GetVisibleRect().Encloses(bounds), $"Podium fits {size}");
                        CheckPodiumControls();
                        if (size.X == 640)
                        {
                            Buttons(_host).Single(button => button.Text == "Rematch").GrabFocus();
                            JoyEvent(JoyButton.DpadDown);
                            Check((GetViewport().GuiGetFocusOwner() as Button)?.Text == "Main Menu", "Compact controller Down selects action in next row");
                            KeyEvent(Key.Up);
                            Check((GetViewport().GuiGetFocusOwner() as Button)?.Text == "Rematch", "Compact keyboard Up returns to preceding action row");
                        }
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
                    await CheckOverlayFocus();
                    await CheckResultLayouts(context);
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

                if (cycle < cycles - 1)
                {
                    if (cycle == 0)
                    {
                        Buttons(_host).Single(button => button.Text == "Rematch").GrabFocus();
                        JoyEvent(JoyButton.A);
                        Check(_host.Stage == ApplicationStage.MatchLoader, "Controller Accept starts rematch through Loader");
                    }
                    else Press(_host, "Rematch");
                    Check(_host.Stage == ApplicationStage.MatchLoader && _host.Arena is null && _host.PostMatch is null, "Rematch immediately enters Loader with no arena/results");
                    Check(oldHost.Host is null && oldHost.Match is null && oldHost.EntryContext is null, "Rematch disposes host match state");
                    Check(!_host.NavigatePostMatch(PostMatchDestination.Rematch), "Rapid duplicate rematch rejected");
                    await Until(() => _client.Stage == ApplicationStage.MatchLoader, "Remote sees Loader before new Game Loop");
                    await Until(() => _host.Arena?.Driver.EntryReady == true && _client.Arena?.Driver.EntryReady == true, "Both peers finish fresh Loader/Sync");
                    Check(oldClient.Match is null && oldClient.Prediction is null, "Old client prediction and results disposed");
                    Check(_host.Lobby!.State!.Match == context.Roster.Match + 1, "Rematch advances generation exactly once");
                    Check(_host.Arena!.Driver.Match is { Winner: null } fresh && fresh.Phase != MatchPhase.Finished && fresh.Players.All(row => row.Kills == 0 && row.Deaths == 0 && row.Wins == 0 && row.ProcessedLife == 0 && row.CircusScore == 0 && row.KillStreak == 0 && row.Stunts is null && row.ProcessedDamageLife == 0 && row.ProcessedDamageSequence == 0), "Fresh mode has no winner, Circus totals, streaks, pending stunts or consumed-outcome history");
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
        if (withClient)
        {
            var active = new MatchState(state.Tick, match.Revision + 1, match.KillTarget, MatchPhase.Active, null, null,
                match.Players.Select(row => new PlayerScore(row.Player, row.Player == winner ? match.KillTarget - 1 : 0,
                    row.Player == victim ? match.KillTarget - 1 : 0, 0, row.Player == victim ? (ulong)match.KillTarget - 1 : 0)
                {
                    CircusScore = row.Player == winner ? 375.5 : 125.25,
                    KillStreak = row.Player == winner ? match.KillTarget - 1 : 0,
                    Stunts = row.Player == winner ? new StuntState { Life = world.GetVehicle(winner).LifeId, Tick = state.Tick, TopSpeed = new(1, 15) } : null,
                }));
            // Advance the victim life to represent the seeded previous deaths coherently.
            var vehicles = state.Vehicles.Select(vehicle => vehicle.VehicleId == victim
                ? new Core.Vehicles.VehicleSnapshot(vehicle.VehicleId, (ulong)match.KillTarget, vehicle.Movement, vehicle.Damage, vehicle.ObservedPhysics)
                : vehicle).ToArray();
            world.Restore(new SimulationState(state.Tick, state.LastInput, vehicles, active));
            Check(_host.Arena!.Driver.TryConfigure(new Dictionary<string, double> { ["match.duration_ticks"] = 1 }, out _), "Host configures one remaining Active tick");
            var frame = new InputFrame(state.Tick + 1, 0, 0, 0, 0, 0, 0);
            world.Step(frame, world.State.Vehicles.Select(vehicle => new Core.Vehicles.VehicleStepRequest(vehicle.VehicleId, frame,
                new Core.Vehicles.VehicleObservation(vehicle.ObservedPhysics, System.Numerics.Vector3.UnitY), vehicle.VehicleId == victim
                    ? [new Core.Vehicles.VehicleEffectRequest(new Core.Vehicles.DamageEffect(100000, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero), new Core.Vehicles.DamageContext("missile", winner, "Circus completion check"))] : [])).ToArray());
            Check(world.State.Match!.Phase == MatchPhase.Finished && world.State.Match.Players.All(row => row.Stunts is null), "Authoritative timer completes Circus through Game Loop and cancels pending stunts");
        }
        else
        {
            // Solo quit-navigation fixture: no opponent exists for an attributed completion.
            var final = new MatchState(state.Tick, match.Revision + 1, match.KillTarget, MatchPhase.Finished, null, winner,
                match.Players.Select(row => new PlayerScore(row.Player, match.KillTarget, match.KillTarget, 1, (ulong)match.KillTarget) { CircusScore = 1375.5 }));
            world.Restore(new SimulationState(state.Tick, state.LastInput, state.Vehicles, final));
        }
        await Until(() => _host.Stage == ApplicationStage.Podium && (!withClient || _client?.Stage == ApplicationStage.Podium), "Authoritative Finished transitions to dedicated Podium");
        await Frames(2);
        var podium = _host.GetNode<PodiumScene>("PodiumScene");
        string score = Hud.CircusHudView.FormatPoints(_host.PostMatch!.Results.Standings[0].CircusScore);
        Check(podium.FindChildren("*", "Label", true, false).OfType<Label>().Any(label => label.Text == score && label.IsVisibleInTree()), "Podium renders frozen authoritative Circus score");
        Check(_host.Arena!.Driver.TryConfigure(new Dictionary<string, double> { ["match.duration_ticks"] = 36000 }, out _), "Finished tuning preserves results and prepares a normal next match");
    }

    private static IEnumerable<Button> Buttons(Node node) => node.FindChildren("*", "Button", true, false).OfType<Button>();

    private async Task CheckWarmResourceHandoff()
    {
        foreach (var map in new[] { MatchMap.OldMap, MatchMap.NewMap })
        {
            var retained = new MatchResourceLoader(map);
            for (int frame = 0; !retained.Complete; frame++)
            {
                if (frame >= 1800) throw new InvalidOperationException("Cold resource preparation timed out.");
                retained.Advance();
                await Frames(1);
            }
            var warm = new MatchResourceLoader(map);
            while (!warm.Complete)
            {
                double previous = warm.Progress;
                warm.Advance();
                Check(warm.Progress > previous, "Cached resource handoff progresses on the main thread without a worker/poll round trip");
            }
            Check(ReferenceEquals(retained.MapScene, warm.MapScene), "Warm loader retains the same cached map asset");
            GC.KeepAlive(retained);
        }
    }

    private async Task CheckOverlayFocus()
    {
        var mainMenu = Buttons(_host).Single(button => button.Text == "Main Menu");
        mainMenu.GrabFocus();
        JoyEvent(JoyButton.DpadLeft);
        Check((GetViewport().GuiGetFocusOwner() as Button)?.Text == "End Match", "Controller Left selects adjacent destination");
        JoyEvent(JoyButton.DpadRight);
        Check(GetViewport().GuiGetFocusOwner() == mainMenu, "Controller Right restores Main Menu selection");
        JoyEvent(JoyButton.Start);
        await Frames(2);
        Check(_host.OverlayOpen(), "Controller Start opens Game Menu above Podium");
        JoyEvent(JoyButton.Start);
        await Frames(2);
        Check(GetViewport().GuiGetFocusOwner() == mainMenu, "Closing Game Menu preserves selected Podium destination");
        KeyEvent(Key.F2);
        await Frames(2);
        Check(_host.OverlayOpen(), "Stats overlays Podium");
        KeyState(Key.Enter, true);
        KeyEvent(Key.Escape);
        await Frames(2);
        Check(!_host.OverlayOpen() && GetViewport().GuiGetFocusOwner() == mainMenu && _host.Stage == ApplicationStage.Podium,
            "Stats close preserves focus and does not dispatch held Accept into Podium");
        KeyState(Key.Enter, false);
        await Capture("podium-restored-main-menu-focus");
    }

    private async Task CheckResultLayouts(PostMatchContext original)
    {
        // Renderer-only fixtures: stop the bootstrap's simulation/input while substituting a detached
        // handoff. The private setter is used here rather than adding a production state mutation API.
        var handoff = typeof(DevelopmentSession).GetProperty(nameof(DevelopmentSession.PostMatch),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var podium = _host.GetNode<PodiumScene>("PodiumScene");
        _bootstrap.GetNode<PlayerInput>("PlayerInput").SetPhysicsProcess(false);
        try
        {
            foreach (int participants in new[] { 8, 10 })
            {
                var roster = original.Roster;
                var history = Enumerable.Range(3, participants - 2).Select(id => new MatchParticipant((ulong)id,
                    id == 3 ? "WWWWWWWWWWWWWWWWWWWWWWWW" : $"Retained player {id}"));
                var fixtureRoster = new LobbySnapshot(roster.Session, roster.Revision, roster.Match, roster.Phase,
                    roster.Players, roster.CurrentHostId, roster.AuthorityEpoch, history, roster.Map);
                ulong[] ids = new[] { 3UL, 1UL, 2UL }.Concat(Enumerable.Range(4, participants - 3).Select(id => (ulong)id)).ToArray();
                var results = new FinalMatchResults(original.Results.Tick, new MatchOutcome("layout fixture", 3),
                    ids.Select((id, index) => new FinalMatchStanding(id, index + 1, 1234567.89 - index * 100, index == 0 ? 5 : 0, 0, index == 0 ? 1 : 0)));
                handoff.SetValue(_host, new PostMatchContext(fixtureRoster, results));
                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(640, 360) })
                {
                    GetWindow().Size = size;
                    await Frames(3);
                    Check(GetViewport().GetVisibleRect().Encloses(podium.Bounds), $"{participants}-result layout fits {size}");
                    CheckPodiumControls();
                    Check(ReferenceEquals(results, podium.Displayed!.Results), "Layout preserves authoritative result ordering/object");
                    await Capture($"podium-{participants}-long-name-{size.X}x{size.Y}");
                    if (participants == 8 && size.X == 640 && DisplayServer.GetName() != "headless")
                    {
                        var name = podium.FindChildren("*", "Label", true, false).OfType<Label>()
                            .Single(label => label.IsVisibleInTree() && label.Text.StartsWith("WWWW", StringComparison.Ordinal) && label.GetGlobalRect().Position.Y > 60);
                        using var motion = new InputEventMouseMotion { Position = name.GetGlobalRect().GetCenter(), GlobalPosition = name.GetGlobalRect().GetCenter() };
                        Godot.Input.ParseInputEvent(motion);
                        Godot.Input.FlushBufferedEvents();
                        await Frames(45);
                        await Capture("podium-long-name-tooltip");
                        using var away = new InputEventMouseMotion { Position = Vector2.Zero, GlobalPosition = Vector2.Zero };
                        Godot.Input.ParseInputEvent(away);
                        Godot.Input.FlushBufferedEvents();
                    }
                    var next = Buttons(_host).Single(button => button.Text == "Next");
                    if (next.Visible && !next.Disabled)
                    {
                        next.GrabFocus();
                        KeyEvent(Key.Enter);
                        await Frames(2);
                        Check(Buttons(_host).Single(button => button.Text == "Previous").Disabled == false, "Keyboard advances to retained-results page");
                        await Capture($"podium-{participants}-page2-{size.X}x{size.Y}");
                        Buttons(_host).Single(button => button.Text == "Previous").GrabFocus();
                        JoyEvent(JoyButton.A);
                        Check(Buttons(_host).Single(button => button.Text == "Previous").Disabled, "Controller returns to first results page");
                    }
                }
            }
            handoff.SetValue(_host, new PostMatchContext(original.Roster,
                new FinalMatchResults(original.Results.Tick, new MatchOutcome("draw"), [])));
            await Frames(3);
            Check(podium.FindChildren("*", "Label", true, false).OfType<Label>().Any(label => label.Text == "MATCH FINISHED" && label.IsVisibleInTree()), "No-winner outcome does not invent a winner");
            await Capture("podium-no-winner-640x360");
        }
        finally
        {
            handoff.SetValue(_host, original);
            GetWindow().Size = new Vector2I(1280, 720);
            _bootstrap.GetNode<PlayerInput>("PlayerInput").SetPhysicsProcess(true);
        }
        await Frames(3);
        Check(!Buttons(_host).Single(button => button.Text == "Next").Visible, "Two-player results hide unnecessary paging");
    }

    private static void JoyEvent(JoyButton button)
    {
        foreach (bool down in new[] { true, false })
        {
            using var input = new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = down };
            Godot.Input.ParseInputEvent(input);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private void CheckPodiumControls()
    {
        var podium = _host.GetNode<PodiumScene>("PodiumScene");
        var buttons = Buttons(podium).Where(button => button.IsVisibleInTree()).ToArray();
        Check(buttons.All(button => podium.Bounds.Encloses(button.GetGlobalRect())), "All Podium hit targets fit content bounds");
        Check(!buttons.Where((button, index) => buttons.Skip(index + 1).Any(other => button.GetGlobalRect().Intersects(other.GetGlobalRect()))).Any(), "Podium action and paging hit targets do not overlap");
    }
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
            KeyState(code, down);
        }
    }

    private static void KeyState(Key code, bool down)
    {
        using var key = new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = down };
        Godot.Input.ParseInputEvent(key);
        Godot.Input.FlushBufferedEvents();
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
