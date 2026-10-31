using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Confirmed pickup bursts, recovery, bounds, rendered timing and production-map cleanup.</summary>
public sealed partial class CarnivalSmashChecks : Node3D
{
    private readonly List<string> _evidence = [];
    private string _output = "";
    private ulong _revision;
    private bool Quick => OS.GetCmdlineUserArgs().Contains("--smash-quick");

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            _output = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--smash-output=", StringComparison.Ordinal))[15..];
            var map = ActiveMap.Load(); AddChild(map);
            var config = ActiveMap.ReadConfiguration(map);
            var presentation = new ItemSpawnPresentation(); AddChild(presentation);
            presentation.Initialize(config, map.GetNode<Node3D>("ItemSpawns"));
            var host = new HostVehicleSession(311); host.RegisterSpawns(config);
            ItemPublication Publication(ulong token, bool all = false) => new(++_revision, host.Snapshot(), [], [], [],
                host.Spawns!.States.Select(s => token != 0 && (all || s.Id == "item-infield-06-turn") ? new ItemSpawnState(s.Id, false, 600, 1, token, HeldItem.Missile) : s));
            presentation.Apply(Publication(0)); await Frames(3);
            int baselineNodes = Descendants(presentation).Count();
            var marker = config.Items.Single(s => s.Id == "item-infield-06-turn");
            var intact = presentation.GetNode<Node3D>(marker.Id + "/IntactPickup");
            var pose = intact.GlobalTransform;
            var environment = new EnvironmentPresentation(); AddChild(environment);
            var camera = new Camera3D { Current = true, Fov = 55, Far = 1600 }; AddChild(camera);
            camera.GlobalPosition = pose.Origin + pose.Basis.Z*10 + pose.Basis.X*3 + Vector3.Up*3.2f;
            camera.LookAt(pose.Origin+Vector3.Up*1.5f);

            ExerciseCrossings(config, presentation, marker);
            presentation.Apply(Publication(0), seed: true);
            VerifyProjectilePassage(host, pose);
            var claim = Publication(501);
            ulong before = presentation.SmashCount;
            presentation.Apply(claim);
            presentation.Apply(claim);
            presentation.Apply(Publication(501));
            Check(presentation.SmashCount == before+1 && presentation.ActiveSmashCount == 1, "Duplicate and unrelated publications produce exactly one smash");
            Check(!intact.Visible && presentation.ActiveCount == 26, "A claim hides every crate and aura of exactly one stack");
            presentation.Apply(Publication(503));
            Check(presentation.SmashCount == before+2 && !intact.Visible,
                "A newer claim while already unavailable still emits a smash");
            presentation.Apply(Publication(0), seed: true);
            Check(intact.Visible && presentation.GetNode<Node3D>(marker.Id).GetChildren().OfType<Label3D>().Single().Text == "ITEM",
                "Recovery restores active visibility and label");
            presentation.Apply(Publication(501));
            Check(!Descendants(presentation).Any(n => n is CollisionObject3D or CollisionShape3D), "Intact stack and smash contain no physics bodies, areas or shapes");
            var burst = Descendants(presentation).OfType<CarnivalSmash>().Single();
            var batches = burst.GetChildren().OfType<MultiMeshInstance3D>().ToArray();
            Check(batches.Where(n => n.Name.ToString().StartsWith("Smash0", StringComparison.Ordinal)).Sum(n => n.Multimesh.InstanceCount) == 72, "Exactly 72 visibly sized wood instances in three batches");
            Check(batches.Length == 8 && batches.Sum(n => n.Multimesh.InstanceCount) == 344, "344 wood/foil/star/coin/bolt/spark instances use eight render batches and zero per-piece nodes");
            var gold = batches.Where(n => n.Name.ToString().StartsWith("Smash2", StringComparison.Ordinal)).ToArray();
            Check(gold.Length == 3 && gold.All(n => n.Multimesh.InstanceCount == 12) && gold.Select(n => n.Multimesh.Mesh).Distinct().Count() == 3,
                "Twelve stars, twelve rimmed coins and twelve lightning bolts use three distinct solid meshes");
            Check(burst.Resources.Wood.All(mesh => { var size = mesh.GetAabb().Size; return size.Y > size.X*3 && size.Z > .07f; }),
                "Imported plank masters have long grain-oriented faces and more than 7 cm of modelled thickness before instance scale");
            presentation.Apply(claim, seed: true);
            Check(presentation.ActiveSmashCount == 0 && !intact.Visible, "Recovery clears transient debris and restores availability without replay");
            presentation.Apply(Publication(502), observedTick: 1200);
            Check(presentation.ActiveSmashCount == 0, "Outcomes already older than the effect lifetime do not start late debris");
            presentation.Apply(new ItemPublication(++_revision, host.Snapshot(), [], [], [], []));
            Check(presentation.ActiveCount == 0 && presentation.GetNode<Node3D>(marker.Id).GetChildren().OfType<Label3D>().Single().Text.Length == 0,
                "Missing state hides the stack and clears the cooldown label");
            presentation.Apply(Publication(0));
            Check(presentation.ActiveCount == config.Items.Count && presentation.GetNode<Node3D>(marker.Id).GetChildren().OfType<Label3D>().Single().Text == "ITEM",
                "Availability after missing state restores every stack and its active label");

