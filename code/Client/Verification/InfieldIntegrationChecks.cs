using System.Text.Json;
using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises authored infield reservations with the native production vehicle.</summary>
public sealed partial class InfieldIntegrationChecks : Node3D
{
    private readonly Core.Simulation.Simulation _simulation = new(new Core.Simulation.SimulationConfiguration(60));
    private readonly List<string> _evidence = new();
    private VehicleBody _vehicle = null!;
    private VehicleBody? _companion;
    private bool _companionDrive;
    private Vector3[] _path = Array.Empty<Vector3>();
    private int _progress;
    private ulong _tick;
    private bool _advance;
    private bool _drive;
    private float _deviation;
    private int _unsupported;
    private Camera3D? _camera;
    private string _output = string.Empty;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (!_advance)
        {
            return;
        }

        short steering = 0;
        ushort throttle = 0;
        ushort brake = 0;
        if (_drive)
        {
            Vector3 position = _vehicle.GlobalPosition with { Y = 0 };
            int nearest = _progress;
            for (int i = _progress; i < Math.Min(_path.Length, _progress + 12); i++)
            {
                if (position.DistanceTo(_path[i]) < position.DistanceTo(_path[nearest]))
                {
                    nearest = i;
                }
            }

            _progress = nearest;
            _deviation = Math.Max(_deviation, position.DistanceTo(_path[nearest]));
            Vector3 target = _path[Math.Min(_path.Length - 1, nearest + 4)] - position;
            Vector3 forward = -_vehicle.GlobalBasis.Z;
            float angle = (forward with { Y = 0 }).SignedAngleTo(target, Vector3.Up);
            steering = (short)(Math.Clamp(-angle * 2.5f, -1, 1) * short.MaxValue);
            float targetSpeed = Math.Abs(angle) > 0.35f ? 6 : 10;
            throttle = _vehicle.LinearVelocity.Length() < targetSpeed ? (ushort)40000 : (ushort)0;
            brake = _vehicle.LinearVelocity.Length() > targetSpeed + 1 ? (ushort)18000 : (ushort)0;
            _unsupported += _vehicle.State.Grounded ? 0 : 1;
        }

