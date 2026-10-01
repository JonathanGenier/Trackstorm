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
    private VehicleBody? _body;
    private Networking.NetworkVehicleBody? _network;
    private VehiclePhysicsState? _pendingReset;
    private VehicleConfiguration _configuration = new();
    private PhysicsBody3D Body => (PhysicsBody3D?)_body ?? _network!;
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
    private Input.PlayerInputAdapter _physical = null!;
    private bool _physicalSteering;
    private bool _baseline;
    private StaticBody3D? _road;
    private Core.Arenas.EnvironmentAuthority? _environment;
    private Core.Arenas.EnvironmentLayout? _environmentLayout;
    private Arenas.DestructibleEnvironment? _environmentView;
    private readonly Core.Items.ItemAuthority _items = new(new() { MaximumDamage = 300 });
    private readonly List<Core.Items.ItemEvent> _impacts = new();
    private Core.Items.OilPatch? _oil;

    public override void _Ready()
    {
        _directory = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--steering-playtest") ? "res://.godot/ts-268/playtest" : OS.GetCmdlineUserArgs().Contains("--rock-playtest") ? "res://.godot/ts-267/playtest" : OS.GetCmdlineUserArgs().Contains("--oil-playtest") ? "res://.godot/ts-172/playtest" : OS.GetCmdlineUserArgs().Contains("--destructible-playtest") ? "res://.godot/ts-162/playtest" : "res://.godot/ts-160/playtest");
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
        _physical = new(new Input.PlayerInputBindings());
        _baseline = OS.GetCmdlineUserArgs().Contains("--handling-baseline-grip");
        _world = new(new Core.Simulation.SimulationConfiguration(60));
        var pose = new VehiclePhysicsState(new(0, 3, 0), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        var tuning = _baseline ? new VehicleConfiguration { AsphaltGrip = 1, Dirt = new(1.25f, 1.15f, 0.95f), Grass = new(1.25f, 1.4f, 0.9f) } : new VehicleConfiguration();
        if (OS.GetCmdlineUserArgs().Contains("--handling-round8")) { tuning = tuning with { AsphaltGrip = 2, BrakeGrip = 1.5f, Braking = 95 }; }
        if (OS.GetCmdlineUserArgs().Contains("--handling-host-seed"))
        {
            tuning = new Development.DeveloperSettingsStore(ProjectSettings.GlobalizePath("user://developer-settings.jsonl")).LoadForHost().Vehicle;
        }
        // Exercise the existing developer draft Reset/Apply contract before clean-default trials.
        if (!_baseline && !OS.GetCmdlineUserArgs().Contains("--handling-host-seed") && !OS.GetCmdlineUserArgs().Contains("--handling-round8"))
        {
            var defaults = Core.Development.GameplayConfiguration.HostedDefaults;
            var draft = new Development.DeveloperOptionsDraft();
            draft.ResetToDefaults();
            if (!draft.TryGetEdits(out var edits, out var error) || !Core.Development.GameplayOptions.TryApply(defaults, edits, out var applied, out error))
                throw new InvalidOperationException(error);
            tuning = applied.Vehicle;
            GD.Print("Handling playtest: Developer Options draft Reset to Defaults applied before driving.");
        }
        _configuration = tuning;
        _world.AddVehicle(1, tuning, damage, pose);
        if (OS.GetCmdlineUserArgs().Contains("--handling-network"))
        {
            _network = new() { VehicleId = 1 }; AddChild(_network);
            _network.ApplyConfiguration(tuning); _network.Apply(_world.GetVehicle(1));
        }
        else
        {
            _body = new VehicleBody { Position = new(0, 3, 0), DamageConfiguration = damage, Configuration = tuning, Freeze = true };
            _body.Initialize(_world);
            AddChild(_body);
        }
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
                if (_road is not null && command.TryGetProperty("grade", out var grade)) { _road.RotationDegrees = new(Math.Clamp(grade.GetSingle(), -45, 45), 0, 0); }
                _lastCommand = text;
                if (command.TryGetProperty("oilPatch", out var oil))
                {
                    _oil = oil.ValueKind == JsonValueKind.Null ? null : new Core.Items.OilPatch(1, 2,
                        new(oil[0].GetSingle(), oil[1].GetSingle(), oil[2].GetSingle()), N.Vector3.UnitY, 3);
                }
                _remaining = Math.Clamp(command.GetProperty("frames").GetInt32(), 1, 600);
                _steer = (short)(Math.Clamp(command.GetProperty("steer").GetSingle(), -1, 1) * short.MaxValue);
                _throttle = (ushort)(Math.Clamp(command.GetProperty("throttle").GetSingle(), 0, 1) * ushort.MaxValue);
                _brake = (ushort)(Math.Clamp(command.GetProperty("brake").GetSingle(), 0, 1) * ushort.MaxValue);
                _buttons = command.TryGetProperty("handbrake", out var handbrake) && handbrake.GetBoolean() ? InputButtons.Drift : 0;
                if (command.TryGetProperty("airRoll", out var airRoll) && airRoll.GetBoolean()) { _buttons |= InputButtons.AirRoll; }
                if (command.TryGetProperty("blast", out var blast)) { _impacts.Add(new((ulong)(_world.State.Tick + 1), 1, Core.Items.HeldItem.Missile, new(blast[0].GetSingle(), blast[1].GetSingle(), blast[2].GetSingle()), true)); }
                if (command.TryGetProperty("spawn", out var spawn))
                {
                    float yaw = command.GetProperty("yaw").GetSingle();
                    float speed = command.GetProperty("speed").GetSingle();
                    float pitch = command.TryGetProperty("pitch", out var pitchValue) ? pitchValue.GetSingle() : 0;
                    float roll = command.TryGetProperty("roll", out var rollValue) ? rollValue.GetSingle() : 0;
                    float vertical = command.TryGetProperty("verticalSpeed", out var verticalValue) ? verticalValue.GetSingle() : 0;
                    var orientation = N.Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
                    var spawnPosition = new N.Vector3(spawn[0].GetSingle(), spawn[1].GetSingle(), spawn[2].GetSingle());
                    if (command.TryGetProperty("groundSpawn", out var groundSpawn) && groundSpawn.GetBoolean())
                    {
                        using var query = PhysicsRayQueryParameters3D.Create(new(spawnPosition.X, 100, spawnPosition.Z), new(spawnPosition.X, -100, spawnPosition.Z), 1, new() { Body.GetRid() });
                        using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                        if (hit.Count == 0) { throw new InvalidOperationException("Playtest spawn has no terrain."); }
                        Vector3 normal = hit["normal"].AsVector3();
                        spawnPosition = VehicleBody.ToCore(hit["position"].AsVector3() + normal * 1.145f);
                        Vector3 heading = new(-MathF.Sin(yaw), 0, -MathF.Cos(yaw));
                        heading = (heading - normal * heading.Dot(normal)).Normalized();
                        var rotation = Basis.LookingAt(heading, normal).GetRotationQuaternion();
                        orientation = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
                    }
                    var reset = new VehiclePhysicsState(spawnPosition, orientation, N.Vector3.Transform(new(0, 0, -speed), orientation) + new N.Vector3(0, vertical, 0), N.Vector3.Zero);
                    if (_body is not null) { _body.ResetBody(reset); } else { _pendingReset = reset; }
                }
                _physicalSteering = command.TryGetProperty("keyboard", out var keyboard) && keyboard.GetBoolean();
                if (command.TryGetProperty("steeringSensitivity", out var sensitivity)) { _physical.SteeringSensitivity = sensitivity.GetSingle(); }
                bool analog = command.TryGetProperty("analog", out var analogValue) && analogValue.GetBoolean();
                using var left = new InputEventKey { PhysicalKeycode = Key.A, Pressed = _physicalSteering && _steer < 0 };
                using var rightKey = new InputEventKey { PhysicalKeycode = Key.D, Pressed = _physicalSteering && _steer > 0 };
                using var accelerate = new InputEventKey { PhysicalKeycode = Key.W, Pressed = _physicalSteering && _throttle > 0 };
                using var brakeKey = new InputEventKey { PhysicalKeycode = Key.S, Pressed = _physicalSteering && _brake > 0 };
                using var trigger = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.TriggerRight, AxisValue = analog ? _throttle / 65535f : 0 };
                using var brakeTrigger = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.TriggerLeft, AxisValue = analog ? _brake / 65535f : 0 };
                using var stick = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = analog ? _steer / 32767f : 0 };
                Godot.Input.ParseInputEvent(left); Godot.Input.ParseInputEvent(rightKey); Godot.Input.ParseInputEvent(stick); Godot.Input.ParseInputEvent(accelerate); Godot.Input.ParseInputEvent(brakeKey); Godot.Input.ParseInputEvent(trigger); Godot.Input.ParseInputEvent(brakeTrigger);
                Godot.Input.FlushBufferedEvents();
                _physicalSteering |= analog;
                if (command.TryGetProperty("spawn", out _)) { _physical.Enabled = false; _physical.Capture(0); _physical.Enabled = true; }
                _trace.Clear();
                if (_body is not null)
                {
                    _body.Freeze = false;
                    // Godot clears velocities while frozen; resume the exact last command boundary.
                    _body.LinearVelocity = VehicleBody.ToGodot(_world.GetVehicle(1).Movement.Physics.LinearVelocity);
                    _body.AngularVelocity = VehicleBody.ToGodot(_world.GetVehicle(1).Movement.Physics.AngularVelocity);
                }
            }
        }
        if (_remaining <= 0) { return; }
        var prior = _world.GetVehicle(1).Movement;
        _physical.Shaping = !prior.Grounded && prior.Air.Seconds + 1f / 60 + 0.000001f >= _configuration.AirDelay || _baseline
            ? DrivingInputShaping.Aerial : Core.Development.GameplayConfiguration.HostedDefaults.Input;
        var captured = _physicalSteering ? _physical.Capture(_world.State.Tick + 1) : default;
        var input = new InputFrame(_world.State.Tick + 1, _physicalSteering ? captured.Steering : _steer, _physicalSteering ? captured.Accelerate : _throttle, _physicalSteering ? captured.Brake : _brake, _buttons | captured.Held, captured.Pressed, captured.Released);
        var request = _body is not null ? _body.Capture(input) : new VehicleStepRequest(1, input, _network!.Observe(_world.GetVehicle(1)), reset: _pendingReset);
        _pendingReset = null;
        if (_oil?.Contains(request.Observation) == true)
        {
            request = new(request.VehicleId, request.Input, request.Observation, request.Effects, request.Reset,
                request.Repair, request.RepairCause, oilContact: true);
        }
        var result = _world.Step(input, new[] { request })[0];
        if (_body is not null) { _body.Apply(result); }
        else { _network!.Apply(result.Snapshot); _network.PresentRemote(result.Snapshot.Movement.Physics); }
        _environment?.Advance(input.Tick, [request], _impacts, _items); _impacts.Clear();
        if (_environment is not null) { _environmentView!.Apply(_environment.Snapshot(1, input.Tick)); }
        var state = _world.GetVehicle(1);
        var p = state.Movement.Physics;
        N.Vector3 forward = N.Vector3.Transform(-N.Vector3.UnitZ, p.Orientation);
        N.Vector3 right = N.Vector3.Transform(N.Vector3.UnitX, p.Orientation);
        var w = state.Movement.Wheels.Compression;
        _trace.Add(new { tick = state.Movement.Tick, position = new[] { p.Position.X, p.Position.Y, p.Position.Z }, velocity = new[] { p.LinearVelocity.X, p.LinearVelocity.Y, p.LinearVelocity.Z }, orientation = new[] { p.Orientation.X, p.Orientation.Y, p.Orientation.Z, p.Orientation.W }, compression = new[] { w.X, w.Y, w.Z, w.W }, support = new[] { request.Observation.Support.X, request.Observation.Support.Y, request.Observation.Support.Z }, contacts = request.Observation.Contacts.Count, observedVelocity = new[] { request.Observation.Physics.LinearVelocity.X, request.Observation.Physics.LinearVelocity.Y, request.Observation.Physics.LinearVelocity.Z }, contactDetails = request.Observation.Contacts.Select(c => new { normal = new[] { c.Normal.X, c.Normal.Y, c.Normal.Z }, c.Impulse, c.Terrain, c.StaticObstacle }), longAcceleration = state.Movement.LongitudinalAcceleration, sideAcceleration = state.Movement.LateralAcceleration, up = N.Vector3.Transform(N.Vector3.UnitY, p.Orientation).Y, speed = state.Speed, yaw = p.AngularVelocity.Y, lateral = N.Vector3.Dot(p.LinearVelocity, right), longitudinal = N.Vector3.Dot(p.LinearVelocity, forward), slip = state.Movement.PowerSlip, throttle = state.Movement.Throttle, oilTicks = state.Movement.OilTicks, steering = state.Movement.SteeringAngle, inputSteering = input.Steering, inputThrottle = input.Accelerate, inputBrake = input.Brake, brakeMode = state.Movement.BrakeMode.ToString(), handbrake = state.Movement.Handbrake, frontSlip = state.Movement.FrontSlip, rearSlip = state.Movement.RearSlip, surface = state.Movement.CurrentSurface.ToString(), grounded = state.Movement.Grounded, airSeconds = state.Movement.Air.Seconds, airInput = new[] { state.Movement.Air.Input.X, state.Movement.Air.Input.Y, state.Movement.Air.Input.Z }, crashSeconds = state.Movement.CrashSeconds, angular = new[] { p.AngularVelocity.X, p.AngularVelocity.Y, p.AngularVelocity.Z }, hp = state.Damage.CurrentHP });
        Vector3 position = VehicleBody.ToGodot(p.Position);
        _camera.Position = position - VehicleBody.ToGodot(forward) * 10 + Vector3.Up * 5;
        _camera.LookAt(position + Vector3.Up * 0.5f);
        if (--_remaining == 0)
        {
            if (_body is not null) { _body.Freeze = true; }
            System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "trace.json"), JsonSerializer.Serialize(_trace));
            if (_environment is not null)
            {
                var environment = _environment.Snapshot(1, input.Tick);
                System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "environment.json"), JsonSerializer.Serialize(new { environment.Tick, rocks = environment.Rocks.Select((r, i) => new { r.Stage, r.Damage, size = r.Stage == 0 ? 0 : _environmentLayout!.Size(i, r.Stage), offset = new[] { r.Offset.X, r.Offset.Y, r.Offset.Z } }), destroyedPlants = environment.Plants.Count(p => p) }));
            }
            CallDeferred(MethodName.Capture, _lastCommand);
        }
    }

    public override void _ExitTree() => _physical?.Bindings.Dispose();

    private async void Capture(string completedCommand)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_directory, "view.png"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "completed-command.json"), completedCommand);
    }
}
