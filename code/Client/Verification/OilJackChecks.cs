using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Verification;

/// <summary>Controlled native trajectories, production rack, real UDP and rendered Oil evidence.</summary>
public sealed partial class OilJackChecks : Node
{
    private readonly List<ReplicationTrafficGateway> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<SubViewport> _views = new();
    private readonly List<string> _evidence = new();
    private readonly List<object> _trace = new();
    private int _stage;
    private int _frames;
    private int _boundary;
    private int _cycles;
    private ulong _id;
    private ulong _shooter = 1;
    private ulong _remoteFire;
    private OilPatch? _last;
    private ulong _fireEnd;
    private string _endpoint = "";
    private bool _done;
    private bool _wall;
    private bool _airborneFiring;
    private int _airborneEmitted;
    private ulong _expiration;
    private readonly Dictionary<ulong, OilPatch[]> _publications = new();
    private static readonly N.Vector3 Normal = N.Vector3.Transform(N.Vector3.UnitY, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, .25f));
    private static readonly N.Quaternion Rotation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, .25f);
    private static string Output => ProjectSettings.GlobalizePath("res://.godot/ts-245/runtime");

    public override void _Ready()
    {
        System.IO.Directory.CreateDirectory(Output);
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        AddPeer(); AddPeer();
    }
    private void AddPeer()
    {
        int index = _arenas.Count;
        var gateway = new ReplicationTrafficGateway(); ulong server = 0;
        if (index == 0) { gateway.Listen(TransportEndpoint.DirectIp(_endpoint)); if (OS.GetCmdlineUserArgs().Contains("--oil-impaired")) { gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); } }
        else { server = gateway.Connect(TransportEndpoint.DirectIp(_endpoint)); }
        _gateways.Add(gateway);
        var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        if (index == 0) { var container = new SubViewportContainer(); AddChild(container); container.AddChild(view); } else { AddChild(view); }
        _views.Add(view);
        var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
        arena.Initialize(gateway, index == 0 ? 245ul : 0, server);
        arena.Driver.ItemsReceived += publication =>
        {
            if (index == 0)
            {
                _publications[publication.World.Tick] = publication.Patches.ToArray();
                if (_publications.Count > 256) { _publications.Remove(_publications.Keys.Min()); }
            }
            else if (_publications.TryGetValue(publication.World.Tick, out var expected))
            { Check(publication.Patches.SequenceEqual(expected), "exact replicated Oil flight/geometry/quota/deadlines"); }
        };
        view.AddChild(arena); _arenas.Add(arena);
        var bank = new StaticBody3D { Position = new(0, 20, 0), Rotation = new(0, 0, .25f), CollisionLayer = 1 };
        bank.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } });
        bank.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(100, 1, 100) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.24f, .24f, .24f) } }); arena.AddChild(bank);
        var wall = new StaticBody3D { Position = new(0, 25, 6), CollisionLayer = 1 };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(30, 30, .5f) } });
        wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(30, 30, .5f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.3f,.28f,.25f) } }); arena.AddChild(wall);
        var camera = new Camera3D { Name = "OilEvidenceCamera", Position = new(6, 27, 9) }; arena.AddChild(camera); camera.LookAt(new(0, 23, 0)); camera.MakeCurrent();
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done) { return; }
        try
        {
            _frames++;
            if (_frames - _boundary > 2600) { throw new InvalidOperationException($"Oil jack stage {_stage} timeout"); }
            if (_stage == 3 && (_last?.Pour?.Emitted ?? 0) == 0) { Pose(new(0,40,-15), N.Quaternion.Identity, new(8,3,-10)); }
            if (_stage == 2 && _frames - _boundary < 220)
            {
                float a = (_frames - _boundary) * .009f;
                Pose(new N.Vector3(14 * MathF.Sin(a), 20, -14 * MathF.Cos(a)) + Normal * 1.4f,
                    N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -a) * Rotation, new(12 * MathF.Cos(a), 0, 12 * MathF.Sin(a)));
            }
            foreach (var arena in _arenas)
            {
                arena.Advance(default); Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
                if (arena == _arenas[0] && arena.Driver.Host is { } authority)
                {
                    _publications[authority.World.State.Tick] = authority.Items.Patches.ToArray();
                    if (_publications.Count > 512) { _publications.Remove(_publications.Keys.Min()); }
                }
            }
            if (_stage == 3 && _frames - _boundary == 100) { AddPeer(); _evidence.Add("Fresh peer joins during airborne firing and reconstructs active hardware/flight."); }
            var host = _arenas[0].Driver.Host!;
            if (_remoteFire != 0 && _arenas[1].Driver.LocalItem?.Active.Token == _remoteFire)
            { Check(_arenas[1].Driver.RequestItemUse(), "ordinary remote Oil wire request queued/sent"); _remoteFire = 0; }
            foreach (var p in host.Items.Patches)
            {
                Check(p.AdditionalSegments.Segments.Count <= 63 && (p.Pour?.Drops.Length ?? 0) <= 48, "bounded flight/footprint");
                Check(p.PassLimit == 2, "single captured two-contact quota");
                if (_stage == 4 && (Math.Abs(p.Normal.Y) < .1f || p.AdditionalSegments.Segments.Any(s => Math.Abs(s.Normal.Y) < .1f))) { _wall = true; }
            }
            if (_stage == 6 && host.World.State.Tick < _expiration) { Check(host.Items.Patches.Any(p => p.Id == _id), "untouched hazard remains until captured 30-second deadline"); }
            if (_frames % 10 == 0)
            {
                var p = host.Items.Patches.FirstOrDefault(p => p.Id == _id);
                _trace.Add(new { stage = _stage, frame = _frames, tick = host.World.State.Tick, hazards = host.Items.Patches.Count,
                    headOffset = p?.Pour is { } motion ? new[] { motion.HeadOffset.X, motion.HeadOffset.Y, motion.HeadOffset.Z } : null,
                    emitted = p?.Pour?.Emitted, flight = p?.Pour?.Drops.Length, deposited = p?.Pour?.Deposited, segments = p?.AdditionalSegments.Segments.Count,
                    rack = _arenas[0].Bodies.GetValueOrDefault(1ul)?.Rack.Progress, presented = _arenas[0].Bodies.GetValueOrDefault(1ul)?.Rack.PresentedItem.ToString() });
            }
            var current = host.Items.Patches.FirstOrDefault(p => p.Id == _id);
            if (_stage == 3 && current?.Pour is { } air && air.Emitted > _airborneEmitted)
            { _airborneFiring |= !host.World.GetVehicle(1).Movement.Grounded; _airborneEmitted = air.Emitted; }
            if (current is not null) { _last = current; _fireEnd = current.Pour!.StartTick + (ulong)current.Pour.DurationTicks; }
            if (_stage is >= 1 and <= 5 && _frames - _boundary == 140) { Capture($"stage-{_stage}-firing.png"); }
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2) && host.World.State.Match?.Phase == MatchPhase.Active:
                    Check(Items.OilSurfaceQuery.Cast(_arenas[0].GetWorld3D().DirectSpaceState, new(0,25,6), new(0,25,7), .65f) is { Radius: 0 }, "embedded static impact retires without phantom deposition");
                    if (OS.GetCmdlineUserArgs().Contains("--oil-query-only"))
                    {
                        GD.Print("Oil query safety passed."); _done = true;
                        foreach (var arena in _arenas) { arena.QueueFree(); } foreach (var gateway in _gateways) { gateway.Dispose(); }
                        GetTree().CreateTimer(.5).Timeout += () => GetTree().Quit(); break;
                    }
                    Pose(new N.Vector3(0,20,0) + Normal * 1.4f, Rotation, new(0,0,-10)); Fire(); Next("Native banked straight firing with two real UDP peers."); break;
                case 1 when _frames - _boundary > 360:
                    Check(_last?.Pour is { Deposited: true, Emitted: 40 }, "two-second straight pour");
                    Check(N.Vector3.Dot(_last!.Normal, Normal) > .99f, "banked surface impact orientation");
                    Capture("straight-deposits.png"); Fire(); Next("Controlled turning/drifting launch poses through native collision."); break;
                case 2 when _frames - _boundary > 360:
                    Check(_last!.AdditionalSegments.Segments.Count > 12, "curved multipart spill"); Capture("curved-deposits.png");
                    Pose(new(0,40,-15), N.Quaternion.Identity, new(8,3,-10)); Fire(); Next("Airborne activation with native gravity and landing."); break;
                case 3 when _frames - _boundary > 360:
                    Check(_airborneFiring, "native parcel emission while the vehicle has no wheel support");
                    Check(_last?.Pour is { Deposited: true, Emitted: 40 }, "airborne deposition completes"); Capture("airborne-deposits.png");
                    Pose(new(0,35,2), N.Quaternion.Identity, N.Vector3.Zero); Fire(); Next("Native first-hit deposition on a vertical world wall."); break;
                case 4 when _frames - _boundary > 360:
                    Check(_wall, "vertical surface deposition observed"); Capture("wall-deposits.png");
                    Pose(new N.Vector3(-20,20,-20) + Normal * 1.4f, Rotation, N.Vector3.Zero); Fire(); Next("Replacement pickup while Oil owns the shared rack."); break;
                case 5:
                    if (current is not null && host.Items.Slots.Single(s=>s.Vehicle==1).Active.Item == HeldItem.None)
                    { Check(host.Items.Grant(host.World, 1, HeldItem.Wrench), "replacement acquired"); }
                    if (current is not null && host.World.State.Tick < _fireEnd)
                    { Check(_arenas[0].Bodies[1].Rack.PresentedItem != HeldItem.Wrench, "replacement cannot interrupt Oil firing"); }
                    if (_frames - _boundary > 420)
                    {
                        Check(_arenas[0].Bodies[1].Rack.PresentedItem == HeldItem.Wrench, "replacement appears after jester/box/rack return"); Capture("replacement-ready.png");
                        _expiration = _last!.ExpiresAtTick; Check(_last.PassesUsed == 0, "expiry scenario hazard has no contacts");
                        AddPeer(); Pose(new N.Vector3(35,20,-35) + Normal * 1.4f, Rotation, N.Vector3.Zero);
                        Next("Late admission, retained deadlines and 30-second cleanup.");
                    }
                    break;
                case 6 when _frames - _boundary > 1900 && _arenas.All(a=>a.Driver.ItemState?.Patches.Count==0):
                    Check(_arenas.Count==4 && _arenas.All(a=>a.Driver.Latest?.Vehicles.Count==4), "late peer joined");
                    var slot=host.Items.Slots.Single(s=>s.Vehicle==1); Check(host.Items.Discard(host.World, 1, slot.Life, slot.Active.Token, slot.SelectionRevision), "clear fixture replacement");
                    Fire(); Next("Twelve repeated production activations and bounded cleanup."); break;
                case 7 when _frames - _boundary > 320:
                    _cycles++; Check(_last?.Pour is { Emitted: 40 }, "repeated activation completes");
                    if (_cycles < 12) { Fire(_cycles % 2 == 0 ? 1ul : 2ul); _boundary=_frames; }
                    else { Capture("repeated-use.png"); Next("Repeated use completed; wait for the final deadline."); } break;
                case 8 when _frames - _boundary > 1900 && _arenas.All(a=>a.Driver.ItemState?.Patches.Count==0):
                    _evidence.Add("All hazards and owned visual/flight representations expire on four peers; no runtime exception was accepted.");
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(Output,"evidence.txt"),_evidence);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(Output,"trace.json"),System.Text.Json.JsonSerializer.Serialize(_trace));
                    GD.Print("Oil jack integration passed: "+string.Join("\n",_evidence)); _done=true;
                    foreach(var arena in _arenas){arena.QueueFree();} foreach(var g in _gateways){g.Dispose();}
                    GetTree().CreateTimer(.5).Timeout += () => GetTree().Quit(); break;
            }
        }
        catch(Exception e){ GD.PrintErr(e); foreach(var g in _gateways){g.Dispose();} GetTree().Quit(1); }
    }
    private void Fire(ulong shooter = 1)
    {
        var host=_arenas[0].Driver.Host!; _shooter = shooter; Check(host.Items.Grant(host.World,shooter,HeldItem.Oil),"Oil granted");
        var inventory=host.Items.Slots.Single(s=>s.Vehicle==shooter); _id=inventory.Active.Token; _last=null;
        if (shooter == 1) { Check(_arenas[0].Driver.RequestItemUse(),"ordinary Oil use queued/sent"); }
        else { _remoteFire = _id; }
    }
    private void Pose(N.Vector3 position,N.Quaternion orientation,N.Vector3 velocity)
    {
        var host=_arenas[0].Driver.Host!; var w=host.World.State; var pose=new VehiclePhysicsState(position,orientation,velocity,N.Vector3.Zero);
        host.World.Restore(new(w.Tick,w.LastInput,w.Vehicles.Select(v=>v.VehicleId!=1?v:new VehicleSnapshot(1,v.LifeId,new VehicleState(w.Tick,pose,true,false,0,0),v.Damage,pose)),w.Match));
        _arenas[0].Bodies[1].Apply(pose);
    }
    public override void _Process(double delta)
    {
        if (_done || _arenas.Count == 0 || !_arenas[0].Bodies.TryGetValue(_shooter, out var car)) { return; }
        var camera = _arenas[0].GetNode<Camera3D>("OilEvidenceCamera");
        camera.GlobalPosition = car.GlobalPosition + new Vector3(4, 3, 6);
        camera.LookAt(car.GlobalPosition + Vector3.Up * 1.65f);
    }
    private void Capture(string name)
    {
        if(DisplayServer.GetName()=="headless"){return;}
        var car=_arenas[0].Bodies[_shooter]; var camera=_arenas[0].GetNode<Camera3D>("OilEvidenceCamera");
        camera.GlobalPosition=car.GlobalPosition+new Vector3(5,4,7); camera.LookAt(car.GlobalPosition+Vector3.Up*1.5f);
        _views[0].GetTexture().GetImage().SavePng(System.IO.Path.Combine(Output,name));
    }
    private void Next(string text){GD.Print(text);_evidence.Add(text);_stage++;_boundary=_frames;}
    private static void Check(bool value,string text){if(!value){throw new InvalidOperationException(text);}}
}
