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
                arena.Advance(new Core.Input.InputFrame(0, 0, ushort.MaxValue, 0, 0, 0, 0));
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
                    Position();
                    Next("Two native UDP peers drive on the same isolated flat platform.");
                    break;
                case 1 when _frames - _boundary > 360:
                    _baseline = host.World.GetVehicle(1).Speed;
                    Check(_baseline > 40 && _baseline < 45, $"baseline {_baseline}");
                    UseBoth();
                    Next($"Normal drive settles at {_baseline:0.00} m/s; both players request Nitro.");
                    break;
                case 2 when host.World.State.Vehicles.All(v => v.Movement.Nitro.Active) && _arenas[1].Driver.LocalState!.Movement.Nitro.Active:
                    Check(host.Items.Slots.All(s => s.Item == HeldItem.None), "both slots consumed");
                    Next("Host and client observe active boost from normal local/remote use; both slots consumed once.");
                    break;
                case 3 when _frames - _boundary > 220:
                    float speed = host.World.GetVehicle(1).Speed;
                    Check(speed > _baseline * 1.2f, $"boosted speed {speed}");
                    Check(_arenas[1].Driver.LocalState!.Speed > _baseline * 1.2f, "remote boost motion");
                    Check(host.World.State.Match!.Players.All(p => p.CircusScore > 30), "duration score");
                    Capture("active-nitro.png");
                    Next($"Native boosted speed reaches {speed:0.00} m/s; both authoritative totals include duration points.");
                    break;
                case 4 when host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active) && !_arenas[1].Driver.LocalState!.Movement.Nitro.Active:
                    Check(host.Configuration.Configuration.Vehicle.ForwardSpeed < 45, "canonical cap unchanged");
                    Check(host.TryConfigure(0, new Dictionary<string, double> { ["items.nitro_duration_ticks"] = 60 }, out _), "short repeat tuning");
                    UseBoth();
                    Next("Both peers expire cleanly; canonical tuning is unchanged. Beginning repeated one-second boosts.");
                    break;
                case 5:
                    if (_frames - _boundary == 40) { Check(host.World.State.Vehicles.All(v => v.Movement.Nitro.Active), "repeated activation"); }
                    if (_frames - _boundary > 90)
                    {
                        Check(host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active), "repeated expiry");
                        if (host.World.Events.Entries.Count(e => e.Kind == "Used" && e.Cause == "Nitro") < 8)
                        {
                            UseBoth();
                            _boundary = _frames;
                        }
                        else { Next("Four activations per vehicle complete with bounded expiry and no accumulated boost."); }
                    }
                    break;
                case 6 when _frames - _boundary > 10:
                    Check(_arenas[1].Driver.Match!.Players.All(p => p.CircusScore > 79), "remote score publication");
                    Check(host.World.Events.Entries.Count(e => e.Kind == "Effect ended" && e.Cause == "Nitro") == 8, "one expiry per use");
                    Capture("expired-nitro.png");
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
        foreach (ulong id in new ulong[] { 1, 2 }) { Check(host.Items.Grant(host.World, id, HeldItem.Nitro), "grant"); }
        Check(_arenas[0].Driver.RequestItemUse(), "host request");
        // Remote acquisition is published on the next step, so send the same authenticated request
        // via its transport only after the confirmed slot arrives.
        _arenas[1].Driver.ItemsReceived += RequestRemote;
    }

    private void RequestRemote(ItemPublication publication)
    {
        if (_arenas[1].Driver.LocalItem?.Item != HeldItem.Nitro) { return; }
        _arenas[1].Driver.ItemsReceived -= RequestRemote;
        Check(_arenas[1].Driver.RequestItemUse(), "remote request");
        Check(_arenas[1].Driver.RequestItemUse(), "duplicate remote request delivered for authority rejection");
    }

    private void Position()
    {
        var host = _arenas[0].Driver.Host!;
        var w = host.World.State;
        host.World.Restore(new(w.Tick, w.LastInput, w.Vehicles.Select(v =>
        {
            var pose = new VehiclePhysicsState(new N.Vector3(v.VehicleId == 1 ? -8 : 8, 21.4f, 1200), N.Quaternion.Identity, new N.Vector3(0, 0, -40), N.Vector3.Zero);
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
