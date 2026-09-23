using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises the production camera against native solid geometry, with optional movie evidence.</summary>
public sealed partial class CameraObstructionChecks : Node3D
{
    private VehicleChaseCamera _camera = new() { Current = true, Fov = 65 };
    private readonly PlayerInput _input = new();
    private readonly Label _label = new() { Position = new Vector2(24, 24) };
    private readonly List<object> _results = new();
    private readonly List<object> _frames = new();
    private VehicleSnapshot _state = null!;
    private Node3D _car = null!;
    private StaticBody3D _obstacle = null!;
    private StaticBody3D _followedBody = null!;
    private StaticBody3D _ceiling = null!;
    private RigidBody3D _prop = null!;
    private Vector3 _bankPosition;
    private int _phase = -1;
    private int _frame;
    private int _fps;
    private float _previousDistance;
    private float _minDistance;
    private float _maxStep;
    private float _maxCorrection;
    private string _output = string.Empty;
    private bool _baseline;
    private static readonly string[] Names = ["Wall approach / hold", "Wall clears / recovery", "Thin barrier / low orbit", "Building corner orbit", "Moving solid prop", "Sloped terrain", "Repeated boundary crossings", "Shake beside wall", "Life reset", "Replacement reset", "Resume reset", "Scene reconstruction", "Production yard wall", "Production container", "Production barrier", "Production oval bank", "Overlapping target recovery", "Wide near plane", "Cramped ceiling"];