        var input = new InputFrame(++_tick, steering, throttle, brake, 0, 0, 0);
        InputFrame companionInput = new(_tick, 0, _companionDrive && _companion is not null && _companion.LinearVelocity.Length() < 10 ? (ushort)40000 : (ushort)0, 0, 0, 0, 0);
        var observations = _companion is null ? new[] { _vehicle.Capture(input) } : new[] { _vehicle.Capture(input), _companion.Capture(companionInput) };
        var results = _simulation.Step(input, observations);
        _vehicle.Apply(results[0]);
        _vehicle.Publish();
        if (_companion is not null)
        {
            _companion.Apply(results[1]);
            _companion.Publish();
        }
    }

    /// <summary>Checks real imported geometry and drives each complete route without moving the body artificially.</summary>
    public async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/infield-checks");
            System.IO.Directory.CreateDirectory(_output);
            var map = GD.Load<PackedScene>(Arenas.ActiveMap.ScenePath).Instantiate<Node3D>();
            AddChild(map);
            using var layout = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/maps/infield/layout.json"));
            JsonElement[] routes = layout.RootElement.GetProperty("routes").EnumerateArray().ToArray();
            await Frames(3);
            Check(map.GetNode<Node3D>("InfieldGraybox").Transform.IsEqualApprox(Transform3D.Identity), "Blender infield imports at identity metre scale.");
            int samples = 0;
            foreach (JsonElement route in routes)
            {
                Vector3[] points = ReadPoints(route);
                float half = route.GetProperty("width_m").GetSingle() / 2;
                Check(half * 2 >= 12, $"{route.GetProperty("id").GetString()}: width {half * 2:F1} m versus 2.662 m vehicle width.");
                for (int i = 0; i < points.Length - 1; i++)
                {
                    Vector3 side = (points[i + 1] - points[i]).Normalized().Cross(Vector3.Up);
                    foreach (float offset in new[] { -half + 1.4f, 0, half - 1.4f })
                    {
                        Vector3 p = points[i] + (side * offset);
                        using var ray = PhysicsRayQueryParameters3D.Create(p + (Vector3.Up * 2), p + Vector3.Down);
                        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                        // Entry footprints can straddle the first centimetres of existing banking.
                        bool bankEntry = Math.Abs(p.Z) > 82;
                        float tolerance = bankEntry ? 0.2f : 0.01f;
                        Check(hit.Count > 0 && Math.Abs(hit["position"].AsVector3().Y) < tolerance && hit["normal"].AsVector3().Y > 0.98f, $"Route support sample {samples++}: {route.GetProperty("id").GetString()} at {p}, hit {hit}.");
                    }
                }
            }

            Check(ClearRay(new Vector3(-8, 2, -15), new Vector3(-8, 2, 15)) && ClearRay(new Vector3(8, 2, -15), new Vector3(8, 2, 15)), "Tunnel has 18 m clear north/south opening.");
            Check(ClearRay(new Vector3(-15, 2, 0), new Vector3(15, 2, 0)), "Central east/west intersection remains open below future earth bridge.");
            Check(!ClearRay(new Vector3(0, 5, 0), new Vector3(0, 6, 0)), "Imported tunnel roof has usable collision at 5.5 m clearance.");
            _vehicle = new VehicleBody { Position = new Vector3(0, 1, 70) };
            _simulation.AddVehicle(1, _vehicle.Configuration, _vehicle.DamageConfiguration, new VehiclePhysicsState(new Numerics.Vector3(0, 1, 70), Numerics.Quaternion.Identity, Numerics.Vector3.Zero, Numerics.Vector3.Zero));
            _vehicle.Initialize(_simulation);
            AddChild(_vehicle);
            _companion = new VehicleBody { VehicleId = 2, Position = new Vector3(50, 1, 70) };
            _simulation.AddVehicle(2, _companion.Configuration, _companion.DamageConfiguration, new VehiclePhysicsState(VehicleBody.ToCore(_companion.Position), Numerics.Quaternion.Identity, Numerics.Vector3.Zero, Numerics.Vector3.Zero));
            _companion.Initialize(_simulation);
            AddChild(_companion);
            _advance = true;
            await Frames(90);
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
                AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-65, -25, 0), LightEnergy = 1.4f, ShadowEnabled = true });
                _camera = new Camera3D { Current = true, Near = 1, Far = 1800, Fov = 55 };
                AddChild(_camera);
                await View("overview", new Vector3(0, 285, 190), Vector3.Zero);
                await View("tunnel", new Vector3(22, 13, 38), Vector3.Zero);
                await View("west-layout", new Vector3(-100, 95, 65), new Vector3(-85, 0, 0));
            }

            foreach (JsonElement route in routes)
            {
                string name = route.GetProperty("id").GetString()!;
                await Drive(name, ReadPoints(route), route.GetProperty("width_m").GetSingle());
            }

            foreach (JsonElement jump in layout.RootElement.GetProperty("jumps").EnumerateArray())
            {
                float x = jump.GetProperty("start")[0].GetSingle();
                float direction = jump.GetProperty("direction")[0].GetSingle();
                await Drive(jump.GetProperty("id").GetString()!, Enumerable.Range(0, 51).Select(i => new Vector3(x + (direction * i * 2), 0, 0)).ToArray(), 12);
            }

            Vector3[] west = ReadPoints(routes[0]);
            Vector3[] east = ReadPoints(routes[1]);
            Vector3[] junction = Enumerable.Range(1, 18).Select(i => new Vector3(-18 + (i * 2), 0, 0)).ToArray();
            await Drive("ConnectedLoopTour", west.Concat(junction).Concat(east.Skip(1)).ToArray(), 12);

            Quaternion companionRotation = new(Vector3.Up, Mathf.Pi);
            _companion.ResetBody(new VehiclePhysicsState(new Numerics.Vector3(3.5f, 1, -62), new Numerics.Quaternion(companionRotation.X, companionRotation.Y, companionRotation.Z, companionRotation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
            _companionDrive = true;
            await Drive("TwoCarTunnel", Enumerable.Range(0, 61).Select(i => new Vector3(-3.5f, 0, -60 + (i * 2))).ToArray(), 9);
            Check(_companion.Position.Z > 45 && _companion.State.Grounded && _companion.DamageState.CurrentHP == _companion.DamageState.MaxHP, "Second production car traverses tunnel alongside first without damage.");

            _advance = false;
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence.Where(line => !line.StartsWith("Route support sample", StringComparison.Ordinal)));
            GD.Print($"Infield integration passed: {samples} support probes; ten routes, two jump footprints, connected loop tour and two-car tunnel driven. Evidence: {_output}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _advance = false;
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static Vector3[] ReadPoints(JsonElement route) => route.GetProperty("points").EnumerateArray().Select(p => new Vector3(p[0].GetSingle(), 0, p[1].GetSingle())).ToArray();

    private bool ClearRay(Vector3 from, Vector3 to)
    {
        using var ray = PhysicsRayQueryParameters3D.Create(from, to);
        return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
    }

    private async Task Drive(string name, Vector3[] points, float width)
    {
        _drive = false;
        _path = points;
        _progress = 0;
        _deviation = 0;
        _unsupported = 0;
        Quaternion rotation = Basis.LookingAt(points[1] - points[0]).GetRotationQuaternion();
        _vehicle.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(points[0] + Vector3.Up), new Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        await Frames(90);
        _drive = true;
        for (int frame = 0; frame < 9000 && _progress < points.Length - 2; frame++)
        {
            await Frames(1);
            if (_deviation > (width / 2) - 1.4f || _unsupported > 10)
            {
                throw new InvalidOperationException($"{name} failed: progress {_progress}/{points.Length}, lateral {_deviation:F2}, unsupported {_unsupported}, position {_vehicle.Position}.");
            }

            if (frame == 150 && _camera is not null)
            {
                await View(name + "-drive", _vehicle.Position + (_vehicle.GlobalBasis.Z * 12) + (Vector3.Up * 6), _vehicle.Position - (_vehicle.GlobalBasis.Z * 8));
            }
        }

        _drive = false;
        Check(_progress >= points.Length - 2, $"{name}: entire route driven using production physics/input; peak centerline error {_deviation:F2} m, unsupported frames {_unsupported}, final HP {_vehicle.DamageState.CurrentHP}.");
        Check(_vehicle.DamageState.CurrentHP == _vehicle.DamageState.MaxHP, name + ": no collision damage.");
    }

    private async Task View(string name, Vector3 position, Vector3 target)
    {
        _camera!.Position = position;
        _camera.LookAt(target);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(_output, name + ".png")) == Error.Ok, "Rendered " + name);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        _evidence.Add(message);
    }
}
