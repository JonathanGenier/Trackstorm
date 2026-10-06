using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises real airborne orientation and landing through both production collision adapters.</summary>
public sealed partial class AirControlIntegrationChecks : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private VehicleBody? _native;
    private NetworkVehicleBody? _network;
    private Camera3D? _camera;
    private bool _advance;
    private string _case = "";
    private int _frame;
    private int _landings;
    private float _rotation;
    private float _peak;
    private float _maximumAirSeconds;
    private int _airCommandFrames;
    private N.Quaternion _released;
    private readonly List<string> _evidence = new();
    private string _output = "";
    private readonly List<object> _trace = new();
    private Input.PlayerInputAdapter? _physical;
    private N.Quaternion _releasePose;
    private N.Vector3 _releasePosition;
    private const int ObservationExtension = 21;

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _Process(double delta) => _network?.PresentLocal((float)delta);

    public override void _PhysicsProcess(double delta)
    {
        if (!_advance) { return; }
        _frame++;
        bool held = _frame <= (_case == "sustained" ? 180 : 100) + ObservationExtension;
        short steer = held && _case is "yaw" or "roll" or "combined" or "sustained" or "release-command" ? (short)32767 : (short)0;
        ushort throttle = held && _case is "pitch" or "combined" ? (ushort)65535 : (ushort)0;
        bool roll = _case is "roll" or "combined" or "sustained";
        var previous = _world.GetVehicle(1).Movement;
        ushort brake = 0;
        if (_case is "correction" or "heading" && !previous.Grounded)
        {
            // Test pilot uses the same logical controls to prepare an upright landing.
            N.Quaternion error = N.Quaternion.Conjugate(previous.Physics.Orientation);
            float sign = error.W < 0 ? -1 : 1;
            float pitch = Math.Clamp(error.X * sign * 3, -1, 1);
            float turn = Math.Clamp((_case == "heading" ? error.Y : error.Z) * sign * -3, -1, 1);
            throttle = (ushort)(Math.Max(0, pitch) * 65535);
            brake = (ushort)(Math.Max(0, -pitch) * 65535);
            steer = (short)(turn * 32767);
            roll = _case == "correction";
        }
        if (_case == "coast") { throttle = 65535; steer = 32767; }
        if (_case == "short-gap") { throttle = 65535; }
        if (_case == "repeat" && !previous.Grounded && previous.Air.Seconds < 0.4f) { steer = 32767; }
        var input = new InputFrame(_world.State.Tick + 1, steer, throttle, brake, InputButtons.None, 0, 0, (short)((throttle - brake) / 65535f * 32767), roll ? (short)0 : steer, roll ? steer : (short)0);
        if (_case.StartsWith("device-", StringComparison.Ordinal))
        {
            bool pad = _case.Contains("pad", StringComparison.Ordinal);
            bool pitchAxis = _case.EndsWith("pitch", StringComparison.Ordinal);
            bool rollAxis = _case.EndsWith("roll", StringComparison.Ordinal);
            void Send(InputEvent e) { Godot.Input.ParseInputEvent(e); e.Dispose(); }
            Send(new InputEventKey { PhysicalKeycode = Key.Shift, Pressed = !pad && held && rollAxis });
            Send(new InputEventKey { PhysicalKeycode = Key.W, Pressed = !pad && held && pitchAxis });
            Send(new InputEventKey { PhysicalKeycode = Key.D, Pressed = !pad && held && !pitchAxis });
            Send(new InputEventKey { PhysicalKeycode = Key.E, Pressed = false });
            Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.LeftShoulder, Pressed = pad && held && rollAxis });
            Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = false });
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = pad && held && !pitchAxis ? 0.6f : 0 });
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.TriggerRight, AxisValue = pad && held && pitchAxis ? 0.6f : 0 });
            Godot.Input.FlushBufferedEvents();
            input = _physical!.Capture(_world.State.Tick + 1);
        }
        var request = _native is not null ? _native.Capture(input) : new VehicleStepRequest(1, input, _network!.Observe(_world.GetVehicle(1)));
        var result = _world.Step(input, [request])[0];
        if (_native is not null) { _native.Apply(result); }
        else { _network!.Apply(result.Snapshot); }
        var state = result.Snapshot.Movement;
        _maximumAirSeconds = Math.Max(_maximumAirSeconds, state.Air.Seconds);
        if (state.Air.Input != N.Vector3.Zero) { _airCommandFrames++; }
        if (state.Air.Seconds > 0 && state.Air.Seconds < 0.15f)
        {
            Require(state.Air.Input == N.Vector3.Zero && state.Air.Stabilization == N.Vector3.Zero, "short support loss cannot activate aerial control");
        }
        if (_case.StartsWith("device-", StringComparison.Ordinal) && _frame == 101 + ObservationExtension)
        {
            Require(previous.Physics.AngularVelocity.Length() > 0.5f, "release follows meaningful commanded rotation");
            _releasePosition = state.Physics.Position;
            Require(Math.Abs(state.Physics.LinearVelocity.Z - previous.Physics.LinearVelocity.Z) < 0.01f, "release preserves horizontal travel");
            Require(state.Physics.LinearVelocity.Y < previous.Physics.LinearVelocity.Y, "gravity continues on release");
        }
        var p = state.Physics; var w = state.Wheels.Compression;
        _trace.Add(new { frame = _frame, position = new[] { p.Position.X, p.Position.Y, p.Position.Z },
            velocity = new[] { p.LinearVelocity.X, p.LinearVelocity.Y, p.LinearVelocity.Z },
            angular = new[] { p.AngularVelocity.X, p.AngularVelocity.Y, p.AngularVelocity.Z }, airSeconds = state.Air.Seconds, airInput = new[] { state.Air.Input.X, state.Air.Input.Y, state.Air.Input.Z },
            inputAxes = new[] { input.AirPitch, input.AirYaw, input.AirRoll },
            up = N.Vector3.Transform(N.Vector3.UnitY, p.Orientation).Y, state.Grounded,
            compression = new[] { w.X, w.Y, w.Z, w.W }, contacts = request.Observation.Contacts.Count });
        if (state.Grounded && !previous.Grounded) { _landings++; }
        if (held)
        {
            float speed = state.Physics.AngularVelocity.Length();
            _rotation += speed / 60;
            _peak = Math.Max(_peak, speed);
        }
        if (_frame == (_case == "sustained" ? 240 : 160) + ObservationExtension) { _released = state.Physics.Orientation; _releasePose = _released; }
        if (_camera is not null)
        {
            Vector3 position = VehicleBody.ToGodot(state.Physics.Position);
            _camera.Position = position + new Vector3(8, 5, 9);
            _camera.LookAt(position);
        }
    }

    public async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/air-control-checks");
            System.IO.Directory.CreateDirectory(_output);
            var floor = new SurfaceBody { Surface = SurfaceType.Asphalt, Position = new Vector3(0, -1, 0) };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(300, 2, 300) } });
            floor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(300, 2, 300) } });
            AddChild(floor);
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-60, -30, 0), LightEnergy = 1.4f });
                _camera = new Camera3D { Current = true }; AddChild(_camera);
            }
            foreach (bool network in new[] { false, true })
            foreach (string scenario in new[] { "short-gap", "pitch", "yaw", "roll", "combined", "sustained", "crooked", "landing", "repeat", "correction", "heading", "coast", "release-command", "device-key-pitch", "device-key-yaw", "device-key-roll", "device-pad-pitch", "device-pad-yaw", "device-pad-roll" })
            {
                if (OS.GetCmdlineUserArgs().Contains("--air-correction-only") && scenario != "correction") { continue; }
                await Exercise(network, scenario);
            }
            GD.Print(OS.GetCmdlineUserArgs().Contains("--air-correction-only") ? "Air correction diagnostic passed: 2 production-adapter scenarios." : "Air control integration passed: 38 production-adapter scenarios.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _advance = false; Log(exception.ToString()); GD.PushError(exception.ToString()); GetTree().Quit(1);
        }
    }

    private async Task Exercise(bool network, string scenario)
    {
        _physical?.Bindings.Dispose();
        _physical = new(new Input.PlayerInputBindings());
        _trace.Clear();
        _case = scenario; _frame = 0; _rotation = 0; _peak = 0; _landings = 0; _maximumAirSeconds = 0; _airCommandFrames = 0;
        _world = new(new Core.Simulation.SimulationConfiguration(60));
        bool landing = scenario is "landing" or "repeat" or "correction" or "heading";
        var position = new Vector3(0, scenario == "short-gap" ? 1.68f : landing ? 2 : 200, 0);
        var rotation = scenario == "crooked" ? Quaternion.FromEuler(new Vector3(0.6f, 0.4f, 1.2f)) : Quaternion.Identity;
        if (scenario is "correction" or "heading")
        {
            position.Y = 15;
            rotation = scenario == "correction" ? Quaternion.FromEuler(new Vector3(0.5f, 0, 0.9f)) : Quaternion.FromEuler(new Vector3(0, 1.2f, 0));
        }
        var velocity = landing || scenario.StartsWith("device-", StringComparison.Ordinal) ? new Vector3(0, 10, -8) : Vector3.Zero;
        var angular = scenario == "crooked" ? new Vector3(2, -1, 3) : Vector3.Zero;
        var physics = new VehiclePhysicsState(VehicleBody.ToCore(position), new N.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(velocity), VehicleBody.ToCore(angular));
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        _world.AddVehicle(1, new(), damage, physics);
        if (network)
        {
            _network = new NetworkVehicleBody { VehicleId = 1 }; AddChild(_network); _network.Apply(_world.GetVehicle(1));
        }
        else
        {
            _native = new VehicleBody { VehicleId = 1, Position = position, Quaternion = rotation, LinearVelocity = velocity, AngularVelocity = angular, DamageConfiguration = damage };
            _native.Initialize(_world); AddChild(_native);
        }
        await Frames(3); _advance = true;
        // Preserve bounded command and post-release observation windows.
        await Frames((scenario == "sustained" ? 300 : 220) + ObservationExtension); _advance = false;
        var state = _world.GetVehicle(1).Movement;
        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, $"{(network ? "network" : "native")}-{scenario}.json"), System.Text.Json.JsonSerializer.Serialize(_trace));
        float drift = 2 * MathF.Acos(Math.Clamp(Math.Abs(N.Quaternion.Dot(_released, state.Physics.Orientation)), 0, 1));
        Log($"{(network ? "network" : "native")}-{scenario}: rotation={_rotation:F3} peak={_peak:F3} releaseSpeed={state.Physics.AngularVelocity.Length():F4} lateOrientationDrift={drift:F4} landings={_landings} grounded={state.Grounded} airSeconds={state.Air.Seconds:F3}");
        if (scenario == "short-gap")
        {
            Require(_maximumAirSeconds > 0 && _maximumAirSeconds < 0.15f, "native tire observations expose a genuine short support gap");
            Require(_airCommandFrames == 0 && state.Grounded && state.Air == default, "brief gap never activates held pitch and landing resets continuation");
            Log($"{(network ? "network" : "native")}-short-gap: maxAir={_maximumAirSeconds:F4}s, commandedFrames={_airCommandFrames}");
        }
        else if (scenario == "coast")
        {
            Require(state.Air.Input.Length() > 0.9f && state.Physics.AngularVelocity.Length() > 1, "unmodified driving controls automatically rotate in sustained flight");
        }
        else if (scenario == "release-command")
        {
            Require(state.Air.Input.Length() < 0.001f && state.Physics.AngularVelocity.Length() < 0.02f && drift < 0.02f, "releasing directional controls damps spin and holds chosen attitude");
        }
        else if (scenario.StartsWith("device-", StringComparison.Ordinal))
        {
            float releaseDrift = 2 * MathF.Acos(Math.Clamp(Math.Abs(N.Quaternion.Dot(_releasePose, state.Physics.Orientation)), 0, 1));
            Require(state.Physics.AngularVelocity.Length() < 0.02f && releaseDrift < 0.02f, "chosen release attitude remains stable");
            Require(N.Vector3.Distance(_releasePosition, state.Physics.Position) > 10, "vehicle keeps travelling after rotation stops");
            Log($"{scenario}: settled angular speed <0.02; attitude drift={releaseDrift:F5}; continued travel={N.Vector3.Distance(_releasePosition, state.Physics.Position):F2}m");
        }
        else if (!landing)
        {
            Require(state.Physics.AngularVelocity.Length() < 0.02f, "release must arrest residual rotation");
            Require(drift < 0.02f, "released chosen attitude must remain stable");
            if (scenario != "crooked") { Require(_rotation > 2.5f, "held command must produce substantial rotation"); }
            if (scenario == "sustained") { Require(_rotation > 8, "sustained roll must not seek wheels-down"); }
        }
        else
        {
            Require(_landings > 0 && state.Grounded && state.Air == default, "landing must clear airborne continuation");
            if (scenario == "correction") { Require(_landings == 1, "corrected four-wheel landing must not produce a second hop"); }
            if (scenario == "repeat")
            {
                for (int jump = 0; jump < 3; jump++)
                {
                    int commandsBeforeJump = _airCommandFrames;
                    ulong tick = _world.State.Tick + 1;
                    var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
                    var observation = _native is not null ? _native.Capture(input).Observation : _network!.Observe(_world.GetVehicle(1));
                    // Preserve the launch velocity when the default body mass changes.
                    var result = _world.Step(input, [new VehicleStepRequest(1, input, observation, [new VehicleEffectRequest(new DamageEffect(0, new N.Vector3(0, 10000 * new VehicleConfiguration().Mass / 1400, 0), N.Vector3.Zero), new DamageContext("test", 0, "jump"))])])[0];
                    if (_native is not null) { _native.Apply(result); } else { _network!.Apply(result.Snapshot); }
                    _advance = true; await Frames(240); _advance = false;
                    Require(_world.GetVehicle(1).Movement.Grounded, "repeated jump must land");
                    Require(_airCommandFrames > commandsBeforeJump, "every repeated jump regains automatic yaw control after its fresh delay");
                }
                Require(_landings >= 4, "repeated launches must create separate landing transitions");
                Log($"{(network ? "network" : "native")}-repeat: {_landings} landing transitions");
            }
        }
        if (_camera is not null)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(_output, $"{network}-{scenario}.png"));
        }
        (_native as Node ?? _network!).QueueFree(); _native = null; _network = null; await Frames(3);
    }

    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    private void Log(string line) { _evidence.Add(line); System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence); GD.Print(line); }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
}
