using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Actual imported basin immersion and repeated native water lifecycles.</summary>
public sealed partial class WaterIntegrationChecks : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private VehicleBody? _native;
    private NetworkVehicleBody? _network;
    private bool _advance;
    private ushort _throttle;
    private float _lastDepth;
    private readonly List<string> _lines = new();
    private string _output = "";
    private Camera3D? _camera;

    public override void _Ready() => CallDeferred(MethodName.Run);
    public override void _PhysicsProcess(double delta)
    {
        if (!_advance) { return; }
        var input = new InputFrame(_world.State.Tick + 1, 0, _throttle, 0, 0, 0, 0);
        var request = _native is not null ? _native.Capture(input) : new VehicleStepRequest(1, input, _network!.Observe(_world.GetVehicle(1)));
        _lastDepth = request.Observation.WaterDepth;
        var result = _world.Step(input, new[] { request })[0];
        if (_native is not null) { _native.Apply(result); _native.Publish(); }
        else { _network!.Apply(result.Snapshot.Movement.Physics); }
    }

    private async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/water-checks");
            System.IO.Directory.CreateDirectory(_output);
            AddChild(GD.Load<PackedScene>(Arenas.ActiveMap.ScenePath).Instantiate<Node3D>());
            await Frames(4);
            Check(GetTree().GetNodesInGroup("water_terrain").Count > 0, "Imported water field and level are registered");
            VerifyWaterRenderBounds();
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
                AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightEnergy = 1.4f });
                _camera = new Camera3D { Current = true, Far = 1500, Position = new(91, 10, -20) };
                AddChild(_camera); _camera.LookAt(new(77, -1, -35));
            }
            // Native collision heights define the vehicle poses, not hard-coded basin depth.
            var probe = new NetworkVehicleBody { VehicleId = 99 }; AddChild(probe);
            int shallow = 0, deep = 0;
            for (float x = 66; x <= 88; x += .5f)
            for (float z = -41; z <= -29; z += .5f)
            {
                Vector3 floor = Ground(x, z);
                float depth = WaterObservation.Observe(probe, new Transform3D(Basis.Identity, floor + Vector3.Up * VehicleDimensions.RideHeight));
                if (depth > 0 && depth < 1) { shallow++; }
                if (depth >= 1) { deep++; }
            }
            Check(shallow > 20 && deep > 20, $"Actual Water basin samples: {shallow} shallow / {deep} deep");
            foreach (var p in new[] { new Vector3(-57, -100, -29), new Vector3(-85, -100, 28), new Vector3(85, -100, 28) })
            { Check(WaterObservation.Observe(probe, new(Basis.Identity, p)) == 0, "Mud basin excludes Water even below terrain: " + p); }
            Check(WaterObservation.Observe(probe, new(Basis.Identity, new(77, 5, -35))) == 0, "Jump above pond is dry");
            Check(WaterObservation.Observe(probe, new(new Basis(Vector3.Forward, Mathf.Pi), new(77, -5, -35))) > 1, "Inverted/subterrain pose still detects deep water without contact");
            probe.QueueFree(); await Frames(2);
            foreach (bool network in new[] { false, true })
            {
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    await Start(network, Ground(77, -35) + Vector3.Up * VehicleDimensions.RideHeight);
                    _advance = true; _throttle = 0;
                    await Frames(90);
                    Check(_lastDepth >= 1 && _world.GetVehicle(1).Damage.CurrentHP < 800, $"{network}/{cycle}: submerged HP={_world.GetVehicle(1).Damage.CurrentHP:F2}, immersion={_lastDepth:F3}");
                    await Capture($"{network}-{cycle}-submerged");
                    await Frames(170);
                    Check(!_world.GetVehicle(1).CanInteract && _world.GetVehicle(1).Damage.CurrentHP == 0, $"{network}/{cycle}: ordinary water death");
                    await Frames(70);
                    Check(_world.GetVehicle(1).LifeId == 2 && _world.GetVehicle(1).CanInteract && _world.GetVehicle(1).Damage.CurrentHP == 1000, $"{network}/{cycle}: ordinary respawn, fresh HP and life");
                    await Stop();
                }
                for (int exit = 0; exit < 3; exit++)
                {
                    await Start(network, Ground(85, -35) + Vector3.Up * VehicleDimensions.RideHeight, -Mathf.Pi / 2);
                    _advance = true; _throttle = 65535;
                    await Frames(1);
                    Check(_lastDepth is > 0 and < 1, $"{network}/{exit}: shallow starting immersion {_lastDepth:F3}");
                    await Frames(600);
                    Check(_lastDepth == 0 && _world.GetVehicle(1).Damage.CurrentHP == 1000, $"{network}/{exit}: drives out of shallow water without damage or sticky state; depth={_lastDepth:F3}, HP={_world.GetVehicle(1).Damage.CurrentHP:F2}, position={_world.GetVehicle(1).ObservedPhysics.Position}");
                    await Stop();
                }
                // Drive into the basin from the wet-soil bank, observing the actual transition.
                await Start(network, Ground(77, -43) + Vector3.Up * VehicleDimensions.RideHeight, Mathf.Pi);
                _advance = true; _throttle = 30000;
                bool sawShallow = false, sawDeep = false;
                for (int frame = 0; frame < 600; frame++)
                {
                    await Frames(1);
                    if (_lastDepth is > 0 and < 1) { sawShallow = true; }
                    if (_lastDepth >= 1) { sawDeep = true; break; }
                }
                Check(sawShallow && sawDeep, $"{network}: input-driven bank → shallow → deep transition");
                await Capture($"{network}-entry");
                await Stop();
            }
            GD.Print("Water integration passed: actual basin, both adapters, repeated death/respawn and input-driven entry.");
            GetTree().Quit();
        }
        catch (Exception error) { _advance = false; Log(error.ToString()); GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void VerifyWaterRenderBounds()
    {
        var water = (MeshInstance3D)FindChild("WaterSurface", true, false);
        var material = (ShaderMaterial)water.MaterialOverride;
        Vector4 bounds = material.GetShaderParameter("surface_bounds").AsVector4();
        Vector2 origin = material.GetShaderParameter("water_origin").AsVector2();
        Check(origin.IsEqualApprox(new Vector2(water.Position.X, water.Position.Z)), "Water shading retains the field's original coordinates");
        var plane = (PlaneMesh)water.Mesh;
        Check(plane.Size.X * plane.Size.Y < bounds.Z * bounds.W / 100, $"Water render area reduced from {bounds.Z * bounds.W:F0} to {plane.Size.X * plane.Size.Y:F2} square metres");
        using var image = ((Texture2D)material.GetShaderParameter("surface_field")).GetImage();
        int wet = 0;
        for (int y = 0; y < image.GetHeight(); y++)
        for (int x = 0; x < image.GetWidth(); x++)
        {
            if (image.GetPixel(x, y).A < .5f) { continue; }
            Vector2 point = new(bounds.X + (x + .5f) * bounds.Z / image.GetWidth(), bounds.Y + (y + .5f) * bounds.W / image.GetHeight());
            Vector2 margin = plane.Size / 2 - (point - origin).Abs();
            if (margin.X < bounds.Z / image.GetWidth() || margin.Y < bounds.W / image.GetHeight())
            { throw new InvalidOperationException("Water crop clips the field or bilinear guard band."); }
            wet++;
        }
        Check(wet > 0, $"All {wet} water texels and their filtering margins remain inside the rendered surface");
    }

    private async Task Start(bool network, Vector3 position, float yaw = 0)
    {
        _world = new(new(60), new RespawnConfiguration { DelayTicks = 60 });
        var pose = new VehiclePhysicsState(VehicleBody.ToCore(position), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, yaw), N.Vector3.Zero, N.Vector3.Zero);
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        _world.AddVehicle(1, new(), damage, pose);
        if (network) { _network = new NetworkVehicleBody { VehicleId = 1 }; AddChild(_network); _network.Apply(pose); }
        else { _native = new VehicleBody { Position = position, Rotation = new(0, yaw, 0), CollisionLayer = 2, CollisionMask = 1, DamageConfiguration = damage }; _native.Initialize(_world); AddChild(_native); }
        await Frames(2);
    }
    private async Task Stop() { _advance = false; _native?.QueueFree(); _network?.QueueFree(); _native = null; _network = null; await Frames(3); }
    private Vector3 Ground(float x, float z)
    {
        using var query = PhysicsRayQueryParameters3D.Create(new(x, 20, z), new(x, -20, z), 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) { throw new InvalidOperationException($"Missing terrain {x},{z}"); }
        return hit["position"].AsVector3();
    }
    private async Task Capture(string name)
    {
        if (_camera is null) { return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(_output, name + ".png")) == Error.Ok, "Rendered " + name);
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
    private void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } Log(message); }
    private void Log(string message) { _lines.Add(message); System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _lines); GD.Print(message); }
}
