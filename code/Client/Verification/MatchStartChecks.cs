using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Staggered native scene construction and real UDP exercise the production entry/start path.</summary>
public sealed partial class MatchStartChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _wires = new();
    private readonly List<LobbyNetworkDriver> _lobbies = new();
    private readonly List<SubViewport> _views = new();
    private readonly List<NetworkVehicleArena?> _arenas = new();
    private readonly List<List<string>> _values = new();
    private readonly HashSet<string> _captures = new();
    private int _count;
    private int _stage;
    private int _frame;
    private int _started;
    private int _cleanup;
    private int _resyncs;
    private bool _leave;
    private bool _resume;
    private bool _dropped;
    private bool _done;
    private bool _waitVerified;
    private string _output = string.Empty;

    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        string[] args = OS.GetCmdlineUserArgs();
        _count = args.Contains("--four") ? 4 : 2;
        _leave = args.Contains("--leave");
        _resume = args.Contains("--resume");
        _output = args.First(arg => arg.StartsWith("--start-output=", StringComparison.Ordinal))[15..];
        System.IO.Directory.CreateDirectory(_output);
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
        reservation.Close();
        for (int i = 0; i < _count; i++)
        {
            var wire = new GameNetworkingSocketsTransport();
            _wires.Add(wire);
            if (i == 0) wire.Listen(TransportEndpoint.DirectIp(endpoint));
            ulong peer = i == 0 ? 0 : wire.Connect(TransportEndpoint.DirectIp(endpoint));
            var lobby = new LobbyNetworkDriver(wire, i == 0 ? 173ul : 0, peer, $"Racer {i + 1}",
                identity: i == 0 ? id => _resume ? "resume-player" : $"player-{id}" : null);
            if (_resume && i == 1) lobby.Reconnect = () => wire.Connect(TransportEndpoint.DirectIp(endpoint));
            _lobbies.Add(lobby);
            var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            _views.Add(view);
            if (i == 0)
            {
                var display = new SubViewportContainer();
                AddChild(display);
                display.AddChild(view);
            }
            else AddChild(view);
            _arenas.Add(null);
            _values.Add(new());
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done)
        {
            if (++_cleanup == 30) GetTree().Quit();
            return;
        }
        try
        {
            Require(++_frame < 3600, "Native start scenario exceeded 60 seconds.");
            for (int i = 0; i < _count; i++)
            {
                if (_leave && _dropped && i == _count - 1) continue;
                if (_arenas[i] is { } arena) arena.Advance(new InputFrame(0, 0, ushort.MaxValue, 0, 0, 0, 0));
                else _lobbies[i].Pump(delta);
                Require(_lobbies[i].Failure.Length == 0, _lobbies[i].Failure);
                if (_arenas[i] is { } active)
                {
                    Require(active.Driver.Failure.Length == 0, active.Driver.Failure);
                    if (active.Driver.Match?.Phase is MatchPhase.Waiting or MatchPhase.Countdown)
                    {
                        Require(!active.Driver.AllowsParticipation, "Pre-GO controls enabled.");
                        Require(active.Driver.Inputs is null || active.Driver.Inputs.Pending.All(command => command.Frame.Accelerate == 0), "Pre-GO predicted throttle.");
                        if (active.Driver.Host is { } host)
                        {
                            Require(host.World.State.LastInput.Accelerate == 0, "Pre-GO host throttle.");
                            Require(host.World.State.Match!.Lifecycle.RemainingMatchTicks(host.World.State.Tick) == 36000, "Timer consumed before GO.");
                            Require(host.World.State.Vehicles.All(v => new System.Numerics.Vector2(v.Movement.Physics.LinearVelocity.X, v.Movement.Physics.LinearVelocity.Z).Length() < 0.5f), "Vehicle raced before GO.");
                        }
                    }
                }
            }
            if (_stage == 0 && _lobbies.All(lobby => lobby.State?.Players.Count == _count))
            {
                for (int i = 1; i < _count; i++) _lobbies[i].Request(LobbyCommand.Ready, true);
                _stage = 1;
            }
            if (_stage == 1 && _lobbies[0].State!.CanStart)
            {
                Require(_lobbies[0].Request(LobbyCommand.Start), "Start rejected.");
                _started = _frame;
                _stage = 2;
            }
            if (_stage == 2)
            {
                for (int i = 0; i < _count; i++)
                {
                    int delay = i == _count - 1 ? 600 : i * 100;
                    if (_arenas[i] is not null || _frame - _started < delay || _lobbies[i].State?.Phase != SessionPhase.Arena || (_leave && i == _count - 1)) continue;
                    var arena = new NetworkVehicleArena { ApplicationEntry = true };
                    arena.Initialize(_wires[i], i == 0 ? _lobbies[0].State!.Match : 0, _lobbies[i].ServerPeer, _lobbies[i]);
                    _views[i].AddChild(arena);
                    _views[i].AddChild(new Hud.CombatHud
                    {
                        Vehicle = () => arena.Driver.LocalState,
                        Match = () => arena.Driver.Match,
                        AuthoritativeTick = () => arena.Driver.Latest?.Tick ?? 0,
                        Player = () => arena.Driver.LocalVehicleId,
                    });
                    _arenas[i] = arena;
                    if (i == 1) arena.Driver.Resynchronized += _ => _resyncs++;
                }
                if (_frame - _started == 550)
                {
                    Require(_arenas[0]!.Driver.Host!.World.State.Tick == 0 && !_arenas[0]!.Driver.EntryReady, "Slow final peer did not hold tick zero.");
                    Require(_arenas.Take(_count - 1).All(arena => arena?.Driver.AllowsParticipation == false), "Loaded peer started early.");
                    _waitVerified = true;
                    GD.Print($"VERIFIED: {_count} players; first {_count - 1} loaded, final scene delayed ten seconds; authority remains tick zero.");
                }
                if (_leave && !_dropped && _frame - _started >= 600)
                {
                    _lobbies[^1].BeginLeave();
                    _lobbies[^1].Pump(delta);
                    _wires[^1].Disconnect(_lobbies[^1].ServerPeer);
                    _dropped = true;
                }
                if (_arenas[0]?.Driver.EntryReady == true) _stage = 3;
            }
            if (_stage == 3)
            {
                var host = _arenas[0]!.Driver.Host!;
                if (_resume && !_dropped && host.World.State.Tick >= 65)
                {
                    _wires[0].Disconnect(_lobbies[0].Authority!.Peers.Keys.Single());
                    _wires[1].Disconnect(_lobbies[1].ServerPeer);
                    _dropped = true;
                    GD.Print("Interrupted the client during Countdown; production reconnect will install current state.");
                }
                if (host.World.State.Tick < 300) Require(host.World.State.Match!.CountdownAtTick == 300, "Countdown restarted or changed deadline.");
                if (host.World.State.Tick >= 390)
                {
                    Require(_waitVerified, "Missing staggered-loading evidence.");
                    for (int i = 0; i < _count - (_leave ? 1 : 0); i++)
                    {
                        var driver = _arenas[i]!.Driver;
                        if (driver.Prediction is not null)
                        {
                            GD.Print($"Entry correction quality player {driver.LocalVehicleId}: startup {driver.StartupCorrections}; steady {driver.SteadyCorrections}; hard snaps {_arenas[i]!.Bodies[driver.LocalVehicleId].Smoothing.HardSnaps}");
                        }
                        Require(driver.EntryReady && driver.AllowsParticipation && driver.Match!.ActiveStartedAtTick == 300, "Participant did not share authoritative GO.");
                        Require(_arenas[i]!.CountdownText.Length == 0, "GO did not clear.");
                        Require(_values[i].Contains("GO"), "GO was not rendered.");
                        if (!_resume) Require(_values[i].SequenceEqual(new[] { "5", "4", "3", "2", "1", "GO" }), "Incomplete countdown sequence: " + string.Join(',', _values[i]));
                    }
                    Require(!_resume || _resyncs >= 2, "Countdown reconnect was not exercised.");
                    Require(host.World.State.Match!.Lifecycle.RemainingMatchTicks(host.World.State.Tick) == 36000 - (host.World.State.Tick - 300), "Timer did not start exactly at GO.");
                    GD.Print($"Match start integration passed: players={_count}, leave={_leave}, reconnect={_resume}, checkpoint installations={_resyncs}, GO tick=300; neutral controls and native vehicles before GO; timer starts at GO; sequences=" + string.Join(" / ", _values.Select(values => string.Join(',', values))));
                    _done = true;
                    foreach (var arena in _arenas) { arena?.Driver.Dispose(); arena?.QueueFree(); }
                    foreach (var wire in _wires) wire.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            _done = true;
            GD.PrintErr(exception);
            foreach (var arena in _arenas) { arena?.Driver.Dispose(); arena?.QueueFree(); }
            foreach (var wire in _wires) wire.Dispose();
            GetTree().Quit(1);
        }
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        for (int i = 0; i < _count; i++)
        {
            if (_arenas[i] is not { } arena) continue;
            string value = arena.CountdownText;
            if (value.Length > 0)
            {
                var number = arena.FindChildren("*", "Label", true, false).OfType<Label>().Single(label => label.Text == value);
                Require(number.GetGlobalRect().HasPoint(new Vector2(640, 350)), "Countdown number is not centered in the gameplay viewport.");
            }
            if (value.Length > 0 && _values[i].LastOrDefault() != value) _values[i].Add(value);
            if (DisplayServer.GetName() != "headless" && value.Length > 0 && _captures.Add($"{i}-{value}")) Capture(i, value);
        }
    }

    private async void Capture(int peer, string value)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!_done) _views[peer].GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, $"peer-{peer}-{value}.png"));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
