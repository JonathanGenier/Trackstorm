using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Statistics;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises imported material identity, native support, driving transitions and read-only Stats.</summary>
public sealed partial class SurfaceIntegrationChecks : Node3D
{
    private readonly Core.Simulation.Simulation _simulation = new(new Core.Simulation.SimulationConfiguration(60));
    private readonly List<string> _evidence = new();
    private readonly HashSet<SurfaceIdentity> _seen = new();
    private VehicleBody _vehicle = null!;
    private Camera3D? _camera;
    private VehicleArena? _practice;
    private ulong _practiceTick;
    private bool _advance;
    private bool _drive;
    private ulong _tick;
    private string _output = string.Empty;

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _PhysicsProcess(double delta)
    {
        _practice?.Advance(new InputFrame(++_practiceTick, 0, 0, 0, 0, 0, 0));
        if (!_advance) { return; }
        var input = new InputFrame(++_tick, 0, _drive && _vehicle.LinearVelocity.Length() < 8 ? (ushort)35000 : (ushort)0, 0, 0, 0, 0);
        var result = _simulation.Step(input, new[] { _vehicle.Capture(input) });
        _vehicle.Apply(result[0]);
        _vehicle.Publish();
        if (_vehicle.DetectedSurface is { } identity) { _seen.Add(identity); }
    }

    private async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/surface-checks");
            System.IO.Directory.CreateDirectory(_output);
            var map = GD.Load<PackedScene>(Arenas.ActiveMap.ScenePath).Instantiate<Node3D>();
            AddChild(map);
            await Frames(3);
            var locations = new Dictionary<SurfaceIdentity, Vector3>();
            for (int x = -190; x <= 190; x += 2)
            {
                for (int z = -94; z <= 94; z += 2)
                {
                    var hit = Ray(x, z);
                    if (hit.Count == 0) { continue; }
                    var identity = SurfaceIdentityResolver.Resolve(hit["collider"].AsGodotObject(), hit["position"].AsVector3());
                    Check(identity.HasValue, $"Authored support at {x},{z}", false);
                    if (hit["normal"].AsVector3().Y > .96f && !locations.ContainsKey(identity!.Value)) { locations.Add(identity.Value, hit["position"].AsVector3()); }
                }
            }

