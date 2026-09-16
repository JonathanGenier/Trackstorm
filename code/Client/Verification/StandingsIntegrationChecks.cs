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
                var board = new MatchStandings { View = () => session.Standings, IsHost = () => session.Lobby?.Authority is not null, LeaveResults = session.LeaveResults };
                viewport.AddChild(board);
                _boards.Add(board);
                var hud = new CombatHud { Vehicle = () => session.Arena?.LocalState, Slot = () => session.Arena?.Driver.LocalItem, Position = () => session.Standings.Position };
                viewport.AddChild(hud);
                _huds.Add(hud);
            }

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
            SetTotals(false);
            await Until(() => _sessions.All(session => session.Standings.Rows[0].PlayerId == 8));
            key.Pressed = true;
            Send(key);
            _input.Adapter.Observe();
            await Frames(2);
            Refresh(true);
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
                await Capture($"results-{size.X}x{size.Y}");
            }

            await Frames(120);
            Refresh(true);
            _boards[0].FindChildren("LeaveResults", "Button", true, false).Cast<Button>().Single().EmitSignal(BaseButton.SignalName.Pressed);
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
        var match = new MatchState(previous.Tick, previous.Match!.Revision + 1, 5, finished ? MatchPhase.Finished : MatchPhase.Active, null, finished ? 8ul : null, previous.Match.Players.Select(player => new PlayerScore(player.Player, player.Player == 8 ? finished ? 5 : 2 : 0, player.Player == 1 ? 5 : 0, finished && player.Player == 8 ? 1 : 0, 5)));
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