            if (DisplayServer.GetName() != "headless")
            {
                await VerifyRenderedFootprint();
                foreach (var preset in Enum.GetValues<EnvironmentPreset>())
                {
                    environment.Apply(preset);
                    presentation.Apply(Publication(0), seed: true);
                    await Capture(preset + "-intact");
                    presentation.Apply(Publication(600));
                    burst = Descendants(presentation).OfType<CarnivalSmash>().Single();
                    burst.SetProcess(false);
                    float previous = 0;
                    foreach (float age in new[] { .06f, .22f, .55f, 1.1f, 3f, 7f, 11.8f })
                    {
                        burst.Advance(age-previous); previous = age;
                        await Capture($"{preset}-{age:F2}s");
                        if (age == .22f)
                        {
                            var originalCamera = camera.GlobalTransform;
                            camera.GlobalPosition = pose.Origin+pose.Basis.Z*5+pose.Basis.X*1.5f+Vector3.Up*2.4f;
                            camera.LookAt(pose.Origin+Vector3.Up*1.5f);
                            await Capture(preset+"-material-close");
                            camera.GlobalTransform = originalCamera;
                        }
                    }
                    burst.Advance(.3f); await Frames(2);
                    Check(presentation.ActiveSmashCount == 0, preset + " burst completely removed at 12 seconds");
                }
            }
            environment.Apply(EnvironmentPreset.ClearBlue);
            presentation.Apply(Publication(0), seed: true);
            GC.Collect(); GC.WaitForPendingFinalizers(); await Frames(3);
            double resourcesBefore = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
            double orphansBefore = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
            ulong token = 1000;
            var timings = new List<double>();
            int waves = 0, peak = 0;
            double duration = Quick ? 4 : 60;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            double nextWave = 0, last = 0;
            while (timer.Elapsed.TotalSeconds < duration)
            {
                double now = timer.Elapsed.TotalSeconds;
                if (now >= nextWave)
                {
                    presentation.Apply(Publication(token++, all: true));
                    nextWave = now + .5; waves++;
                }
                await Frames(1);
                double end = timer.Elapsed.TotalSeconds;
                timings.Add((end-last)*1000); last = end;
                peak = Math.Max(peak, presentation.ActiveSmashCount);
                Check(presentation.ActiveSmashCount <= ItemSpawnPresentation.MaximumSmashes, "Concurrent effects stay bounded", false);
            }
            _evidence.Add($"Synthetic publication pressure: {waves} waves of 27 claims over {timer.Elapsed.TotalSeconds:F2}s; peak {peak} bursts; frame median={Percentile(timings,.5):F2}ms, p95={Percentile(timings,.95):F2}ms, max={timings.Max():F2}ms. Whole-map timings, 60 FPS cap; not actual inventory grants.");
            // Real elapsed-time expiry, including when gameplay is paused.
            GetTree().Paused = true;
            await ToSignal(GetTree().CreateTimer(12.5, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            GetTree().Paused = false; await Frames(2);
            Check(presentation.ActiveSmashCount == 0 && Descendants(presentation).Count() == baselineNodes, "All debris/effects removed within 12.5 real seconds while paused; node count returns to intact baseline");
            GC.Collect(); GC.WaitForPendingFinalizers(); await Frames(3);
            _evidence.Add($"Native resource/orphan counts after warmup vs after pressure, expiry and managed finalization: resources {resourcesBefore}->{Performance.GetMonitor(Performance.Monitor.ObjectResourceCount)}, orphans {orphansBefore}->{Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)}.");
            presentation.Apply(Publication(token++, all: true));
            var owned = Descendants(presentation).OfType<CarnivalSmash>().ToArray();
            presentation.QueueFree(); await Frames(2);
            Check(!IsInstanceValid(presentation) && owned.All(n => !IsInstanceValid(n)), "Match owner teardown frees all active burst nodes");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            GD.Print("Carnival smash checks passed: " + string.Join("\n", _evidence));
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void ExerciseCrossings(ArenaConfiguration config, ItemSpawnPresentation presentation, ArenaSpawn marker)
    {
        int attempts = 0;
        foreach (int crate in new[] { 0, 1, 2 })
        foreach (float speed in new[] { 8f, 40f, 65f })
        foreach (float glance in new[] { 0f, .55f })
        {
            var host = new HostVehicleSession(311); host.RegisterSpawns(config);
            float x = crate == 0 ? -.77f-glance : crate == 1 ? .77f+glance : glance;
            float y = crate == 2 ? 2.12f : .72f;
            N.Vector3 Rotate(N.Vector3 value) => N.Vector3.Transform(value, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, marker.Yaw));
            var start = marker.Position+Rotate(new(x,y,6));
            var velocity = Rotate(new(0,0,-speed));
            Place(host, start, velocity);
            ItemPublication State() => new(++_revision, host.Snapshot(), host.Items.Slots, [], [], host.Spawns!.States);
            presentation.Apply(State(), seed: true);
            ulong before = presentation.SmashCount;
            for (int step = 0; step < (int)(12/speed*60)+1; step++)
            {
                host.Step(default, s => new(new(s.Movement.Physics.Position+velocity/60, N.Quaternion.Identity, velocity, N.Vector3.Zero), N.Vector3.UnitY));
                host.CollectPickups();
                presentation.Apply(ItemCodec.DecodeState(ItemCodec.EncodeState(State())));
            }
            Check(host.Spawns!.States.Single(s => s.Id == marker.Id).Token > 0 && host.Spawns.Balances.Single().Total == 1 && presentation.SmashCount == before+1,
                $"Crate {crate}, speed {speed}, offset {glance}: expected one grant/smash; actual grants {host.Spawns.Balances.Sum(b => (long)b.Total)}, smashes {presentation.SmashCount-before}", false);
            attempts++;
        }
        _evidence.Add($"{attempts}/{attempts} authoritative swept crossings at lower-left/lower-right/upper-crate heights, 8/40/65 m/s, centre and glancing offsets; one grant and one codec-delivered smash each. Controlled observations, not native driving; pickup-drive covers native cars.");
    }

    private static void Place(HostVehicleSession host, N.Vector3 position, N.Vector3 velocity)
    {
        var original = host.World.State.Vehicles.Single();
        var pose = new VehiclePhysicsState(position, N.Quaternion.Identity, velocity, N.Vector3.Zero);
        var state = new VehicleSnapshot(1, original.LifeId, new(host.World.State.Tick, pose, false, false, 0, 0), original.Damage, pose);
        host.World.Restore(new SimulationState(host.World.State.Tick, host.World.State.LastInput, [state], host.World.State.Match));
    }

    private void VerifyProjectilePassage(HostVehicleSession host, Transform3D pose)
    {
        var before = host.Spawns!.States.ToArray();
        foreach (var local in new[] { new Vector3(-.77f,.7f,0), new Vector3(.77f,.7f,0), new Vector3(0,2.1f,0) })
        {
            Vector3 centre = pose*local;
            using var query = PhysicsRayQueryParameters3D.Create(centre+pose.Basis.Z*3, centre-pose.Basis.Z*3, uint.MaxValue);
            using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            Check(hit.Count == 0, "Native weapon ray passes through each of the three crates", false);
        }
        Check(host.Spawns.States.SequenceEqual(before) && host.Items.Slots.Count == 0, "Weapon queries leave collection availability and inventory unchanged");
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren()) { yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private void Check(bool valid, string message, bool record = true)
    {
        if (!valid) throw new InvalidOperationException(message);
        if (record) _evidence.Add(message);
    }
    private static double Percentile(List<double> values, double p) => values.Order().ElementAt((int)((values.Count-1)*p));
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name)
    {
        await Frames(3); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(System.IO.Path.Combine(_output, name+".png")) != Error.Ok) throw new InvalidOperationException("Capture failed");
    }
}
