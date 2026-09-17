using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
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
    private int _stage;
    private int _resyncs;
    private ulong _player;
    private NetworkVehicleBody? _originalBody;
    private RemoteVehicleTag? _originalTag;
    private byte[]? _retiredLatency;
    private bool _finished;
    private int _cleanup;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
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
            Reconnect = () => _gateways[1].Connect(TransportEndpoint.DirectIp(_endpoint)),
        };
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
        {
            if (++_cleanup == 4)
            {
                GD.Print("Reconnect integration passed: immediate lobby removal/fresh admission, three arena resyncs, native body reuse, prediction/interpolation reset, held-item/spawn/match continuity, match-long reservation and Return cleanup over real UDP. EOS identity is a test seam.");
                GetTree().Quit();
            }

            return;
        }

        try
        {
            _elapsed += delta;
            Require(_elapsed < 35, $"Reconnect stage {_stage} timed out: {_client.Failure}");
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
                var arena = new NetworkVehicleArena();
                arena.Initialize(_gateways[i], i == 0 ? _host.State!.Match : 0, i == 0 ? 0 : _client.ServerPeer, i == 0 ? _host : _client);
                _views[i].AddChild(arena);
                _arenas.Add(arena);
            }

            _arenas[1].Driver.Resynchronized += world =>
            {
                _resyncs++;
                Require(_arenas[1].Driver.Inputs!.Pending.Count == 0, "Old pending input was discarded before prediction.");
                Require(_arenas[1].Driver.History!.Snapshots.Count == 1, "Interpolation data contains only the fresh boundary.");
                Require(_arenas[1].Bodies[_player] == _originalBody, "The native vehicle is reused, never duplicated.");
                Require(_arenas[1].LocalState!.Damage == world.Vehicles.Single(v => v.State.VehicleId == _player).State.Damage, "Current HP is restored.");
                Require(_arenas[1].Driver.Configuration == _arenas[0].Driver.Configuration, "Current host tuning and revision are restored before prediction.");
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
            _originalBody = _arenas[1].Bodies[_player];
            Require(_arenas[0].Driver.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 7, ["damage.max_hp"] = 1500, ["items.missile_speed"] = 60, ["spawns.cooldown_ticks"] = 90 }, out _), "Live host configuration commits before interruption.");
            _arenas[0].Driver.Host!.Items.Grant(_arenas[0].Driver.Host!.World, _player, HeldItem.Wrench);
            Drop();
            _stage = 5;
        }
        else if (_stage == 5 && !_client.Reconnecting && _resyncs > 0 && _arenas[1].Driver.IsActive && _client.Latency.Get(_client.State!, _player) is not null)
        {
            Require(_retiredLatency is not null && !_client.Latency.Accept(_retiredLatency, _client.State!), "Retired connection diagnostics cannot replace the rebound player's RTT.");
            var standings = Hud.MatchStandingsView.From(_client.State, _arenas[1].Driver.Match, _player, Core.Input.InputButtons.Leaderboard, id => _client.Latency.Get(_client.State!, id));
            Require(standings.Rows.Count == 2 && standings.Rows.Single(row => row.Local).PlayerId == _player && standings.Rows.Single(row => row.Local).Ping != "--", "Resumed standings retain identity, rank and fresh transport-neutral ping.");
            Require(Settings.DiagnosticsView.Create(new(), null, TransportDiagnostics.Capture(_gateways[1], _client)).Ping == "Ping  " + standings.Rows.Single(row => row.Local).Ping, "Resumed HUD and leaderboard use exactly the same published ping.");
            Require(_arenas[0].Bodies.Count == 2 && _arenas[1].Bodies.Count == 2, "Exactly one vehicle per player remains.");
            RemoteVehicleTagChecks.Verify(_arenas[0], _host);
            RemoteVehicleTagChecks.Verify(_arenas[1], _client);
            Require(_arenas[0].Bodies[_player].GetNode<RemoteVehicleTag>("PlayerTag") == _originalTag, "Three reconnects retain exactly the same remote tag.");
            Require(_arenas[1].Driver.LocalItem?.Item == HeldItem.Wrench, "Held item survives match-long retention.");
            Require(_arenas[1].Driver.ItemState?.Spawns.Count == 8 && _arenas[1].Driver.Match?.Players.Count == 2, "Pickup and match state arrive in the checkpoint.");
            if (_resyncs < 3)
            {
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
            Require(_host.Authority!.Return(0), "Return ends the retained match.");
            Require(_host.State!.Players.Count == 1 && _host.Authority.FindPlayer("native-test-user") == 0, "Return clears disconnected player and subject.");
            Cleanup();
            _finished = true;
        }
    }

    private void Drop()
    {
        _retiredLatency = _host.Latency.Sample(_host.State!, _host.Authority!.Peers, _gateways[0]);
        _gateways[0].Disconnect(_host.Authority!.Peers.Keys.Single());
        _gateways[1].Disconnect(_client.ServerPeer);
        var disconnected = TransportDiagnostics.Capture(_gateways[1], _client);
        Require(disconnected.State == ConnectionDiagnosticState.Disconnected && disconnected.Statistics.PingMilliseconds is null, "Disconnect clears displayed latency immediately.");
        _host.Pump(0);
        _client.Pump(0);
        _host.Pump(181);
        _client.Pump(181);
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
            Reconnect = () => _gateways[1].Connect(TransportEndpoint.DirectIp(_endpoint)),
        };
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
