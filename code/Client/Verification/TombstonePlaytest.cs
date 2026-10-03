using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Opt-in command-driven rendered playtest, using real production arenas and two UDP peers.</summary>
public sealed partial class TombstonePlaytest : Node
{
    private readonly List<GameNetworkingSocketsTransport> _wires = [];
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<SubViewport> _views = [];
    private string _output = "";
    private string _commandId = "";
    private int _frame, _remaining;
    private int _controlledPeer;
    private bool _ready;
    private ushort _throttle, _brake;
    private short _steer;
    private InputButtons _held, _pressed;
    private readonly List<object> _trace = [];

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _output = ProjectSettings.GlobalizePath("res://.godot/ts-219/playtest");
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
            _wires.Add(wire);
            var view = new SubViewport { Size = new(1440, 900), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            _views.Add(view);
            if (i == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
            else { AddChild(view); }
            var arena = new NetworkVehicleArena { PrototypeMapForVerification = true, SpawnConfiguration = new() { PickupRadius = .01f } };
            arena.Initialize(wire, i == 0 ? 219ul : 0, server);
            view.AddChild(arena);
            var floor = new StaticBody3D { Position = new(0, 200, 0), CollisionLayer = 1 };
            floor.AddToGroup("landing_terrain");
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 1, 200) } });
            floor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(200, 1, 200) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.19f, .21f, .23f) } });
            arena.AddChild(floor);
            var camera = new Camera3D { Name = "InspectionCamera", Position = new(5, 204, 9), Fov = 55 };
            arena.AddChild(camera);
            camera.LookAt(new(0, 201.7f, 1)); camera.MakeCurrent();
            _arenas.Add(arena);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            _frame++;
            var host = _arenas[0].Driver.Host!;
            if (!_ready && _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2))
            {
                if (!host.TryConfigure(host.Configuration.Revision, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1, ["items.tombstone_lifetime"] = 300 }, out _))
                { throw new InvalidOperationException("Playtest tuning rejected."); }
                _ready = true;
                Place(1, new(0, 201.7f, 0)); Place(2, new(10, 201.7f, 0));
                System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "ready.txt"), "Two production UDP arenas ready.");
            }
            if (_ready && _remaining == 0) { ReadCommand(); }
            for (int i = 0; i < _arenas.Count; i++)
            {
                var input = i == _controlledPeer && _remaining > 0
                    ? new InputFrame((ulong)_frame, _steer, _throttle, _brake, _held, _pressed, 0)
                    : new InputFrame((ulong)_frame, 0, 0, 0, 0, 0, 0);
                _arenas[i].Advance(input);
                if (_arenas[i].Driver.Failure.Length != 0) { throw new InvalidOperationException(_arenas[i].Driver.Failure); }
            }
            _pressed = 0;
            if (_remaining > 0)
            {
                _trace.Add(new { frame = _frame, tick = host.World.State.Tick,
                    walls = _arenas[0].Walls.Bodies.Values.Select(b => new { id = b.Identity, expansion = b.Visual.Expansion, hp = b.Visual.PresentedHP, centerFold = b.Visual.CenterFold }).ToArray(),
                    shields = _arenas[0].Bodies.Values.Select(b => new { id = b.VehicleId, shield = b.HasRearShield, rack = b.Rack.Progress,
                        mount = b.ShieldMountProgress, visible = b.ShieldVisible, centerFold = b.ShieldCenterFold, scale = b.ShieldScale,
                        articulationClearance = b.ShieldVisible ? ArticulationClearance(b) : (float?)null }).ToArray(),
                    remoteShields = _arenas[1].Bodies.Values.Select(b => new { id = b.VehicleId, mount = b.ShieldMountProgress, visible = b.ShieldVisible, rack = b.Rack.Progress, centerFold = b.ShieldCenterFold, scale = b.ShieldScale }).ToArray() });
                if (--_remaining == 0) { Save(); }
            }
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    private void ReadCommand()
    {
        string path = System.IO.Path.Combine(_output, "input.json");
        if (!System.IO.File.Exists(path)) { return; }
        string json;
        try
        {
            using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete);
            using var reader = new System.IO.StreamReader(stream);
            json = reader.ReadToEnd();
        }
        catch (System.IO.IOException) { return; }
        using var document = JsonDocument.Parse(json);
        var command = document.RootElement;
        string id = command.GetProperty("id").GetString()!;
        if (id == _commandId) { return; }
        _commandId = id;
        if (command.TryGetProperty("quit", out var quit) && quit.GetBoolean()) { GetTree().Quit(); return; }
        float Number(string key, float fallback = 0) => command.TryGetProperty(key, out var value) ? value.GetSingle() : fallback;
        Vector3 Point(JsonElement value) { var a = value.EnumerateArray().Select(v => v.GetSingle()).ToArray(); return new(a[0], a[1], a[2]); }
        var host = _arenas[0].Driver.Host!;
        ulong owner = (ulong)Number("owner", 1);
        _controlledPeer = checked((int)owner - 1);
        if (command.TryGetProperty("spawn", out var spawn))
        {
            var p = Point(spawn);
            Place(owner, new(p.X, p.Y, p.Z), Number("yaw"));
        }
        HeldItem granted = command.TryGetProperty("item", out var item) ? Enum.Parse<HeldItem>(item.GetString()!) : HeldItem.Tombstone;
        if (command.TryGetProperty("grant", out var grant) && grant.GetBoolean() && !host.Items.Grant(host.World, owner, granted))
        { throw new InvalidOperationException("Fixture grant rejected; slots/live limit must be respected."); }
        if (command.TryGetProperty("damage", out var damage))
        {
            ulong entity = command.GetProperty("entity").GetUInt64();
            var stone = host.Items.Tombstones.Single(s => s.Id == entity);
            host.Items.DamageTombstone(host.World, entity, stone.DamageSequence + 1, damage.GetSingle(), new("world", 0, "playtest-damage"));
        }
        if (command.TryGetProperty("camera", out var camera))
        {
            Vector3 eye = Point(camera), target = Point(command.GetProperty("look"));
            foreach (var arena in _arenas)
            { var view = arena.GetNode<Camera3D>("InspectionCamera"); view.Position = eye; view.LookAt(target); view.MakeCurrent(); }
        }
        if (command.TryGetProperty("chase", out var chase) && chase.GetBoolean())
        { foreach (var arena in _arenas) { arena.GetNode<Camera3D>("ChaseCamera").MakeCurrent(); } }
        _remaining = Math.Clamp((int)Number("frames", 90), 1, 600);
        _throttle = (ushort)(Math.Clamp(Number("throttle"), 0, 1) * ushort.MaxValue);
        _brake = (ushort)(Math.Clamp(Number("brake"), 0, 1) * ushort.MaxValue);
        _steer = (short)(Math.Clamp(Number("steer"), -1, 1) * short.MaxValue);
        _held = 0;
        if (command.TryGetProperty("use", out var use) && use.GetBoolean()) { _held |= InputButtons.UseItem; }
        if (command.TryGetProperty("select", out var select) && select.GetBoolean()) { _held |= InputButtons.SwitchItem; }
        _pressed = _held;
        _trace.Clear();
    }

    private void Place(ulong id, N.Vector3 point, float yaw = 0)
    {
        var host = _arenas[0].Driver.Host!;
        var world = host.World.State;
        var pose = new VehiclePhysicsState(point, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, yaw), N.Vector3.Zero, N.Vector3.Zero);
        _arenas[0].Bodies[id].Apply(pose);
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != id ? v :
            new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(world.Tick, pose, true, false, 0, 0), v.Damage, pose)), world.Match));
    }

    private static float ArticulationClearance(NetworkVehicleBody body)
    {
        var model = (Node3D)body.FindChild("WastelandVehicle", true, false);
        var armor = body.FindChildren("*", "Node3D", true, false).OfType<Items.TombstoneVisual>().Single();
        var inverse = model.GlobalTransform.AffineInverse();
        Aabb Bounds(MeshInstance3D mesh) => (inverse * mesh.GlobalTransform) * mesh.GetAabb();
        var panels = armor.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().Select(Bounds).ToArray();
        // Conservative imported mesh bounds in the same moving Car frame. The rear
        // tires and trunk lids exercise suspension/spin and ordered rack articulation.
        var moving = new[] { "WheelCarrier_RL", "WheelCarrier_RR", "TrunkHinge_L", "TrunkHinge_R" }
            .SelectMany(path => model.GetNode(path).FindChildren("*", "MeshInstance3D", true, false))
            .Cast<MeshInstance3D>().Select(Bounds).ToArray();
        float minimum = float.MaxValue;
        foreach (var a in panels)
        {
            foreach (var b in moving)
            {
                Vector3 separation = (a.Position - b.End).Max(b.Position - a.End);
                minimum = Math.Min(minimum, Math.Max(separation.X, Math.Max(separation.Y, separation.Z)));
            }
        }
        return minimum;
    }

    private void Save()
    {
        for (int i = 0; i < _views.Count; i++)
        {
            if (DisplayServer.GetName() != "headless")
            {
                using var frame = _views[i].GetTexture().GetImage();
                frame.SavePng(System.IO.Path.Combine(_output, $"{_commandId}-peer{i}.png"));
            }
        }
        var host = _arenas[0].Driver.Host!;
        var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, _commandId + ".json"), JsonSerializer.Serialize(new
        {
            id = _commandId, tick = host.World.State.Tick, tombstones = host.Items.Tombstones,
            effects = _arenas[0].GetChildren().OfType<Items.TombstoneVisual>().Count() + _arenas[0].Walls.GetChildren().OfType<Items.TombstoneVisual>().Count(),
            vehicles = host.World.State.Vehicles.Select(v => new { id = v.VehicleId, pose = v.ObservedPhysics }),
            remote = _arenas[1].Driver.ItemState?.Tombstones, trace = _trace,
        }, options));
        GD.Print("Tombstone playtest capture: " + _commandId);
    }

    public override void _ExitTree()
    {
        foreach (var arena in _arenas) { if (GodotObject.IsInstanceValid(arena)) { arena.Free(); } }
        foreach (var wire in _wires) { wire.Dispose(); }
    }
}
