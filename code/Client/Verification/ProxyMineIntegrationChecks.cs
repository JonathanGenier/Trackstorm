using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native flat/banked/uneven placement, distance-force trials, contacts and real UDP state delivery.</summary>
public sealed partial class ProxyMineIntegrationChecks : Node
{
    private readonly ItemDamageScoringCheck _scoring = new();
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<SubViewport> _views = new();
    private string _endpoint = "";
    private int _frames;
    private int _stage;
    private int _boundary;
    private ProxyMineState? _mine;
    private float _outerSpeed;
    private float _midSpeed;
    private float _hp;
    private int _surface;
    private bool _done;
    private readonly List<string> _evidence = new();
    private static readonly N.Vector3 Normal = N.Vector3.Transform(N.Vector3.UnitY, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, 0.25f));
    private static readonly N.Quaternion Rotation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, 0.25f);

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
            if (OS.GetCmdlineUserArgs().Contains("--mine-impaired")) { gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
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
        for (int terrain = 0; terrain < 3; terrain++)
        {
            var bank = new StaticBody3D { Position = new Vector3(terrain * 50, 20, 0), Rotation = terrain == 1 ? new Vector3(0, 0, 0.25f) : Vector3.Zero, CollisionLayer = 1 };
            bank.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(42, 1, 42) } });
            bank.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(42, 1, 42) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.34f, 0.36f) } });
            arena.AddChild(bank);
            if (terrain == 2)
            {
                // Small uneven triangles, not an ideal plane, under the complete mine footprint.
                var vertices = new List<Vector3>();
                Vector3 Point(int x, int z) => new(x, 20.65f + 0.055f * Mathf.Sin(x * 2.1f + z * 1.7f), z);
                for (int x = -5; x < 5; x++) for (int z = 0; z < 10; z++)
                {
                    vertices.AddRange(new[] { Point(x,z), Point(x,z+1), Point(x+1,z), Point(x+1,z), Point(x,z+1), Point(x+1,z+1) });
                }
                var uneven = new StaticBody3D { Position = new Vector3(100,0,0), CollisionLayer = 1 };
                uneven.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = vertices.ToArray(), BackfaceCollision = true } });
                var mesh = new ImmediateMesh(); mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
                foreach (var point in vertices) { mesh.SurfaceSetNormal(Vector3.Up); mesh.SurfaceAddVertex(point); }
                mesh.SurfaceEnd();
                uneven.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f,0.3f,0.2f), CullMode = BaseMaterial3D.CullModeEnum.Disabled } });
                arena.AddChild(uneven);
            }
        }        var camera = new Camera3D { Position = new Vector3(15, 35, 23) };
        arena.AddChild(camera);
        camera.LookAt(new Vector3(0, 20, 2));
        camera.MakeCurrent();
        _arenas.Add(arena);
        arena.Driver.MatchReceived += match => _scoring.Observe(match, index == 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) { if (++_frames > _boundary + 20) { GetTree().Quit(); } return; }
        try
        {
            _frames++;
            foreach (var arena in _arenas) { var before = arena.Driver.Host?.World.State; arena.Advance(default); if (before is not null) { _scoring.Verify(arena.Driver.Host!, before.Value, "gameplay effect"); } Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure); }
            Check(_frames - _boundary < 1200, $"Mine stage {_stage} timeout; mines={_arenas[0].Driver.Host?.Items.Mines.Count}");
            var host = _arenas[0].Driver.Host!;
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2):
                    Check(host.TryConfigure(0, new Dictionary<string,double> { ["items.mine_damage"] = 60, ["match.countdown_ticks"] = 1, ["match.item_points_per_damage"] = 0.5 }, out _), "damage tuning");
                    Position(1, new N.Vector3(0,21.4f,0));
                    Position(2, new N.Vector3(-35,21.4f,0));
                    Grant(1);
                    Next("Two UDP peers ready; ordinary host Proxy Mine use requested.");
                    break;
                case 1 when host.Items.Mines.Count == 1:
                    _mine = host.Items.Mines.Single();
                    Check(Math.Abs(_mine.Position.Y - 20.758f) < 0.03, "flat terrain seating");
                    Next("Mine installed on the real flat platform, with terrain clearance and seated timer.");
                    break;
                case 2 when _frames - _boundary == 15:
                    Check(host.Items.Mines.Single().Position == _mine!.Position, "stable seating before attraction");
                    Position(1, new N.Vector3(-35,21.4f,-10));
                    AddPeer();
                    Next("Initial seating remains exactly stable; fresh late admission started.");
                    break;
                case 3 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 3 && a.Driver.ItemState?.Mines.Count == 1):
                    Position(3, new N.Vector3(-35,21.4f,10));
                    Check(_arenas[2].Driver.ItemState!.Mines.Single().Id == _mine!.Id, "late join mine identity");
                    Trial(22);
                    Next("Late join reconstructed the persistent mine. Outer-radius force trial begins.");
                    break;
                case 4 when _frames - _boundary == 10:
                    _outerSpeed = host.Items.Mines.Single().Velocity.X;
                    Check(_outerSpeed > 0 && _outerSpeed < 0.5f, $"weak outer speed {_outerSpeed}");
                    Capture("outer.png"); Trial(12);
                    Next($"Outer pull produced {_outerSpeed:0.000} m/s after ten native ticks.");
                    break;
                case 5 when _frames - _boundary == 10:
                    _midSpeed = host.Items.Mines.Single().Velocity.X;
                    Check(_midSpeed > _outerSpeed * 3, "progressive mid force");
                    Capture("mid.png"); Trial(4.5f);
                    Next($"Mid pull produced {_midSpeed:0.000} m/s after ten native ticks.");
                    break;
                case 6 when _frames - _boundary == 10:
                    float close = host.Items.Mines.Single().Velocity.X;
                    Check(close > _midSpeed * 2, $"aggressive close force {close}");
                    Capture("close.png");
                    _hp = host.World.GetVehicle(2).Damage.CurrentHP;
                    Position(2, _mine!.Position + new N.Vector3(6, 0.65f, 0), new N.Vector3(-12,0,0));
                    Next($"Close pull produced {close:0.000} m/s; moving remote vehicle approaches for contact.");
                    break;
                case 7 when host.Items.Mines.Count == 0:
                    Check(host.World.GetVehicle(2).Damage.CurrentHP == _hp - 60, "exact moderate contact damage");
                    Check(host.World.GetVehicle(2).Effects.Any(e => e.Attribution.Source == "proxy-mine" && e.Effect.Impulse.Length() > 23000), "large committed knockback");
                    Next("Contact detonated once, applying 60 HP and 24000 N.s to the remote vehicle.");
                    break;
                case 8 when _frames - _boundary > 20 && _arenas.All(a => a.Driver.ItemState?.Mines.Count == 0):
                    Check(host.World.GetVehicle(2).Movement.Physics.LinearVelocity.Length() > 5, "native knockback motion");
                    Check(_arenas.All(a => a.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == 2).State.Damage.CurrentHP <= _hp - 60), "replicated damage");
                    Position(1, new N.Vector3(0,21.4f,0));
                    Position(2, new N.Vector3(8,21.4f,4.5f));
                    Grant(1);
                    _stage = 81; _boundary = _frames;
                    GD.Print("Replicated removal/HP and native knockback passed; stationary target trial requested.");
                    break;
                case 81 when host.Items.Mines.Count == 1:
                    Position(1, new N.Vector3(-35,21.4f,-10));
                    _hp = host.World.GetVehicle(2).Damage.CurrentHP;
                    _stage = 82; _boundary = _frames;
                    break;
                case 82 when host.Items.Mines.Count == 0:
                    Check(host.World.GetVehicle(2).Damage.CurrentHP == _hp - 60, "magnet reaches stationary vehicle");
                    _surface = 1; DeploySurface();
                    _evidence.Add("Repeated host use magnetically reached a stationary vehicle and detonated without target movement.");
                    GD.Print(_evidence[^1]);
                    _stage = 9; _boundary = _frames;
                    break;
                case 9 when host.Items.Mines.Count == 1:
                    _mine = host.Items.Mines.Single();
                    Check(_surface == 1 ? N.Vector3.Dot(_mine.Normal, Normal) > 0.995f : _mine.Position.Y > 20.84f, "slope/uneven placement");
                    Position(2, new N.Vector3(-35,21.4f,0));
                    Focus(_mine.Position);
                    Next($"Surface {_surface}: authoritative position {_mine.Position}, normal {_mine.Normal}.");
                    break;
                case 10 when _frames - _boundary == 45:
                    Check(host.Items.Mines.Single().Position == _mine!.Position, "slope/uneven stable seating");
                    Capture(_surface == 1 ? "slope.png" : "uneven.png");
                    // Drive over the still seated mine; zero magnetic force isolates contact detection.
                    Check(host.TryConfigure(0, new Dictionary<string,double> { ["items.mine_minimum_force"] = 0, ["items.mine_maximum_force"] = 0 }, out _), "disable attraction for drive-over");
                    Position(2, _mine.Position + _mine.Normal * 0.65f + new N.Vector3(0,0,5), new N.Vector3(0,0,-15));
                    _hp = host.World.GetVehicle(2).Damage.CurrentHP;
                    Next("Stable terrain seating captured; drive-over contact trial with attraction disabled.");
                    break;
                case 11 when host.Items.Mines.Count == 0:
                    Check(host.World.GetVehicle(2).Damage.CurrentHP <= _hp - 60, "drive-over damage");
                    if (_surface == 1) { _surface = 2; DeploySurface(); _stage = 9; _boundary = _frames; GD.Print("Bank drive-over passed; uneven terrain deployment requested."); }
                    else { Next("Uneven terrain drive-over detonated and consumed the mine once; repeated remote use passed."); }
                    break;
                case 12 when _frames - _boundary > 20 && _arenas.All(a => a.Driver.ItemState?.Mines.Count == 0):
                    Check(_scoring.Hits >= 2 && _scoring.Points >= 60, "Repeated mine contacts award configured applied-damage points");
                    _evidence.Add($"Item scoring verified: {_scoring.Hits} rival hits, {_scoring.Points} points; host, remote and late-join publications agree.");
                    var path = ProjectSettings.GlobalizePath("res://.godot/mine-checks");
                    System.IO.Directory.CreateDirectory(path);
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(path, "evidence.txt"), _evidence);
                    GD.Print("Proxy Mine integration passed: " + string.Join("\n", _evidence));
                    _done = true; _boundary = _frames;
                    foreach (var arena in _arenas) { arena.QueueFree(); }
                    foreach (var gateway in _gateways) { gateway.Dispose(); }
                    break;
            }
        }
        catch (Exception error) { GD.PrintErr(error); foreach (var gateway in _gateways) { gateway.Dispose(); } GetTree().Quit(1); }
    }

    private void Grant(ulong player)
    {
        var host = _arenas[0].Driver.Host!;
        Check(host.Items.Grant(host.World, player, HeldItem.ProxyMine), "grant mine");
        Check(_arenas[(int)player-1].Driver.RequestItemUse(), "ordinary use request");
    }
    private void DeploySurface()
    {
        Position(2, new N.Vector3(_surface * 50, 21.5f, 0));
        var host = _arenas[0].Driver.Host!;
        Check(host.Items.Grant(host.World, 2, HeldItem.ProxyMine), "remote mine grant");
        // A reliable grant must arrive before the remote ordinary input-use request.
        _ = RequestRemoteUse();
    }
    private async Task RequestRemoteUse()
    {
        for (int i=0; i<120; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (_arenas[1].Driver.LocalItem?.Active.Item == HeldItem.ProxyMine) { Check(_arenas[1].Driver.RequestItemUse(), "remote ordinary use"); return; }
        }
        GD.PrintErr("Remote mine grant timeout"); GetTree().Quit(1);
    }
    private void Trial(float distance)
    {
        var host = _arenas[0].Driver.Host!;
        var mine = _mine! with { SeatingTicks = 0, Velocity = N.Vector3.Zero };
        host.Items.Restore(new(1, host.Snapshot(), host.Items.Slots, [], [], mines: [mine]), host.Items.Revision + 1, host.Items.TokenHighWater);
        Position(2, mine.Position + new N.Vector3(distance,0.65f,0));
        Focus(mine.Position);
    }
    private void Position(ulong id, N.Vector3 position, N.Vector3 velocity = default)
    {
        var host = _arenas[0].Driver.Host!;
        var world = host.World.State;
        var pose = new VehiclePhysicsState(position, _surface == 1 && id == 2 ? Rotation : N.Quaternion.Identity, velocity, N.Vector3.Zero);
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != id ? v :
            new VehicleSnapshot(id, v.LifeId, new VehicleState(world.Tick, pose, true, false, 0, 0), v.Damage, pose)), world.Match));
        _arenas[0].Bodies[id].Apply(pose);
    }
    private void Focus(N.Vector3 point)
    {
        var camera = _views[0].GetCamera3D();
        camera.Position = new Vector3(point.X + 6, point.Y + 5, point.Z + 8);
        camera.LookAt(new Vector3(point.X, point.Y, point.Z));
    }
    private void Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        string path = ProjectSettings.GlobalizePath("res://.godot/mine-checks");
        System.IO.Directory.CreateDirectory(path);
        _views[0].GetTexture().GetImage().SavePng(System.IO.Path.Combine(path, name));
    }
    private void Next(string text) { _evidence.Add(text); GD.Print(text); _stage++; _boundary = _frames; }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
