using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native terrain placement, real UDP state delivery and sustained oil handling exercise.</summary>
public sealed partial class OilIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<SubViewport> _views = new();
    private string _endpoint = "";
    private int _frames;
    private int _stage;
    private int _boundary;
    private OilPatch? _patch;
    private bool _done;
    private readonly List<string> _evidence = new();
    private readonly Dictionary<ulong, MatchState> _matches = new();
    private readonly double[] _oilPoints = new double[4];
    private bool Play => OS.GetCmdlineUserArgs().Contains("--oil-play");
    private Label? _playStatus;
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
            if (OS.GetCmdlineUserArgs().Contains("--oil-impaired")) { gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
        }
        else { server = gateway.Connect(TransportEndpoint.DirectIp(_endpoint)); }
        _gateways.Add(gateway);
        var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        if (index == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
        else { AddChild(view); }
        _views.Add(view);
        var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
        arena.Initialize(gateway, index == 0 ? 88ul : 0, server);
        arena.Driver.MatchReceived += match =>
        {
            if (index == 0) { _matches[match.Revision] = match; }
            else
            {
                Check(_matches.TryGetValue(match.Revision, out var authoritative) && match.Players.SequenceEqual(authoritative.Players), "exact reliable score revision");
            }
            foreach (var award in match.Awards.Where(a => a.Category == CircusScoreCategory.Oil))
            {
                Check(award.Player == 1 && award.Points == 50, "Oil owner receives one 50-point award");
                _oilPoints[index] += award.Points;
            }
        };
        view.AddChild(arena);
        var bank = new StaticBody3D { Position = new Vector3(0, 20, 0), Rotation = new Vector3(0, 0, 0.25f), CollisionLayer = 1 };
        bank.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40, 1, 40) } });
        bank.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(40, 1, 40) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.34f, 0.36f) } });
        arena.AddChild(bank);
        var camera = new Camera3D { Position = new Vector3(15, 35, 23) };
        arena.AddChild(camera);
        camera.LookAt(new Vector3(0, 20, 2));
        camera.MakeCurrent();
        _arenas.Add(arena);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done && !Play) { if (++_frames > _boundary + 20) { GetTree().Quit(); } return; }
        try
        {
            _frames++;
            foreach (var arena in _arenas)
            {
                arena.Advance(default);
                Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
            }
            var host = _arenas[0].Driver.Host!;
            if (_done)
            {
                _playStatus!.Text = $"Oil awards: host {_oilPoints[0]}, peer {_oilPoints[1]}, late peer {_oilPoints[2]}\nOwner traction ticks: {host.World.GetVehicle(1).Movement.OilTicks}; rival: {host.World.GetVehicle(2).Movement.OilTicks}";
                return;
            }
            Check(_frames - _boundary < 1800, $"Oil stage {_stage} timeout");
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2) && host.World.State.Match?.Phase == MatchPhase.Active:
                    Position(1, new N.Vector3(0, 20, 0) + Normal * 1.4f);
                    Check(host.Items.Grant(host.World, 1, HeldItem.Oil), "grant");
                    Check(_arenas[0].Driver.RequestItemUse(), "ordinary host use");
                    Next("Two native UDP peers ready; Oil requested through the normal held slot.");
                    break;
                case 1 when host.Items.Patches.Count == 1 && _arenas[1].Driver.ItemState?.Patches.Count == 1:
                    _patch = host.Items.Patches.Single();
                    Check(N.Vector3.Dot(_patch.Normal, Normal) > 0.999f, "bank normal");
                    Check(Math.Abs(N.Vector3.Dot(_patch.Position - new N.Vector3(0, 20, 0), Normal) - 0.5f) < 0.02f, "bank elevation");
                    Check(host.Items.Slots.Single().Item == HeldItem.None, "consumed once");
                    AddPeer();
                    Next("Host terrain query deployed on a 14-degree bank; peer received the exact patch.");
                    break;
                case 2 when _arenas.All(a => a.Driver.ItemState?.Patches.Count == 1 && a.Driver.Latest?.Vehicles.Count == 3):
                    Check(_arenas.All(a => a.Driver.ItemState!.Patches.Single().Id == _patch!.Id), "late join exact patch");
                    Position(1, _patch!.Position + Normal * 0.9f, new N.Vector3(0, 0, -15));
                    Next("Fresh late join restored one identical patch without duplication; owner enters at speed.");
                    break;
                case 3 when host.World.GetVehicle(1).Movement.OilTicks > 0:
                    Check(Math.Abs(N.Vector3.Dot(host.World.GetVehicle(1).Movement.Physics.AngularVelocity, Normal)) < 0.5f, "no forced entry yaw");
                    Check(_oilPoints.All(points => points == 0), "self-trigger awards zero offensive points");

                    Next("Owner receives temporary traction loss without a forced spin, score; one pass is consumed.");
                    break;
                case 4 when _frames - _boundary > 180:
                    Capture("banked-oil.png");
                    Check(host.Items.Patches.Single().PassesUsed == 1, "first owner pass remains consumed after leaving");
                    Check(host.World.GetVehicle(1).Movement.OilTicks == 0, "effect recovers");
                    Position(1, _patch!.Position + Normal * 0.9f);
                    Next("Owner exits, recovers, and returns for a second pass.");
                    break;
                case 5 when _frames - _boundary > 5 && _arenas.All(a => a.Driver.ItemState?.Patches.Count == 0):
                    Check(_oilPoints.All(points => points == 0), "two owner passes score zero");
                    Position(1, new N.Vector3(0, 20, 0) + Normal * 1.4f);
                    Check(host.Items.Grant(host.World, 1, HeldItem.Oil) && _arenas[0].Driver.RequestItemUse(), "repeat deployment after owner consumption");
                    Next("The owner's second pass removed the patch on all peers without score; deploy again.");
                    break;
                case 6 when _arenas.All(a => a.Driver.ItemState?.Patches.Count == 1):
                    _patch = host.Items.Patches.Single();
                    Position(1, new N.Vector3(-10, 20, 0) + Normal * 1.4f);
                    Position(2, _patch.Position + Normal * 0.9f);
                    Next("Remote enemy begins the first pass through a new patch.");
                    break;
                case 7 when host.World.GetVehicle(2).Movement.OilTicks > 0:
                    Check(_oilPoints[0] == 50 && host.Items.Patches.Single().PassesUsed == 1, "enemy first pass banks 50 and consumes one pass");
                    Next("Remote first pass applied and scored once; remain inside.");
                    break;
                case 8 when _frames - _boundary > 5:
                    Check(host.Items.OilContacts.Any(c => c.Vehicle == 2), "inside latch");
                    Check(_oilPoints[0] == 50 && host.Items.Patches.Single().PassesUsed == 1, "continuous overlap never consumes another pass");
                    Position(1, new N.Vector3(0, 20, 0) + N.Vector3.Transform(new N.Vector3(10, 0, 0), Rotation) + Normal * 1.4f);
                    Check(host.Items.Grant(host.World, 1, HeldItem.Oil) && _arenas[0].Driver.RequestItemUse(), "deployment remains available with an active patch");
                    Next("Continuous overlap counted once; deploy another simultaneous patch.");
                    break;
                case 9 when _frames - _boundary > 5 && _arenas.All(a => a.Driver.ItemState?.Patches.Count == 2):
                    Check(host.Items.Slots.Single().Item == HeldItem.None, "new Oil consumed with active patches");
                    Position(2, _patch!.Position + Normal * 0.9f + new N.Vector3(0, 0, 10));
                    Next("Two simultaneous patches replicated; enemy leaves the original patch.");
                    break;
                case 10 when _frames - _boundary > 5:
                    Check(!host.Items.OilContacts.Any(c => c.Patch == _patch!.Id && c.Vehicle == 2), "exit releases overlap latch");
                    Position(2, _patch!.Position + Normal * 0.9f);
                    Next("The same enemy returns for the second pass.");
                    break;
                case 11 when _frames - _boundary > 5 && _oilPoints.Take(3).All(points => points == 100) && _arenas.All(a => a.Driver.ItemState?.Patches.Count == 1):
                    Check(host.Items.Patches.All(p => p.Id != _patch!.Id), "same enemy's second pass removed only the original patch");
                    Check(host.Items.Patches.Single().PassesUsed == 0, "separate patch budget is untouched");
                    _evidence.Add("Two owner passes removed their patch for zero points; two passes by the same enemy removed another patch and banked 50 each on all three peers.");
                    _patch = OilRecoveryFixture.Seed(_arenas[0]);
                    Next("Seeded 1,500 detached patches with one consumed pass to exercise complete continuation.");
                    break;
                case 12 when _arenas.All(a => a.Driver.ItemState?.Patches.Count == OilRecoveryFixture.PatchCount):
                    foreach (var arena in _arenas) { OilRecoveryFixture.Verify(arena.Driver.ItemState!, _patch!); }
                    AddPeer();
                    Next("Large publication converged atomically; a fourth peer requests a complete join checkpoint.");
                    break;
                case 13 when _arenas.All(a => a.Driver.ItemState?.Patches.Count == OilRecoveryFixture.PatchCount && a.Driver.Latest?.Vehicles.Count == 4):
                    foreach (var arena in _arenas) { OilRecoveryFixture.Verify(arena.Driver.ItemState!, _patch!); }
                    _evidence.Add("All four peers retain 1,500 exact patch identities, deadlines and consumed-pass counts after fresh admission.");
                    var path = ProjectSettings.GlobalizePath("res://.godot/oil-checks");
                    System.IO.Directory.CreateDirectory(path);
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(path, "evidence.txt"), _evidence);
                    GD.Print("Oil integration passed: " + string.Join("\n", _evidence));
                    _done = true;
                    _boundary = _frames;
                    if (Play) { BeginPlay(); break; }
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

    private void BeginPlay()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var panel = new VBoxContainer { Position = new Vector2(20, 20) }; layer.AddChild(panel);
        _playStatus = new Label(); panel.AddChild(_playStatus);
        foreach (ulong id in new ulong[] { 1, 2 })
        {
            var button = new Button { Text = id == 1 ? "Drive owner through Oil" : "Drive rival through Oil" };
            button.Pressed += () =>
            {
                Position(id, _patch!.Position + Normal * 0.9f + new N.Vector3(0, 0, 5), new N.Vector3(0, 0, -8));
                GD.Print($"Interactive Oil crossing requested for player {id}.");
            };
            panel.AddChild(button);
        }
        var quit = new Button { Text = "Finish playtest" };
        quit.Pressed += () =>
        {
            Capture("interactive-oil.png");
            GD.Print($"Interactive Oil final awards: {string.Join(", ", _oilPoints)}");
            foreach (var gateway in _gateways) { gateway.Dispose(); }
            GetTree().Quit();
        };
        panel.AddChild(quit);
    }

    private void Position(ulong id, N.Vector3 position, N.Vector3 velocity = default)
    {
        var host = _arenas[0].Driver.Host!;
        var world = host.World.State;
        var pose = new VehiclePhysicsState(position, Rotation, velocity, N.Vector3.Zero);
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != id ? v :
            new VehicleSnapshot(id, v.LifeId, new VehicleState(world.Tick, pose, true, false, 0, 0), v.Damage, pose)), world.Match));
        _arenas[0].Bodies[id].Apply(pose);
    }

    private void Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        string path = ProjectSettings.GlobalizePath("res://.godot/oil-checks");
        System.IO.Directory.CreateDirectory(path);
        _views[0].GetTexture().GetImage().SavePng(System.IO.Path.Combine(path, name));
    }

    private void Next(string text) { _evidence.Add(text); GD.Print(text); _stage++; _boundary = _frames; }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
