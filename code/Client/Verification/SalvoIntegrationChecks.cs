using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Three real UDP peers, native banked terrain, local marker privacy and repeated salvos.</summary>
public sealed partial class SalvoIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = [];
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<SubViewport> _views = [];
    private readonly List<List<ItemEvent>> _events = [];
    private readonly List<string> _evidence = [];
    private readonly Input.PlayerInput _input = new();
    private int _frame;
    private int _boundary;
    private int _stage;
    private int _wave;
    private bool _done;
    private bool _captured;
    private ulong Shooter => _wave == 2 ? 1ul : 2ul;
    private int ShooterIndex => (int)Shooter - 1;
    private string _output = "";
    private bool Play => OS.GetCmdlineUserArgs().Contains("--salvo-play");

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _output = ProjectSettings.GlobalizePath("res://.godot/salvo-checks");
        System.IO.Directory.CreateDirectory(_output);
        AddChild(_input); _input.SetPhysicsProcess(false);
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        for (int i = 0; i < 3; i++)
        {
            var gateway = new GameNetworkingSocketsTransport();
            ulong server = 0;
            if (i == 0)
            {
                gateway.Listen(TransportEndpoint.DirectIp(endpoint));
                if (OS.GetCmdlineUserArgs().Contains("--salvo-impaired")) { gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
            }
            else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
            _gateways.Add(gateway);
            var view = new SubViewport { Size = new(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            if (i == 1) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
            else { AddChild(view); }
            _views.Add(view);
            var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
            arena.Initialize(gateway, i == 0 ? 88ul : 0, server);
            view.AddChild(arena);
            view.AddChild(new Hud.CombatHud { Vehicle = () => arena.LocalState, Slot = () => arena.Driver.LocalItem });
            var bank = new StaticBody3D { Position = new(0, 20, 0), Rotation = new(0, 0, 0.12f), CollisionLayer = 1 };
            bank.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 1, 300) } });
            bank.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(200, 1, 300) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.24f, 0.28f, 0.3f) } });
            arena.AddChild(bank);
            _arenas.Add(arena);
            var events = new List<ItemEvent>(); _events.Add(events);
            arena.Driver.ItemsReceived += p => events.AddRange(p.Events.Where(e => e.Item == HeldItem.Salvo));
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;
        if (_done && !Play) { if (_frame > _boundary + 20) { GetTree().Quit(); } return; }
        try
        {
            foreach (var arena in _arenas)
            {
                arena.Advance(_done && Play && arena == _arenas[1] ? _input.Adapter.Capture(0) : default);
                Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
            }
            var host = _arenas[0].Driver.Host!;
            if (_done)
            {
                if (host.Items.Missiles.Count == 0 && host.Items.Slots.SingleOrDefault(s => s.Vehicle == 2)?.Active.Item != HeldItem.Salvo)
                { host.Items.Grant(host.World, 2, HeldItem.Salvo); }
                return;
            }
            Check(_frame - _boundary < 1200, $"Salvo stage {_stage} timeout");
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 3):
                    Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1 }, out _), "fixture countdown");
                    Position(); Next("Three UDP peers joined; native bank prepared."); break;
                case 1 when _frame - _boundary > 60:
                    foreach (var a in _arenas) { Check(!a.SalvoMarker.Visible, "No marker without local salvo."); }
                    Check(host.Items.Grant(host.World, 2, HeldItem.Salvo), "Remote grant");
                    Check(host.Items.Grant(host.World, 2, HeldItem.Wrench), "Second slot retained");
                    Next("Remote player receives Salvo and second-slot Wrench."); break;
                case 2 when _arenas[ShooterIndex].SalvoMarker.Visible:
                    Check(_arenas[ShooterIndex].SalvoMarker.RingCount == 1, "Local marker circular footprint");
                    var vertices = _arenas[ShooterIndex].SalvoMarker.SurfaceVertices;
                    Check(vertices.Max(v => v.Y) - vertices.Min(v => v.Y) > 1, "Marker conforms to bank height");
                    Check(_arenas.Where(a => a.Driver.LocalVehicleId != Shooter).All(a => !a.SalvoMarker.Visible), "Other peers cannot see held weapon marker");
                    Capture(ShooterIndex, "owner-aim.png"); Capture(ShooterIndex == 0 ? 1 : 0, "remote-no-marker.png");
                    if (_wave == 0)
                    {
                        Check(_arenas[ShooterIndex].Driver.RequestItemSwitch(), "Remote switch away");
                        _stage = 20; _boundary = _frame; break;
                    }
                    Check(_arenas[ShooterIndex].Driver.RequestItemUse(), "Remote capability use");
                    Next("Banked ring visible only to owner; remote use sent over UDP."); break;
                case 20 when _arenas[ShooterIndex].Driver.LocalItem?.Active.Item == HeldItem.Wrench && _frame - _boundary > 10:
                    Check(!_arenas[ShooterIndex].SalvoMarker.Visible, "Switch away clears unused aiming marker");
                    Check(_arenas[ShooterIndex].Driver.RequestItemSwitch(), "Switch back");
                    _stage = 21; _boundary = _frame; break;
                case 21 when _arenas[ShooterIndex].SalvoMarker.Visible && _arenas[ShooterIndex].Driver.LocalItem?.Active.Item == HeldItem.Salvo:
                    Check(_arenas[ShooterIndex].Driver.RequestItemUse(), "Switched-back remote use");
                    _stage = 3; _boundary = _frame; break;
                case 3:
                    if (host.Items.Missiles.Any(m => m.Launched))
                    {
                        Check(_arenas.Where(a => a.Driver.LocalVehicleId != Shooter).All(a => !a.SalvoMarker.Visible), "Other peers cannot see firing marker");
                        if (!_captured && _frame - _boundary > 28) { Capture(ShooterIndex, "owner-arc.png"); _captured = true; }
                    }
                    if (_events.All(events => events.Count(e => e.Impact) == 5))
                    {
                        foreach (var events in _events)
                        {
                            Check(events.Count(e => !e.Impact) == 5, "Five distinct launches on every peer");
                            Check(events.SequenceEqual(_events[0]), "Identical ordered salvo events on all peers");
                            Check(events.Where(e => e.Impact).Select(e => e.Token).Distinct().Count() == 5, "Unique explosion identities");
                        }
                        Check(host.World.State.Vehicles.Where(v => v.VehicleId != Shooter).All(v => v.Damage.CurrentHP < 1000), "Multiple native targets damaged");
                        Check(host.Items.Slots.Single(s => s.Vehicle == Shooter).SecondItem == HeldItem.Wrench, "Second slot unchanged");
                        Capture(ShooterIndex, "owner-impact.png");
                        Next($"Wave {_wave + 1}: five matching launches/impacts, damage to both targets, remote marker privacy.");
                    }
                    break;
                case 4 when _frame - _boundary > 45:
                    Check(_arenas.All(a => !a.SalvoMarker.Visible), "Marker clears after final round on all peers");
                    if (++_wave < 3)
                    {
                        foreach (var events in _events) { events.Clear(); }
                        Position();
                        Check(host.Items.Grant(host.World, Shooter, HeldItem.Salvo), "Repeated use regrant");
                        if (Shooter == 1) { Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Host second slot"); }
                        _stage = 2; _boundary = _frame; _captured = false;
                    }
                    else
                    {
                        _done = true; _boundary = _frame;
                        if (Play) { _wave = 0; Position(); }
                        System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
                        GD.Print("Salvo integration passed: three peers, three salvos, banked marker privacy, 15 replicated impacts and multiple targets.");
                    }
                    break;
            }
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); _done = true; }
    }

    private void Position()
    {
        var host = _arenas[0].Driver.Host!;
        var state = host.World.State;
        host.World.Restore(new(state.Tick, state.LastInput, state.Vehicles.Select(v =>
        {
            float x = v.VehicleId == Shooter ? 0 : v.VehicleId == 3 ? 3 : -2;
            var pose = new VehiclePhysicsState(new N.Vector3(x, 22 + x * 0.12f, v.VehicleId == Shooter ? 70 : 5), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
            _arenas[0].Bodies[v.VehicleId].Apply(pose);
            return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(state.Tick, pose, true, false, 0, 0), new VehicleDamageState(1000, 1000, null, null), pose);
        }), state.Match));
    }

    private void Capture(int peer, string name)
    {
        if (DisplayServer.GetName() != "headless") { _views[peer].GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, name)); }
    }
    private void Next(string evidence) { GD.Print(evidence); _evidence.Add(evidence); _stage++; _boundary = _frame; }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    public override void _ExitTree() { foreach (var gateway in _gateways) { gateway.Dispose(); } }
}
