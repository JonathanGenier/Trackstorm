using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Eight production Godot session UIs over real UDP, each with an isolated native physics world.</summary>
public sealed partial class LobbyIntegrationChecks : Node
{
    private readonly List<DevelopmentSession> _sessions = new();
    private readonly List<string> _evidence = new();
    private string _endpoint = string.Empty;
    private string _output = string.Empty;
    private double _elapsed;
    private double _stageStarted;
    private bool _countdownVerified;
    private int _stage;
    private int _rejected;
    private ulong _departedId;
    private ulong _firstMatch;
    private bool _finished;
    private bool _interruptedFresh;
    private SubViewport _hostView = null!;
    private readonly List<Core.Matches.FinalMatchResults> _completedResults = new();
    private readonly List<VehicleNetworkDriver> _retiredDrivers = new();

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        _output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--lobby-output=", StringComparison.Ordinal))?[15..] ?? ProjectSettings.GlobalizePath("res://.godot/lobby-checks");
        System.IO.Directory.CreateDirectory(_output);
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
        reservation.Close();
        for (int index = 0; index < 8; index++)
        {
            var viewport = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = index == 0 ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled };
            if (index == 0)
            {
                _hostView = viewport;
                var display = new SubViewportContainer();
                AddChild(display);
                display.AddChild(viewport);
            }
            else
            {
                AddChild(viewport);
            }

