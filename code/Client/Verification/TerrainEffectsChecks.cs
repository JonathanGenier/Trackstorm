using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native wheel feedback, bounded repeated use, and rendered discrete environment packages.</summary>
public sealed partial class TerrainEffectsChecks : Node3D
{
    private readonly Core.Simulation.Simulation _simulation = new(new Core.Simulation.SimulationConfiguration(60));
    private readonly List<string> _evidence = new();
    private readonly List<VehicleBody> _cars = new();
    private VehicleBody? _car;
    private Camera3D _camera = null!;
    private ulong _tick;
    private bool _drive;
    private bool _hard;
    private bool _stress;
    private string _output = string.Empty;

    public override void _Ready() => CallDeferred(MethodName.Run);
    public override void _PhysicsProcess(double delta)
    {
        if (_car is null) { return; }
        var input = new InputFrame(++_tick, _hard ? (short)16000 : (short)0, _drive ? ushort.MaxValue : (ushort)0, 0, 0, 0, _hard ? InputButtons.Drift : InputButtons.None);
        var result = _simulation.Step(input, _cars.Select(car => car.Capture(_stress || car == _car ? input : new InputFrame(_tick, 0, 0, 0, 0, 0, 0))).ToArray());
        for (int i = 0; i < _cars.Count; i++) { _cars[i].Apply(result[i]); _cars[i].Publish(); }
    }

