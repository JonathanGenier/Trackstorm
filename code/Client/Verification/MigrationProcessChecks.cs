using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>One native UDP participant for an externally terminated host-process scenario.</summary>
public sealed partial class MigrationProcessChecks : Node
{
    private readonly Dictionary<ulong, string> _subjects = new();
    private GameNetworkingSocketsTransport _gateway = null!;
    private LobbyNetworkDriver _driver = null!;
    private NetworkVehicleArena? _arena;
    private PackedScene _preparedMap = null!;
    private int _role;
    private int _assigned;
    private int _frames;
    private int _resumed;
    private string _directory = string.Empty;
    private string[] _ports = [];
    private bool _finished;
    private long? _retiredAt;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        var arguments = OS.GetCmdlineUserArgs().Select(value => value.Split('=', 2)).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1]);
        _role = int.Parse(arguments["--migration-role"], System.Globalization.CultureInfo.InvariantCulture);
        _ports = arguments["--migration-ports"].Split(',');
        _directory = arguments["--migration-output"];
        // Match production resource preparation and the in-process migration fixture:
        // cold map construction must not block this participant's live heartbeat.
        _preparedMap = GD.Load<PackedScene>(Arenas.ActiveMap.ScenePath);
        var warmMap = _preparedMap.Instantiate<Node3D>();
        AddChild(warmMap);
        var warmLayout = Arenas.DestructibleEnvironment.ReadLayout(warmMap)!;
        new Arenas.DestructibleEnvironment(warmMap).Apply(new Core.Arenas.EnvironmentAuthority(warmLayout).Snapshot(1, 0));
        warmMap.Free();
        _gateway = new GameNetworkingSocketsTransport();
        _gateway.ConnectionChanged += change =>
        {
            if (change.State == TransportConnectionState.Connected)
            {
                // The launcher admits original clients sequentially; the sole post-loss connector is role 2.
                _subjects[change.RemotePeerId] = "process-" + (_role == 0 ? ++_assigned : 2);
            }
        };
        ulong server = 0;
        if (_role == 0)
        {
            _gateway.Listen(Endpoint(0));
        }
        else
        {
            server = _gateway.Connect(Endpoint(0));
        }

        _driver = new LobbyNetworkDriver(_gateway, _role == 0 ? 901UL : 0, server, "Process" + _role, _ => true, 901, peer => _subjects.GetValueOrDefault(peer))
        {
            Reconnect = () => throw new InvalidOperationException("Original host process has been terminated by the test launcher."),
        };
        _driver.Migration = new SessionMigration(_driver, _gateway, "process-" + _role, peer => _subjects.GetValueOrDefault(peer), (subject, listen) =>
        {
            _gateway.Stop();
            int target = int.Parse(subject.AsSpan(8), System.Globalization.CultureInfo.InvariantCulture);
            if (listen)
            {
                _gateway.Listen(Endpoint(_role));
                return 0;
            }

            return _gateway.Connect(Endpoint(target));
        });
        // The launcher writes this only after waiting for the old authority process to exit.
        _driver.Migration.RetirementConfirmedAt = _ => _retiredAt;
        Write("started");
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_role != 0 && !_retiredAt.HasValue && System.IO.File.Exists(System.IO.Path.Combine(_directory, "0-retired.json")))
        {
            _retiredAt = TimeProvider.System.GetTimestamp();
        }

        if (_finished)
        {
            return;
        }

        try
        {
            if (++_frames > 2700)
            {
                throw new InvalidOperationException("Separate-process migration timed out.");
            }

            if (_arena is null)
            {
                _driver.Pump(delta);
                if (_driver.State is not null)
                {
                    Write("joined");
                    _driver.Request(LobbyCommand.Ready, true);
                    if (_role == 0 && _driver.State.Players.Count == _ports.Length && _driver.State.CanStart)
                    {
                        _driver.Request(LobbyCommand.Start);
                    }

                    if (_driver.State.Phase == SessionPhase.Arena)
                    {
                        _arena = new NetworkVehicleArena { PreparedMap = _preparedMap };
                        _arena.Initialize(_gateway, _role == 0 ? _driver.State.Match : 0, _driver.ServerPeer, _driver);
                        AddChild(_arena);
                    }
                }
            }
            else
            {
                _arena.Advance(default);
                if (_arena.Driver.Latest?.Tick >= 120 && _driver.Migration!.Subjects?.Count == _ports.Length)
                {
                    Write("ready");
                }

                if (_driver.State!.AuthorityEpoch == 2 && _arena.Driver.IsActive && ++_resumed >= 60)
                {
                    if (_driver.State.CurrentHostId != 2 || _arena.Bodies.Count != _ports.Length || _arena.Driver.Latest?.Vehicles.Count != _ports.Length)
                    {
                        throw new InvalidOperationException("Separate processes did not converge on one intact authority.");
                    }

                    Write("passed");
                    if (_ports.Length == 2 || System.IO.File.Exists(System.IO.Path.Combine(_directory, $"{(_role == 1 ? 2 : 1)}-passed.json")))
                    {
                        _gateway.Dispose();
                        _finished = true;
                        CallDeferred(MethodName.Complete);
                    }
                }
            }

            if (_driver.Failure.Length > 0 || _arena?.Driver.Failure.Length > 0)
            {
                throw new InvalidOperationException(_driver.Failure + _arena?.Driver.Failure);
            }
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            _gateway.Dispose();
            GetTree().Quit(1);
        }
    }

    /// <summary>Releases the arena and allows deferred audio teardown to complete before process shutdown.</summary>
    public async void Complete()
    {
        _arena!.QueueFree();
        _arena = null;
        // Audio playback retirement runs on a separate native clock. A handful of render
        // frames can finish before that clock drains, especially in headless multi-process runs.
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);

        GetTree().Quit();
    }

    private TransportEndpoint Endpoint(int role) => TransportEndpoint.DirectIp("127.0.0.1:" + _ports[role]);

    private void Write(string stage)
    {
        string path = System.IO.Path.Combine(_directory, $"{_role}-{stage}.json");
        if (!System.IO.File.Exists(path))
        {
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new { Role = _role, Stage = stage, Epoch = _driver?.State?.AuthorityEpoch, Host = _driver?.State?.CurrentHostId, Tick = _arena?.Driver.Latest?.Tick }));
        }
    }
}
