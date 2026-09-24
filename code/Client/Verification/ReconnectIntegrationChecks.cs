using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Hud;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Real UDP and native Godot worlds exercising the production resume boundary with a trusted test identity.</summary>
public sealed partial class ReconnectIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<SubViewport> _views = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private LobbyNetworkDriver _host = null!;
    private LobbyNetworkDriver _client = null!;
    private string _endpoint = string.Empty;
    private string _output = string.Empty;
    private double _elapsed;
    private double _captureAt;
    private string _categoryHistory = string.Empty;
    private int _stage;
    private int _resyncs;
    private OilPatch? _oil;
    private ulong _mine;
    private ulong _player;
    private NetworkVehicleBody? _originalBody;
    private RemoteVehicleTag? _originalTag;
    private byte[]? _retiredLatency;
    private bool _finished;
    private int _cleanup;
    private MatchStandings _board = null!;
    private double _resumeAt;
    private PlayerScore? _retainedScore;
    private int _retainedRank;
    private Input.PlayerInput _cameraInput = null!;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        _cameraInput = new Input.PlayerInput { GameplayAvailable = () => true };
        AddChild(_cameraInput);
        _cameraInput.SetPhysicsProcess(false);
        _output = ProjectSettings.GlobalizePath("res://.godot/reconnect-checks");
        System.IO.Directory.CreateDirectory(_output);
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
        reservation.Close();
        for (int i = 0; i < 2; i++)
        {
            var gateway = new GameNetworkingSocketsTransport();
            _gateways.Add(gateway);
            var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = i == 1 ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled };
            _views.Add(view);
            if (i == 1)
            {
                var display = new SubViewportContainer();
                AddChild(display);
                display.AddChild(view);
            }
            else
            {
                AddChild(view);
            }
        }

        _gateways[0].Listen(TransportEndpoint.DirectIp(_endpoint));
        ulong peer = _gateways[1].Connect(TransportEndpoint.DirectIp(_endpoint));
        _host = new LobbyNetworkDriver(_gateways[0], 900, 0, "Host", _ => true, identity: _ => "native-test-user");
        _client = new LobbyNetworkDriver(_gateways[1], 0, peer, "Client", expectedSession: 900)
        {
            Reconnect = Reconnect,
        };
        _board = new MatchStandings { View = Standings };
        _views[1].AddChild(_board);
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
        {
            // Allow the native audio mixer to release stopped arena voices before engine shutdown.
            if (++_cleanup == 30)
            {
                GD.Print("Reconnect integration passed: immediate lobby removal/fresh admission, three arena resyncs including 125 seconds offline, native body reuse, prediction/interpolation reset, held-item/spawn/match continuity, dimmed retained standings, final results and new-generation reset over real UDP. EOS identity and initial scores are test seams.");
                GetTree().Quit();
            }

            return;
        }

        try
        {
            _elapsed += delta;
            Require(_elapsed < 170, $"Reconnect stage {_stage} timed out: {_client.Failure}");
            if (_arenas.Count == 0)
            {
                _host.Pump(delta);
                _client.Pump(delta);
            }
            else
            {
                foreach (var arena in _arenas)
                {
                    arena.Advance(default);
                }
            }

            AdvanceScenario();
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);

            Cleanup();
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition)
        {
            throw new InvalidOperationException(detail);
        }
    }

    private void AdvanceScenario()
    {
        if (_stage == 0 && _client.State?.Players.Count == 2 && TransportDiagnostics.Capture(_gateways[1], _client).Statistics.PingMilliseconds is >= 0)
        {
            _player = _client.LocalPlayerId;
            GD.Print("Diagnostics: Direct-IP current Ping observed through neutral projection.");
            _client.Request(LobbyCommand.Ready, true);
            _host.Pump(0);
            DropLobbyAndJoinFresh();
            _stage = 1;
        }
        else if (_stage == 1 && _client.State?.Players.Count == 2 && !_client.Reconnecting)
        {
            Require(_client.LocalPlayerId != _player && _client.Generation == 1 && !_client.State.Players.Any(p => p.Id == _player), "Lobby return is a fresh PlayerId without a retained reservation.");
            _player = _client.LocalPlayerId;
            _host.Request(LobbyCommand.Ready, true);
            _client.Request(LobbyCommand.Ready, true);
            _stage = 2;
        }
        else if (_stage == 2 && _host.State!.CanStart)
        {
            Require(_host.Request(LobbyCommand.Start), "Host starts after reconnect confirmation.");
            _stage = 3;
        }
        else if (_stage == 3 && _client.State?.Phase == SessionPhase.Arena)
        {
            for (int i = 0; i < 2; i++)
            {
                var arena = new NetworkVehicleArena { ApplicationEntry = true };
                arena.Initialize(_gateways[i], i == 0 ? _host.State!.Match : 0, i == 0 ? 0 : _client.ServerPeer, i == 0 ? _host : _client);
                _views[i].AddChild(arena);
                // Explicit fixture for the existing lethal wall-impact scenario, outside the map scene.
                var impactWall = new StaticBody3D { Name = "LethalImpactFixture", Position = new Vector3(-60, 1, -35) };
                impactWall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1, 4, 8) } });
                arena.AddChild(impactWall);
                _arenas.Add(arena);
            }

            _arenas[1].Driver.Resynchronized += world =>
            {
                if (_originalBody is null)
                {
                    Require(world.Tick == 0 && !_arenas[1].Driver.EntryReady, "Initial checkpoint precedes gameplay release.");
                    return;
                }

                _resyncs++;
                if (_resyncs <= 3)
                {
                    Require(_arenas[1].Driver.ItemState!.Mines.Single().Id == _mine, "Mine identity restored exactly once.");
                    Require(_arenas[1].Driver.ItemState!.Mines.SequenceEqual(_arenas[0].Driver.Host!.Items.Mines), "Complete mine pose/velocity/seating continuation restored.");
                    GD.Print($"Proxy Mine reconnect {_resyncs}: complete state restored without impact replay.");
                }
                var nitro = world.Vehicles.Single(v => v.State.VehicleId == _player).State.Movement.Nitro;
                Require(_arenas[1].LocalState!.Movement.Nitro == nitro, "Nitro restored exactly before prediction.");
                if (_resyncs <= 3)
                {
                    Require(_resyncs == 1 ? !nitro.Active : nitro is { RemainingTicks: > 0 and < 3600 }, "Long offline expiry and active short reconnect preserve Nitro duration.");
                    GD.Print($"Nitro reconnect {_resyncs}: remaining {nitro.RemainingTicks}, exact checkpoint state.");
                }
                var camera = _arenas[1].GetNode<Vehicles.VehicleChaseCamera>("ChaseCamera");
                var cameraPose = _arenas[1].Bodies[_player].VisualTransform;
                camera.Follow(cameraPose, _arenas[1].LocalState!, 0, _arenas[1].Bodies[_player].GetRid());
                float expectedYaw = MathF.Atan2(cameraPose.Basis.Z.X, cameraPose.Basis.Z.Z);
                float actualYaw = MathF.Atan2(camera.GlobalBasis.Z.X, camera.GlobalBasis.Z.Z);
                Require(Math.Abs(Mathf.AngleDifference(expectedYaw, actualYaw)) < 0.0001f, "Resume clears held free-look on the reused displayed vehicle.");
                using var releaseLook = new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false };
                Godot.Input.ParseInputEvent(releaseLook);
                Godot.Input.FlushBufferedEvents();
                Require(_arenas[1].Driver.Inputs!.Pending.Count == 0, "Old pending input was discarded before prediction.");
                Require(_arenas[1].Driver.History!.Snapshots.Count == 1, "Interpolation data contains only the fresh boundary.");
                Require(_arenas[1].Bodies[_player] == _originalBody, "The native vehicle is reused, never duplicated.");
                Require(_arenas[1].LocalState!.Damage == world.Vehicles.Single(v => v.State.VehicleId == _player).State.Damage, "Current HP is restored.");
                Require(_arenas[1].Driver.Configuration == _arenas[0].Driver.Configuration, "Current host tuning and revision are restored before prediction.");
                Require(_arenas[1].Driver.Match!.Players.SequenceEqual(_arenas[0].Driver.Host!.World.State.Match!.Players), "Resume preserves complete Circus totals, streaks, multiplier inputs and damage watermarks.");
            };
            _stage = 40;
        }
        else if (_stage == 40 && _arenas[1].Driver.Match?.Phase == Trackstorm.Core.Matches.MatchPhase.Active)
        {
            _originalTag = _arenas[0].Bodies[_player].GetNode<RemoteVehicleTag>("PlayerTag");
            RestoreHealthFixture(false);
            _stage = 41;
        }
        else if (_stage == 41 && _arenas.All(arena => arena.Driver.Latest!.Vehicles.All(vehicle => vehicle.State.Damage.CurrentHP == 400)))
        {
            VerifyTags();
            foreach (var state in _arenas[0].Driver.Host!.World.State.Vehicles)
            {
                Require(_arenas[0].Driver.Host!.Items.Grant(_arenas[0].Driver.Host!.World, state.VehicleId, HeldItem.Wrench), "Grant production healing item.");
            }

            _stage = 42;
        }
        else if (_stage == 42 && _arenas.All(arena => arena.Driver.LocalItem?.Item == HeldItem.Wrench))
        {
            foreach (var arena in _arenas)
            {
                Require(arena.Driver.RequestItemUse(), "Both peers request actual Wrench healing.");
            }

            _stage = 43;
        }
        else if (_stage == 43 && _arenas.All(arena => arena.Driver.Latest!.Vehicles.All(vehicle => vehicle.State.Damage.CurrentHP > 400)))
        {
            VerifyTags();
            RestoreHealthFixture(true);
            _stage = 44;
        }
        else if (_stage == 44 && _arenas.All(arena => !arena.Driver.Latest!.Vehicles.Single(vehicle => vehicle.State.VehicleId == _player).State.CanInteract))
        {
            VerifyTags();
            Require(!_originalTag!.Visible, "Actual collision death hides remote tag.");
            _stage = 45;
        }
        else if (_stage == 45 && _arenas.All(arena => arena.Driver.Latest!.Vehicles.Single(vehicle => vehicle.State.VehicleId == _player).State.CanInteract))
        {
            VerifyTags();
            Require(_originalTag!.Visible && _arenas.All(arena => arena.Driver.Latest!.Vehicles.Single(vehicle => vehicle.State.VehicleId == _player).State.Damage.CurrentHP == 1000), "Actual respawn restores full health and original tag.");
            GD.Print("Remote tags passed over two-peer UDP: damaged snapshot, actual Wrench healing, collision death, timed respawn, matching host/client names and no local tags.");
            _stage = 4;
        }
        else if (_stage == 4 && _arenas[1].Driver.Prediction is not null && _arenas[1].Driver.Match?.Phase == Trackstorm.Core.Matches.MatchPhase.Active)
        {
            _categoryHistory = CategoryBalanceRecoveryFixture.Seed(_arenas[0]);
            GD.Print("Category pickup history before reconnect: " + _categoryHistory);
            _oil = OilRecoveryFixture.Seed(_arenas[0]);
            _mine = ProxyMineRecoveryFixture.Seed(_arenas[0], _arenas);
            Require(_arenas[0].Driver.TryConfigure(new Dictionary<string, double> { ["match.nitro_points_per_second"] = 0 }, out _), "Disable duration score only in the retention fixture to preserve its fixed rank assertions.");
            NitroRecoveryFixture.Seed(_arenas[0], _player);
            _originalBody = _arenas[1].Bodies[_player];
            SetScores(false);
            _retainedScore = _arenas[0].Driver.Host!.World.State.Match!.Players.Single(score => score.Player == _player);
            _retainedRank = Standings()!.Rows.Single(row => row.PlayerId == _player).Rank;
            _resumeAt = _elapsed + 125;
            Require(_arenas[0].Driver.TryConfigure(new Dictionary<string, double> { ["environment.preset"] = (int)Core.Development.EnvironmentPreset.EmberSky, ["vehicle.acceleration"] = 7, ["damage.max_hp"] = 1500, ["items.missile_speed"] = 60, ["spawns.cooldown_ticks"] = 90 }, out _), "Live host configuration commits before interruption.");
            _arenas[0].Driver.Host!.Items.Grant(_arenas[0].Driver.Host!.World, _player, HeldItem.Oil);
            _arenas[0].Driver.Host!.Items.Grant(_arenas[0].Driver.Host!.World, _player, HeldItem.Wrench);
            Require(_arenas[0].Driver.Host!.Items.Switch(_arenas[0].Driver.Host!.World, _player, _arenas[0].Driver.Host!.World.GetVehicle(_player).LifeId, 1), "Select second slot before reconnect.");
            Drop();
            _stage = 5;
        }
        else if (_stage == 5 && !_client.Reconnecting && _resyncs > 0 && _arenas[1].Driver.IsActive && _client.Latency.Get(_client.State!, _player) is not null)
        {
            Require(_retiredLatency is not null && !_client.Latency.Accept(_retiredLatency, _client.State!), "Retired connection diagnostics cannot replace the rebound player's RTT.");
            var standings = Hud.MatchStandingsView.From(_client.State, _arenas[1].Driver.Match, _player, Core.Input.InputButtons.Leaderboard, id => _client.Latency.Get(_client.State!, id));
            Require(standings.Rows.Count == 2 && standings.Rows.Single(row => row.Local).PlayerId == _player && standings.Rows.Single(row => row.Local).Ping != "--", "Resumed standings retain identity, rank and fresh transport-neutral ping.");
            Require(standings.Rows.Single(row => row.Local) is { Connected: true, Kills: 2, Deaths: 1 } && standings.Rows.Single(row => row.Local).Rank == _retainedRank, "Late resume reactivates exactly one row with the same statistics and rank.");
            VerifyRow(true);
            Require(Settings.DiagnosticsView.Create(new(), null, TransportDiagnostics.Capture(_gateways[1], _client)).Ping == "Ping  " + standings.Rows.Single(row => row.Local).Ping, "Resumed HUD and leaderboard use exactly the same published ping.");
            Require(_arenas[0].Bodies.Count == 2 && _arenas[1].Bodies.Count == 2, "Exactly one vehicle per player remains.");
            RemoteVehicleTagChecks.Verify(_arenas[0], _host);
            RemoteVehicleTagChecks.Verify(_arenas[1], _client);
            Require(_arenas[0].Bodies[_player].GetNode<RemoteVehicleTag>("PlayerTag") == _originalTag, "Three reconnects retain exactly the same remote tag.");
            Require(_arenas[1].Driver.ItemState!.Patches.Count == 1 && _arenas[1].Driver.ItemState!.Patches.Single() == _oil, "Complete persistent Oil patch survives each native reconnect without duplication.");
            Require(_arenas[1].Driver.LocalItem?.Item == HeldItem.Oil, "Oil identity survives match-long retention and three reconnects.");
            Require(_arenas[1].Driver.LocalItem is { SecondItem: HeldItem.Wrench, ActiveSlot: 1, SelectionRevision: 1 }, "Second slot and selection survive reconnect exactly.");
            Require(_arenas[1].Driver.ItemState?.Spawns.Count == 27 && _arenas[1].Driver.Match?.Players.Count == 2, "Twenty-seven-marker map pickup layout and match state arrive in the checkpoint.");
            Require(CategoryBalanceRecoveryFixture.Signature(_arenas[1].Driver.ItemState!.Balances) == _categoryHistory, "Reconnect retains exact per-player credits, counts and last selection.");
            GD.Print("Category history verified after reconnect " + _resyncs);
            OvalGameplayAssertions.Verify(_arenas[1]);
            if (_resyncs < 3)
            {
                NitroRecoveryFixture.Seed(_arenas[0], _player);
                Drop();
            }
            else
            {
                _stage = 6;
            }
        }
        else if (_stage == 6)
        {
            Require(_arenas[1].Driver.Match?.Phase == Trackstorm.Core.Matches.MatchPhase.Active, "The active match continues through every resume.");
            if (DisplayServer.GetName() != "headless")
            {
                var arena = _arenas[1];
                arena.SetProcess(false);
                var body = arena.Bodies[1];
                var camera = arena.GetNode<Camera3D>("ChaseCamera");
                camera.GlobalPosition = body.VisualPosition + new Vector3(0, 4, 9);
                camera.LookAt(body.VisualPosition + Vector3.Up);
                body.GetNode<RemoteVehicleTag>("PlayerTag").Present("Host", arena.Driver.Latest!.Vehicles.Single(vehicle => vehicle.State.VehicleId == 1).State, body.VisualPosition, camera);
            }

            _captureAt = _elapsed;
            _stage = 60;
        }
        else if (_stage == 60 && _elapsed - _captureAt > 0.25)
        {
            if (DisplayServer.GetName() != "headless")
            {
                _views[1].GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, "resumed-arena.png"));
            }

            _arenas[1].SetProcess(true);
            _client.Reconnect = () => throw new InvalidOperationException("Simulated unavailable session");
            Drop();
            _host.Pump(181);
            _client.Pump(181);
            Require(_host.State!.Players.Count == 2 && _client.Failure.Length == 0, "Both drivers retain resume beyond thirty seconds and two minutes.");
            _stage = 7;
        }
        else if (_stage == 7)
        {
            Require(_arenas[0].Driver.Host!.World.State.Vehicles.Count == 2, "Disconnected vehicle remains in the active match.");
            SetScores(true);
            Require(Standings() is { Finished: true, Visible: true, WinnerName: "Host" }, "Finished retains the complete result and winner.");
            VerifyRow(false);
            _captureAt = _elapsed;
            _stage = 8;
        }
        else if (_stage == 8 && _elapsed - _captureAt > 0.25)
        {
            Capture("offline-final-results");
            Require(_host.Authority!.FindPlayer("native-test-user") == _player, "Finished does not release the reservation.");
            Require(_host.Authority!.Return(0), "Return ends the retained match.");
            Require(_host.State!.Players.Count == 1 && _host.Authority.FindPlayer("native-test-user") == 0, "Return clears disconnected player and subject.");
            ulong previous = _host.State.Match;
            _host.Authority.SetReady(0, true);
            Require(_host.Authority.Start(0), "Next match starts after leaving results.");
            var next = new HostVehicleSession(_host.State.Match);
            Require(next.Spawns?.Balances.Count is null or 0, "True new match clears category history.");
            Require(next.SessionId > previous && next.World.State.Match!.Players.Count == 1 && next.World.State.Match.Players.All(score => score.Kills == 0 && score.Deaths == 0), "New match generation starts fresh without retained offline rows or totals.");
            Require(_arenas[0].Driver.Host!.Items.Patches.Count == 0 && next.Items.Patches.Count == 0, "Finished and the next match contain no Oil hazards.");
            Require(next.Items.Mines.Count == 0 && _arenas[0].Driver.Host!.Items.Mines.Count == 0, "Finished and new match clear Proxy Mines.");
            Cleanup();
            _finished = true;
        }

        if (_stage == 5 && _elapsed < _resumeAt)
        {
            VerifyRow(false);
            if (_resumeAt - _elapsed < 1)
            {
                Capture("offline-active-standings");
            }
        }
    }

    private void Drop()
    {
        if (_arenas.Count > 1 && _arenas[1].LocalState is { } cameraState)
        {
            var camera = _arenas[1].GetNode<Vehicles.VehicleChaseCamera>("ChaseCamera");
            _arenas[1].CameraInput = _cameraInput.Adapter;
            _cameraInput.Adapter.Enabled = true;
            _cameraInput._Process(0);
            var pose = _arenas[1].Bodies[_player].VisualTransform;
            camera.Follow(pose, cameraState, 1f / 60, _arenas[1].Bodies[_player].GetRid());
            using var heldLook = new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true };
            Godot.Input.ParseInputEvent(heldLook);
            Godot.Input.FlushBufferedEvents();
            using var motion = new InputEventMouseMotion { ScreenRelative = new Vector2(350, 0) };
            _cameraInput.Adapter.ObserveCamera(motion);
            camera.Follow(pose, cameraState, 1f / 60, _arenas[1].Bodies[_player].GetRid());
            Require(Math.Abs(Mathf.AngleDifference(MathF.Atan2(pose.Basis.Z.X, pose.Basis.Z.Z), MathF.Atan2(camera.GlobalBasis.Z.X, camera.GlobalBasis.Z.Z))) > 0.5f, "Reconnect starts with an active held orbit.");
        }

        _retiredLatency = _host.Latency.Sample(_host.State!, _host.Authority!.Peers, _gateways[0]);
        _gateways[0].Disconnect(_host.Authority!.Peers.Keys.Single());
        _gateways[1].Disconnect(_client.ServerPeer);
        var disconnected = TransportDiagnostics.Capture(_gateways[1], _client);
        Require(disconnected.State == ConnectionDiagnosticState.Disconnected && disconnected.Statistics.PingMilliseconds is null, "Disconnect clears displayed latency immediately.");
        _host.Pump(0);
        _client.Pump(0);
        _host.Pump(181);
        _client.Pump(181);
        Require(_arenas[0].Driver.Host!.World.State.Match!.Players.Single(score => score.Player == _player) == _retainedScore, "Disconnect and elapsed reservation time do not erase authoritative statistics.");
        VerifyRow(false);
        if (_arenas.Count > 0)
        {
            RemoteVehicleTagChecks.Verify(_arenas[0], _host);
            Require(!_arenas[0].Bodies[_player].GetNode<RemoteVehicleTag>("PlayerTag").Visible, "Disconnected reservation hides its tag.");
        }

        var reconnecting = TransportDiagnostics.Capture(_gateways[1], _client);
        Require(reconnecting.State == ConnectionDiagnosticState.Reconnecting && reconnecting.Statistics.PingMilliseconds is null, "Reconnecting never presents old latency.");
        Require(_client.Latency.Get(_client.State!, _player) is null && _host.Latency.Get(_host.State!, _player) is null, "Disconnect immediately clears both sides' stale ping.");
    }

    private void DropLobbyAndJoinFresh()
    {
        _gateways[0].Disconnect(_host.Authority!.Peers.Keys.Single());
        _gateways[1].Disconnect(_client.ServerPeer);
        _host.Pump(0);
        _client.Pump(0);
        Require(_host.State!.Players.Count == 1 && _host.State.Players.All(player => player.Id != _player), "Lobby disconnect removes the player immediately.");
        Require(_client.Failure.Length > 0 && _client.ResumeStatus == "Lobby departure requires a fresh join", "Lobby disconnect exposes fresh-join recovery instead of reconnect grace.");
        ulong peer = _gateways[1].Connect(TransportEndpoint.DirectIp(_endpoint));
        _client = new LobbyNetworkDriver(_gateways[1], 0, peer, "Client", expectedSession: 900)
        {
            Reconnect = Reconnect,
        };
    }

    private ulong Reconnect() => _elapsed < _resumeAt ? throw new InvalidOperationException("Simulated interrupted route") : _gateways[1].Connect(TransportEndpoint.DirectIp(_endpoint));

    private MatchStandingsView? Standings() => _arenas.Count == 0 ? null : MatchStandingsView.From(_host.State, _arenas[0].Driver.Host!.World.State.Match, 1, InputButtons.Leaderboard, id => _host.Latency.Get(_host.State!, id));

    private void VerifyRow(bool connected)
    {
        _board.Refresh();
        StandingsRow row = _board.Displayed!.Rows.Single(row => row.PlayerId == _player);
        Require(row.Connected == connected && row.Kills == 2 && row.Deaths == 1, "The rendered participant preserves authoritative presence and totals.");
        Require(connected || row.Ping == "--", "Offline standings never show a live ping.");
        Label label = _board.FindChildren("*", "Label", true, false).OfType<Label>().Single(label => label.Text.StartsWith("Client", StringComparison.Ordinal));
        Require(Math.Abs(label.Modulate.A - (connected ? 1 : 0.55f)) < 0.001f, "The native row dims offline and restores on resume.");
    }

    private void SetScores(bool finished)
    {
        var world = _arenas[0].Driver.Host!.World;
        MatchState previous = world.State.Match!;
        var match = new MatchState(world.State.Tick, previous.Revision + 1, previous.KillTarget, finished ? MatchPhase.Finished : MatchPhase.Active, null, finished ? 1ul : null, previous.Players.Select(score => score with { Kills = score.Player == _player ? 2 : finished ? 5 : 1, Deaths = score.Player == _player ? 1 : finished ? 6 : 2, Wins = finished && score.Player == 1 ? 1 : 0, ProcessedLife = Math.Max(1, score.ProcessedLife), CircusScore = score.Player == _player ? 375.5 : 125.25, KillStreak = 1 }));
        // Scoring itself is exercised by the match harness; this fixture isolates retention and presentation.
        world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, world.State.Vehicles.Select(v => !finished ? v :
            new VehicleSnapshot(v.VehicleId, v.LifeId, v.Movement with { Nitro = default }, v.Damage, v.ObservedPhysics, v.Effects, v.Lifecycle, v.RespawnAtTick)), match));
    }

    private void Capture(string name)
    {
        if (DisplayServer.GetName() != "headless")
        {
            using Image image = _views[1].GetTexture().GetImage();
            Require(image.SavePng(System.IO.Path.Combine(_output, name + ".png")) == Error.Ok, "Saved rendered standings evidence.");
        }
    }

    private void VerifyTags()
    {
        RemoteVehicleTagChecks.Verify(_arenas[0], _host);
        RemoteVehicleTagChecks.Verify(_arenas[1], _client);
        Require(_arenas[0].Bodies[_player].GetNode<RemoteVehicleTag>("PlayerTag") == _originalTag, "Health and lifecycle updates reuse the original tag.");
    }

    private void RestoreHealthFixture(bool collision)
    {
        var world = _arenas[0].Driver.Host!.World;
        var states = world.State.Vehicles.Select(state =>
        {
            var pose = collision && state.VehicleId == _player
                ? new VehiclePhysicsState(new System.Numerics.Vector3(-57.5f, 0.6f, -35), System.Numerics.Quaternion.Identity, new System.Numerics.Vector3(-60, 0, 0), System.Numerics.Vector3.Zero)
                : world.Arena.Spawn((int)state.VehicleId - 1);
            return new VehicleSnapshot(state.VehicleId, state.LifeId, new VehicleState(world.State.Tick, pose, false, false, 0, 0), new VehicleDamageState(state.Damage.MaxHP, collision && state.VehicleId == _player ? 20 : 400, null, null), pose);
        });
        world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, states, world.State.Match));
    }

    private void Cleanup()
    {
        foreach (var arena in _arenas)
        {
            arena.QueueFree();
        }

        _arenas.Clear();
        foreach (var gateway in _gateways)
        {
            gateway.Dispose();
        }
    }
}
