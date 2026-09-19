using System.Text.Json;
using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Imported-map geometry, collision, scale and native driving acceptance fixture.</summary>
public sealed partial class OvalIntegrationChecks : Node3D
{
    private readonly Core.Simulation.Simulation _simulation = new(new Core.Simulation.SimulationConfiguration(60));
    private readonly List<string> _evidence = new();
    private Node3D _map = null!;
    private VehicleBody _vehicle = null!;
    private VehicleArena? _practice;
    private Vector3[] _inner = Array.Empty<Vector3>();
    private Vector3[] _outer = Array.Empty<Vector3>();
    private Vector3[] _centers = Array.Empty<Vector3>();
    private bool _advance;
    private bool _drive;
    private int _progress;
    private int _start;
    private ulong _tick;
    private float _maximumLaneError;
    private int _airborneFrames;
    private string _output = string.Empty;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_practice is { } practice)
        {
            practice.Advance(new InputFrame(practice.Simulation.State.Tick + 1, 0, 0, 0, 0, 0, 0));
        }

        if (!_advance)
        {
            return;
        }

        short steering = 0;
        ushort throttle = 0;
        if (_drive)
        {
            Vector3 position = _vehicle.GlobalPosition;
            int nearest = _progress;
            for (int candidate = _progress; candidate <= _progress + 12; candidate++)
            {
                if (HorizontalDistance(position, _centers[candidate % _centers.Length]) < HorizontalDistance(position, _centers[nearest % _centers.Length]))
                {
                    nearest = candidate;
                }
            }

            _progress = nearest;
            _maximumLaneError = Math.Max(_maximumLaneError, HorizontalDistance(position, _centers[nearest % _centers.Length]));
            Vector3 target = _centers[(nearest + 12) % _centers.Length] - position;
            Vector3 forward = -_vehicle.GlobalBasis.Z;
            float angle = new Vector3(forward.X, 0, forward.Z).SignedAngleTo(new Vector3(target.X, 0, target.Z), Vector3.Up);
            steering = (short)(Math.Clamp(-angle * 3, -1, 1) * short.MaxValue);
            throttle = _vehicle.LinearVelocity.Length() < 19 ? ushort.MaxValue : (ushort)0;
            _airborneFrames = _vehicle.State.Grounded ? 0 : _airborneFrames + 1;
        }

        var input = new InputFrame(++_tick, steering, throttle, 0, InputButtons.None, InputButtons.None, InputButtons.None);
        IReadOnlyList<VehicleStepResult> results = _simulation.Step(input, new[] { _vehicle.Capture(input) });
        _vehicle.Apply(results[0]);
        _vehicle.Publish();
    }

    /// <summary>Loads the standalone map, checks its imported surfaces and drives one full native lap.</summary>
    public async void Run()
    {
        try
        {
            _output = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--oval-output=", StringComparison.Ordinal))?[14..] ?? ProjectSettings.GlobalizePath("res://.godot/oval-checks");
            System.IO.Directory.CreateDirectory(_output);
            _map = GD.Load<PackedScene>("res://scenes/maps/oval_foundation.tscn").Instantiate<Node3D>();
            AddChild(_map);
            using JsonDocument data = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/maps/oval/measurements.json"));
            JsonElement[] sections = data.RootElement.GetProperty("sections_godot").EnumerateArray().ToArray();
            _inner = sections.Select(section => ReadVector(section[0])).ToArray();
            _outer = sections.Select(section => ReadVector(section[1])).ToArray();
            _centers = _inner.Zip(_outer, (inner, outer) => (inner + outer) / 2).ToArray();
            await Frames(3);
            VerifyGeometry();
            VerifyCollision();
            VerifyGrid();
            await CaptureViews();
            await VerifyDriving();
            _advance = false;
            _vehicle.QueueFree();
            await VerifyPractice();
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"Oval integration passed: {_evidence.Count} checks. Artifacts: {_output}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _advance = false;
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static Vector3 ReadVector(JsonElement array) => new(array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle());

    private static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    private async Task Frames(int count)
    {
        for (int frame = 0; frame < count; frame++)
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

    private (Vector3 Position, Vector3 Normal, GodotObject Body) Hit(Vector3 expected)
    {
        using var query = PhysicsRayQueryParameters3D.Create(expected + (Vector3.Up * 15), expected - (Vector3.Up * 15));
        Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            throw new InvalidOperationException($"Missing collision at {expected}.");
        }

        return (result["position"].AsVector3(), result["normal"].AsVector3(), result["collider"].AsGodotObject());
    }

    private void VerifyGeometry()
    {
        Check(_map.Transform.IsEqualApprox(Transform3D.Identity), "Map root preserves metre scale and identity transform.");
        Node3D geometry = _map.GetNode<Node3D>("Geometry");
        Check(geometry.SceneFilePath == "res://assets/maps/oval/oval_foundation.glb", "Map instances the Blender GLB.");
        MeshInstance3D track = geometry.GetNode<MeshInstance3D>("Track");
        Check(track.GlobalTransform.IsEqualApprox(Transform3D.Identity), "Imported track preserves axis conversion without runtime scale.");
        Vector3 extent = track.GetAabb().Size;
        Check(Math.Abs(extent.X - 410.7416f) < 0.01f && Math.Abs(extent.Z - 200) < 0.001f && Math.Abs(extent.Y - 10.324376f) < 0.001f, $"Imported extent is {extent} m.");
        Vector3[] points = track.Mesh.GetFaces();
        Check(_inner.Concat(_outer).All(point => points.Any(imported => imported.DistanceTo(point) < 0.0001f)), "Every source road vertex survives Blender to Godot export.");
        float lap = 0;
        for (int index = 0; index < _centers.Length; index++)
        {
            lap += HorizontalDistance(_centers[index], _centers[(index + 1) % _centers.Length]);
        }

        Check(Math.Abs(lap - 999.77f) < 0.02f, $"Measured plan centerline lap: {lap:F4} m.");
        Check(_inner.Zip(_outer).All(pair => Math.Abs(pair.First.DistanceTo(pair.Second) - 18) < 0.0001f), "All 916 transverse road sections measure 18 m on the banked surface.");
        Check(_centers.Where(point => Math.Abs(point.X) > 107.001f).All(point => Math.Abs(new Vector2(Math.Abs(point.X) - 107, point.Z).Length() - 91) < 0.0001f), "Both imported turns have 91 m plan radius; tangent centers are 214 m apart.");
        var trackShape = _map.GetNode<CollisionShape3D>("Collision/Track/Shape").Shape as ConcavePolygonShape3D;
        Check(trackShape is not null && trackShape.GetFaces().SequenceEqual(points), $"One static road collision surface exactly matches the {points.Length / 3} exported triangles.");
    }

    private void VerifyCollision()
    {
        Node trackBody = _map.GetNode("Collision/Track");
        Node infieldBody = _map.GetNode("Collision/Infield");
        float worstHeightError = 0;
        float worstNormalStep = 0;
        int samples = 0;
        for (int index = 0; index < _inner.Length; index++)
        {
            int next = (index + 1) % _inner.Length;
            foreach (float along in new[] { 0.001f, 0.25f, 0.5f, 0.75f, 0.999f })
            {
                Vector3 inner = _inner[index].Lerp(_inner[next], along);
                Vector3 outer = _outer[index].Lerp(_outer[next], along);
                foreach (float across in new[] { 0.01f, 0.25f, 0.5f, 0.75f, 0.99f })
                {
                    Vector3 expected = inner.Lerp(outer, across);
                    var hit = Hit(expected);
                    if (hit.Body != trackBody || hit.Normal.Y < 0.8f)
                    {
                        throw new InvalidOperationException($"Wrong road collision or inverted normal at section {index}.");
                    }

                    worstHeightError = Math.Max(worstHeightError, Math.Abs(hit.Position.Y - expected.Y));
                    samples++;
                }
            }

            var before = Hit(_centers[(index + _inner.Length - 1) % _inner.Length].Lerp(_centers[index], 0.999f));
            var after = Hit(_centers[index].Lerp(_centers[next], 0.001f));
            worstNormalStep = Math.Max(worstNormalStep, before.Normal.AngleTo(after.Normal));
            Vector3 inset = _inner[index].Lerp(Vector3.Zero, 0.001f);
            var infield = Hit(inset);
            // Native broad-phase/triangle queries have submillimetre rounding at this map scale.
            if (infield.Body != infieldBody || Math.Abs(infield.Position.Y) > 0.003f)
            {
                throw new InvalidOperationException($"Flat infield does not meet road at section {index}: {infield.Position}, {infield.Body}, expected {infieldBody}.");
            }
        }

        Check(worstHeightError < 0.025f, $"{samples} road raycasts including both sides of every seam: maximum height error {worstHeightError:F6} m.");
        Check(worstNormalStep < 0.03f, $"Continuous banking: maximum adjacent collision-normal step {Mathf.RadToDeg(worstNormalStep):F4} degrees.");
        Check(Math.Abs(Hit(Vector3.Zero).Position.Y) < 0.003f, "Flat infield center and all 916 inner-rim samples meet the road at y=0 within 3 mm native-query tolerance.");
    }

    private void VerifyGrid()
    {
        Marker3D[] markers = _map.GetNode("PlayerSpawns").GetChildren().OfType<Marker3D>().ToArray();
        Check(markers.Length == 8 && markers.Select(marker => marker.Name.ToString()).SequenceEqual(Enumerable.Range(1, 8).Select(index => $"player-{index:00}")), "Eight ordered stable Marker3D player spawn IDs.");
        foreach (Marker3D marker in markers)
        {
            Check(marker.GetMeta("slot_length_m").AsSingle() == 6 && marker.GetMeta("slot_width_m").AsSingle() == 3, $"{marker.Name} declares a 6 by 3 m grid slot.");
            Check((-marker.GlobalBasis.Z).Dot(Vector3.Right) > 0.99f, $"{marker.Name} faces the +X racing direction.");
            foreach (float x in new[] { -3f, 0f, 3f })
            {
                foreach (float z in new[] { -1.5f, 0f, 1.5f })
                {
                    var support = Hit(marker.Position + new Vector3(x, 0, z));
                    Check(Math.Abs(support.Position.Y) < 0.001f && support.Normal.Y > 0.999f, $"{marker.Name} footprint ({x}, {z}) is fully supported on flat track.");
                }
            }
        }
    }

    private async Task CaptureViews()
    {
        var reference = GD.Load<PackedScene>("res://assets/maps/oval/reference_vehicle.glb").Instantiate<Node3D>();
        AddChild(reference);
        MeshInstance3D body = reference.GetNode<MeshInstance3D>("ReferenceCar_Body_4_81m");
        Check(Math.Abs(body.GetAabb().Size.X - 4.81f) < 0.001f, "Imported reference car body measures 4.81 m in Godot.");
        if (DisplayServer.GetName() != "headless")
        {
            AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("253241"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.25f } });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 0.65f, ShadowEnabled = true });
            var camera = new Camera3D { Current = true, Far = 1500 };
            AddChild(camera);
            foreach (var view in new[] { (Name: "overview", Position: new Vector3(260, 230, 270), Target: Vector3.Zero), (Name: "grid-scale", Position: new Vector3(18, 18, 111), Target: new Vector3(-9, 0, 92)), (Name: "banking", Position: new Vector3(227, 34, 100), Target: new Vector3(164, 3, 32)) })
            {
                camera.Fov = view.Name == "overview" ? 45 : 65;
                camera.Position = view.Position;
                camera.LookAt(view.Target);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using Image image = GetViewport().GetTexture().GetImage();
                Check(image.SavePng(System.IO.Path.Combine(_output, view.Name + ".png")) == Error.Ok, $"Rendered {view.Name} in Godot.");
            }
        }

        reference.QueueFree();
    }

    private async Task VerifyPractice()
    {
        var viewport = new SubViewport { OwnWorld3D = true, Size = new Vector2I(320, 180), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(viewport);
        var practice = new VehicleArena();
        viewport.AddChild(practice);
        _practice = practice;
        await Frames(120);
        OvalGameplayAssertions.VerifyMap(practice.Map, practice.Simulation.Arena);
        Check(practice.GetChildren().OfType<Audio.ArenaAudio>().Single().MusicPlaying, "Normal practice starts the existing gameplay music.");
        Check(practice.Vehicles.Count == 8, "Normal practice creates eight production vehicles on the active oval.");
        foreach (VehicleBody vehicle in practice.Vehicles)
        {
            Vector3 spawn = VehicleBody.ToGodot(practice.Simulation.Arena.Spawn((int)vehicle.VehicleId - 1).Position);
            Check(vehicle.State.Grounded && HorizontalDistance(vehicle.GlobalPosition, spawn) < 0.2f, $"Practice vehicle {vehicle.VehicleId} settles on its map grid marker.");
        }

        practice.Player.ResetBody(new VehiclePhysicsState(new Numerics.Vector3(10, 1, 10), Numerics.Quaternion.Identity, Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        await Frames(3);
        practice.ResetVehicles();
        await Frames(120);
        foreach (VehicleBody vehicle in practice.Vehicles)
        {
            Vector3 spawn = VehicleBody.ToGodot(practice.Simulation.Arena.Spawn((int)vehicle.VehicleId - 1).Position);
            Check(vehicle.State.Grounded && HorizontalDistance(vehicle.GlobalPosition, spawn) < 0.2f, $"Practice reset returns vehicle {vehicle.VehicleId} to its map grid marker.");
        }

        _practice = null;
        viewport.QueueFree();
    }

    private async Task VerifyDriving()
    {
        _start = Array.FindIndex(_centers, point => point.X > -1 && Math.Abs(point.Z - 91) < 0.01f);
        _progress = _start;
        _vehicle = new VehicleBody { Name = "NativeDrivingProbe", Position = _centers[_start] + (Vector3.Up * 0.85f), Rotation = new Vector3(0, -Mathf.Pi / 2, 0) };
        Quaternion rotation = _vehicle.Quaternion;
        _simulation.AddVehicle(1, _vehicle.Configuration, _vehicle.DamageConfiguration, new VehiclePhysicsState(VehicleBody.ToCore(_vehicle.Position), new Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        _vehicle.Initialize(_simulation);
        AddChild(_vehicle);
        _advance = true;
        foreach (Marker3D marker in _map.GetNode("PlayerSpawns").GetChildren().OfType<Marker3D>())
        {
            Quaternion orientation = marker.Quaternion;
            _vehicle.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(marker.Position), new Numerics.Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
            await Frames(90);
            Check(_vehicle.State.Grounded && HorizontalDistance(_vehicle.GlobalPosition, marker.Position) < 0.2f && _vehicle.DamageState.CurrentHP == _vehicle.DamageState.MaxHP, $"Production vehicle settles without damage at {marker.Name}.");
        }

        _vehicle.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(_centers[_start] + (Vector3.Up * 0.85f)), new Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        await Frames(90);
        Check(_vehicle.State.Grounded, "Production native VehicleBody settles on the imported road.");
        _drive = true;
        for (int frame = 0; frame < 7200 && _progress - _start < _centers.Length; frame++)
        {
            await Frames(1);
            if (_maximumLaneError > 6 || _airborneFrames > 30 || _vehicle.GlobalPosition.Y < -1)
            {
                throw new InvalidOperationException($"Native lap failed at progress {_progress - _start}: lane error {_maximumLaneError:F3}, airborne {_airborneFrames}, position {_vehicle.GlobalPosition}.");
            }
        }

        _drive = false;
        Check(_progress - _start >= _centers.Length, $"Production VehicleBody completed the full banked loop, maximum centerline deviation {_maximumLaneError:F3} m, without sustained support loss.");
    }
}
