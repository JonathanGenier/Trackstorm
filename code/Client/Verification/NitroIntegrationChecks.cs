using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native two-peer driving, repeated Nitro use, score publication and clean expiry.</summary>
public sealed partial class NitroIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<SubViewport> _views = new();
    private string _endpoint = "";
    private int _frames;
    private int _stage;
    private int _boundary;
    private float _baseline;
    private bool _done;
    private bool _held;
    private ushort _throttle = ushort.MaxValue;
    private ushort _reverse;
    private int _rocketScenario;
    private float _rocketOnlySpeed;
    private float _rocketStartSpeed;
    private readonly bool[] _press = new bool[2];
    private double[] _charges = [];
    private double[] _scores = [];
    private float _releaseSpeed;
    private readonly List<string> _evidence = new();

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        AddPeer();
        AddPeer();
    }

    private void AddPeer()
    {
        int index = _arenas.Count;
        var gateway = new GameNetworkingSocketsTransport();
        ulong server = 0;
        if (index == 0)
        {
            gateway.Listen(TransportEndpoint.DirectIp(_endpoint));
            if (OS.GetCmdlineUserArgs().Contains("--nitro-impaired")) { gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
        }
        else { server = gateway.Connect(TransportEndpoint.DirectIp(_endpoint)); }
        _gateways.Add(gateway);
        var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        if (index == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
        else { AddChild(view); }
        _views.Add(view);
        var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
        arena.Initialize(gateway, index == 0 ? 88ul : 0, server);
        view.AddChild(arena);
        var hud = new Hud.CombatHud { Vehicle = () => arena.Driver.LocalState, Slot = () => arena.Driver.LocalItem,
            Match = () => arena.Driver.Match, Player = () => arena.Driver.LocalVehicleId };
        view.AddChild(hud);
        var bank = new StaticBody3D { Position = new Vector3(0, 20, 0), Rotation = Vector3.Zero, CollisionLayer = 1 };
        bank.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(120, 1, 5000) } });
        bank.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(120, 1, 5000) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.34f, 0.36f) } });
        arena.AddChild(bank);
        var camera = new Camera3D { Name = "NitroCamera", Position = new Vector3(15, 35, 23) };
        arena.AddChild(camera);
        camera.LookAt(new Vector3(0, 20, 2));
        camera.MakeCurrent();
        _arenas.Add(arena);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) { if (++_frames > _boundary + 20) { GetTree().Quit(); } return; }
        try
        {
            _frames++;
            foreach (var arena in _arenas)
            {
                int peerIndex = _arenas.IndexOf(arena);
                bool press = _press[peerIndex];
                _press[peerIndex] = false;
                arena.Advance(new Core.Input.InputFrame(0, 0, _throttle, _reverse, _held ? Core.Input.InputButtons.UseItem : 0, press ? Core.Input.InputButtons.UseItem : 0, 0));
                Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
            }
            Check(_frames - _boundary < 1800, $"Nitro stage {_stage} timeout");
            var host = _arenas[0].Driver.Host!;
            if (host.World.State.Vehicles.Count > 0)
            {
                var position = Vehicles.VehicleBody.ToGodot(host.World.GetVehicle(1).Movement.Physics.Position);
                var camera = _arenas[0].GetNode<Camera3D>("NitroCamera");
                camera.Position = position + new Vector3(9, 6, 13);
                camera.LookAt(position);
            }
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2):
                    Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1 }, out _), "short countdown");
                    _stage = 100; _boundary = _frames;
                    break;
                case 100:
                    PrepareRocketScenario();
                    _stage = 101; _boundary = _frames;
                    break;
                case 101 when _frames - _boundary > 30:
                    _rocketStartSpeed = host.World.GetVehicle(1).Speed;
                    UseBoth();
                    _stage = 102; _boundary = _frames;
                    break;
                case 102 when _frames - _boundary > 60:
                    VerifyRocketScenario();
                    _held = false;
                    _stage = 103; _boundary = _frames;
                    break;
                case 103 when _frames - _boundary > 30:
                    Check(host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active), "rocket release ends thrust on both peers");
                    if (++_rocketScenario < 6) { _stage = 100; _boundary = _frames; break; }
                    foreach (ulong id in new ulong[] { 1, 2 }) { host.Items.RemovePlayer(id); }
                    _throttle = ushort.MaxValue; _reverse = 0;
                    Position();
                    _stage = 0;
                    Next("Rocket scenarios complete; normal-drive and sustained-resource comparison begins.");
                    break;
                case 1 when _frames - _boundary > 360:
                    _baseline = host.World.GetVehicle(1).Speed;
                    Check(_baseline > 40 && _baseline < 45, $"baseline {_baseline}");
                    UseBoth();
                    Next($"Normal drive settles at {_baseline:0.00} m/s; both players request Nitro.");
                    break;
                case 2 when host.World.State.Vehicles.All(v => v.Movement.Nitro.Active) && _arenas[1].Driver.LocalState!.Movement.Nitro.Active:
                    Check(host.Items.Slots.All(s => s.Item == HeldItem.Nitro && s.NitroCharge is > 0 and < 100 && s.SecondItem == HeldItem.Wrench), "independent retained Nitro and second slot");
                    Next("Both peers sustain Nitro; charge drains while Wrench remains in the second slot.");
                    break;
                case 3 when _frames - _boundary > 180:
                    _releaseSpeed = host.World.GetVehicle(1).Speed;
                    Check(_releaseSpeed > _baseline * 1.2f, $"boosted speed {_releaseSpeed}");
                    Check(_arenas[1].Driver.LocalState!.Speed > _baseline * 1.2f, "remote boost motion");
                    _charges = host.Items.Slots.Select(s => s.NitroCharge).ToArray();
                    _scores = host.World.State.Match!.Players.Select(p => p.CircusScore).ToArray();
                    Check(_scores.All(s => s > 0), "actual overspeed score");
                    Capture("partial-nitro.png");
                    _held = false;
                    Next($"Boost reaches {_releaseSpeed:0.00} m/s; released at {_charges[0]:0.00}% charge.");
                    break;
                case 4 when _frames - _boundary > 30:
                    Check(host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active), "release removes boost");
                    Check(Math.Abs(host.Items.Slots[0].NitroCharge - _charges[0]) < 0.00001, "host release preserves charge");
                    Check(host.Items.Slots[1].NitroCharge <= _charges[1] && host.Items.Slots[1].NitroCharge > _charges[1] - 10, "remote release bounded by input delivery");
                    _charges = host.Items.Slots.Select(s => s.NitroCharge).ToArray();
                    float recovering = host.World.GetVehicle(1).Speed;
                    Check(recovering > _baseline && recovering < _releaseSpeed && _releaseSpeed - recovering < 3, "smooth recovery without snapping or sudden braking");
                    Check(host.World.State.Match!.Players.Zip(_scores).All(p => p.First.CircusScore > p.Second), "overspeed scoring continues after release");
                    Capture("released-nitro.png");
                    Next($"Release recovery remains at {recovering:0.00} m/s with continuing authoritative overspeed awards.");
                    break;
                case 5 when _frames - _boundary > 360:
                    Check(host.World.State.Vehicles.All(v => v.Speed <= host.Configuration.Configuration.Vehicle.ForwardSpeed + 0.01f), "recovery reaches normal speed");
                    Check(host.Items.Slots.Select(s => s.NitroCharge).SequenceEqual(_charges), "idle charge retained on both peers");
                    Check(!host.World.State.Match!.Awards.Any(a => a.Category == Core.Matches.CircusScoreCategory.Nitro), "no Nitro scoring at normal speed");
                    UseBoth();
                    Next("Recovery reaches normal speed and overspeed scoring stops; reuse begins with retained charge.");
                    break;
                case 6 when _frames - _boundary > 30:
                    Check(host.World.State.Vehicles.All(v => v.Movement.Nitro.Active), "repeat activation");
                    Check(host.Items.Slots.Zip(_charges).All(p => p.First.NitroCharge < p.Second), "reuse drains same resources");
                    _held = false;
                    Next("Second activation drains the same grant tokens, followed by another release.");
                    break;
                case 7 when _frames - _boundary > 30:
                    Check(host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active), "second release");
                    UseBoth();
                    Next("Third activation continues until resource exhaustion.");
                    break;
                case 8 when host.Items.Slots.All(s => s.Item == HeldItem.None && s.NitroCharge == 0):
                    Check(host.Items.Slots.All(s => s.SecondItem == HeldItem.Wrench), "depletion leaves second slot intact");
                    _held = false;
                    Next("Both Nitro resources reach zero and clear only their physical slots.");
                    break;
                case 9 when _frames - _boundary > 30:
                    Check(_arenas[1].Driver.LocalItem is { Item: HeldItem.None, NitroCharge: 0, SecondItem: HeldItem.Wrench }, "remote depletion publication");
                    Check(host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active), "exhaustion ends boost");
                    Check(_arenas[1].Driver.Match!.Players.All(p => p.CircusScore > 0), "remote score publication");
                    Check(host.World.Events.Entries.Count(e => e.Kind == "Exhausted" && e.Cause == "Nitro") == 2, "one disposal per grant");
                    Capture("exhausted-nitro.png");
                    GD.Print("Nitro integration passed: " + string.Join("\n", _evidence));
                    _done = true; _boundary = _frames;
                    foreach (var arena in _arenas) { arena.QueueFree(); }
                    foreach (var gateway in _gateways) { gateway.Dispose(); }
                    break;
            }
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            foreach (var gateway in _gateways) { gateway.Dispose(); }
            GetTree().Quit(1);
        }
    }

    private void UseBoth()
    {
        var host = _arenas[0].Driver.Host!;
        _held = true;
        foreach (ulong id in new ulong[] { 1, 2 })
        {
            if (!host.Items.Slots.Any(s => s.Vehicle == id))
            {
                Check(host.Items.Grant(host.World, id, HeldItem.Nitro), "grant");
                Check(host.Items.Grant(host.World, id, HeldItem.Wrench), "second slot grant");
            }
        }
        _press[0] = true;
        // Remote acquisition is published on the next step, so send the same authenticated request
        // via its transport only after the confirmed slot arrives.
        if (_arenas[1].Driver.LocalItem?.Item == HeldItem.Nitro)
        {
            _press[1] = true;
        }
        else { _arenas[1].Driver.ItemsReceived += RequestRemote; }
    }

    private void RequestRemote(ItemPublication publication)
    {
        if (_arenas[1].Driver.LocalItem?.Item != HeldItem.Nitro) { return; }
        _arenas[1].Driver.ItemsReceived -= RequestRemote;
        _press[1] = true;
    }

    private void PrepareRocketScenario()
    {
        var host = _arenas[0].Driver.Host!;
        foreach (ulong id in new ulong[] { 1, 2 }) { host.Items.RemovePlayer(id); }
        _held = false;
        _throttle = _rocketScenario == 2 ? ushort.MaxValue : (ushort)0;
        _reverse = _rocketScenario == 3 ? ushort.MaxValue : (ushort)0;
        Check(host.TryConfigure(0, new Dictionary<string, double> { ["items.nitro_airborne_thrust_scale"] = _rocketScenario == 5 ? 0 : 1 }, out _), "airborne live tuning");
        Position(_rocketScenario == 1 ? 10 : 0, _rocketScenario >= 4 ? 100 : 21.4f);
    }

    private void VerifyRocketScenario()
    {
        var host = _arenas[0].Driver.Host!;
        float speed = -host.World.GetVehicle(1).Movement.Physics.LinearVelocity.Z;
        Check(host.Items.Slots.All(s => s.NitroCharge is > 50 and < 100), "activation-time consumption independent of pedals/support");
        Check(host.World.State.Match!.Players.All(p => p.CircusScore == 0), "no points for thrust/airborne motion below normal top speed");
        switch (_rocketScenario)
        {
            case 0: Check(_rocketStartSpeed < 0.1f && speed > 8, "Nitro launches from complete rest without throttle"); _rocketOnlySpeed = speed; break;
            case 1: Check(speed > _rocketStartSpeed + 5, "Nitro accelerates coasting car without throttle"); break;
            case 2: Check(speed > _rocketOnlySpeed + 1, "drivetrain and rocket combine"); break;
            case 3: Check(speed > 0 && speed < _rocketOnlySpeed, "reverse opposes but never reverses rocket force"); break;
            case 4: Check(!host.World.GetVehicle(1).Movement.Grounded && speed > 8, "airborne rocket propels without wheels"); break;
            case 5: Check(!host.World.GetVehicle(1).Movement.Grounded && Math.Abs(speed) < 0.1f, "runtime zero airborne scale disables thrust while charge drains"); break;
        }
        string line = $"Rocket scenario {_rocketScenario}: start {_rocketStartSpeed:0.00}, forward {speed:0.00} m/s; charge {host.Items.Slots[0].NitroCharge:0.00}%; Circus zero.";
        _evidence.Add(line); GD.Print(line);
        Capture($"rocket-{_rocketScenario}.png");
    }

    private void Position(float speed = 40, float height = 21.4f)
    {
        var host = _arenas[0].Driver.Host!;
        var w = host.World.State;
        host.World.Restore(new(w.Tick, w.LastInput, w.Vehicles.Select(v =>
        {
            var pose = new VehiclePhysicsState(new N.Vector3(v.VehicleId == 1 ? -8 : 8, height, 1200), N.Quaternion.Identity, new N.Vector3(0, 0, -speed), N.Vector3.Zero);
            _arenas[0].Bodies[v.VehicleId].Apply(pose);
            return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(w.Tick, pose, true, false, 0, 0), v.Damage, pose);
        }), w.Match));
    }

    private void Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        string path = ProjectSettings.GlobalizePath("res://.godot/nitro-checks");
        System.IO.Directory.CreateDirectory(path);
        _views[0].GetTexture().GetImage().SavePng(System.IO.Path.Combine(path, name));
    }

    private void Next(string text) { _evidence.Add(text); GD.Print(text); _stage++; _boundary = _frames; }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
