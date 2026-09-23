using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Input-driven comparison on authored surface fixtures through both production collision adapters.</summary>
public sealed partial class TerrainHandlingIntegrationChecks : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private VehicleBody? _native;
    private NetworkVehicleBody? _network;
    private bool _advance;
    private ushort _throttle;
    private short _steer;
    private InputButtons _buttons;
    private readonly List<string> _lines = new();
    private string _output = "";
    private Camera3D? _camera;

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _PhysicsProcess(double delta)
    {
        if (!_advance) { return; }
        var input = new InputFrame(_world.State.Tick + 1, _steer, _throttle, 0, _buttons, 0, 0);
        var request = _native is not null ? _native.Capture(input) : new VehicleStepRequest(1, input, _network!.Observe(_world.GetVehicle(1)));
        var result = _world.Step(input, new[] { request })[0];
        if (_native is not null) { _native.Apply(result); }
        else { _network!.Apply(result.Snapshot.Movement.Physics); }
    }

    private async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/terrain-handling-checks");
            System.IO.Directory.CreateDirectory(_output);
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
                AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightEnergy = 1.4f });
                _camera = new Camera3D { Current = true, Far = 1500 };
                AddChild(_camera);
            }

            foreach (bool network in new[] { false, true })
            {
                float previous = float.MaxValue;
                foreach (var entry in new[] { (SurfaceIdentity.Asphalt, 20f), (SurfaceIdentity.Concrete, 20f), (SurfaceIdentity.Dirt, 20f), (SurfaceIdentity.Grass, 15f), (SurfaceIdentity.Mud, 12f), (SurfaceIdentity.DeepMud, 10f) })
                {
                    float speed = await Drive(network, entry.Item1, 0);
                    Check(speed < previous, $"{network}: acceleration order {entry.Item1}, 8s speed {speed:F3} m/s < {previous:F3}");
                    previous = speed;
                    await Drive(network, entry.Item1, entry.Item2);
                }
            }
            GD.Print("Terrain handling integration passed: both adapters, six surfaces, slope starts, steering, handbrake recovery and transitions.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            _advance = false;
            Log(error.ToString());
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task<float> Drive(bool network, SurfaceIdentity identity, float degrees)
    {
        string name = $"{(network ? "network" : "practice")}-{identity}-{degrees}";
        var road = new StaticBody3D { CollisionLayer = 1, CollisionMask = 2, Rotation = new(Mathf.DegToRad(degrees), 0, 0) };
        road.SetMeta("surface_identity", identity.ToString());
        road.AddToGroup("landing_terrain");
        road.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(250, 2, 1200) }, Position = new(0, -1, 0) });
        road.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(250, 2, 1200) }, Position = new(0, -1, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = identity switch { SurfaceIdentity.Grass => new("527038"), SurfaceIdentity.Mud => new("66503c"), SurfaceIdentity.DeepMud => new("3e3029"), SurfaceIdentity.Dirt => new("947454"), _ => new("686b70") } } });
        AddChild(road);
        await Frames(3);
        _world = new(new Core.Simulation.SimulationConfiguration(60));
        var q = road.Quaternion;
        var pose = new VehiclePhysicsState(VehicleBody.ToCore(road.Basis.Y * 0.9f), new N.Quaternion(q.X, q.Y, q.Z, q.W), N.Vector3.Zero, N.Vector3.Zero);
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        _world.AddVehicle(1, new(), damage, pose);
        if (network)
        {
            _network = new NetworkVehicleBody { VehicleId = 1 };
            AddChild(_network);
            _network.Apply(pose);
        }
        else
        {
            _native = new VehicleBody { Position = VehicleBody.ToGodot(pose.Position), Quaternion = q, DamageConfiguration = damage };
            _native.Initialize(_world);
            AddChild(_native);
        }
        _throttle = ushort.MaxValue;
        _steer = 0;
        _buttons = 0;
        _advance = true;
        await Frames(480);
        var state = _world.GetVehicle(1);
        float speed = state.Movement.CommandSpeed;
        float progress = N.Vector3.Dot(state.Movement.Physics.Position - pose.Position, VehicleBody.ToCore(-road.Basis.Z));
        Check(state.Movement.CurrentSurface == SurfaceHandling.Resolve(identity), name + " uses authored support profile");
        Check(progress > (degrees == 0 ? 8 : 2) && speed > .5f, $"{name}: from rest progress {progress:F3} m, speed {speed:F3} m/s");
        Check(state.Damage.CurrentHP == 1000, name + " traversable without damage");
        if (degrees == 0)
        {
            _steer = 10000;
            await Frames(90);
            Check(Math.Abs(_world.GetVehicle(1).Movement.Physics.AngularVelocity.Y) > .03f, name + " responds to steering");
            _buttons = InputButtons.Drift;
            await Frames(35);
            _buttons = 0;
            _steer = 0;
            await Frames(180);
            state = _world.GetVehicle(1);
            Check(state.Movement.Grounded && state.Movement.Handbrake == 0 && N.Vector3.Transform(N.Vector3.UnitY, state.Movement.Physics.Orientation).Y > .9f, name + " recovers upright after handbrake and steering");
            Check(state.Movement.CommandSpeed > 1 && state.Damage.CurrentHP == 1000, name + " retains drive after release");
            // Change the authored material under a moving body; no velocity/reset shortcut.
            road.SetMeta("surface_identity", SurfaceIdentity.Asphalt.ToString());
            await Frames(180);
            Check(_world.GetVehicle(1).Movement.CurrentSurface == SurfaceType.Asphalt && _world.GetVehicle(1).Movement.CommandSpeed > state.Movement.CommandSpeed, name + " returns to asphalt and accelerates continuously");
        }
        if (_camera is not null)
        {
            Vector3 p = VehicleBody.ToGodot(_world.GetVehicle(1).Movement.Physics.Position);
            _camera.Position = p + new Vector3(10, 7, 12);
            _camera.LookAt(p);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng(System.IO.Path.Combine(_output, name + ".png")) == Error.Ok, "Rendered " + name);
        }
        _advance = false;
        _native?.QueueFree();
        _network?.QueueFree();
        _native = null;
        _network = null;
        road.QueueFree();
        await Frames(3);
        return speed;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    }
    private void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
        Log(message);
    }
    private void Log(string message)
    {
        _lines.Add(message);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _lines);
        GD.Print(message);
    }
}
