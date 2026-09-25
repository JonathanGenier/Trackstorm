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
    private N.Quaternion _released;
    private readonly List<string> _evidence = new();
    private string _output = "";

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _Process(double delta) => _network?.PresentLocal((float)delta);

    public override void _PhysicsProcess(double delta)
    {
        if (!_advance) { return; }
        _frame++;
        bool held = _frame <= (_case == "sustained" ? 180 : 100);
        short steer = held && _case is "yaw" or "roll" or "combined" or "sustained" ? (short)32767 : (short)0;
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
            throttle = (ushort)(Math.Max(0, -pitch) * 65535);
            brake = (ushort)(Math.Max(0, pitch) * 65535);
            steer = (short)(turn * 32767);
            roll = _case == "correction";
        }
        var input = new InputFrame(_world.State.Tick + 1, steer, throttle, brake, roll ? InputButtons.AirRoll : 0, 0, 0);
        var request = _native is not null ? _native.Capture(input) : new VehicleStepRequest(1, input, _network!.Observe(_world.GetVehicle(1)));
        var result = _world.Step(input, [request])[0];
        if (_native is not null) { _native.Apply(result); }
        else { _network!.Apply(result.Snapshot); }
        var state = result.Snapshot.Movement;
        if (state.Grounded && !previous.Grounded) { _landings++; }
        if (held)
        {
            float speed = state.Physics.AngularVelocity.Length();
            _rotation += speed / 60;
            _peak = Math.Max(_peak, speed);
        }
        if (_frame == (_case == "sustained" ? 240 : 160)) { _released = state.Physics.Orientation; }
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
            foreach (string scenario in new[] { "pitch", "yaw", "roll", "combined", "sustained", "crooked", "landing", "repeat", "correction", "heading" })
            {
                await Exercise(network, scenario);
            }
            GD.Print("Air control integration passed: 20 production-adapter scenarios.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _advance = false; Log(exception.ToString()); GD.PushError(exception.ToString()); GetTree().Quit(1);
        }
    }

    private async Task Exercise(bool network, string scenario)
    {
        _case = scenario; _frame = 0; _rotation = 0; _peak = 0; _landings = 0;
        _world = new(new Core.Simulation.SimulationConfiguration(60));
        bool landing = scenario is "landing" or "repeat" or "correction" or "heading";
        var position = new Vector3(0, landing ? 2 : 200, 0);
        var rotation = scenario == "crooked" ? Quaternion.FromEuler(new Vector3(0.6f, 0.4f, 1.2f)) : Quaternion.Identity;
        if (scenario is "correction" or "heading")
        {
            position.Y = 15;
            rotation = scenario == "correction" ? Quaternion.FromEuler(new Vector3(0.5f, 0, 0.9f)) : Quaternion.FromEuler(new Vector3(0, 1.2f, 0));
        }
        var velocity = landing ? new Vector3(0, 10, -8) : Vector3.Zero;
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
        await Frames(scenario == "sustained" ? 300 : 220); _advance = false;
        var state = _world.GetVehicle(1).Movement;
        float drift = 2 * MathF.Acos(Math.Clamp(Math.Abs(N.Quaternion.Dot(_released, state.Physics.Orientation)), 0, 1));
        Log($"{(network ? "network" : "native")}-{scenario}: rotation={_rotation:F3} peak={_peak:F3} releaseSpeed={state.Physics.AngularVelocity.Length():F4} lateOrientationDrift={drift:F4} landings={_landings} grounded={state.Grounded} airSeconds={state.Air.Seconds:F3}");
        if (!landing)
        {
            Require(state.Physics.AngularVelocity.Length() < 0.02f, "release must arrest residual rotation");
            Require(drift < 0.02f, "released chosen attitude must remain stable");
            if (scenario != "crooked") { Require(_rotation > 2.5f, "held command must produce substantial rotation"); }
            if (scenario == "sustained") { Require(_rotation > 8, "sustained roll must not seek wheels-down"); }
        }
        else
        {
            Require(_landings > 0 && state.Grounded && state.Air == default, "landing must clear airborne continuation");
            if (scenario == "repeat")
            {
                for (int jump = 0; jump < 3; jump++)
                {
                    ulong tick = _world.State.Tick + 1;
                    var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
                    var observation = _native is not null ? _native.Capture(input).Observation : _network!.Observe(_world.GetVehicle(1));
                    var result = _world.Step(input, [new VehicleStepRequest(1, input, observation, [new VehicleEffectRequest(new DamageEffect(0, new N.Vector3(0, 10000, 0), N.Vector3.Zero), new DamageContext("test", 0, "jump"))])])[0];
                    if (_native is not null) { _native.Apply(result); } else { _network!.Apply(result.Snapshot); }
                    _advance = true; await Frames(240); _advance = false;
                    Require(_world.GetVehicle(1).Movement.Grounded, "repeated jump must land");
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