            var session = new DevelopmentSession();
            viewport.AddChild(session);
            _sessions.Add(session);
            OpenThroughUi(session, index == 0, $"  Player <{index}>\u202e  ");
        }
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
        {
            return;
        }

        try
        {
            _elapsed += delta;
            foreach (DevelopmentSession session in _sessions)
            {
                if (_stage == 16 && !_interruptedFresh && session == _sessions[7] && session.Lobby?.JoiningArena == true)
                {
                    _interruptedFresh = true;
                    session.Gateway!.Stop();
                    _stage = 20;
                    _stageStarted = _elapsed;
                    _evidence.Add("Interrupted the fresh client after roster assignment and before checkpoint activation.");
                }

                session.Advance(new InputFrame(0, 0, 20000, 0, 0, 0, 0));
                if (session.Arena?.Driver is { Match.Phase: Core.Matches.MatchPhase.Countdown } driver)
                {
                    Require(!driver.AllowsParticipation, "Countdown denies local participation despite held throttle.");
                    Require(driver.Host is null || driver.Host.World.State.LastInput.Accelerate == 0, "Host throttle is suppressed during Countdown.");
                    Require(driver.Inputs is null || driver.Inputs.Pending.All(command => command.Frame.Accelerate == 0), "Client prediction and outgoing controls are neutral during Countdown.");
                }
            }

            if (_stage == 5 && _sessions.All(session => session.Arena?.Driver.Match?.Phase == Core.Matches.MatchPhase.Countdown))
            {
                Require(_sessions.Select(session => session.Arena!.Driver.Match!.CountdownAtTick).Distinct().Count() == 1, "Every peer observes the same authoritative countdown deadline.");
                _countdownVerified = true;
            }

            if (_elapsed - _stageStarted > 20)
            {
                throw new InvalidOperationException($"Lobby stage {_stage} timed out: " + string.Join(" | ", _sessions.Select(session => $"{session.Lobby?.State?.Phase} {session.Lobby?.State?.Players.Count} {session.Lobby?.Failure}")));
            }

            AdvanceScenario();
        }
        catch (Exception exception)
        {
            _finished = true;
            GD.PrintErr(exception);
            foreach (DevelopmentSession session in _sessions)
            {
                session.Leave();
            }

            GetTree().Quit(1);
        }
    }

    /// <summary>Allows queued arena destruction to drain before checking native process teardown.</summary>
    public async void Finish()
    {
        for (int frame = 0; frame < 3; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        GetTree().Quit();
    }

    private static void Click(DevelopmentSession session, string text)
    {
        session._Process(0);
        Button button = session.FindChildren("*", "Button", true, false).Cast<Button>().First(candidate => candidate.Text == text && candidate.IsVisibleInTree());
        Require(!button.Disabled, $"UI action is enabled: {text}");
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private void PrepareFinishedFixture()
    {
        var world = _sessions[0].Arena!.Driver.Host!.World;
        var state = world.State;
        var match = state.Match!;
        ulong winner = _sessions[0].Lobby!.LocalPlayerId;
        ulong victim = state.Vehicles.First(vehicle => vehicle.VehicleId != winner).VehicleId;
        var final = new Core.Matches.MatchState(state.Tick, match.Revision + 1, match.KillTarget, Core.Matches.MatchPhase.Finished, null, winner,
            match.Players.Select(row => new Core.Matches.PlayerScore(row.Player, row.Player == winner ? match.KillTarget : 0, row.Player == victim ? match.KillTarget : 0, row.Player == winner ? 1 : 0, row.Player == victim ? (ulong)match.KillTarget : 0)));
        world.Restore(new Core.Simulation.SimulationState(state.Tick, state.LastInput, state.Vehicles, final));
    }

    private void VerifyFinishedHandoff()
    {
        var expected = _sessions[0].FinalResults!;
        foreach (var session in _sessions)
        {
            Require(session.Stage == ApplicationStage.Podium && session.Lobby!.State!.Phase == SessionPhase.Arena, "Application Flow owns Podium while retaining the Finished arena.");
            Require(session.FinalResults!.Standings.SequenceEqual(expected.Standings) && session.FinalResults.Tick == expected.Tick && session.FinalResults.Outcome == expected.Outcome, "All Application Flow handoffs expose identical Core results.");
            Require(!session.Arena!.Driver.AllowsParticipation, "Finished denies driving and item use.");
            _completedResults.Add(session.FinalResults);
            _retiredDrivers.Add(session.Arena.Driver);
        }
    }

    private void VerifyDisposedResults()
    {
        Require(_retiredDrivers.All(driver => driver.Host is null && driver.Match is null && driver.EntryContext is null && !driver.IsActive), "Return disposes every old match driver.");
        Require(_sessions.All(session => session.FinalResults is null), "Application Flow releases its result handoff on exit.");
        Require(_completedResults.All(result => result.Standings.Count == 8 && result.Standings[0].Wins == 1), "Detached handoffs survive native arena teardown.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void AdvanceScenario()
    {
        LobbyNetworkDriver? host = _sessions[0].Lobby;
        switch (_stage)
        {
            case 0 when AllRoster(8):
                Require(_sessions.All(session => session.Arena is null), "No arena exists before start.");
                Require(host!.State!.Players.Select(player => player.Name).Order().SequenceEqual(Enumerable.Range(0, 8).Select(index => $"Player {index}").Order()), "Names sanitize consistently.");
                Require(!host.Request(LobbyCommand.Start), "Unready host start is rejected.");
                Require(host.SelectMap(MatchMap.OldMap), "Host can select Old Map.");
                Require(!_sessions[1].Lobby!.SelectMap(MatchMap.NewMap), "Clients cannot select maps.");
                Click(_sessions[1], "Ready");
                Next("Eight production UIs joined; names and IDs match; unready start rejected.");
                break;
            case 1 when SameState() && host!.State!.Players.Count(player => player.Ready) == 1:
                Click(_sessions[1], "Unready");
                Next("Targeted ready replicated to all eight peers.");
                break;
            case 2 when SameState() && host!.State!.Players.All(player => !player.Ready):
                Require(_sessions.All(session => session.Lobby!.State!.Map == MatchMap.OldMap), "All clients observe Old Map.");
                Require(host.SelectMap(MatchMap.NewMap), "Host can select New Map.");
                foreach (DevelopmentSession session in _sessions)
                {
                    Click(session, "Ready");
                }

                Next("Targeted unready replicated to all peers.");
                break;
            case 3 when SameState() && host!.State!.CanStart:
                _rejected = host.RejectedPackets;
                _sessions[1].Lobby!.Request(LobbyCommand.Start);
                Next("Non-host start intent submitted over the real transport.");
                break;
            case 4 when host!.RejectedPackets > _rejected:
                Require(_sessions.All(session => session.Lobby!.State!.Phase == SessionPhase.Lobby), "Non-host cannot transition any peer.");
                Capture("lobby.png");
                Click(_sessions[0], "Start Match (host only)");
                _firstMatch = host.State!.Match;
                Next("Host rejected non-host start; one host UI start command issued.");
                break;
            case 5 when AllArena():
                Require(_sessions.All(session => session.Arena!.Driver.LocalVehicleId == session.Lobby!.LocalPlayerId), "Vehicle IDs preserve session IDs.");
                Require(_sessions.All(session => session.Lobby!.State!.Match == _firstMatch), "Every peer has the same arena generation.");
                if (!_sessions.All(session => session.Arena!.Driver.Match?.Phase == Core.Matches.MatchPhase.Active))
                {
                    break;
                }

                Require(_countdownVerified, "Observed the shared Countdown before authoritative Active.");
                Require(_sessions.All(session => session.Arena!.Driver.AllowsParticipation), "All synchronized players may participate in Active.");

                Capture("arena.png");
                foreach (var session in _sessions)
                {
                    OvalGameplayAssertions.Verify(session.Arena!);
                    RemoteVehicleTagChecks.Verify(session.Arena!, session.Lobby!);
                }

                RemoteVehicleTagChecks.VerifyBoundaries(_sessions[0].Arena!);
                PrepareFinishedFixture();
                _stage = 23;
                _stageStarted = _elapsed;
                break;
            case 23 when _sessions.All(session => session.FinalResults is not null):
                VerifyFinishedHandoff();
                _sessions[0].Lobby!.Request(LobbyCommand.Return);
                _stage = 5;
                Next("All eight peers entered Podium with retained Finished results; host explicitly returned.");
                break;
            case 6 when AllRoster(8) && _sessions.All(session => session.Arena is null && session.Lobby!.State!.Phase == SessionPhase.Lobby):
                VerifyDisposedResults();
                Require(host!.State!.Players.All(player => !player.Ready), "Return clears all ready state.");
                Require(host.SelectMap(MatchMap.OldMap), "Select Old Map for the second match.");
                _departedId = _sessions[7].Lobby!.LocalPlayerId;
                Click(_sessions[7], "Ready");
                Next("Return preserved identities and cleared readiness on every peer.");
                break;
            case 7 when SameState() && host!.State!.Players.Single(player => player.Id == _departedId).Ready:
                _sessions[7].Leave();
                Next("Ready client left the lobby.");
                break;
            case 8 when _sessions.Take(7).All(session => session.Lobby!.State!.Players.Count == 7):
                Require(_sessions.Take(7).All(session => session.Lobby!.State!.Players.All(player => player.Id != _departedId && !player.Ready)), "Removal clears identity and readiness everywhere.");
                OpenThroughUi(_sessions[7], false, "Replacement");
                Next("Disconnect removal replicated to all remaining peers; rejoining.");
                break;
            case 9 when AllRoster(8):
                Require(_sessions[7].Lobby!.LocalPlayerId > _departedId, "Rejoin receives a fresh stable identity.");
                foreach (DevelopmentSession session in _sessions)
                {
                    Click(session, "Ready");
                }

                Next("Replacement joined with fresh identity; readying second match.");
                break;
            case 10 when SameState() && host!.State!.CanStart:
                Click(_sessions[0], "Start Match (host only)");
                Next("Second host start issued on the retained session connection.");
                break;
            case 11 when AllArena():
                Require(_sessions.All(session => session.FinalResults is null), "A fresh match has no previous final results.");
                if (!_sessions.All(session => session.Arena!.Driver.Match?.Phase == Core.Matches.MatchPhase.Active))
                {
                    break;
                }

                foreach (var session in _sessions)
                {
                    RemoteVehicleTagChecks.Verify(session.Arena!, session.Lobby!);
                }

                Require(_sessions.All(session => session.Arena!.Map is Arenas.CombatArena && session.Arena.Driver.EntryReady), "All eight players load and synchronize Old Map.");
                Require(host!.State!.Match > _firstMatch, "Second match advances vehicle generation.");
                Require(_sessions.All(session => session.Arena!.Driver.Match!.Winner is null && session.Arena.Driver.Match.Players.All(row => row.Kills == 0 && row.Deaths == 0 && row.Wins == 0 && row.ProcessedLife == 0)), "Second synchronized match starts with fresh mode state.");
                PrepareFinishedFixture();
                _stage = 24;
                _stageStarted = _elapsed;
                break;
            case 24 when _sessions.All(session => session.FinalResults is not null):
                VerifyFinishedHandoff();
                _departedId = _sessions[7].Lobby!.LocalPlayerId;
                _sessions[7].Leave();
                _stage = 11;
                Next("Second Finished handoff succeeded; client departed during results.");
                break;
            case 12 when _sessions.Take(7).All(session => session.Arena?.Driver.Latest?.Vehicles.Count == 8 && session.Lobby!.State!.Players.Any(player => player.Id == _departedId && !player.Connected)):
                foreach (var session in _sessions.Take(7))
                {
                    RemoteVehicleTagChecks.Verify(session.Arena!, session.Lobby!);
                    Require(session.Arena!.Bodies.ContainsKey(_departedId), "Disconnected vehicle remains retained during the match.");
                }

                Require(host!.Request(LobbyCommand.Return), "Return ends the match and its reservations.");
                Next("Arena departure retained the vehicle; host ended the match.");
                break;
            case 13 when _sessions.Take(7).All(session => session.Arena is null && session.Lobby!.State!.Players.All(player => player.Id != _departedId)):
                VerifyDisposedResults();
                Require(host!.SelectMap(MatchMap.NewMap), "Return to New Map for active admission.");
                foreach (var session in _sessions.Take(7))
                {
                    Click(session, "Ready");
                }

                Next("Return released the eighth reservation; readying seven players for active admission.");
                break;
            case 14 when host!.State!.CanStart:
                Click(_sessions[0], "Start Match (host only)");
                Next("Seven-player match started before fresh client joins.");
                break;
            case 15 when _sessions.Take(7).All(session => session.Arena?.Driver.Match?.Phase == Core.Matches.MatchPhase.Active):
                var scoringWorld = _sessions[0].Arena!.Driver.Host!.World;
                var scoringFrame = new Core.Input.InputFrame(scoringWorld.State.Tick + 1, 0, 0, 0, 0, 0, 0);
                ulong collisionVictim = scoringWorld.State.Vehicles.First(vehicle => vehicle.VehicleId != 1).VehicleId;
                scoringWorld.Step(scoringFrame, scoringWorld.State.Vehicles.Select(vehicle => new Core.Vehicles.VehicleStepRequest(vehicle.VehicleId, scoringFrame,
                    new Core.Vehicles.VehicleObservation(vehicle.ObservedPhysics, System.Numerics.Vector3.UnitY),
                    vehicle.VehicleId == collisionVictim ? [new Core.Vehicles.VehicleEffectRequest(new Core.Vehicles.DamageEffect(12.5f, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero), new Core.Vehicles.DamageContext("collision", 1, "join fixture"))] : [])).ToArray());
                Require(scoringWorld.State.Match!.Players.Single(player => player.Player == 1).CircusScore == 12.5, "Applied nonlethal damage banks points before fresh admission.");
                _firstMatch = host!.State!.Match;
                OpenThroughUi(_sessions[7], false, "Active newcomer");
                Next("Fresh eighth client joining after authoritative gameplay is active.");
                break;
            case 16 when AllArena() && SameState() && _sessions[7].Arena!.Driver.IsActive:
                Require(host!.State!.Match == _firstMatch, "Fresh admission does not restart arena generation.");
                Require(_sessions[7].Lobby!.LocalPlayerId > _departedId, "Active admission allocates a new identity.");
                Require(_sessions.All(session => session.Arena!.Driver.Match!.Phase == Core.Matches.MatchPhase.Active), "Match remains active on every peer.");
                var expectedScores = _sessions[0].Arena!.Driver.Host!.World.State.Match!.Players;
                Require(_sessions.All(session => session.Arena!.Driver.Match!.Players.SequenceEqual(expectedScores)), "Fresh native bootstrap and existing peers retain identical Circus scores and damage watermarks.");
                Require(expectedScores.Single(player => player.Player == 1).CircusScore == 12.5 && expectedScores.Single(player => player.Player == _sessions[7].Lobby!.LocalPlayerId).CircusScore == 0, "Existing banked points survive admission and the newcomer starts at zero.");
                Require(_sessions.All(session => session.Arena!.Driver.Latest!.Vehicles.Select(vehicle => vehicle.State.VehicleId).Distinct().Count() == 8), "Every peer observes exactly eight unique vehicles.");
                Require(_sessions[7].Arena!.Driver.ItemState!.Spawns.Count == 20, "Fresh bootstrap preserves the oval's twenty-marker pickup layout.");
                Capture("active-join.png");
                _departedId = _sessions[7].Lobby!.LocalPlayerId;
                _sessions[7].Leave();
                Next("Fresh client bootstrapped into active native gameplay exactly once; testing retained-slot capacity.");
                break;
            case 17 when host!.State!.Players.Any(player => player.Id == _departedId && !player.Connected) && _sessions[7].Lobby is null && _sessions[7].Arena is null:
                OpenThroughUi(_sessions[7], false, "Overflow");
                Next("Attempted fresh admission with seven connected plus one retained participant.");
                break;
            case 18 when _sessions[7].Lobby is null && _sessions[7].Arena is null:
                Require(host!.State!.Players.Count == 8, "Rejected new player cannot consume a ninth slot.");
                Require(_sessions[0].Arena!.Driver.Latest!.Vehicles.Count == 8, "Retained vehicle survives rejected admission.");
                _sessions[0].Leave();
                Next("Authoritative retained-slot rejection preserved all eight vehicles; host closed the session.");
                break;
            case 19 when _sessions.All(session => session.Lobby is null && session.Arena is null):
                _finished = true;
                _evidence.Add("Host loss returned all clients to Host/Join and removed every arena.");
                System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
                GD.Print("Lobby integration passed: " + string.Join("\n", _evidence));
                CallDeferred(MethodName.Finish);
                break;
            case 20 when host!.State!.Players.Count == 7 && _sessions[7].Lobby is null:
                Require(_sessions[0].Arena!.Driver.Host!.World.State.Vehicles.Count == 7, "Interrupted bootstrap leaves no vehicle.");
                Require(_sessions[0].Arena!.Driver.Host!.World.State.Match!.Players.Count == 7, "Interrupted bootstrap leaves no score row.");
                OpenThroughUi(_sessions[7], false, "Active newcomer retry");
                _stage = 16;
                _stageStarted = _elapsed;
                _evidence.Add("Interrupted bootstrap released its slot without a vehicle or score row; retrying fresh admission.");
                break;
        }
    }

    private bool AllRoster(int count) => _sessions.All(session => session.Lobby?.State?.Players.Count == count) && SameState();

    private bool SameState() => _sessions.All(session => session.Lobby?.State is LobbySnapshot state && _sessions[0].Lobby?.State is LobbySnapshot host && state.Map == host.Map && state.Revision == host.Revision && state.Players.SequenceEqual(host.Players));

    private bool AllArena() => _sessions.All(session => session.Arena?.Driver.Latest?.Vehicles.Count == 8 && session.Arena.Driver.LocalState is not null && session.Arena.Driver.EntryReady);

    private void Next(string evidence)
    {
        _evidence.Add(evidence);
        GD.Print($"Lobby stage {_stage}: {evidence}");
        _stage++;
        _stageStarted = _elapsed;
    }

    private void OpenThroughUi(DevelopmentSession session, bool host, string name)
    {
        session.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>().Single(button => button.Text == "Developer fallback: Direct-IP / LAN").ButtonPressed = true;
        session.FindChildren("PlayerName", "LineEdit", true, false).Cast<LineEdit>().Single().Text = name;
        session.FindChildren("DirectAddress", "LineEdit", true, false).Cast<LineEdit>().Single().Text = _endpoint;
        Click(session, host ? "Host Game" : "Join Game by address");
    }

    private void Capture(string filename)
    {
        if (DisplayServer.GetName() != "headless")
        {
            _hostView.GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, filename));
        }
    }

}
