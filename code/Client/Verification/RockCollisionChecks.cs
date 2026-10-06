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

/// <summary>Imported intact rocks: impact, pressure, reverse, repeated re-contact and final separation.</summary>
public sealed partial class RockCollisionChecks : Node3D
{
    private int _failures;
    private Camera3D? _camera;
    private readonly List<object> _summaries = new();

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            string directory = ProjectSettings.GlobalizePath("res://.godot/ts-267/" + Option("label", "checks"));
            System.IO.Directory.CreateDirectory(directory);
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
                AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightEnergy = 1.4f });
                _camera = new Camera3D { Current = true, Far = 300, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off }; AddChild(_camera);
            }
            foreach (bool network in new[] { true, false })
            foreach (string model in new[] { "RockCluster", "RockLedge", "BoulderTall", "RockSlab", "BoulderLow" })
            foreach (float speed in new[] { 3f, 12f, 35f })
            foreach (float angle in new[] { 0f, 1.2f, 2.4f })
            {
                if (Option("adapter", network ? "network" : "practice") != (network ? "network" : "practice") ||
                    Option("rock", model) != model || !Selected("speed", speed) || !Selected("angle", angle)) continue;
                await Scenario(directory, network, model, speed, angle);
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(_summaries, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"Rock collision checks: failures={_failures}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Scenario(string directory, bool network, string model, float speed, float angle)
    {
        string name = $"{(network ? "network" : "practice")}-{model}-{speed}-{angle:F1}";
        var fixture = new Node3D(); AddChild(fixture);
        float grade = Number("grade", 0) * MathF.PI / 180;
        var floor = new StaticBody3D { Rotation = new(grade, 0, 0) }; floor.AddToGroup("landing_terrain");
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(2000, 2, 2000) }, Position = new(0, -1, 0) });
        if (_camera is not null) floor.AddChild(VehicleBody.Box(new(2000, 2, 2000), new(0, -1, 0), new Color("80634b")));
        fixture.AddChild(floor);
        var rock = GD.Load<PackedScene>($"res://assets/environment/models/{model}.glb").Instantiate<Node3D>();
        rock.Scale = Vector3.One * Number("scale", 2); fixture.AddChild(rock);
        foreach (var collider in rock.FindChildren("*", "StaticBody3D", true, false).OfType<StaticBody3D>())
        {
            collider.SetMeta("environment_rock", 1);
            collider.CollisionLayer |= Arenas.DestructibleEnvironment.RockContactLayer;
        }
        foreach (var shape in rock.FindChildren("*", "CollisionShape3D", true, false).OfType<CollisionShape3D>())
            Check(shape.Shape is ConvexPolygonShape3D hull && hull.Points.Length is >= 4 and <= 32, name + " imported hull budget");

        // Intact geometry is intentionally retained: breaking it must not hide a contact defect.
        var world = new Core.Simulation.Simulation(new(60));
        var rotation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, grade) * N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, angle);
        var pose = new VehiclePhysicsState(N.Vector3.Transform(new(Number("offset", 0), Number("height", VehicleDimensions.RideHeight), Number("distance", 10)), rotation), rotation,
            N.Vector3.Transform(new(0, Number("vertical-speed", 0), -Number("initial-speed", speed)), rotation), N.Vector3.Zero);
        var damage = new DamageConfiguration { MaxHP = 100000, CollisionScale = 5 };
        var configuration = Option("tuning", "defaults") == "host"
            ? new Development.DeveloperSettingsStore(ProjectSettings.GlobalizePath("user://developer-settings.jsonl")).LoadForHost().Vehicle : new VehicleConfiguration();
        world.AddVehicle(1, configuration, damage, pose);
        VehicleBody? practice = null; NetworkVehicleBody? proxy = null;
        if (network) { proxy = new() { VehicleId = 1 }; fixture.AddChild(proxy); proxy.ApplyConfiguration(configuration); proxy.Apply(world.GetVehicle(1)); }
        else
        {
            practice = new() { Position = VehicleBody.ToGodot(pose.Position), Quaternion = VehicleBody.ToGodot(rotation), LinearVelocity = VehicleBody.ToGodot(pose.LinearVelocity), DamageConfiguration = damage, Configuration = configuration };
            practice.Initialize(world); fixture.AddChild(practice);
        }
        PhysicsBody3D body = (PhysicsBody3D?)practice ?? proxy!;
        float initialPenetration = 0;
        float initialRecovery = 0;
        if (Number("embed", 0) > 0 || Number("approach-gap", 0) > 0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Vector3 direction = VehicleBody.ToGodot(N.Vector3.Transform(-N.Vector3.UnitZ, rotation));
            using var entry = new PhysicsTestMotionParameters3D { From = body.GlobalTransform, Motion = direction * 15, Margin = 0.005f, MaxCollisions = 4, RecoveryAsCollision = true };
            using var entryHit = new PhysicsTestMotionResult3D();
            Check(PhysicsServer3D.BodyTestMotion(body.GetRid(), entry, entryHit), name + " embedded fixture reaches geometry");
            Vector3 position = entry.From.Origin + entryHit.GetTravel() + direction * (Number("embed", 0) - Number("approach-gap", 0));
            pose = new(VehicleBody.ToCore(position), rotation, N.Vector3.Zero, N.Vector3.Zero);
            world.Restore(new(0, default, [new VehicleSnapshot(1, 1, new VehicleState(0, pose, true, false, 0, 0), new(damage.MaxHP, damage.MaxHP, null, null), pose)]));
            var embedded = new Transform3D(new Basis(VehicleBody.ToGodot(rotation)), position);
            if (network) proxy!.Apply(world.GetVehicle(1));
            else
            {
                var native = PhysicsServer3D.BodyGetDirectState(body.GetRid());
                native.Transform = embedded; native.LinearVelocity = Vector3.Zero; native.AngularVelocity = Vector3.Zero;
            }
            entry.From = embedded; entry.Motion = Vector3.Zero;
            if (PhysicsServer3D.BodyTestMotion(body.GetRid(), entry, entryHit))
            {
                initialRecovery = entryHit.GetTravel().Length();
                for (int i = 0; i < entryHit.GetCollisionCount(); i++) initialPenetration = Math.Max(initialPenetration, entryHit.GetCollisionDepth(i));
            }
            if (Number("embed", 0) > 0) Check(initialRecovery > 0.01f, name + $" embedded fixture requires native recovery: travel={initialRecovery:F4}, residualDepth={initialPenetration:F4}");
        }
        var traces = new List<object>();
        var contactTimes = new List<double>();
        float deepest = 0, peakAngular = 0, peakStep = 0;
        int contactFrames = 0, overlaps = 0, finalContacts = 0, repeatContacts = 0;
        float finalHeight = 0, finalUp = 0, finalVertical = 0, finalDistance = 0, finalAngular = 0;
        float settledSpeedChange = 0;
        float pressureMinimum = float.PositiveInfinity, pressureMaximum = float.NegativeInfinity;
        float pressureFirst = 0, pressureLast = 0, pressureTravel = 0, pressureLiftStep = 0;
        N.Vector3 approach = N.Vector3.Transform(-N.Vector3.UnitZ, rotation);
        float longitudinalFirst = 0, longitudinalLast = 0, longitudinalTravel = 0, longitudinalPeakStep = 0;
        int longitudinalDirection = 0, longitudinalReversals = 0;
        N.Vector3 previousVelocity = pose.LinearVelocity;
        using var query = new PhysicsTestMotionParameters3D { Margin = 0.001f, MaxCollisions = 16, RecoveryAsCollision = true };
        using var hit = new PhysicsTestMotionResult3D();
        int duration = (int)Number("frames", 705);
        int holdFrames = (int)Number("hold", 0);
        for (int frame = 0; frame < duration; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            bool drive = frame < 180 || frame is >= 240 and < 330 || frame is >= 390 and < 480;
            bool reverse = frame is >= 195 and < 240 || frame is >= 345 and < 390 || frame >= 495;
            if (holdFrames > 0) { drive = frame < holdFrames; reverse = frame >= holdFrames + 15; }
            var input = new InputFrame(world.State.Tick + 1, (short)(short.MaxValue * Number("steer", 0)), drive ? (ushort)(ushort.MaxValue * Number("throttle", 1)) : (ushort)0, reverse ? ushort.MaxValue : (ushort)0, 0, 0, 0);
            var watch = Stopwatch.StartNew();
            var before = world.GetVehicle(1).Movement.Physics;
            var observation = network ? proxy!.Observe(world.GetVehicle(1)) : practice!.Capture(input).Observation;
            double observeMs = watch.Elapsed.TotalMilliseconds;
            var result = world.Step(input, [new(1, input, observation)])[0];
            if (network) { proxy!.Apply(result.Snapshot); proxy.PresentRemote(result.Snapshot.Movement.Physics); } else practice!.Apply(result);
            watch.Stop();
            query.From = body.GlobalTransform; query.Motion = Vector3.Zero;
            float depth = 0;
            if (PhysicsServer3D.BodyTestMotion(body.GetRid(), query, hit))
                for (int i = 0; i < hit.GetCollisionCount(); i++)
                    if (hit.GetCollider(i) is Node node && node.HasMeta("environment_rock")) depth = Math.Max(depth, hit.GetCollisionDepth(i));
            if (depth > 0.02f) overlaps++;
            deepest = Math.Max(deepest, depth);
            var p = observation.Physics;
            if (holdFrames > 180 && frame >= 180 && frame < holdFrames)
            {
                float position = N.Vector3.Dot(p.Position, approach);
                if (frame == 180) { longitudinalFirst = position; }
                else
                {
                    float step = position - longitudinalLast;
                    longitudinalTravel += Math.Abs(step);
                    longitudinalPeakStep = Math.Max(longitudinalPeakStep, Math.Abs(step));
                    // Ignore only float/native recovery noise below ten microns
                    // when counting reversals; retain every step in travel.
                    if (Math.Abs(step) >= 0.00001f)
                    {
                        int direction = Math.Sign(step);
                        if (longitudinalDirection != 0 && direction != longitudinalDirection) { longitudinalReversals++; }
                        longitudinalDirection = direction;
                    }
                }
                longitudinalLast = position;
            }
            if (holdFrames > 120 && frame >= 120 && frame < holdFrames)
            {
                if (frame == 120) { pressureFirst = p.Position.Y; }
                else
                {
                    pressureTravel += Math.Abs(p.Position.Y - pressureLast);
                    pressureLiftStep = Math.Max(pressureLiftStep, p.Position.Y - pressureLast);
                }
                pressureLast = p.Position.Y;
                pressureMinimum = Math.Min(pressureMinimum, p.Position.Y);
                pressureMaximum = Math.Max(pressureMaximum, p.Position.Y);
            }
            finalHeight = p.Position.Y;
            finalUp = N.Vector3.Transform(N.Vector3.UnitY, p.Orientation).Y;
            finalVertical = Math.Abs(p.LinearVelocity.Y);
            finalDistance = new N.Vector2(p.Position.X, p.Position.Z).Length();
            finalAngular = p.AngularVelocity.Length();
            if (frame >= duration - 60) settledSpeedChange = Math.Max(settledSpeedChange, N.Vector3.Distance(p.LinearVelocity, previousVelocity));
            previousVelocity = p.LinearVelocity;
            peakStep = Math.Max(peakStep, N.Vector3.Distance(p.Position, before.Position));
            peakAngular = Math.Max(peakAngular, p.AngularVelocity.Length());
            int contacts = observation.Contacts.Count(c => c.EnvironmentRock != 0);
            if (contacts > 0)
            {
                contactFrames++;
                if (frame >= 90) contactTimes.Add(watch.Elapsed.TotalMilliseconds);
                if (frame >= 240 && drive) repeatContacts++;
                if (frame >= duration - 30) finalContacts++;
            }
            traces.Add(new { frame, x = p.Position.X, y = p.Position.Y, z = p.Position.Z, vx = p.LinearVelocity.X, vy = p.LinearVelocity.Y, vz = p.LinearVelocity.Z,
                angular = p.AngularVelocity.Length(), up = finalUp, wheels = observation.Wheels?.Compression.ToString(),
                support = observation.Support.ToString(), terrainSupport = observation.TerrainSupport.ToString(),
                commandVx = result.Snapshot.Movement.Physics.LinearVelocity.X,
                commandVz = result.Snapshot.Movement.Physics.LinearVelocity.Z,
                commandVy = result.Snapshot.Movement.Physics.LinearVelocity.Y, throttle = result.Snapshot.Movement.Throttle,
                crashSeconds = result.Snapshot.Movement.CrashSeconds,
                normals = observation.Contacts.Where(c => c.EnvironmentRock != 0).Select(c => c.Normal.ToString()).ToArray(),
                rockContacts = observation.Contacts.Where(c => c.EnvironmentRock != 0).Select(c => new { normal = c.Normal.ToString(), local = c.LocalPosition.ToString(), c.StaticObstacle }).ToArray(),
                depth, contacts, observeMs, stepMs = watch.Elapsed.TotalMilliseconds });
            if (_camera is not null && ((contacts > 0 && contactFrames == 1) || frame is 170 or 310 or 460 or 700))
            {
                Vector3 position = VehicleBody.ToGodot(p.Position);
                _camera.Position = position + new Vector3(9, 6, 10); _camera.LookAt(position);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng(System.IO.Path.Combine(directory, $"{name}-{frame}.png"));
            }
        }
        double contactMeanMs = contactTimes.Count == 0 ? 0 : contactTimes.Average();
        double contactP95Ms = contactTimes.Count == 0 ? 0 : contactTimes.Order().ElementAt((int)((contactTimes.Count - 1) * 0.95));
        float pressureRange = holdFrames > 120 ? pressureMaximum - pressureMinimum : 0;
        float pressureRepeatedTravel = pressureTravel - Math.Abs(pressureLast - pressureFirst);
        float longitudinalRepeatedTravel = longitudinalTravel - Math.Abs(longitudinalLast - longitudinalFirst);
        if (Number("fore-aft-check", 0) > 0)
        {
            // A blocked start may settle once as suspension loads. It must not
            // cycle forward/recovery/backward throughout sustained full throttle.
            Check(holdFrames == 600 && longitudinalReversals <= 2 && longitudinalRepeatedTravel < 0.04f && longitudinalPeakStep < 0.005f,
                name + $" settled fore/aft: reversals={longitudinalReversals}, repeatedTravel={longitudinalRepeatedTravel:F6}, peakStep={longitudinalPeakStep:F6}");
        }
        if (Number("pressure-check", 0) > 0)
        {
            // These starts face a blocking side, without momentum to mount it.
            // Allow millimetre native recovery; reject repeated centimetre hops
            // and accumulated climbing during the five-second settled interval.
            Check(holdFrames == 420 && pressureRange < 0.05f && pressureLiftStep < 0.005f && pressureRepeatedTravel < 0.1f,
                name + $" settled rock pressure: range={pressureRange:F5}, liftStep={pressureLiftStep:F5}, repeatedTravel={pressureRepeatedTravel:F5}");
        }
        Check(contactFrames > 0 || Number("offset", 0) != 0, name + " reaches rock");
        Check(overlaps < 5 && deepest < 0.1f, name + $" no persistent penetration: depth={deepest:F4}, frames={overlaps}");
        if (Number("height", VehicleDimensions.RideHeight) > 3)
        {
            // Landing changes heading, so final reverse may meet another side.
            // Require wheel-down recovery off the top as well as stable contact.
            Check(finalHeight < 1.7f && finalDistance > 3 && finalUp > 0.8f && finalVertical < 0.5f && finalAngular < 0.5f && settledSpeedChange < 0.3f,
                name + $" stable landing: height={finalHeight:F3} up={finalUp:F3} vertical={finalVertical:F3} angular={finalAngular:F3} deltaSpeed={settledSpeedChange:F3}");
        }
        else { Check(finalContacts == 0, name + " separates after reverse"); }
        Check(peakStep < 1.2f && peakAngular <= 8.01f, name + " bounded correction/motion");
        // Wall-time measurements include OS scheduling; budget the mean, retain p95 evidence.
        Check(contactMeanMs < 8, name + $" contact step budget: {contactMeanMs:F3}ms");
        _summaries.Add(new { name, initialPenetration, initialRecovery, contactFrames, repeatContacts, finalContacts, deepest, overlaps, peakStep, peakAngular, finalHeight, finalUp, finalVertical, finalDistance, finalAngular, settledSpeedChange, pressureRange, pressureLiftStep, pressureRepeatedTravel, longitudinalReversals, longitudinalRepeatedTravel, longitudinalPeakStep, contactMeanMs, contactP95Ms });
        GD.Print($"ROCK {name}: contacts={contactFrames} repeat={repeatContacts} final={finalContacts} depth={deepest:F4} overlapFrames={overlaps} angular={peakAngular:F3} contactMeanMs={contactMeanMs:F3} p95Ms={contactP95Ms:F3}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(traces));
        fixture.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string message) { if (!condition) { _failures++; GD.Print("FAIL: " + message); } }
    private static string Option(string name, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? fallback;
    private static float Number(string name, float fallback) => float.Parse(Option(name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
    private static bool Selected(string name, float value) => Number(name, value) == value;
}
