using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Two or three real UDP peers and isolated Godot worlds; identity is an explicit local test seam.</summary>
public sealed partial class MigrationIntegrationChecks : Node
{
    private readonly GameNetworkingSocketsTransport[] _gateways = new GameNetworkingSocketsTransport[3];
    private readonly LobbyNetworkDriver?[] _drivers = new LobbyNetworkDriver?[3];
    private readonly NetworkVehicleArena?[] _arenas = new NetworkVehicleArena?[3];
    private readonly SubViewport[] _views = new SubViewport[3];
    private readonly Dictionary<ulong, string>[] _subjects = Enumerable.Range(0, 3).Select(_ => new Dictionary<ulong, string>()).ToArray();
    private readonly Queue<string>[] _connecting = Enumerable.Range(0, 3).Select(_ => new Queue<string>()).ToArray();
    private readonly string[] _endpoints = new string[3];
    private int _stage;
    private int _frames;
    private int _boundary;
    private int _players = 3;
    private bool _finished;
    private NetworkVehicleBody? _retainedBody;
    private Core.Development.GameplayConfigurationState? _configuration;
    private ulong _randomState;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        _players = OS.GetCmdlineUserArgs().Contains("--migration-players=2") ? 2 : 3;
        for (int i = 0; i < _players; i++)
        {
            int index = i;
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _endpoints[i] = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            _gateways[i] = new GameNetworkingSocketsTransport();
            _gateways[i].ConnectionChanged += change =>
            {
                if (change.State == TransportConnectionState.Connected && _connecting[index].TryDequeue(out string? subject))
                {
                    _subjects[index][change.RemotePeerId] = subject;
                }
            };
            _views[i] = new SubViewport { Size = new Vector2I(640, 360), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
            AddChild(_views[i]);
        }

        _gateways[0].Listen(TransportEndpoint.DirectIp(_endpoints[0]));
        CreateDriver(0, 0, true);
        Require(_drivers[0]!.Authority!.TryConfigure(0, new Dictionary<string, double> { ["vehicle.acceleration"] = 7 }, out _), "Original lobby host configures the session.");
        _configuration = _drivers[0]!.Authority!.Configuration;
        CreateDriver(1, Connect(1, 0), false);
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
        {
            if (++_frames > _boundary + 4)
            {
                GD.Print($"Migration integration passed: {_players} native UDP peers; intentional lobby host leave; former-host rebind; active native match authority loss; sequential epochs; retained vehicles; prediction reset; item/spawn/match restore. Identity is a trusted test seam, not real EOS.");
                GetTree().Quit();
            }

            return;
        }

        try
        {
            Require(++_frames < 3000, $"Migration stage {_stage} timed out: {string.Join("; ", _drivers.Select(driver => driver?.Failure))}");
            for (int i = 0; i < _players; i++)
            {
                if (_drivers[i] is not null)
                {
                    if (_arenas[i] is { } arena)
                    {
                        arena.Advance(default);
                    }
                    else
                    {
                        _drivers[i]!.Pump(delta);
                    }
                }
            }

            Scenario();
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            Cleanup();
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private ulong Connect(int client, int host)
    {
        _connecting[host].Enqueue("native-" + client);
        return _gateways[client].Connect(TransportEndpoint.DirectIp(_endpoints[host]));
    }

    private void CreateDriver(int index, ulong server, bool host, ulong player = 0, ulong epoch = 1)
    {
        var driver = new LobbyNetworkDriver(_gateways[index], host ? 900UL : 0, server, "Player" + index, _ => true, 900, peer => _subjects[index].GetValueOrDefault(peer), 180, epoch);
        driver.Reconnect = () => throw new InvalidOperationException("Host is unavailable in this controlled loss scenario.");
        driver.Migration = new SessionMigration(driver, _gateways[index], "native-" + index, peer => _subjects[index].GetValueOrDefault(peer), (subject, listen) =>
        {
            _gateways[index].Stop();
            _subjects[index].Clear();
            int target = int.Parse(subject.AsSpan(7), System.Globalization.CultureInfo.InvariantCulture);
            if (listen)
            {
                _gateways[index].Listen(TransportEndpoint.DirectIp(_endpoints[index]));
                return 0;
            }

            return Connect(index, target);
        });
        if (player != 0)
        {
            driver.BeginResume(player, 1);
        }

        _drivers[index] = driver;
    }

    private void Scenario()
    {
        if (_stage == 0 && _drivers[1]!.State?.Players.Count == 2)
        {
            if (_players == 3)
            {
                CreateDriver(2, Connect(2, 0), false);
            }

            _stage = 1;
        }
        else if (_stage == 1 && _drivers.Take(_players).All(driver => driver?.Migration?.Subjects?.Count == _players))
        {
            Require(_drivers[0]!.BeginLeave(), "Host drain begins.");
            _stage = 2;
        }
        else if (_stage == 2 && _drivers[0]!.LeaveComplete)
        {
            _gateways[0].Stop();
            _drivers[0] = null;
            _stage = 3;
        }
        else if (_stage == 3 && _drivers[1]!.State?.AuthorityEpoch == 2 && (_players == 2 || (_drivers[2]!.State?.AuthorityEpoch == 2 && !_drivers[2]!.Reconnecting)))
        {
            Require(_drivers[1]!.State!.Players.All(player => !player.Ready), "Migration clears Ready.");
            Require(_drivers[1]!.Authority!.Configuration == _configuration, "Lobby migration retains the original host's tuning.");
            CreateDriver(0, Connect(0, 1), false, 1, 2);
            _stage = 4;
        }
        else if (_stage == 4 && _drivers[0]!.State?.AuthorityEpoch == 2 && !_drivers[0]!.Reconnecting)
        {
            Require(_drivers[0]!.Authority is null && _drivers[0]!.LocalPlayerId == 1, "Former host is an ordinary stable player.");
            foreach (var driver in _drivers.Take(_players))
            {
                driver!.Request(LobbyCommand.Ready, true);
            }

            _stage = 5;
        }
        else if (_stage == 5 && _drivers[1]!.State!.CanStart)
        {
            Require(_drivers[1]!.Request(LobbyCommand.Start), "Replacement can start normally.");
            _stage = 6;
        }
        else if (_stage == 6 && _drivers.Take(_players).All(driver => driver!.State!.Phase == SessionPhase.Arena))
        {
            for (int i = 0; i < _players; i++)
            {
                var arena = new NetworkVehicleArena();
                arena.Initialize(_gateways[i], i == 1 ? _drivers[i]!.State!.Match : 0, _drivers[i]!.ServerPeer, _drivers[i], i == 1 ? _drivers[i]!.Authority!.Configuration.Configuration : new() { Vehicle = new() { Acceleration = 80 } });
                _views[i].AddChild(arena);
                _arenas[i] = arena;
            }

            _arenas[1]!.Driver.Host!.Items.Grant(_arenas[1]!.Driver.Host!.World, _players == 2 ? 1UL : 3UL, HeldItem.Wrench);
            Require(_arenas[1]!.Driver.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 9, ["spawns.seed"] = 42, ["match.minimum_players"] = 8 }, out _), "First replacement configures normal gameplay owners.");
            _configuration = _arenas[1]!.Driver.Configuration;
            _randomState = _arenas[1]!.Driver.Host!.Spawns!.RandomState;
            _boundary = _frames;
            _stage = 7;
        }
        else if (_stage == 7 && _frames - _boundary > 150)
        {
            int survivor = _players == 2 ? 0 : 2;
            _retainedBody = _arenas[survivor]!.Bodies[(ulong)survivor + 1];
            _arenas[survivor]!.Driver.Resynchronized += _ =>
            {
                Require(_arenas[survivor]!.Driver.Inputs!.Pending.Count == 0, "No old pending input.");
                Require(_arenas[survivor]!.Driver.History!.Snapshots.Count == 1, "Interpolation reseeded at one boundary.");
            };
            _gateways[1].Stop();
            _drivers[1] = null;
            _arenas[1]!.QueueFree();
            _arenas[1] = null;
            _stage = 8;
        }
        else if (_stage == 8 && _drivers[0]!.State?.AuthorityEpoch == 3 && _arenas[0]!.Driver.IsActive && (_players == 2 || (_drivers[2]!.State?.AuthorityEpoch == 3 && _arenas[2]!.Driver.IsActive)))
        {
            int survivor = _players == 2 ? 0 : 2;
            var arena = _arenas[survivor]!;
            Require(_drivers[0]!.State!.CurrentHostId == 1 && _drivers[survivor]!.State!.CurrentHostId == 1, "Second election converges on stable ID.");
            Require(_arenas[0]!.Bodies.Count == _players && arena.Bodies.Count == _players && arena.Bodies[(ulong)survivor + 1] == _retainedBody, "No duplicate or replaced surviving vehicles.");
            Require(arena.Driver.LocalItem?.Item == HeldItem.Wrench && arena.Driver.ItemState!.Spawns.Count == 8 && arena.Driver.Match!.Players.Count == _players, "Complete gameplay continuation.");
            Require(_arenas[0]!.Driver.Configuration == _configuration && arena.Driver.Configuration == _configuration, "Successive hosts retain configuration revision and ignore successor-local presets.");
            Require(_arenas[0]!.Driver.Host!.Spawns!.RandomState == _randomState, "Migrated RNG continuation.");
            Require(_arenas[0]!.Driver.ForceDeveloperStart(), "Replacement Force Start uses the existing Waiting/countdown authority.");
            Require(_arenas[0]!.Driver.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 11 }, out _), "Second replacement can edit live tuning.");
            if (_players == 3)
            {
                Require(!arena.Driver.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 13 }, out _) && !arena.Driver.GiveDeveloperItem(HeldItem.Missile) && !arena.Driver.ForceDeveloperStart(), "Ordinary survivor cannot invoke developer authority.");
            }

            _stage = 9;
        }
        else if (_stage == 9)
        {
            if (_players == 2 && _arenas[0]!.Driver.LocalItem?.Item == HeldItem.Wrench)
            {
                Require(_arenas[0]!.Driver.RequestItemUse(), "Normal use clears the replacement slot.");
                return;
            }

            Require(_arenas[0]!.Driver.GiveDeveloperItem(HeldItem.Missile), "Give Item targets the second replacement's stable player ID.");
            Cleanup();
            _finished = true;
            _boundary = _frames;
        }
    }

    private void Cleanup()
    {
        foreach (var arena in _arenas)
        {
            arena?.QueueFree();
        }

        foreach (var gateway in _gateways)
        {
            gateway?.Dispose();
        }
    }
}
