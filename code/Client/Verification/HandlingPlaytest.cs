using System.Text.Json;
using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Explicit verification scene for observe/drive/observe playtesting with bounded logical input segments.</summary>
public sealed partial class HandlingPlaytest : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private VehicleBody _body = null!;
    private Camera3D _camera = null!;
    private string _directory = "";
    private string _lastCommand = "";
    private int _remaining;
    private short _steer;
    private ushort _throttle;
    private ushort _brake;
    private InputButtons _buttons;
    private readonly List<object> _trace = new();
    private bool _ready;
    private StaticBody3D? _road;
    private Core.Arenas.EnvironmentAuthority? _environment;
    private Core.Arenas.EnvironmentLayout? _environmentLayout;
    private Arenas.DestructibleEnvironment? _environmentView;
    private readonly Core.Items.ItemAuthority _items = new(new() { MaximumDamage = 300 });
    private readonly List<Core.Items.ItemEvent> _impacts = new();

    public override void _Ready()
    {
        _directory = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--destructible-playtest") ? "res://.godot/ts-162/playtest" : "res://.godot/ts-160/playtest");
        System.IO.Directory.CreateDirectory(_directory);
        if (OS.GetCmdlineUserArgs().Contains("--handling-flat"))
        {
            _road = new StaticBody3D();
            _road.SetMeta("surface_identity", "Dirt");
            _road.AddToGroup("landing_terrain");
            _road.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(2000, 2, 2000) }, Position = new(0, -1, 0) });
            _road.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(2000, 2, 2000) }, Position = new(0, -1, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new("947454") } });
            AddChild(_road);
        }
        else
        {
            var map = Arenas.ActiveMap.Load(); AddChild(map);
            var layout = Arenas.DestructibleEnvironment.ReadLayout(map)!;
            _environmentLayout = layout;
            _environment = new(layout); _environmentView = new(map);
            System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "environment-layout.json"), JsonSerializer.Serialize(new { layout.MinimumSize, profiles = layout.Sizes.Select((size, root) => new { root, size, finalStage = layout.FinalStage(root * 4) }), rocks = layout.Rocks.Select(p => new[] { p.X, p.Y, p.Z }), plants = layout.Plants.Select(p => new[] { p.X, p.Y, p.Z }) }));
        }
        AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
        AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightEnergy = 1.4f });
        _world = new(new Core.Simulation.SimulationConfiguration(60));
        var pose = new VehiclePhysicsState(new(0, 3, 0), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        _world.AddVehicle(1, new(), damage, pose);
        _body = new VehicleBody { Position = new(0, 3, 0), DamageConfiguration = damage, Freeze = true };
        _body.Initialize(_world);
        AddChild(_body);
        _camera = new Camera3D { Current = true, Far = 1500, Position = new(0, 8, 12) };
        AddChild(_camera);
        _ready = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_ready) { return; }
        string path = System.IO.Path.Combine(_directory, "input.json");
        if (_remaining == 0 && System.IO.File.Exists(path))
        {
            string text;
            try { text = System.IO.File.ReadAllText(path); }
            catch (System.IO.IOException) { return; }
            if (text != _lastCommand)
            {
                using var document = JsonDocument.Parse(text);
                var command = document.RootElement;
                if (_road is not null && command.TryGetProperty("surface", out var surface))
                {
                    _road.SetMeta("surface_identity", surface.GetString()!);
                }
                _lastCommand = text;
                _remaining = Math.Clamp(command.GetProperty("frames").GetInt32(), 1, 600);
                _steer = (short)(Math.Clamp(command.GetProperty("steer").GetSingle(), -1, 1) * short.MaxValue);
                _throttle = (ushort)(Math.Clamp(command.GetProperty("throttle").GetSingle(), 0, 1) * ushort.MaxValue);
                _brake = (ushort)(Math.Clamp(command.GetProperty("brake").GetSingle(), 0, 1) * ushort.MaxValue);
                _buttons = command.TryGetProperty("handbrake", out var handbrake) && handbrake.GetBoolean() ? InputButtons.Drift : 0;
                if (command.TryGetProperty("blast", out var blast)) { _impacts.Add(new((ulong)(_world.State.Tick + 1), 1, Core.Items.HeldItem.Missile, new(blast[0].GetSingle(), blast[1].GetSingle(), blast[2].GetSingle()), true)); }
                if (command.TryGetProperty("spawn", out var spawn))
                {
                    float yaw = command.GetProperty("yaw").GetSingle();
                    float speed = command.GetProperty("speed").GetSingle();
                    var orientation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, yaw);
                    _body.ResetBody(new(new(spawn[0].GetSingle(), spawn[1].GetSingle(), spawn[2].GetSingle()), orientation, N.Vector3.Transform(new(0, 0, -speed), orientation), N.Vector3.Zero));
                }
                _trace.Clear();
                _body.Freeze = false;
                // Godot clears velocities while frozen; resume the exact last command boundary.
                _body.LinearVelocity = VehicleBody.ToGodot(_world.GetVehicle(1).Movement.Physics.LinearVelocity);
                _body.AngularVelocity = VehicleBody.ToGodot(_world.GetVehicle(1).Movement.Physics.AngularVelocity);
            }
        }
        if (_remaining <= 0) { return; }
        var input = new InputFrame(_world.State.Tick + 1, _steer, _throttle, _brake, _buttons, 0, 0);
        var request = _body.Capture(input);
        _body.Apply(_world.Step(input, new[] { request })[0]);
        _environment?.Advance(input.Tick, [request], _impacts, _items); _impacts.Clear();
        if (_environment is not null) { _environmentView!.Apply(_environment.Snapshot(1, input.Tick)); }
        var state = _world.GetVehicle(1);
        var p = state.Movement.Physics;
        N.Vector3 forward = N.Vector3.Transform(-N.Vector3.UnitZ, p.Orientation);
        N.Vector3 right = N.Vector3.Transform(N.Vector3.UnitX, p.Orientation);
        _trace.Add(new { tick = state.Movement.Tick, position = new[] { p.Position.X, p.Position.Y, p.Position.Z }, speed = state.Speed, yaw = p.AngularVelocity.Y, lateral = N.Vector3.Dot(p.LinearVelocity, right), longitudinal = N.Vector3.Dot(p.LinearVelocity, forward), slip = state.Movement.PowerSlip, surface = state.Movement.CurrentSurface.ToString(), grounded = state.Movement.Grounded, hp = state.Damage.CurrentHP });
        Vector3 position = VehicleBody.ToGodot(p.Position);
        _camera.Position = position - VehicleBody.ToGodot(forward) * 10 + Vector3.Up * 5;
        _camera.LookAt(position + Vector3.Up * 0.5f);
        if (--_remaining == 0)
        {
            _body.Freeze = true;
            System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "trace.json"), JsonSerializer.Serialize(_trace));
            if (_environment is not null)
            {
                var environment = _environment.Snapshot(1, input.Tick);
                System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "environment.json"), JsonSerializer.Serialize(new { environment.Tick, rocks = environment.Rocks.Select((r, i) => new { r.Stage, r.Damage, size = r.Stage == 0 ? 0 : _environmentLayout!.Size(i, r.Stage), offset = new[] { r.Offset.X, r.Offset.Y, r.Offset.Z } }), destroyedPlants = environment.Plants.Count(p => p) }));
            }
            CallDeferred(MethodName.Capture);
        }
    }

    private async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_directory, "view.png"));
    }
}
