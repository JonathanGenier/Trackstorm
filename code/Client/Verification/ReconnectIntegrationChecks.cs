using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

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
    private int _stage;
    private int _resyncs;
    private ulong _player;
    private NetworkVehicleBody? _originalBody;
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
        _host = new LobbyNetworkDriver(_gateways[0], 900, 0, "Host", _ => true, identity: _ => "native-test-user", graceTicks: 300);
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
                GD.Print("Reconnect integration passed: lobby identity/Ready reset, three arena resyncs, native body reuse, prediction/interpolation reset, held-item/spawn/match continuity, and grace expiry over real UDP. EOS identity is a test seam.");
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
        if (_stage == 0 && _client.State?.Players.Count == 2)
        {
            _player = _client.LocalPlayerId;
            _client.Request(LobbyCommand.Ready, true);
            _host.Pump(0);
            Drop();
            _stage = 1;
        }
        else if (_stage == 1 && _client.Generation == 2 && !_client.Reconnecting)
        {
            Require(_client.LocalPlayerId == _player && !_client.State!.Players.Single(p => p.Id == _player).Ready, "Lobby rebind preserves identity and resets Ready.");
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
            };
            _stage = 4;
        }
        else if (_stage == 4 && _arenas[1].Driver.Prediction is not null && _arenas[1].Driver.Match?.Phase == Trackstorm.Core.Matches.MatchPhase.Active)
        {
            _originalBody = _arenas[1].Bodies[_player];
            _arenas[0].Driver.Host!.Items.Grant(_arenas[0].Driver.Host!.World, _player, HeldItem.Wrench);
            Drop();
            _stage = 5;
        }
        else if (_stage == 5 && !_client.Reconnecting && _resyncs > 0 && _arenas[1].Driver.IsActive && _client.Latency.Get(_client.State!, _player) is not null)
        {
            Require(_retiredLatency is not null && !_client.Latency.Accept(_retiredLatency, _client.State!), "Retired connection diagnostics cannot replace the rebound player's RTT.");
            var standings = Hud.MatchStandingsView.From(_client.State, _arenas[1].Driver.Match, _player, Core.Input.InputButtons.Leaderboard, id => _client.Latency.Get(_client.State!, id));
            Require(standings.Rows.Count == 2 && standings.Rows.Single(row => row.Local).PlayerId == _player && standings.Rows.Single(row => row.Local).Ping != "--", "Resumed standings retain identity, rank and fresh transport-neutral ping.");
            Require(_arenas[0].Bodies.Count == 2 && _arenas[1].Bodies.Count == 2, "Exactly one vehicle per player remains.");
            Require(_arenas[1].Driver.LocalItem?.Item == HeldItem.Wrench, "Held item survives grace.");
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
                _views[1].GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, "resumed-arena.png"));
            }

            _client.Reconnect = () => throw new InvalidOperationException("Simulated unavailable session");
            Drop();
            _stage = 7;
        }
        else if (_stage == 7 && _host.State!.Players.Count == 1 && _arenas[0].Driver.Host!.World.State.Vehicles.Count == 1 && _client.Failure.Length > 0)
        {
            Require(_client.ResumeStatus == "Grace expired", "Retry loop terminates at the configured deadline.");
            Cleanup();
            _finished = true;
        }
    }

    private void Drop()
    {
        _retiredLatency = _host.Latency.Sample(_host.State!, _host.Authority!.Peers, _gateways[0]);
        _gateways[0].Disconnect(_host.Authority!.Peers.Keys.Single());
        _gateways[1].Disconnect(_client.ServerPeer);
        _host.Pump(0);
        _client.Pump(0);
        Require(_client.Latency.Get(_client.State!, _player) is null && _host.Latency.Get(_host.State!, _player) is null, "Disconnect immediately clears both sides' stale ping.");
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
