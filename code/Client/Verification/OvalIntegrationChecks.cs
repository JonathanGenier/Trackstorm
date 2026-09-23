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
    private float _maximumSpeed;
    private float _maximumNormalSpeed;
    private int _supportedFrames;
    private int _drivingFrames;
    private string _output = string.Empty;
    private VehicleChaseCamera? _chase;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_chase is not null && _advance)
        {
            _chase.Follow(_vehicle.GetGlobalTransformInterpolated(), _vehicle.Snapshot, (float)delta, _vehicle.GetRid());
        }
    }

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
            Vector3 target = _centers[(nearest + 24) % _centers.Length] - position;
            Vector3 forward = -_vehicle.GlobalBasis.Z;
            float angle = new Vector3(forward.X, 0, forward.Z).SignedAngleTo(new Vector3(target.X, 0, target.Z), Vector3.Up);
            steering = (short)(Math.Clamp(-angle * 3, -1, 1) * short.MaxValue);
            throttle = _vehicle.LinearVelocity.Length() < 43 ? ushort.MaxValue : (ushort)0;
            _maximumSpeed = Math.Max(_maximumSpeed, _vehicle.LinearVelocity.Length());
            _drivingFrames++;
            _supportedFrames += _vehicle.State.Grounded ? 1 : 0;
            if (_vehicle.State.Grounded)
            {
                var support = WheelSuspension.Observe(_vehicle, _vehicle.GlobalTransform, _vehicle.Configuration);
                _maximumNormalSpeed = Math.Max(_maximumNormalSpeed, Math.Abs(_vehicle.LinearVelocity.Dot(support.Normal)));
            }

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
            await VerifyHandling();
            await VerifyContent();
            _advance = false;
            _vehicle.QueueFree();
            await VerifyPractice();
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Task.Delay(100);
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
        var terrain = _map.GetNode("InfieldTerrain").FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Single(mesh => mesh.Name.ToString().StartsWith("InfieldTerrain", StringComparison.Ordinal));
        Vector3[] terrainFaces = terrain.Mesh.GetFaces();
        var terrainVertices = terrainFaces.Where(point => Math.Abs(point.Y) < 0.0001f).Distinct().ToArray();
        Check(_inner.All(point => terrainVertices.Any(vertex => vertex.DistanceTo(point) < 0.0001f)), "Every original oval inner-edge vertex is retained in the terrain mesh within 0.1 mm float conversion tolerance.");
        var terrainShape = terrain.GetChildren().OfType<StaticBody3D>().Single().GetChildren().OfType<CollisionShape3D>().Single().Shape as ConcavePolygonShape3D;
        Check(terrainShape is not null, "Blender terrain has static concave collision.");
        Vector3[] collisionFaces = terrainShape!.GetFaces();
        // Godot reorders imported visual triangles and rounds their vertices.
        // Compare the complete vertex sets spatially, not by importer index order.
        static (int X, int Y, int Z) Cell(Vector3 point) => ((int)MathF.Floor(point.X * 1000), (int)MathF.Floor(point.Y * 1000), (int)MathF.Floor(point.Z * 1000));
        var visualCells = terrainFaces.Distinct().GroupBy(Cell).ToDictionary(group => group.Key, group => group.ToArray());
        float terrainError = 0;
        foreach (Vector3 point in collisionFaces.Distinct())
        {
            var cell = Cell(point);
            float nearest = float.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (visualCells.TryGetValue((cell.X + dx, cell.Y + dy, cell.Z + dz), out Vector3[]? candidates))
                        {
                            nearest = Math.Min(nearest, candidates.Min(candidate => candidate.DistanceTo(point)));
                        }
                    }
                }
            }

            terrainError = Math.Max(terrainError, nearest);
        }

        Check(collisionFaces.Length == terrainFaces.Length && terrainError < 0.001f, $"Blender terrain visual/collision correspondence: {terrainFaces.Length / 3} triangles, maximum nearest-vertex import rounding error {terrainError:F7} m (limit 1 mm).");
    }

    private void VerifyCollision()
    {
        Node trackBody = _map.GetNode("Collision/Track");
        Node infieldBody = _map.GetNode("InfieldTerrain").FindChildren("*", "StaticBody3D", true, false).First(body => body.GetParent().Name.ToString().StartsWith("InfieldTerrain", StringComparison.Ordinal));
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
            var rimRoad = Hit(_inner[index].Lerp(_outer[index], 0.01f));
            Check(infield.Normal.AngleTo(rimRoad.Normal) < 0.12f, $"Rim {index}: bank/terrain normal change {Mathf.RadToDeg(infield.Normal.AngleTo(rimRoad.Normal)):F3} degrees.");
            // Native broad-phase/triangle queries have submillimetre rounding at this map scale.
            float insetDistance = HorizontalDistance(inset, _inner[index]);
            if (infield.Body != infieldBody || Math.Abs(infield.Position.Y) > insetDistance * 0.72f + 0.003f || infield.Normal.Y < 0.8f)
            {
                throw new InvalidOperationException($"Terrain rim does not meet road at section {index}: {infield.Position}, {infield.Body}, expected {infieldBody}.");
            }
        }

        Check(worstHeightError < 0.025f, $"{samples} road raycasts including both sides of every seam: maximum height error {worstHeightError:F6} m.");
        Check(worstNormalStep < 0.03f, $"Continuous banking: maximum adjacent collision-normal step {Mathf.RadToDeg(worstNormalStep):F4} degrees.");
        // Sample below the authored tunnel roof; the foundation floor is still y=0.
        using var centerRay = PhysicsRayQueryParameters3D.Create(Vector3.Up * 2, Vector3.Down);
        var centerHit = GetWorld3D().DirectSpaceState.IntersectRay(centerRay);
        Check(centerHit.Count > 0 && Math.Abs(centerHit["position"].AsVector3().Y) < 0.003f, "Tunnel floor remains at zero; all 916 rim samples have continuous support within the bank grade bound.");
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
            AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 1.4f, ShadowEnabled = true });
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
        // Let the physics server publish the newly created body before testing resets.
        await Frames(3);
        foreach (Marker3D marker in _map.GetNode("PlayerSpawns").GetChildren().OfType<Marker3D>())
        {
            Quaternion orientation = marker.Quaternion;
            _vehicle.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(marker.Position + (Vector3.Up * VehicleDimensions.SpawnLift)), new Numerics.Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
            await Frames(90);
            Check(_vehicle.State.Grounded && HorizontalDistance(_vehicle.GlobalPosition, marker.Position) < 0.2f && _vehicle.DamageState.CurrentHP == _vehicle.DamageState.MaxHP, $"Production vehicle settles without damage at {marker.Name}: position {_vehicle.GlobalPosition}, grounded {_vehicle.State.Grounded}, HP {_vehicle.DamageState.CurrentHP}.");
        }

        _vehicle.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(_centers[_start] + (Vector3.Up * 0.85f)), new Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        await Frames(90);
        Check(_vehicle.State.Grounded, "Production native VehicleBody settles on the imported road.");
        VerifyVehicleScale();
        if (DisplayServer.GetName() != "headless")
        {
            var side = new Camera3D { Current = true, Fov = 50, Position = _vehicle.Position + new Vector3(0, 2, 8) };
            AddChild(side);
            side.LookAt(_vehicle.Position);
            await CaptureVehicleView("vehicle-side");
            side.QueueFree();
            _chase = new VehicleChaseCamera { Current = true, Fov = 65 };
            AddChild(_chase);
            await CaptureVehicleView("vehicle-chase");
        }

        _drive = true;
        for (int frame = 0; frame < 18000 && _progress - _start < _centers.Length * 3; frame++)
        {
            await Frames(1);
            if (frame == 900 && _chase is not null)
            {
                await CaptureVehicleView("vehicle-banked");
            }

            if (_maximumLaneError > 6 || _airborneFrames > 30 || _vehicle.GlobalPosition.Y < -1)
            {
                throw new InvalidOperationException($"Native lap failed at progress {_progress - _start}: lane error {_maximumLaneError:F3}, airborne {_airborneFrames}, position {_vehicle.GlobalPosition}, velocity {_vehicle.LinearVelocity}, angular {_vehicle.AngularVelocity}, wheel {_vehicle.State.SteeringAngle}, slip {_vehicle.State.FrontSlip}/{_vehicle.State.RearSlip}, normal-speed {_maximumNormalSpeed}.");
            }
        }

        _drive = false;
        Check(_maximumSpeed > 40 && _supportedFrames > _drivingFrames * 0.99f && _maximumNormalSpeed < 2, $"Sustained high-speed support: peak {_maximumSpeed:F3} m/s, grounded {_supportedFrames}/{_drivingFrames}, peak normal speed {_maximumNormalSpeed:F3} m/s.");
        Check(_progress - _start >= _centers.Length * 3, $"Production VehicleBody completed three banked loops, maximum centerline deviation {_maximumLaneError:F3} m, without sustained support loss.");
    }

    private async Task CaptureVehicleView(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(System.IO.Path.Combine(_output, name + ".png")) == Error.Ok, $"Rendered {name} in Godot.");
    }

    private void VerifyVehicleScale()
    {
        Node3D model = _vehicle.GetNode<Node3D>("WastelandVehicle");
        MeshInstance3D[] meshes = model.GetChildren().OfType<MeshInstance3D>().ToArray();
        Aabb bounds = meshes.Select(mesh => mesh.Transform * mesh.GetAabb()).Aggregate((left, right) => left.Merge(right));
        Check(Math.Abs(bounds.Size.Z - 4.81f) < 0.001f && Math.Abs(bounds.Size.X - 2.662311f) < 0.001f && Math.Abs(bounds.Size.Y - 1.856070f) < 0.001f, $"Production silhouette measures {bounds.Size} metres.");
        Check(model.Scale.IsEqualApprox(Vector3.One) && meshes.All(mesh => mesh.Scale.IsEqualApprox(Vector3.One)), "Blender geometry has applied scale; runtime nodes remain unit scale.");
        CollisionShape3D collision = _vehicle.GetChildren().OfType<CollisionShape3D>().Single();
        Vector3[] hull = ((ConvexPolygonShape3D)collision.Shape).Points;
        Aabb collisionBounds = new(hull[0], Vector3.Zero);
        foreach (Vector3 point in hull)
        {
            collisionBounds = collisionBounds.Expand(point);
        }

        Vector3 size = collisionBounds.Size;
        Check(Math.Abs(size.X - bounds.Size.X) < 0.001f && Math.Abs(size.Z - bounds.Size.Z) < 0.001f && Math.Abs(collisionBounds.GetCenter().Z - bounds.GetCenter().Z) < 0.001f, "Offline collision agrees with the complete armor/bumper footprint and origin.");
        CollisionShape3D online = VehicleVisual.CreateCollision();
        Check(((ConvexPolygonShape3D)online.Shape).Points.SequenceEqual(hull) && online.Position.IsEqualApprox(collision.Position), "Network and offline beveled collision definitions agree.");
        online.Free();
        foreach (MeshInstance3D wheel in meshes.Where(mesh => mesh.Name.ToString().StartsWith("wheel-", StringComparison.Ordinal)))
        {
            Aabb tire = wheel.Transform * wheel.GetAabb();
            Vector3 center = tire.GetCenter();
            Check(Math.Abs(Math.Abs(center.X) - (VehicleDimensions.WheelTrack / 2)) < 0.001f && Math.Abs(Math.Abs(center.Z) - (_vehicle.Configuration.Wheelbase / 2)) < 0.001f, $"{wheel.Name} aligns with its suspension ray.");
            Check(Math.Abs((tire.Size.Y / 2) - VehicleDimensions.WheelRadius) < 0.001f && Math.Abs(_vehicle.Position.Y + tire.Position.Y) < 0.025f, $"{wheel.Name} radius and settled level-road contact agree.");
        }

        Aabb body = model.GetNode<MeshInstance3D>("body").GetAabb();
        Check(Math.Abs(collisionBounds.Position.Y - body.Position.Y) < 0.001f && Math.Abs(collisionBounds.End.Y - bounds.End.Y) < 0.001f, "Collision spans the visible underbody through the roof identification panel.");
        float clearance = _vehicle.Position.Y + body.Position.Y;
        Check(clearance is > 0.2f and < 0.3f, $"Settled body clearance is {clearance:F3} m.");
    }
}