    /// <inheritdoc/>
    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _output = args.Single(value => value.StartsWith("--obstruction-output=", StringComparison.Ordinal))[21..];
        _fps = int.Parse(args.Single(value => value.StartsWith("--test-fps=", StringComparison.Ordinal))[11..]);
        _baseline = args.Contains("--baseline");
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("27384e"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightEnergy = 0.7f } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 1.4f });
        AddBox(new Vector3(100, 1, 100), new Vector3(0, -0.5f, 0), new Color("55616d"));
        _obstacle = AddBox(new Vector3(40, 12, 1), new Vector3(0, 6, 16), new Color("ba704a"));
        _ceiling = AddBox(new Vector3(20, 0.4f, 20), new Vector3(100, 3.2f, 0), new Color("899ead"));
        _followedBody = new StaticBody3D();
        _followedBody.AddChild(VehicleVisual.CreateCollision());
        AddChild(_followedBody);
        _prop = new RigidBody3D { Freeze = true, FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic, Position = new Vector3(100, 4, 0) };
        _prop.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4, 8, 2) } });
        _prop.AddChild(VehicleBody.Box(new Vector3(4, 8, 2), Vector3.Zero, new Color("b6a150")));
        AddChild(_prop);
        AddChild(new Arenas.CombatArena { Position = new Vector3(-500, 0, 0), Replica = true });
        Node3D oval = Arenas.ActiveMap.Load();
        oval.Position = new Vector3(500, 0, 0);
        AddChild(oval);
        _car = VehicleVisual.Create(new StandardMaterial3D { AlbedoColor = new Color("69b5c9") });
        AddChild(_car);
        AddChild(_input);
        _input.SetPhysicsProcess(false);
        _input.GameplayAvailable = () => true;
        _input._Process(0);
        _input.Adapter.Enabled = true;
        _input.Adapter.CameraAvailable = true;
        AddChild(_camera);
        _camera.InputSource = _input.Adapter;
        var layer = new CanvasLayer();
        AddChild(layer);
        layer.AddChild(_label);
        _label.AddThemeFontSizeOverride("font_size", 24);
        var simulation = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60));
        simulation.AddVehicle(1, new VehicleConfiguration(), new DamageConfiguration(), new VehiclePhysicsState(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero));
        _state = simulation.GetVehicle(1);
        NextPhase();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        try
        {
            float dt = 1f / _fps;
            // Keep synthetic input active if a different verification window gains focus.
            _input.Adapter.Enabled = true;
            _input._Process(0);
            float time = _frame++ * dt;
            Vector3 position = new(0, VehicleDimensions.RideHeight, 0);
            if (_phase == 0) position.Z = Math.Min(time, 1.5f) * 6;
            if (_phase == 1) position.Z = 9 - Math.Min(time, 1.5f) * 12;
            if (_phase is 2 or 3)
            {
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
                if (_frame == 2 && _phase == 2) Send(new InputEventMouseMotion { ScreenRelative = new Vector2(0, -200) });
                if (_phase == 3 && time < 2) Send(new InputEventMouseMotion { ScreenRelative = new Vector2(750 * dt, 0) });
            }
            if (_phase == 4) _prop.Position = new Vector3(0, 4, 12 - 5 * MathF.Sin(time * 1.5f));
            if (_phase == 6) position.X = 3.1f + MathF.Sin(time * 15) * 0.1f;
            if (_phase == 7 && _frame % (_fps / 2) == 2) _camera.Motion.Impulse(0.85f);
            if (_phase is >= 8 and <= 11 && time >= 1)
            {
                position.X = 40;
                if (_frame == _fps + 1)
                {
                    if (_phase is 8 or 9) _state = new VehicleSnapshot(_state.VehicleId + (_phase == 9 ? 1ul : 0), _state.LifeId + (_phase == 8 ? 1ul : 0), _state.Movement, _state.Damage, _state.ObservedPhysics);
                    else if (_phase == 10) _camera.ResetFollow();
                    else
                    {
                        _camera.QueueFree();
                        _camera = new VehicleChaseCamera { Current = true, Fov = 65, InputSource = _input.Adapter };
                        AddChild(_camera);
                    }
                }
            }
            if (_phase == 12) position = new Vector3(-500, VehicleDimensions.RideHeight, 44);
            if (_phase == 13) position = new Vector3(-517, VehicleDimensions.RideHeight, -6);
            if (_phase == 14) position = new Vector3(-500, VehicleDimensions.RideHeight, 12);
            if (_phase == 15) position = _bankPosition;
            if (_phase is 13 or 14 or 15)
            {
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
                if (_frame == 2) Send(new InputEventMouseMotion { ScreenRelative = new Vector2(0, -200) });
                if (_phase == 15 && time > 1 && time < 2) Send(new InputEventMouseMotion { ScreenRelative = new Vector2(150 * dt, 0) });
            }
            if (_phase == 16) position.Y = -0.45f;
            var pose = new Transform3D(Basis.Identity, position);
            _car.GlobalTransform = pose;
            _followedBody.GlobalTransform = pose;
            _camera.Follow(pose, _state, dt, _followedBody.GetRid());
            Vector3 pivot = position + Vector3.Up * 0.5f;
            float distance = _camera.GlobalPosition.DistanceTo(pivot);
            float chaseRadius = new Vector2(_camera.FollowDistance, _camera.CameraHeight - 0.5f).Length();
            _maxCorrection = Math.Max(_maxCorrection, _camera.GlobalPosition.DistanceTo(pivot + _camera.GlobalBasis.Z * chaseRadius));
            float step = Math.Abs(distance - _previousDistance);
            _minDistance = Math.Min(_minDistance, distance);
            _maxStep = Math.Max(_maxStep, _frame > 2 ? step : 0);
            using var sphere = new SphereShape3D { Radius = 0.20f };
            using var query = new PhysicsShapeQueryParameters3D { Shape = sphere, Transform = new Transform3D(Basis.Identity, _camera.GlobalPosition), CollisionMask = 1, Margin = 0, Exclude = new Godot.Collections.Array<Rid> { _followedBody.GetRid() } };
            bool overlaps = GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count > 0;
            if (_baseline)
            {
                if (overlaps)
                {
                    GD.Print("Camera obstruction baseline reproduced: camera volume enters a native wall.");
                    GetTree().Quit();
                }
                if (time > 2.9f) throw new InvalidOperationException("Baseline did not reproduce clipping.");
                return;
            }
            Require(!overlaps, $"{Names[_phase]} frame {_frame}: rendered camera volume must remain outside solids at {_camera.GlobalPosition}");
            if (_phase == 18) Require(_camera.GlobalPosition.Y < 3, "Close-view lift cannot pass through the ceiling");
            sphere.Radius = 0.01f;
            Vector2 screen = GetViewport().GetVisibleRect().Size;
            foreach (Vector2 corner in new[] { Vector2.Zero, new Vector2(screen.X, 0), screen, new Vector2(0, screen.Y) })
            {
                query.Transform = new Transform3D(Basis.Identity, _camera.ProjectPosition(corner, _camera.Near));
                Require(GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0, "Actual near-plane corners stay outside world geometry");
            }
            Require(_camera.GlobalBasis.Y.Y > 0 && _camera.GlobalTransform.IsFinite(), "Finite level camera");
            if (_phase == 0 && time > 2.8f) Require(step < 0.001f, "Stationary wall must not jitter");
            if (_phase == 1 && time > 1.6f) Require(distance >= _previousDistance - 0.001f, "Clearing must recover monotonically");
            if (_phase is >= 8 and <= 11 && time >= 1) Require(distance > 16, "Lifecycle boundary discards stale contraction immediately");
            _frames.Add(new { phase = Names[_phase], time, distance, x = _camera.Position.X, y = _camera.Position.Y, z = _camera.Position.Z });
            _previousDistance = distance;
            _label.Text = $"TS-132 | {Names[_phase]}\nCamera distance {distance:0.00} m | {time:0.00}s | {_fps} FPS";
            if (time >= 3)
            {
                Require(_phase != 1 || distance > 16.3f, "Returns to full chase distance");
                Require(_phase != 3 || _maxStep < 100f / _fps, $"Corner contraction must not jump several metres per frame: {_maxStep}");
                Require(_phase is not (0 or 2 or 3 or 4 or 5 or 7) || _minDistance < 14, "Fixture must actually obstruct the camera");
                Require(_phase is not (12 or 13 or 14 or 15 or 17) || _maxCorrection > 0.2f, "Production/near-plane fixture must actually correct the camera");
                _results.Add(new { phase = Names[_phase], minimumDistance = _minDistance, finalDistance = distance, maximumDistanceStep = _maxStep });
                GD.Print($"Obstruction: {Names[_phase]}, min={_minDistance:F3}m, final={distance:F3}m, max-step={_maxStep:F3}m");
                NextPhase();
            }
        }
        catch (Exception exception)
        {
            File.WriteAllText(_output + ".failed.json", System.Text.Json.JsonSerializer.Serialize(_frames));
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private void NextPhase()
    {
        if (++_phase == Names.Length)
        {
            File.WriteAllText(_output + ".json", System.Text.Json.JsonSerializer.Serialize(new { results = _results, frames = _frames }));
            GD.Print("Camera obstruction checks passed.");
            GetTree().Quit();
            return;
        }
        _frame = 0;
        _minDistance = float.MaxValue;
        _maxStep = 0;
        _maxCorrection = 0;
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        if (_phase != 1) _camera.ResetFollow();
        _camera.Near = _phase == 17 ? 0.5f : 0.05f;
        _camera.Fov = _phase == 17 ? 90 : 65;
        _prop.Position = new Vector3(100, 4, 0);
        _ceiling.Position = new Vector3(_phase == 18 ? 0 : 100, 3.2f, 0);
        _obstacle.Rotation = Vector3.Zero;
        Vector3 size = _phase switch { 2 => new(20, 2, 0.15f), 3 => new(8, 12, 8), 4 => new(4, 8, 2), 5 => new(30, 1, 30), 6 => new(6, 12, 1), _ => new(40, 12, 1) };
        _obstacle.GetNode<CollisionShape3D>("Shape").Shape = new BoxShape3D { Size = size };
        _obstacle.GetNode<MeshInstance3D>("Mesh").Mesh = new BoxMesh { Size = size };
        _obstacle.Position = _phase switch { 0 or 1 => new(0, 6, 16), 2 => new(0, 1, 6), 3 => new(-7, 6, 7), 5 => new(0, 4, 8), _ => new(0, 6, 8) };
        if (_phase == 5) _obstacle.Rotation = new Vector3(-0.55f, 0, 0);
        if (_phase == 18) _obstacle.Position = new Vector3(0, 6, 3);
        if (_phase is 4 or >= 12 and <= 16) _obstacle.Position = new Vector3(100, 6, 0);
        if (_phase == 15)
        {
            for (int z = 1; z < 250; z++)
            {
                using var ray = PhysicsRayQueryParameters3D.Create(new Vector3(650, 80, z), new Vector3(650, -10, z), 1);
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                if (hit.Count == 0 || hit["position"].AsVector3().Y < 2) continue;
                _bankPosition = hit["position"].AsVector3() + Vector3.Up * VehicleDimensions.RideHeight;
                break;
            }
            Require(_bankPosition != Vector3.Zero, "Find actual authored bank triangles");
        }
    }

    private StaticBody3D AddBox(Vector3 size, Vector3 position, Color color)
    {
        var body = new StaticBody3D { Position = position };
        body.AddChild(new CollisionShape3D { Name = "Shape", Shape = new BoxShape3D { Size = size } });
        var mesh = VehicleBody.Box(size, Vector3.Zero, color);
        mesh.Name = "Mesh";
        body.AddChild(mesh);
        AddChild(body);
        return body;
    }

    private static void Send(InputEvent value)
    {
        using (value)
        {
            Godot.Input.ParseInputEvent(value);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
