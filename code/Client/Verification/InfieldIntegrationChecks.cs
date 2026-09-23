using System.Text.Json;
using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises Blender terrain with the native production vehicle and unchanged route topology.</summary>
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
    private int _airStreak;
    private int _longestFlight;
    private float _targetSpeed = 10;
    private float _peakHeight;
    private float _peakClearance;
    private float _launchSpeed;
    private int _drivenCases;
    private Vector3? _launch;
    private Vector3? _landing;
    private string _caseFilter = string.Empty;
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
            float targetSpeed = Math.Abs(angle) > 0.35f ? 6 : _targetSpeed;
            throttle = _vehicle.LinearVelocity.Length() < targetSpeed ? (ushort)40000 : (ushort)0;
            brake = _vehicle.LinearVelocity.Length() > targetSpeed + 1 ? (ushort)18000 : (ushort)0;
            _unsupported += _vehicle.State.Grounded ? 0 : 1;
            _airStreak = _vehicle.State.Grounded ? 0 : _airStreak + 1;
            _longestFlight = Math.Max(_longestFlight, _airStreak);
            _peakHeight = Math.Max(_peakHeight, _vehicle.Position.Y);
            using var clearanceRay = PhysicsRayQueryParameters3D.Create(_vehicle.Position, _vehicle.Position + Vector3.Down * 30, 1);
            clearanceRay.Exclude = new Godot.Collections.Array<Rid> { _vehicle.GetRid() };
            var ground = GetWorld3D().DirectSpaceState.IntersectRay(clearanceRay);
            if (ground.Count > 0) { _peakClearance = Math.Max(_peakClearance, _vehicle.Position.Y - ground["position"].AsVector3().Y); }
            if (_airStreak == 4 && _launch is null)
            {
                _launch = _vehicle.Position;
                _launchSpeed = (_vehicle.LinearVelocity with { Y = 0 }).Length();
            }

            if (_launch is not null && _vehicle.State.Grounded && _landing is null)
            {
                _landing = _vehicle.Position;
            }
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
            _caseFilter = OS.GetCmdlineUserArgs().FirstOrDefault(value => value.StartsWith("--infield-case=", StringComparison.Ordinal))?[15..] ?? string.Empty;
            System.IO.Directory.CreateDirectory(_output);
            var map = GD.Load<PackedScene>(Arenas.ActiveMap.ScenePath).Instantiate<Node3D>();
            AddChild(map);
            using var layout = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/maps/infield/layout.json"));
            JsonElement[] routes = layout.RootElement.GetProperty("routes").EnumerateArray().ToArray();
            await Frames(3);
            Check(map.GetNode<Node3D>("InfieldTerrain").Transform.IsEqualApprox(Transform3D.Identity), "Blender infield imports at identity metre scale.");
            Check(!map.HasNode("Collision/Infield") && !map.GetNode<MeshInstance3D>("Geometry/Infield").Visible, "Original flat floor has no active visual or collision; negative basins are usable.");
            using var terrainData = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/maps/infield/terrain.json"));
            string topologyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Godot.FileAccess.GetFileAsBytes("res://assets/maps/infield/layout.json"))).ToLowerInvariant();
            Check(topologyHash == terrainData.RootElement.GetProperty("topology_sha256").GetString(), "Terrain was authored against the retained TS-74 topology manifest.");
            foreach (Vector3 basin in new[] { new Vector3(-57, 0, -29), new Vector3(77, 0, -35), new Vector3(-85, 0, 28), new Vector3(85, 0, 28) })
            {
                using var basinRay = PhysicsRayQueryParameters3D.Create(basin + Vector3.Up * 4, basin + Vector3.Down * 5);
                var basinHit = GetWorld3D().DirectSpaceState.IntersectRay(basinRay);
                Check(basinHit.Count > 0 && basinHit["position"].AsVector3().Y < -1, $"Preserved basin {basin}: negative terrain support {basinHit}.");
            }
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
                        using var ray = PhysicsRayQueryParameters3D.Create(p + (Vector3.Up * 15), p + (Vector3.Down * 5));
                        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                        Check(hit.Count > 0 && hit["normal"].AsVector3().Y > 0.80f, $"Route support sample {samples++}: {route.GetProperty("id").GetString()} at {p}, hit {hit}.");
                    }
                }
            }

            Check(ClearRay(new Vector3(-8, 2, -15), new Vector3(-8, 2, 15)) && ClearRay(new Vector3(8, 2, -15), new Vector3(8, 2, 15)), "Tunnel has 18 m clear north/south opening.");
            Check(ClearRay(new Vector3(-15, 2, 0), new Vector3(15, 2, 0)), "Central east/west intersection remains open below the structural deck.");
            Check(!ClearRay(new Vector3(0, 5, 0), new Vector3(0, 6, 0)), "Imported tunnel roof has usable collision at 5.5 m clearance.");
            CheckStructures(map);
            _vehicle = new VehicleBody { Position = new Vector3(0, 1, 70), DamageConfiguration = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 } };
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
                await View("tunnel-clearance", new Vector3(0, 2.5f, 23), new Vector3(0, 2.5f, -15));
                await View("structure-join", new Vector3(21, 6, 22), new Vector3(11, 1, 11));
                await View("west-layout", new Vector3(-100, 95, 65), new Vector3(-85, 0, 0));
            }

            // Repeated ground-level crossing in both directions on both axes.
            // These scenarios precede the wider terrain suite so its known
            // slope-start limitation cannot hide the structural evidence.
            foreach (int pass in Enumerable.Range(0, 3))
            {
                foreach (int direction in new[] { -1, 1 })
                {
                    await Drive($"StructureNorthSouth{pass}_{direction}", Enumerable.Range(0, 25).Select(i => new Vector3(0, 0, direction * (-24 + i * 2))).ToArray(), 16, 14);
                    await Drive($"StructureEastWest{pass}_{direction}", Enumerable.Range(0, 25).Select(i => new Vector3(direction * (-24 + i * 2), 0, 0)).ToArray(), 16, 14);
                }
            }

            await StructureImpacts();

            foreach (JsonElement route in routes)
            {
                string name = route.GetProperty("id").GetString()!;
                await Drive(name, ReadPoints(route), route.GetProperty("width_m").GetSingle());
            }

            foreach (JsonElement jump in layout.RootElement.GetProperty("jumps").EnumerateArray())
            {
                float x = jump.GetProperty("start")[0].GetSingle();
                float direction = jump.GetProperty("direction")[0].GetSingle();
                foreach (float speed in new[] { 14f, 16f, 18f })
                {
                    await Drive(jump.GetProperty("id").GetString()! + speed, Enumerable.Range(0, 51).Select(i => new Vector3(x + (direction * i * 2), 0, 0)).ToArray(), 12, speed, true);
                }
            }

            foreach (Vector3 basin in new[] { new Vector3(-57, 0, -29), new Vector3(77, 0, -35), new Vector3(-85, 0, 28), new Vector3(85, 0, 28) })
            {
                await Drive($"BasinRecovery{basin.X}-{basin.Z}", Enumerable.Range(0, 21).Select(i => basin + Vector3.Right * (-20 + i * 2)).ToArray(), 12, 8);
            }

            foreach (int direction in new[] { -1, 1 })
            {
                await Drive($"TerrainToBank{direction}", Enumerable.Range(0, 21).Select(i => new Vector3(direction * (160 + i * 2), 0, 0)).ToArray(), 12, 8);
            }

            Vector3[] west = ReadPoints(routes[0]);
            Vector3[] east = ReadPoints(routes[1]);
            Vector3[] junction = Enumerable.Range(1, 18).Select(i => new Vector3(-18 + (i * 2), 0, 0)).ToArray();
            await Drive("ConnectedLoopTour", west.Concat(junction).Concat(east.Skip(1)).ToArray(), 12);

            Quaternion companionRotation = new(Vector3.Up, Mathf.Pi);
            _companion.ResetBody(new VehiclePhysicsState(new Numerics.Vector3(3.5f, 1, -62), new Numerics.Quaternion(companionRotation.X, companionRotation.Y, companionRotation.Z, companionRotation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
            _companionDrive = true;
            await Drive("TwoCarTunnel", Enumerable.Range(0, 61).Select(i => new Vector3(-3.5f, 0, -60 + (i * 2))).ToArray(), 9);
            if (_caseFilter.Length == 0 || "TwoCarTunnel".StartsWith(_caseFilter, StringComparison.Ordinal))
            {
                Check(_companion.Position.Z > 45 && _companion.State.Grounded && _companion.DamageState.CurrentHP == _companion.DamageState.MaxHP, "Second production car traverses tunnel alongside first without damage.");
            }

            Check(_drivenCases > 0, "Case filter exercised at least one driving scenario.");
            _advance = false;
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence.Where(line => !line.StartsWith("Route support sample", StringComparison.Ordinal)));
            GD.Print($"Infield integration passed: {samples} support probes; case filter '{_caseFilter}' (empty = complete suite). Evidence: {_output}");
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

    private void CheckStructures(Node3D map)
    {
        Node3D structures = map.GetNode<Node3D>("InfieldStructures");
        Check(structures.Transform.IsEqualApprox(Transform3D.Identity), "Production Blender structures retain metre-scale identity transform.");
        Check(map.GetNode("InfieldTerrain").FindChildren("Tunnel*", "MeshInstance3D", true, false).Count == 0, "Inherited graybox tunnel meshes and child colliders are removed at import.");
        var bodies = structures.FindChildren("*", "StaticBody3D", true, false);
        Check(bodies.Count >= 50, $"Production structures import {bodies.Count} static colliders.");
        foreach (Node child in bodies)
        {
            var body = (StaticBody3D)child;
            Check(body.CollisionLayer == 1 && !body.IsInGroup("landing_terrain"), $"{body.GetParent().Name}: obstacle collision retains layer 1 and crash classification.");
        }

        int clearances = 0;
        foreach (float height in new[] { .3f, 2f, 5.45f })
        {
            for (int offset = -8; offset <= 8; offset++)
            {
                Check(ClearRay(new Vector3(offset, height, -24), new Vector3(offset, height, 24)), $"North/south clear at x={offset}, y={height}.");
                Check(ClearRay(new Vector3(-24, height, offset), new Vector3(24, height, offset)), $"East/west clear at z={offset}, y={height}.");
                clearances += 2;
            }
        }

        foreach (int x in new[] { -10, 10 })
        {
            foreach (int z in new[] { -10, 10 })
            {
                Check(!ClearRay(new Vector3(x, 1, z-3), new Vector3(x, 1, z+3)), $"Pier ({x},{z}) has front/back collision.");
                // Test surrounding toe support outside the solid, not an inside-origin ray.
                using var joinRay = PhysicsRayQueryParameters3D.Create(new Vector3(x * 1.15f, 1, z), new Vector3(x * 1.15f, -1, z));
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(joinRay);
                Check(hit.Count > 0 && Math.Abs(hit["position"].AsVector3().Y) < .02f, $"Pier ({x},{z}) meets retained flat terrain without a gap.");
                Check(!ClearRay(new Vector3(x * 1.4f, .4f, z), new Vector3(x * 1.4f, .4f, z * 1.6f)), $"Retaining wing at ({x},{z}) has solid collision.");
                Check(!ClearRay(new Vector3(x * 1.21f, .2f, z), new Vector3(x * 1.21f, -.2f, z)), $"Drain invert at ({x},{z}) has solid collision.");
            }

            Check(!ClearRay(new Vector3(0, 6.8f, 0), new Vector3(x * 1.2f, 6.8f, 0)), $"Deck parapet on x={x} has solid collision.");
            Check(!ClearRay(new Vector3(0, 6.8f, 0), new Vector3(0, 6.8f, x * 1.2f)), $"Deck parapet on z={x} has solid collision.");
        }

        Check(clearances == 102, "102 crossing-envelope rays verify ground-to-soffit clearance on both axes.");
    }

    private async Task StructureImpacts()
    {
        if (_caseFilter.Length != 0 && !"StructureImpacts".StartsWith(_caseFilter, StringComparison.Ordinal))
        {
            return;
        }

        _drivenCases++;
        _drive = false;
        foreach (int side in new[] { -1, 1 })
        {
            foreach (int pass in Enumerable.Range(0, 2))
            {
                Quaternion rotation = new(Vector3.Up, Mathf.Pi);
                _vehicle.ResetBody(new VehiclePhysicsState(new Numerics.Vector3(side * 10, .9f, -22), new Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), new Numerics.Vector3(0, 0, 15), Numerics.Vector3.Zero));
                // Reset is queued through Core; observe only after native state applies.
                await Frames(3);
                Check(Math.Abs(_vehicle.Position.X - side * 10) < .1f && _vehicle.Position.Z < -20, "Pier impact begins at the requested native setup pose.");
                float furthest = -22;
                for (int frame = 0; frame < 100; frame++)
                {
                    await Frames(1);
                    furthest = Math.Max(furthest, _vehicle.Position.Z);
                }

                Check(furthest < -11 && _vehicle.DamageState.CurrentHP < _vehicle.DamageState.MaxHP && _vehicle.Position.IsFinite(), $"Structure impact {side}/{pass}: native 15 m/s pier approach stopped outside solid, furthest z={furthest:F2}, HP={_vehicle.DamageState.CurrentHP}; stable repeated reset/impact.");
            }
        }
    }

    private bool ClearRay(Vector3 from, Vector3 to)
    {
        using var ray = PhysicsRayQueryParameters3D.Create(from, to);
        return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
    }

    private async Task Drive(string name, Vector3[] points, float width, float speed = 10, bool jump = false)
    {
        if (_caseFilter.Length != 0 && !name.StartsWith(_caseFilter, StringComparison.Ordinal))
        {
            return;
        }

        _drive = false;
        _drivenCases++;
        _path = points;
        _progress = 0;
        _deviation = 0;
        _unsupported = 0;
        _airStreak = 0;
        _longestFlight = 0;
        _targetSpeed = speed;
        _peakHeight = 0;
        _peakClearance = 0;
        _launchSpeed = 0;
        _launch = null;
        _landing = null;
        Quaternion rotation = Basis.LookingAt(points[1] - points[0]).GetRotationQuaternion();
        using var spawnRay = PhysicsRayQueryParameters3D.Create(points[0] + Vector3.Up * 4, points[0] + Vector3.Down * 5);
        var spawnHit = GetWorld3D().DirectSpaceState.IntersectRay(spawnRay);
        Check(spawnHit.Count > 0, name + ": supported initial pose.");
        _vehicle.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(spawnHit["position"].AsVector3() + Vector3.Up), new Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        await Frames(90);
        _drive = true;
        bool airCaptured = false;
        bool landingCaptured = false;
        for (int frame = 0; frame < 9000 && _progress < points.Length - 2; frame++)
        {
            await Frames(1);
            if (frame == 300 && _progress == 0)
            {
                throw new InvalidOperationException($"{name}: stalled at {_vehicle.Position}, forward {-_vehicle.GlobalBasis.Z}, up {_vehicle.GlobalBasis.Y}, wheel compression {_vehicle.State.Wheels.Compression}, steering {_vehicle.State.SteeringAngle}, speed {_vehicle.LinearVelocity}.");
            }
            if (_deviation > (width / 2) - 1.4f || _airStreak > (jump ? 180 : 120) || _vehicle.GlobalBasis.Y.Y < 0.5f)
            {
                throw new InvalidOperationException($"{name} failed: progress {_progress}/{points.Length}, lateral {_deviation:F2}, unsupported {_unsupported}, position {_vehicle.Position}.");
            }

            if (frame == 150 && _camera is not null)
            {
                await View(name + "-drive", _vehicle.Position + (_vehicle.GlobalBasis.Z * 12) + (Vector3.Up * 6), _vehicle.Position - (_vehicle.GlobalBasis.Z * 8));
            }

            if (jump && _camera is not null && !airCaptured && _airStreak >= 12 && _vehicle.LinearVelocity.Y <= 0)
            {
                airCaptured = true;
                await View(name + "-air", _vehicle.Position + new Vector3(0, 5, 18), _vehicle.Position);
            }

            if (jump && _camera is not null && !landingCaptured && _landing is not null)
            {
                landingCaptured = true;
                await View(name + "-landing", _vehicle.Position + new Vector3(0, 5, 18), _vehicle.Position);
            }
        }

        _drive = false;
        if (jump)
        {
            Check(_longestFlight >= 4 && _landing is not null && _vehicle.State.Grounded, $"{name}: real launch {_launch}, landing {_landing}, longest flight {_longestFlight / 60f:F2}s, peak origin {_peakHeight:F2}m, peak origin clearance {_peakClearance:F2}m, launch horizontal speed {_launchSpeed:F2}m/s, airborne horizontal travel {((_landing!.Value - _launch!.Value) with { Y = 0 }).Length():F2}m; recovered grounded.");
            float landingDistance = Math.Abs(_landing!.Value.X - points[0].X);
            Check(landingDistance >= 55 && landingDistance <= 85, $"{name}: lands on descending dirt zone at corridor metre {landingDistance:F2}.");
        }
        Check(_progress >= points.Length - 2, $"{name}: entire route driven using production physics/input; progress {_progress}/{points.Length}, position {_vehicle.Position}, velocity {_vehicle.LinearVelocity}; peak centerline error {_deviation:F2} m, unsupported frames {_unsupported}, final HP {_vehicle.DamageState.CurrentHP}.");
        Check(_vehicle.DamageState.CurrentHP == _vehicle.DamageState.MaxHP, $"{name}: no collision damage ({_vehicle.DamageState.CurrentHP}/{_vehicle.DamageState.MaxHP}); launch {_launch}, landing {_landing}, flight {_longestFlight} frames.");
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
        if (!message.StartsWith("Route support sample", StringComparison.Ordinal))
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence.Where(line => !line.StartsWith("Route support sample", StringComparison.Ordinal)));
        }
    }
}