    private async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/terrain-effects-checks");
            System.IO.Directory.CreateDirectory(_output);
            var environment = new EnvironmentPresentation();
            AddChild(environment);
            _camera = new Camera3D { Current = true, Far = 1800, Fov = 60 };
            AddChild(_camera);
            if (!OS.GetCmdlineUserArgs().Contains("--presets-only"))
            {
            var pad = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
            pad.AddToGroup("landing_terrain");
            pad.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(120, 1, 120) }, Position = new Vector3(0, -.5f, 0) });
            var material = new StandardMaterial3D { AlbedoColor = new Color("837251") };
            pad.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(120,120), Material = material } });
            AddChild(pad);
            _car = new VehicleBody { Position = new Vector3(0,.9f,15), VehicleId = 1, CollisionLayer = 2, CollisionMask = 1 };
            _simulation.AddVehicle(1, _car.Configuration, _car.DamageConfiguration, Physics(_car.Position));
            _car.Initialize(_simulation);
            AddChild(_car);
            _cars.Add(_car);
            for (int slot = 1; slot < 8; slot++)
            {
                var extra = new VehicleBody { Position = new Vector3(-49+slot*14,.9f,50), VehicleId = (ulong)slot+1, CollisionLayer = 2, CollisionMask = 1 };
                _simulation.AddVehicle(extra.VehicleId, extra.Configuration, extra.DamageConfiguration, Physics(extra.Position));
                extra.Initialize(_simulation);
                AddChild(extra);
                _cars.Add(extra);
            }

            var effects = _car.GetChildren().OfType<TireFeedback>().Single();
            await Frames(10);
            int surfaceIndex = 0;
            foreach (var identity in new[] { SurfaceIdentity.Asphalt, SurfaceIdentity.Concrete, SurfaceIdentity.Dirt, SurfaceIdentity.Grass, SurfaceIdentity.Mud, SurfaceIdentity.DeepMud, SurfaceIdentity.Water })
            {
                pad.SetMeta("surface_identity", identity.ToString());
                material.AlbedoColor = identity switch
                {
                    SurfaceIdentity.Asphalt => new Color("383938"), SurfaceIdentity.Concrete => new Color("b4b1a4"),
                    SurfaceIdentity.Grass => new Color("59603a"), SurfaceIdentity.Mud => new Color("4c3725"),
                    SurfaceIdentity.DeepMud => new Color("382b22"), SurfaceIdentity.Water => new Color("23535b"), _ => new Color("876b45"),
                };
                if (identity == SurfaceIdentity.Water)
                {
                    pad.AddToGroup("water_terrain");
                    pad.SetMeta("water_level", .1f);
                    pad.SetMeta("surface_bounds", new Vector4(-60,-60,120,120));
                }
                _hard = identity is SurfaceIdentity.Asphalt or SurfaceIdentity.Concrete;
                _drive = true;
                _car.ResetBody(Physics(new Vector3(-48 + surfaceIndex++ * 16,.9f,15), _hard ? new Vector3(7,0,-9) : new Vector3(0,0,-7)));
                int marks = effects.MarksWritten;
                int water = effects.WaterSamples;
                await Frames(_hard ? 35 : identity == SurfaceIdentity.Water ? 20 : 65);
                Check(effects.Observed.Contains(identity), "Wheel contacts resolve " + identity);
                if (identity == SurfaceIdentity.Water)
                {
                    Check(effects.MarksWritten == marks, "Water creates zero persistent marks");
                    Check(effects.WaterSamples > water, "Water samples generate wet interaction");
                }
                else { Check(effects.MarksWritten > marks, identity + " produces tracks: " + (effects.MarksWritten-marks)); }
                await View(identity.ToString(), _car.Position + new Vector3(7,5,8), _car.Position + new Vector3(0,-.5f,2));
            }
            pad.RemoveFromGroup("water_terrain");
            pad.SetMeta("surface_identity", "Dirt");
            _hard = false;
            _stress = true;
            _camera.Position = new Vector3(0,35,30);
            _camera.LookAt(Vector3.Zero);
            await Frames(3);
            int nodes = Descendants(this);
            ulong started = Time.GetTicksMsec();
            for (int repetition = 0; repetition < 24; repetition++)
            {
                for (int slot = 0; slot < _cars.Count; slot++) { _cars[slot].ResetBody(Physics(new Vector3(-49+slot*14,.9f,15), new Vector3(0,0,-9))); }
                await Frames(100);
            }
            Check(_cars.All(car => car.GetChildren().OfType<TireFeedback>().Single().MarksWritten > TireFeedback.Capacity * 2), "Repeated use wraps the fixed track buffer: " + effects.MarksWritten);
            Check(Descendants(this) == nodes, "Repeated tire use creates no additional nodes");
            Check(effects.GetChildren().OfType<MultiMeshInstance3D>().Single(node => node.Multimesh.InstanceCount == TireFeedback.Capacity).Multimesh.InstanceCount == TireFeedback.Capacity, "Track storage stays bounded");
            _evidence.Add($"Eight vehicles, 2400 sustained physics frames in {Time.GetTicksMsec()-started} ms; scene nodes {nodes}; no performance threshold inferred.");
            await View("eight-car-stress", new Vector3(0,35,30), Vector3.Zero);
            foreach (var car in _cars) { car.GetChildren().OfType<TireFeedback>().Single().Density = 0; }
            int stopped = effects.MarksWritten;
            started = Time.GetTicksMsec();
            for (int repetition = 0; repetition < 12; repetition++)
            {
                for (int slot = 0; slot < _cars.Count; slot++) { _cars[slot].ResetBody(Physics(new Vector3(-49+slot*14,.9f,15), new Vector3(0,0,-9))); }
                await Frames(100);
            }
            _evidence.Add($"Eight vehicles, density zero: 1200 physics frames in {Time.GetTicksMsec()-started} ms; existing marks still fade. This is not a full GPU profiler.");
            Check(effects.MarksWritten == stopped, "Zero visual density suppresses emissions");
            _drive = false;
            _car = null;
            foreach (var car in _cars) { car.QueueFree(); }
            _cars.Clear();
            pad.QueueFree();
            await Frames(3);
            }
            var map = ActiveMap.Load();
            AddChild(map);
            await Frames(5);
            int mapNodes = Descendants(map);
            foreach (var preset in Enum.GetValues<EnvironmentPreset>())
            {
                environment.Apply(preset);
                await View("preset-" + preset, new Vector3(-125,13,57), new Vector3(-15,4,-25));
                await View("overview-" + preset, new Vector3(-185,105,140), Vector3.Zero);
                Check(environment.Current == preset && Descendants(map) == mapNodes, "Preset applies without map reconstruction: " + preset);
            }
            for (int repeat = 0; repeat < 20; repeat++)
            {
                foreach (var preset in Enum.GetValues<EnvironmentPreset>()) { environment.Apply(preset); await Frames(1); }
            }
            Check(Descendants(map) == mapNodes, "100 repeated preset switches retain map topology");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output,"evidence.txt"), _evidence);
            GD.Print("Terrain effects integration passed: " + _evidence.Count + " observations.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output,"evidence.txt"), _evidence.Append(exception.ToString()));
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static VehiclePhysicsState Physics(Vector3 position, Vector3 velocity = default) => new(VehicleBody.ToCore(position), N.Quaternion.Identity, VehicleBody.ToCore(velocity), N.Vector3.Zero);
    private static int Descendants(Node node) => 1 + node.GetChildren().Sum(Descendants);
    private async Task Frames(int count) { for (int i=0;i<count;i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
    private async Task View(string name, Vector3 position, Vector3 target)
    {
        _camera.Position = position;
        _camera.LookAt(target);
        await Frames(3);
        if (DisplayServer.GetName() == "headless") { return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_output, name + ".png"));
    }
    private void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
        _evidence.Add("PASS: " + message);
    }
}
