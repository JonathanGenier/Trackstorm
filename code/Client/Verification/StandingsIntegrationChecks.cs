using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Hud;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Simulation;

namespace Trackstorm.Client.Verification;

/// <summary>Eight real Direct-IP sessions exercise production standings, input, replicated totals and native rendering.</summary>
public sealed partial class StandingsIntegrationChecks : Node
{
    private readonly List<DevelopmentSession> _sessions = new();
    private readonly List<MatchStandings> _boards = new();
    private readonly List<CombatHud> _huds = new();
    private PlayerInput _input = null!;
    private SubViewport _view = null!;
    private Settings.SettingsPanel _diagnostics = null!;
    private bool _done;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_done || _input is null)
        {
            return;
        }

        _input.Adapter.Enabled = true; // Synthetic fixture is independent of desktop window focus.
        InputFrame input = _input.Adapter.Capture(0);
        foreach (var session in _sessions)
        {
            session.Advance(input);
        }
    }

    /// <summary>Runs the native integration scenario and exports actual rendered boards.</summary>
    public async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            _input = new PlayerInput();
            AddChild(_input);
            _input.SetPhysicsProcess(false);
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            reservation.Close();
            for (int index = 0; index < 8; index++)
            {
                var viewport = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = index == 0 ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled };
                AddChild(viewport);
                if (index == 0)
                {
                    _view = viewport;
                }

                var session = new DevelopmentSession();
                viewport.AddChild(session);
                session.Open(index == 0, endpoint, index == 7 ? "RoadKillQueen" : $"Player {index + 1}");
                _sessions.Add(session);
                var board = new MatchStandings { View = () => session.Standings };
                viewport.AddChild(board);
                _boards.Add(board);
                var hud = new CombatHud
                {
                    Vehicle = () => session.Arena?.LocalState,
                    Slot = () => session.Arena?.Driver.LocalItem,
                    Position = () => session.Standings.Position,
                    Match = () => session.Arena?.Driver.Match,
                    MatchUpdates = session.DrainMatchPresentation,
                    Player = () => session.Lobby?.LocalPlayerId ?? 0,
                };
                viewport.AddChild(hud);
                _huds.Add(hud);
            }

            var settings = new Settings.PlayerSettingsController();
            settings.Initialize(_input.Adapter, ProjectSettings.GlobalizePath("res://.godot/ts32-standings-settings.json"));
            AddChild(settings);
            _diagnostics = new Settings.SettingsPanel();
            _diagnostics.Initialize(settings, _input.Adapter);
            _view.AddChild(_diagnostics);
            settings.UpdateSettings(settings.Current with { ShowFps = true, ShowPing = true });
            _diagnostics.SetConnectionTelemetry(new(ConnectionDiagnosticState.Reconnecting, default));
            await Until(() => _sessions.All(session => session.Lobby?.State?.Players.Count == 8));
            foreach (var session in _sessions)
            {
                session.Lobby!.Request(LobbyCommand.Ready, true);
            }

            await Until(() => _sessions[0].Lobby!.State!.CanStart);
            Require(_sessions[0].Lobby!.Request(LobbyCommand.Start), "Host starts all eight sessions");
            await Until(() => _sessions.All(session => session.Arena?.Driver.Match?.Phase == MatchPhase.Active && session.Arena.LocalState is not null));
            Refresh(false);
            using var key = new InputEventKey { PhysicalKeycode = Key.Tab, Pressed = true };
            Send(key);
            _input.Adapter.Observe();
            await Frames(2);
            Refresh(true);
            key.Pressed = false;
            Send(key);
            _input.Adapter.Observe();
            await Frames(2);
            Refresh(false);
            using var controller = new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.Back, Pressed = true };
            Send(controller);
            _input.Adapter.Observe();
            await Frames(2);
            Refresh(true);
            controller.Pressed = false;
            Send(controller);
            _input.Adapter.Observe();
            await Frames(2);
            Refresh(false);
            await Until(() => _sessions.All(session => session.Standings.Rows.Count(row => row.Ping != "--") == 7));
            Require(_sessions.All(session => session.Standings.Rows.Single(row => row.PlayerId == 1).Ping == "--"), "Host no-hop rule");
            foreach (var session in _sessions)
            {
                _diagnostics.SetConnectionTelemetry(session.Diagnostics);
                var counter = _diagnostics.FindChild("Diagnostics", true, false) as Settings.SettingsHud ?? throw new InvalidOperationException("Missing diagnostics HUD.");
                Require(counter.PingText == "Ping  " + session.Standings.Rows.Single(row => row.Local).Ping, "Rendered HUD Ping equals the local leaderboard row for every peer");
            }

            _diagnostics.SetConnectionTelemetry(new(ConnectionDiagnosticState.Reconnecting, default));
            SetTotals(false);
            await Until(() => _sessions.All(session => session.Standings.Rows[0].PlayerId == 8));
            for (int index = 0; index < _sessions.Count; index++)
            {
                DevelopmentSession session = _sessions[index];
                MatchState published = session.Arena!.Driver.Match!;
                ulong local = session.Lobby!.LocalPlayerId;
                var pending = new StuntState
                {
                    Life = session.Arena.LocalState!.LifeId,
                    Tick = published.Tick,
                    Drift = new StuntProgress(30, 12),
                    Airtime = new StuntProgress(30, 10),
                    TopSpeed = new StuntProgress(30, 5),
                    JumpOrigin = new System.Numerics.Vector3(1, 0, 1),
                    JumpDistance = 4,
                    LongJumpBasePoints = 8,
                };
                var fixture = new MatchState(published.Tick, published.Revision + 1, published.KillTarget, MatchPhase.Active, null, null,
                    published.Players.Select(player => player.Player == local ? player with { Stunts = pending } : player));
                _huds[index].Match = () => fixture;
                _huds[index].MatchUpdates = () => Array.Empty<MatchState>();
            }

            Refresh(false);
            Require(_huds.All(hud => hud.ScoreDisplayed!.Rows.Count(row => row.Kind == CircusFeedbackKind.Pending) == 4), "Drift, Airtime, Long Jump and Top Speed coexist as one live row per category.");
            await Capture("circus-hud");
            for (int index = 0; index < _sessions.Count; index++)
            {
                DevelopmentSession session = _sessions[index];
                _huds[index].Match = () => session.Arena?.Driver.Match;
                _huds[index].MatchUpdates = session.DrainMatchPresentation;
            }

            key.Pressed = true;
            Send(key);
            _input.Adapter.Observe();
            await Frames(2);
            Refresh(true);
            Require(_sessions.All(session => session.Standings.Rows[0] is { PlayerId: 8, CircusScore: 802 }), "Live Circus score is the primary rank and is projected into every board.");
            Require(_huds.Select((hud, index) => hud.ScoreDisplayed?.Total == CircusHudView.FormatPoints(_sessions[index].Standings.Rows.Single(row => row.Local).CircusScore)).All(equal => equal), "Every local HUD consumes the same authoritative banked total as its standings row.");
            await Capture("active");
            key.Pressed = false;
            Send(key);
            _input.Adapter.Observe();
            SetTotals(true);
            await Until(() => _sessions.All(session => session.Standings.Finished));
            Refresh(true);
            Require(_sessions.All(session => session.Standings.Rows[0] is { PlayerId: 8, Winner: true, Kills: 5 }), "Synchronized winner identity and final totals");
            foreach (Vector2I size in new[] { new Vector2I(640, 360), new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(1024, 768), new Vector2I(2560, 1080) })
            {
                _view.Size = size;
                await Frames(2);
                Refresh(true);
                Require(_diagnostics.DiagnosticsBounds.End.Y <= _boards[0].Bounds.Position.Y && _diagnostics.DiagnosticsBounds.End.X <= size.X, "FPS/Ping fit above standings without overlap");
                await Capture($"results-{size.X}x{size.Y}");
            }

            await Frames(120);
            Refresh(true);
            var roster = _sessions[0].Lobby!.State!;
            var totals = _sessions[0].Arena!.Driver.Match!;
            var historyRoster = new LobbySnapshot(roster.Session, roster.Revision + 1, roster.Match, roster.Phase, roster.Players, departed: [new(9, "Departed contender")]);
            var historyTotals = new MatchState(totals.Tick, totals.Revision + 1, totals.KillTarget, totals.Phase, null, totals.Winner, totals.Players.Append(new PlayerScore(9, 0, 6, 0, 6)));
            _boards[0].View = () => MatchStandingsView.From(historyRoster, historyTotals, 1, InputButtons.None, _ => null);
            _boards[0].Refresh();
            using var nextPage = new InputEventKey { PhysicalKeycode = Key.Pagedown, Pressed = true };
            _view.PushInput(nextPage);
            await Frames(2);
            var historyLabel = _boards[0].FindChildren("*", "Label", true, false).OfType<Label>().Single(label => label.Text == "Departed contender");
            Require(historyLabel.Modulate.A == 0.55f, "Paged abandoned history remains dimmed and reachable.");
            Require(_boards[0].FindChildren("*", "Label", true, false).OfType<Label>().Any(label => label.Text.Contains("2/2", StringComparison.Ordinal)), "Native Page Down changes the visible history page.");
            await Capture("results-departed-page");
            _boards[0].View = () => _sessions[0].Standings;
            _sessions[0].LeaveResults();
            await Until(() => _sessions.All(session => session.Arena is null));
            Refresh(false, false);
            GD.Print("Standings integration passed: eight UDP sessions; TAB/controller hold and release; host-published ping; synchronized changing ranks/HP HUD; winner/final results; five rendered sizes; return cleanup.");
            Cleanup();
            await Frames(3);
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            Cleanup();
            GetTree().Quit(1);
        }
    }

    private static void Send(InputEvent input)
    {
        using var copy = (InputEvent)input.Duplicate();
        Godot.Input.ParseInputEvent(copy);
        Godot.Input.FlushBufferedEvents();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void SetTotals(bool finished)
    {
        var world = _sessions[0].Arena!.Driver.Host!.World;
        var previous = world.State;
        var players = previous.Match!.Players.Select(player => new PlayerScore(player.Player, player.Player == 8 ? finished ? 5 : 2 : 0, player.Player == 1 ? 5 : 0, finished && player.Player == 8 ? 1 : 0, 5)
        {
            CircusScore = (player.Player * 100) + 2,
        }).ToArray();
        var awards = finished ? Array.Empty<CircusScoreAward>() : players.Select(player => new CircusScoreAward(player.Player, CircusScoreCategory.Collision, 2)).ToArray();
        var match = new MatchState(previous.Tick, previous.Match.Revision + 1, 5, finished ? MatchPhase.Finished : MatchPhase.Active, null, finished ? 8ul : null, players, awards: awards);
        // Fixture installs an authoritative boundary. The existing match harness separately verifies actual lethal combat.
        world.Restore(new SimulationState(previous.Tick, previous.LastInput, previous.Vehicles, match));
    }

    private void Refresh(bool visible, bool arena = true)
    {
        for (int index = 0; index < _sessions.Count; index++)
        {
            _boards[index].Refresh();
            _huds[index].Refresh();
            Require(_boards[index].Visible == visible, "Native board visibility");
            if (arena)
            {
                var view = _boards[index].Displayed!;
                Require(view.Rows.Count == 8, "Complete roster");
                Require(_huds[index].Displayed!.Standing == view.Position, "Native HUD equals board rank");
            }
        }
    }

    private async Task Until(Func<bool> predicate)
    {
        for (int count = 0; count < 900 && !predicate(); count++)
        {
            await Frames(1);
        }

        Require(predicate(), "Standings scenario timed out");
    }

    private async Task Frames(int count)
    {
        for (int index = 0; index < count; index++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string output = ProjectSettings.GlobalizePath("res://.godot/standings-checks");
        System.IO.Directory.CreateDirectory(output);
        using Image image = _view.GetTexture().GetImage();
        Require(image.SavePng(System.IO.Path.Combine(output, name + ".png")) == Error.Ok, "Rendered screenshot");
    }

    private void Cleanup()
    {
        _done = true;
        foreach (var session in _sessions)
        {
            session.Leave();
        }
    }
}