            foreach (SurfaceIdentity identity in Enum.GetValues<SurfaceIdentity>()) { Check(locations.ContainsKey(identity), "Imported native support includes " + identity); }
            // Use the broad driving slab, not a narrow pier/parapet top, for a whole-car probe.
            locations[SurfaceIdentity.Concrete] = Ray(0, 0)["position"].AsVector3();
            foreach (var basin in new[] { new Vector3(-57, 0, -29), new Vector3(77, 0, -35), new Vector3(-85, 0, 28), new Vector3(85, 0, 28) })
            {
                var hit = Ray(basin.X, basin.Z);
                var identity = SurfaceIdentityResolver.Resolve(hit["collider"].AsGodotObject(), hit["position"].AsVector3());
                Check(identity == (basin.X == 77 ? SurfaceIdentity.Water : SurfaceIdentity.DeepMud), $"Basin {basin}: {identity}; unchanged negative collision {hit["position"].AsVector3().Y:F3}m");
            }

            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
                AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 1.4f, ShadowEnabled = true });
                _camera = new Camera3D { Current = true, Far = 1800, Fov = 55 };
                AddChild(_camera);
                await View("overview", new Vector3(0, 285, 190), Vector3.Zero);
                await View("wet-basins", new Vector3(70, 42, 67), new Vector3(70, 0, 24));
                await View("water", new Vector3(100, 14, -17), new Vector3(77, -1, -35));
                await View("route-shoulders", new Vector3(-145, 8, 38), new Vector3(-140, 1, 8));
            }

            _vehicle = new VehicleBody { Position = new Vector3(0, 1, 70), CollisionLayer = 2, CollisionMask = 1, DamageConfiguration = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 } };
            _simulation.AddVehicle(1, _vehicle.Configuration, _vehicle.DamageConfiguration, Physics(_vehicle.Position));
            _vehicle.Initialize(_simulation);
            AddChild(_vehicle);
            _advance = true;
            await Frames(3); // Let the native body enter physics before requesting the first reset.
            foreach (var pair in locations)
            {
                _drive = false;
                _vehicle.ResetBody(Physics(pair.Value + Vector3.Up * .9f));
                await Frames(20);
                Check(_vehicle.DetectedSurface == pair.Key, $"Production practice detects {pair.Key}: {_vehicle.DetectedSurface}; requested {pair.Value}, actual {_vehicle.Position}");
                var proxy = new NetworkVehicleBody { VehicleId = 2 };
                AddChild(proxy);
                proxy.CollisionMask = 1;
                _vehicle.CollisionLayer = 2;
                proxy.Apply(_vehicle.Snapshot.Movement.Physics);
                var observation = proxy.Observe(_vehicle.Snapshot);
                Check(proxy.DetectedSurface == pair.Key, $"Host/prediction native adapter detects {pair.Key}: {proxy.DetectedSurface}");
                Check(observation.Surface == SurfaceHandling.Resolve(pair.Key) && _vehicle.State.CurrentSurface == observation.Surface, $"Both adapters apply {observation.Surface} handling for {pair.Key}");
                proxy.QueueFree();
                await Frames(1);
            }

            foreach (var route in new[] {
                ("oval-infield",new Vector3(0,0,98),0f,300,new[]{SurfaceIdentity.Asphalt,SurfaceIdentity.Dirt}),
                ("mud-west",new Vector3(-57,0,-47),Mathf.Pi,160,new[]{SurfaceIdentity.Mud,SurfaceIdentity.DeepMud}),
                ("water-northeast",new Vector3(77,0,-53),Mathf.Pi,170,new[]{SurfaceIdentity.Mud,SurfaceIdentity.Water}),
                ("grass-shoulder",new Vector3(-130,0,35),0f,150,new[]{SurfaceIdentity.Grass,SurfaceIdentity.Dirt}),
                ("rock-shoulder",locations[SurfaceIdentity.Rock]-new Vector3(6,0,0),-Mathf.Pi/2,180,new[]{SurfaceIdentity.Rock}),
                ("deck",new Vector3(-19,0,0),-Mathf.Pi/2,150,new[]{SurfaceIdentity.Dirt,SurfaceIdentity.Concrete}) })
            {
                var hit = Ray(route.Item2.X, route.Item2.Z);
                Vector3 start = hit["position"].AsVector3() + Vector3.Up * .9f;
                Quaternion rotation = new(Vector3.Up, route.Item3);
                Vector3 velocity = new Basis(rotation) * Vector3.Forward * 7;
                _vehicle.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(start), new N.Quaternion(rotation.X,rotation.Y,rotation.Z,rotation.W),VehicleBody.ToCore(velocity),N.Vector3.Zero));
                await Frames(3);
                _seen.Clear();
                _drive = true;
                await Frames(route.Item4);
                Check(route.Item5.All(_seen.Contains), $"Driven {route.Item1}: {string.Join(", ",_seen)}; end {_vehicle.Position}; HP {_vehicle.DamageState.CurrentHP}");
                Check(_vehicle.Position.Y > -5 && _vehicle.State.Grounded, "Route retains native support after " + route.Item1);
                if (_camera is not null) { await View(route.Item1,_vehicle.Position+new Vector3(9,6,10),_vehicle.Position); }
            }

            _drive = false;
            _vehicle.ResetBody(Physics(new Vector3(0, 30, 0)));
            await Frames(4);
            Check(_vehicle.DetectedSurface is null, "Airborne detection clears instead of inventing current contact");
            _advance = false;
            _vehicle.QueueFree();
            map.QueueFree();
            await Frames(2);
            var practice = new VehicleArena();
            AddChild(practice);
            _practice = practice;
            await Frames(100);
            var stats = RuntimeStatistics.Capture(null, practice, practice.Player.VehicleId);
            Check(stats.Global.Any(s => s.Title == "Local surface / material" && s.Text.Contains("Detected surface: Asphalt",StringComparison.Ordinal)), "Existing Stats reads the local practice surface as Asphalt: " + string.Join("; ",stats.Global.Where(s=>s.Title.Contains("surface",StringComparison.Ordinal)).Select(s=>s.Text)));
            var panel = new StatisticPanel { Capture = id => RuntimeStatistics.Capture(null, practice, id), Size = new Vector2(1200,650) };
            var canvas = new CanvasLayer();
            AddChild(canvas);
            canvas.AddChild(panel);
            await Frames(35);
            if (_camera is not null)
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var screenshot = GetViewport().GetTexture().GetImage();
                screenshot.SavePng(System.IO.Path.Combine(_output,"stats.png"));
            }

            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output,"evidence.txt"),_evidence);
            // Stop callbacks and release native arena/audio resources while the scene tree
            // and mixer still run, before Godot begins terminal managed-resource shutdown.
            _practice = null;
            canvas.QueueFree();
            practice.QueueFree();
            await Frames(6);
            await Task.Delay(100);
            GD.Print("Surface integration passed: all eight identities, native adapters, driving transitions and Stats.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output,"evidence.txt"),_evidence.Append(error.ToString()));
            GetTree().Quit(1);
        }
    }

    private static VehiclePhysicsState Physics(Vector3 p) => new(VehicleBody.ToCore(p),N.Quaternion.Identity,N.Vector3.Zero,N.Vector3.Zero);
    private Godot.Collections.Dictionary Ray(float x,float z)
    {
        using var ray = PhysicsRayQueryParameters3D.Create(new Vector3(x,25,z),new Vector3(x,-8,z),1);
        return GetWorld3D().DirectSpaceState.IntersectRay(ray);
    }

    private async Task View(string name,Vector3 position,Vector3 target)
    {
        _camera!.Position=position;
        _camera.LookAt(target);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using var image=GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(_output,name+".png"))==Error.Ok,"Rendered "+name);
    }

    private async Task Frames(int count)
    {
        for(int i=0;i<count;i++) { await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame); }
    }

    private void Check(bool condition,string message,bool record=true)
    {
        if(!condition) { throw new InvalidOperationException(message); }
        if(record) { _evidence.Add(message); GD.Print(message); }
    }
}
