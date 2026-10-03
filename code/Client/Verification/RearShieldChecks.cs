using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native rear armor, real UDP replication, spatial shots and moving collision scenarios.</summary>
public sealed partial class RearShieldChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _wires = [];
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<SubViewport> _views = [];
    private readonly List<string> _evidence = [];
    private int _frame, _boundary, _stage, _scenario;
    private float _vehicleHP, _shieldHP;
    private bool _done;
    private string _output = "";
    private TombstoneState[] _cyclePools = [];
    private int _cycles;

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _output = ProjectSettings.GlobalizePath("res://.godot/rear-shield-checks");
        System.IO.Directory.CreateDirectory(_output);
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        for (int i = 0; i < 2; i++)
        {
            var wire = new GameNetworkingSocketsTransport();
            ulong server = 0;
            if (i == 0) { wire.Listen(TransportEndpoint.DirectIp(endpoint)); }
            else { server = wire.Connect(TransportEndpoint.DirectIp(endpoint)); }
            if (OS.GetCmdlineUserArgs().Contains("--rear-shield-impaired")) { wire.ConfigureSimulation(new(30, 5, 2, 0, 0)); }
            _wires.Add(wire);
            var view = new SubViewport { Size = new(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            _views.Add(view);
            if (i == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
            else { AddChild(view); }
            var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
            arena.Initialize(wire, i == 0 ? 88ul : 0, server);
            view.AddChild(arena);
            var floor = new StaticBody3D { Position = new(0, 200, 0), CollisionLayer = 1 };
            floor.AddToGroup("landing_terrain");
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(600, 1, 600) } });
            floor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(600, 1, 600) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(0.23f, 0.25f, 0.28f) } });
            arena.AddChild(floor);
            var camera = new Camera3D { Position = new(9, 207, 14) };
            arena.AddChild(camera); camera.LookAt(new(0, 201.5f, 2)); camera.MakeCurrent();
            _arenas.Add(arena);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;
        if (_done) { if (_frame > _boundary + 10) { GetTree().Quit(); } return; }
        try
        {
            if (_stage == 3 && _scenario is not (1 or 6 or 7)) { Position(); }
            int elapsed = _frame - _boundary;
            for (int i = 0; i < 2; i++)
            {
                bool fire = _stage == 3 && _scenario != 7 && i == 0;
                ushort throttle = _stage == 3 && _scenario is 1 or 6 ? ushort.MaxValue : (ushort)0;
                var input = new InputFrame((ulong)_frame, 0, throttle, 0, fire ? InputButtons.UseItem : 0,
                    fire && elapsed == 1 ? InputButtons.UseItem : 0, 0);
                _arenas[i].Advance(input);
                Check(_arenas[i].Driver.Failure.Length == 0, _arenas[i].Driver.Failure);
            }
            Check(elapsed < 1200, $"Rear shield stage {_stage}, scenario {_scenario} timed out.");
            var host = _arenas[0].Driver.Host!;
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2):
                    Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1,
                        ["match.minimum_players"] = 1, ["items.machine_gun_spread"] = 0, ["items.machine_gun_fire_rate"] = 60 }, out _), "Fixture configuration");
                    Next(5); break;
                case 5 when host.World.State.Match?.Phase == Trackstorm.Core.Matches.MatchPhase.Active:
                    Setup(); break;
                case 1 when host.Items.Tombstones.Count == 2 && host.Items.Tombstones.All(s => s.Stage == TombstoneStage.RearShield) &&
                    _arenas.All(a => a.Bodies.Count == 2 && a.Bodies.Values.All(b => b.HasRearShield)):
                    Check(host.Items.Tombstones.All(s => s.HP == (_scenario == 6 && s.Owner == 2 ? 10 : 900)), "Deployment preserves damaged pools");
                    _cyclePools = host.Items.Tombstones.ToArray(); _cycles = 0;
                    foreach (var arena in _arenas) { Check(arena.Driver.RequestItemSwitch(), "Ordinary switch stows shield"); }
                    Next(10); break;
                case 10 when elapsed > 30 && _arenas.All(a => a.Bodies.Values.All(b => !b.HasRearShield)):
                    Check(host.Items.Tombstones.SequenceEqual(_cyclePools.Select(s => s with { Stage = TombstoneStage.Held })), "Stowing preserves both damaged identities");
                    Capture($"{_scenario}-stored.png");
                    foreach (var arena in _arenas) { Check(arena.Driver.RequestItemSwitch(), "Ordinary switch exposes shield without use"); }
                    Next(11); break;
                case 11 when elapsed > 30 && _arenas.All(a => a.Bodies.Values.All(b => b.HasRearShield)):
                    Check(host.Items.Tombstones.SequenceEqual(_cyclePools), "Reselection restores exact pools");
                    if (++_cycles < 3)
                    {
                        foreach (var arena in _arenas) { Check(arena.Driver.RequestItemSwitch(), "Repeated switch"); }
                        Next(10); break;
                    }
                    Check(!host.Items.RequestUse(host.World, 2, host.World.GetVehicle(2).LifeId, host.Items.Slots.Single(s => s.Vehicle == 2).Active.Token), "Use cannot deploy a wall");
                    Check(host.Items.Grant(host.World, 1, HeldItem.MachineGun), "Shooter second slot");
                    Check(_arenas[0].Driver.RequestItemSwitch(), "Select weapon and stow shooter shield");
                    Next(2); break;
                case 2 when elapsed > 40:
                    Check(_arenas.All(a => !a.Bodies[1].HasRearShield && a.Bodies[2].HasRearShield), "Only selected shield has physical cover on both peers");
                    Position();
                    _vehicleHP = host.World.GetVehicle(2).Damage.CurrentHP;
                    _shieldHP = host.Items.Tombstones.Single(s => s.Owner == 2).HP;
                    Capture($"{_scenario}-deployed.png");
                    Next(3); break;
                case 3 when elapsed >= (_scenario == 7 ? 90 : 40):
                    float vehicle = host.World.GetVehicle(2).Damage.CurrentHP;
                    float shield = host.Items.Tombstones.SingleOrDefault(s => s.Owner == 2)?.HP ?? 0;
                    bool blocked = _scenario is 0 or 1 or 2 or 7;
                    if (blocked) { Check(shield < _shieldHP && vehicle == _vehicleHP, $"Scenario {_scenario}: shield {shield}/{_shieldHP}, vehicle {vehicle}/{_vehicleHP}"); }
                    else if (_scenario == 6) { Check(shield == 0 && vehicle < _vehicleHP, "Destruction removes cover during driving and sustained fire"); }
                    else { Check(shield == _shieldHP && vehicle < _vehicleHP, $"Uncovered scenario {_scenario}: shield {shield}, vehicle {vehicle}"); }
                    Check(host.Items.Tombstones.Single(s => s.Owner == 1).HP == 900, "Other vehicle shield remains independent");
                    _evidence.Add($"Scenario {_scenario}: shield {_shieldHP} -> {shield}, protected vehicle {_vehicleHP} -> {vehicle}; other shield 900 HP.");
                    GD.Print(_evidence.Last()); Capture($"{_scenario}-result.png"); Next(4); break;
                case 4 when elapsed > 60:
                    Check(_arenas.All(a => a.Driver.ItemState!.Tombstones.SequenceEqual(host.Items.Tombstones)), "Replicated pools converge");
                    Check(_arenas.All(a => a.Bodies[2].HasRearShield == host.Items.Tombstones.Any(s => s.Owner == 2)), "Native shield removal converges");
                    _cyclePools = host.Items.Tombstones.ToArray();
                    Check(_arenas[1].Driver.RequestItemSwitch(), "Stow damaged or destroyed shield after impact");
                    Next(12); break;
                case 12 when elapsed > 40 && _arenas.All(a => !a.Bodies[2].HasRearShield):
                    Check(host.Items.Tombstones.SequenceEqual(_cyclePools.Select(s => s.Owner == 2 ? s with { Stage = TombstoneStage.Held } : s)), "Post-hit stow preserves damage");
                    Check(_arenas[1].Driver.RequestItemSwitch(), "Reselect damaged shield");
                    Next(13); break;
                case 13 when elapsed > 40:
                    Check(host.Items.Tombstones.SequenceEqual(_cyclePools), "Post-hit reselect preserves identity and HP or absence");
                    Check(_arenas.All(a => a.Driver.ItemState!.Tombstones.SequenceEqual(_cyclePools)), "Selection recovery converges");
                    if (++_scenario < 8) { Setup(); }
                    else
                    {
                        System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
                        GD.Print("Rear shield integration passed: selection/stow cycles without use, persistent damaged identity, native rays, moving fire, edge/front/side, destruction, collision and UDP replication.");
                        _done = true; _boundary = _frame;
                        foreach (var arena in _arenas) { arena.QueueFree(); }
                        foreach (var wire in _wires) { wire.Dispose(); }
                    }
                    break;
            }
        }
        catch (Exception error)
        {
            GD.PrintErr(error); _done = true;
            foreach (var arena in _arenas) { arena.QueueFree(); }
            foreach (var wire in _wires) { wire.Dispose(); }
            GetTree().Quit(1);
        }
    }

    private void Setup()
    {
        var host = _arenas[0].Driver.Host!;
        foreach (ulong id in new ulong[] { 1, 2 })
        {
            ulong previousSelection = host.Items.Slots.SingleOrDefault(s => s.Vehicle == id)?.SelectionRevision ?? 0;
            host.Items.RemovePlayer(id);
            // Scenario reset preserves the monotonic input boundary without reusing a stale toggle.
            ulong boundary = (previousSelection + 1) & ~1ul;
            if (boundary > 0) { host.Items.Switch(host.World, id, host.World.GetVehicle(id).LifeId, boundary); }
            Check(host.Items.Grant(host.World, id, HeldItem.Tombstone), "Fixture shield grant");
            var stone = host.Items.Tombstones.Single(s => s.Owner == id);
            host.Items.DamageTombstone(host.World, stone.Id, 7, _scenario == 6 && id == 2 ? 990 : 100, new("world", 0, "fixture"));
        }
        Position(); Next(1);
    }

    private void Position()
    {
        var host = _arenas[0].Driver.Host!;
        var world = host.World.State;
        N.Vector3 shooter = _scenario switch { 2 => new(4, 201.65f, 10), 3 => new(7, 201.65f, 10),
            4 => new(10, 201.65f, 0), 5 => new(0, 201.65f, -10), 7 => new(0, 201.65f, 8), _ => new(0, 201.65f, 10) };
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v =>
        {
            bool firing = v.VehicleId == 1;
            var heading = firing ? N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, MathF.Atan2(shooter.X, shooter.Z)) : N.Quaternion.Identity;
            var velocity = _scenario == 7 && firing && _stage == 2 ? new N.Vector3(0, 0, -20) : N.Vector3.Zero;
            var pose = new VehiclePhysicsState(firing ? shooter : new(0, 201.65f, 0), heading, velocity, N.Vector3.Zero);
            _arenas[0].Bodies[v.VehicleId].Apply(pose);
            return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(world.Tick, pose, true, false, 0, 0), v.Damage, pose);
        }), world.Match));
    }

    private void Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        using var frame = _views[0].GetTexture().GetImage(); frame.SavePng(System.IO.Path.Combine(_output, name));
    }
    private void Next(int stage) { _stage = stage; _boundary = _frame; }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
