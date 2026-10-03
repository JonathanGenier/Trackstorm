using System.Diagnostics;
using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Repeated actual-map falls and scraping contacts, retaining per-tick performance and motion evidence.</summary>
public sealed partial class TunnelScrapeChecks : Node3D
{
    private readonly List<object> _summary = new();
    private string _directory = "";
    private Camera3D? _camera;
    private int _failures;

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            string label = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("label=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "checks";
            _directory = ProjectSettings.GlobalizePath("res://.godot/tunnel-scrape-checks/" + label);
            System.IO.Directory.CreateDirectory(_directory);
            AddChild(Arenas.ActiveMap.Load());
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
                AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightEnergy = 1.4f });
                _camera = new Camera3D { Current = true, Far = 1500 }; AddChild(_camera);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await VerifyQueries();
            foreach (bool network in new[] { true, false })
            for (int repeat = 0; repeat < 2; repeat++)
            foreach (float speed in new[] { 3f, 12f, 25f })
            foreach (float angle in new[] { 0f, 0.45f, 1.2f })
            foreach (float side in new[] { -1f, 1f })
            {
                await Scenario(network, speed, angle, side, repeat);
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, "summary.json"), JsonSerializer.Serialize(_summary));
            GD.Print($"Tunnel scrape checks: failures={_failures}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Scenario(bool network, float speed, float angle, float side, int repeat)
    {
        string name = $"{(network ? "network" : "practice")}-fall-{speed}-{angle:F2}-{side}-{repeat}";
        string prefix = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("case=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) { return; }
        // Airborne beside the bridge: travel toward the inner dirt face while falling,
        // with an oblique body attitude. The remainder is ordinary Core/native motion.
        var start = new Vector3(side * 7, 5.7f, 14);
        var orientation = N.Quaternion.CreateFromYawPitchRoll(-side * MathF.PI / 2 + angle, 0.25f, side * 0.45f);
        var pose = new VehiclePhysicsState(VehicleBody.ToCore(start), orientation, new(side * speed, -4, -speed * 0.15f), N.Vector3.Zero);
        var world = new Core.Simulation.Simulation(new(60));
        var configuration = new VehicleConfiguration();
        world.AddVehicle(1, configuration, new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 }, pose);
        NetworkVehicleBody? proxy = null;
        VehicleBody? practice = null;
        if (network) { proxy = new() { VehicleId = 1 }; AddChild(proxy); proxy.Apply(world.GetVehicle(1)); }
        else
        {
            practice = new VehicleBody { Position = start, Quaternion = VehicleBody.ToGodot(orientation), LinearVelocity = VehicleBody.ToGodot(pose.LinearVelocity),
                Configuration = configuration, DamageConfiguration = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 } };
            practice.Initialize(world); AddChild(practice);
        }
        PhysicsBody3D body = (PhysicsBody3D?)proxy ?? practice!;
        var trace = new List<object>();
        var costs = new List<double>();
        int frame = 0, contacts = 0, fallContacts = 0;
        float peakStep = 0;
        for (frame = 0; frame < 420; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var input = new InputFrame(world.State.Tick + 1, (short)(0.6f * short.MaxValue), frame < 270 ? ushort.MaxValue : (ushort)0, frame >= 300 ? ushort.MaxValue : (ushort)0, 0, 0, 0);
            var before = world.GetVehicle(1).Movement.Physics;
            var pauseBefore = GC.GetTotalPauseDuration();
            long stamp = Stopwatch.GetTimestamp();
            var observation = network ? proxy!.Observe(world.GetVehicle(1)) : practice!.Capture(input).Observation;
            double observeMs = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
            double gcPauseMs = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
            var result = world.Step(input, [new(1, input, observation)])[0];
            if (network) { proxy!.Apply(result.Snapshot); proxy.PresentRemote(result.Snapshot.Movement.Physics); } else { practice!.Apply(result); }
            var p = observation.Physics;
            if (observation.Contacts.Count > 0)
            {
                contacts++; if (before.LinearVelocity.Y < -0.5f) fallContacts++;
                costs.Add(observeMs);
            }
            peakStep = Math.Max(peakStep, N.Vector3.Distance(before.Position, p.Position));
            trace.Add(new { frame, position = new[] { p.Position.X, p.Position.Y, p.Position.Z }, velocity = new[] { p.LinearVelocity.X, p.LinearVelocity.Y, p.LinearVelocity.Z }, observeMs, gcPauseMs, contacts = observation.Contacts.Count, step = N.Vector3.Distance(before.Position, p.Position), hp = result.Snapshot.Damage.CurrentHP });
            if (_camera is not null && frame is 25 or 60 or 150 or 419)
            {
                var position = VehicleBody.ToGodot(p.Position);
                _camera.Position = position + new Vector3(-side * 5, 3, 9); _camera.LookAt(position);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(_directory, name + "-" + frame + ".png"));
            }
        }
        double mean = costs.Count == 0 ? 0 : costs.Average();
        double p99 = costs.Count == 0 ? 0 : costs.Order().ElementAt((int)((costs.Count - 1) * 0.99));
        double maximum = costs.Count == 0 ? 0 : costs.Max();
        Check(fallContacts > 0, name + " reaches falling contact");
        Check(peakStep < 1.5f, name + " has bounded position correction");
        // Native query comparisons above isolate the engine optimization. The full
        // contact callback budget additionally catches repeated sweep regressions.
        double p95 = costs.Count == 0 ? 0 : costs.Order().ElementAt((int)((costs.Count - 1) * 0.95));
        if (network) { Check(mean < 8 && p95 < 20, name + " contact processing budget"); }
        GD.Print($"SCRAPE {name}: contacts={contacts} falling={fallContacts} mean={mean:F3} p99={p99:F3} max={maximum:F3} p95={p95:F3} step={peakStep:F3}");
        _summary.Add(new { name, contacts, fallContacts, mean, p95, p99, maximum, peakStep });
        System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, name + ".json"), JsonSerializer.Serialize(trace));
        body.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
