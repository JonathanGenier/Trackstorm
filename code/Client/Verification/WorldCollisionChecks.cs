using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Actual-map static impacts, sustained contact and driver-controlled escape through both adapters.</summary>
public sealed partial class WorldCollisionChecks : Node3D
{
    private int _failures;
    private Camera3D? _camera;
    private string _directory = "";
    private readonly List<object> _summary = new();

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            _directory = ProjectSettings.GlobalizePath("res://.godot/world-collision-checks/" + Option("label", "checks"));
            System.IO.Directory.CreateDirectory(_directory);
            var map = Arenas.ActiveMap.Load(); AddChild(map);
            var importedTerrain = map.GetNode("InfieldTerrain").FindChildren("*", "CollisionShape3D", true, false).OfType<CollisionShape3D>().First(s => s.GetParent().IsInGroup("landing_terrain"));
            Check(importedTerrain.Shape is ConcavePolygonShape3D shape && shape.Data.Length / 3 <= 40500 && shape.ResourcePath == "res://assets/maps/infield/TerrainCollision.res", "audited terrain collision is imported; reimport the terrain GLB after changing its hook");
            if (OS.GetCmdlineUserArgs().Contains("original-terrain"))
            {
                var terrain = map.GetNode("InfieldTerrain").FindChildren("*", "CollisionShape3D", true, false).OfType<CollisionShape3D>().First(s => s.GetParent().IsInGroup("landing_terrain"));
                terrain.Shape = map.GetNode("InfieldTerrain").FindChildren("InfieldTerrain*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First().Mesh.CreateTrimeshShape();
            }
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
                AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightEnergy = 1.4f });
                _camera = new Camera3D { Current = true, Far = 1500 }; AddChild(_camera);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            foreach (bool network in new[] { true, false })
            foreach (var target in new[]
            {
                ("bank-face", new Vector3(0, 0, 0), -Mathf.Pi / 2),
                ("bank-neck", new Vector3(12, 0, 28), 0f),
                ("bank-side", new Vector3(30, 0, 32), 0f),
                ("pillar", new Vector3(0, 0, -10), -Mathf.Pi / 2),
                ("retaining-wing", new Vector3(10, 0, 22), 0f),
                ("perimeter", new Vector3(0, 0, 91), Mathf.Pi),
                ("rock", new Vector3(-43, 0, 78), 0f),
            })
            foreach (float speed in new[] { 3f, 12f, 35f })
            foreach (float angle in new[] { 0f, 0.45f, 1.57f })
            {
                string adapter = network ? "network" : "practice";
                if (Option("adapter", adapter) != adapter || Option("surface", target.Item1) != target.Item1 ||
                    Number("speed", speed) != speed || Number("angle", angle) != angle) { continue; }
                await Scenario(network, target.Item1, target.Item2, target.Item3, speed, angle);
            }
            if (OS.GetCmdlineUserArgs().Length == 0)
                foreach (bool network in new[] { true, false }) { await Scenario(network, "bank-face", Vector3.Zero, -Mathf.Pi / 2, 12, 0, 1); }
            System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "summary.json"), JsonSerializer.Serialize(_summary));
            GD.Print($"World collision checks: failures={_failures}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Scenario(bool network, string surface, Vector3 start, float approach, float speed, float angle, float steering = 0)
    {
        string name = $"{(network ? "network" : "practice")}-{surface}-{speed}-{angle:F2}";
        if (steering != 0) { name += "-steering"; }
        using var ray = PhysicsRayQueryParameters3D.Create(start + Vector3.Up * 40, start + Vector3.Down * 40, 1);
        using var ground = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        Vector3 normal = ground["normal"].AsVector3();
        start = ground["position"].AsVector3() + normal * VehicleDimensions.RideHeight;
        // Start beneath the tunnel rather than on its upper deck.
        if (surface is "bank-face" or "pillar") { start = new(start.X, VehicleDimensions.RideHeight, start.Z); normal = Vector3.Up; }
        Vector3 heading = new(-MathF.Sin(approach), 0, -MathF.Cos(approach));
        heading = heading.Slide(normal).Normalized();
        var orientation = Basis.LookingAt(heading, normal).Rotated(normal, angle).GetRotationQuaternion();
        var pose = new VehiclePhysicsState(VehicleBody.ToCore(start), new(orientation.X, orientation.Y, orientation.Z, orientation.W), VehicleBody.ToCore(heading * speed), N.Vector3.Zero);
        var world = new Core.Simulation.Simulation(new(60));
        var configuration = new VehicleConfiguration();
        var damage = new DamageConfiguration { MaxHP = 100000, CollisionScale = 5 };
        world.AddVehicle(1, configuration, damage, pose);
        VehicleBody? practice = null; NetworkVehicleBody? proxy = null;
        if (network) { proxy = new() { VehicleId = 1 }; AddChild(proxy); proxy.Apply(world.GetVehicle(1)); }
        else { practice = new() { Position = start, Quaternion = orientation, LinearVelocity = heading * speed, DamageConfiguration = damage }; practice.Initialize(world); AddChild(practice); }
        PhysicsBody3D body = (PhysicsBody3D?)practice ?? proxy!;
        if (angle > 0.4f && surface is "bank-face" or "pillar" or "perimeter" or "rock")
        {
            // At low speeds an angled car turns away or stops before the whole approach.
            // Seed it one tick before contact, using the real native chassis sweep.
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            using var approachQuery = new PhysicsTestMotionParameters3D { From = new(new Basis(orientation), start), Motion = heading * 20, Margin = 0.005f };
            using var approachHit = new PhysicsTestMotionResult3D();
            if (PhysicsServer3D.BodyTestMotion(body.GetRid(), approachQuery, approachHit))
            {
                start += approachHit.GetTravel() - heading * (speed / 120);
                pose = new(VehicleBody.ToCore(start), pose.Orientation, pose.LinearVelocity, pose.AngularVelocity);
                world.Restore(new(world.State.Tick, world.State.LastInput, [new VehicleSnapshot(1, 1, new VehicleState(world.State.Tick, pose, true, false, 0, 0), world.GetVehicle(1).Damage, pose)], world.State.Match));
                if (network) { proxy!.Apply(world.GetVehicle(1)); }
                else { practice!.GlobalTransform = new(new Basis(orientation), start); practice.LinearVelocity = heading * speed; }
            }
        }
        var trace = new List<object>();
        var costs = new List<double>();
        int contactFrames = 0, finalContacts = 0, deepFrames = 0;
        float deepest = 0, peakStep = 0, lateStep = 0, peakAngular = 0, peakUp = 0;
        N.Vector3? firstObstacle = null;
        float reverseEscape = 0;
        using var query = new PhysicsTestMotionParameters3D { Margin = 0.001f, MaxCollisions = 8, RecoveryAsCollision = true };
        using var hit = new PhysicsTestMotionResult3D();
        for (int frame = 0; frame < 660; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            bool drive = frame < 210 || frame is >= 300 and < 420;
            bool reverse = frame is >= 225 and < 300 || frame >= 435;
            // Side impacts retain their lateral initial momentum; subsequent escape uses ordinary pedals/steering.
            var input = new InputFrame(world.State.Tick + 1, (short)(Number("steer", steering) * short.MaxValue), drive ? ushort.MaxValue : (ushort)0, reverse ? ushort.MaxValue : (ushort)0, 0, 0, 0);
            var before = world.GetVehicle(1).Movement.Physics;
            var watch = Stopwatch.StartNew();
            var observation = network ? proxy!.Observe(world.GetVehicle(1)) : practice!.Capture(input).Observation;
            var result = world.Step(input, [new(1, input, observation)])[0];
            if (network) { proxy!.Apply(result.Snapshot); proxy.PresentRemote(result.Snapshot.Movement.Physics); } else practice!.Apply(result);
            watch.Stop();
            var p = observation.Physics;
            float step = N.Vector3.Distance(p.Position, before.Position);
            peakStep = Math.Max(peakStep, step);
            if (frame is >= 120 and < 210) { lateStep = Math.Max(lateStep, step); }
            peakAngular = Math.Max(peakAngular, p.AngularVelocity.Length());
            peakUp = Math.Max(peakUp, p.LinearVelocity.Y);
            int contacts = observation.Contacts.Count(c => c.StaticObstacle || c.Terrain);
            if (observation.Contacts.Any(c => c.StaticObstacle)) { firstObstacle ??= p.Position; }
            if (reverse && firstObstacle.HasValue) { reverseEscape = Math.Max(reverseEscape, N.Vector3.Distance(firstObstacle.Value, p.Position)); }
            if (contacts > 0) { contactFrames++; costs.Add(watch.Elapsed.TotalMilliseconds); if (frame >= 630) finalContacts++; }
            query.From = body.GlobalTransform; query.Motion = Vector3.Zero;
            float depth = 0;
            if (PhysicsServer3D.BodyTestMotion(body.GetRid(), query, hit))
                for (int i = 0; i < hit.GetCollisionCount(); i++) { depth = Math.Max(depth, hit.GetCollisionDepth(i)); }
            deepest = Math.Max(deepest, depth); if (depth > 0.1f) deepFrames++;
            trace.Add(new { frame, position = new[] { p.Position.X, p.Position.Y, p.Position.Z }, velocity = new[] { p.LinearVelocity.X, p.LinearVelocity.Y, p.LinearVelocity.Z }, angular = p.AngularVelocity.Length(), step, depth, contacts,
                up = N.Vector3.Transform(N.Vector3.UnitY, p.Orientation).Y, observationMs = watch.Elapsed.TotalMilliseconds,
                normals = observation.Contacts.Select(c => new { n = new[] { c.Normal.X, c.Normal.Y, c.Normal.Z }, c.StaticObstacle, c.Terrain }) });
            if (_camera is not null && ((contacts > 0 && contactFrames == 1) || frame is 200 or 410 or 659))
            {
                Vector3 position = VehicleBody.ToGodot(p.Position);
                _camera.Position = position - heading * 6 + normal * 2 + heading.Cross(normal) * 3; _camera.LookAt(position);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(_directory, name + "-" + frame + ".png"));
            }
        }
        double mean = costs.Count == 0 ? 0 : costs.Average();
        double p95 = costs.Count == 0 ? 0 : costs.Order().ElementAt((int)((costs.Count - 1) * 0.95));
        Check(peakStep < 1.5f, name + " bounded positional correction");
        Check(deepFrames < 6, name + " no sustained deep penetration");
        if (surface is "bank-face" or "pillar" or "perimeter" or "rock")
        {
            Check(firstObstacle.HasValue, name + " reaches solid obstacle");
            Check(reverseEscape > 2, name + " driver can leave initial contact region");
        }
        if (contactFrames >= 30) { Check(mean < 8 && p95 < 30, name + $" sustained contact budget mean={mean:F3}ms p95={p95:F3}ms"); }
        GD.Print($"WORLD {name}: contacts={contactFrames} final={finalContacts} depth={deepest:F4} deepFrames={deepFrames} step={peakStep:F4} lateStep={lateStep:F4} up={peakUp:F3} angular={peakAngular:F3} meanMs={mean:F3} p95Ms={p95:F3}");
        _summary.Add(new { name, contactFrames, finalContacts, deepest, deepFrames, peakStep, lateStep, peakUp, peakAngular, reverseEscape, mean, p95 });
        System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, name + ".json"), JsonSerializer.Serialize(trace));
        body.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string message) { if (!condition) { _failures++; GD.Print("FAIL: " + message); } }
    private static string Option(string name, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? fallback;
    private static float Number(string name, float fallback) => float.Parse(Option(name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
}
