using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Intact pickup asset/lifecycle checks and rendered production-map lighting/distance evidence.</summary>
public sealed partial class CarnivalPickupChecks : Node3D
{
    private readonly List<string> _evidence = [];
    private readonly string _output = ProjectSettings.GlobalizePath("res://.godot/ts-310/presentation");
    private readonly List<double> _frameTimes = [];
    private bool _measure;

    public override void _Ready() => CallDeferred(MethodName.Run);
    public override void _Process(double delta) { if (_measure) _frameTimes.Add(delta * 1000); }

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            System.IO.Directory.CreateDirectory(_output);
            var map = ActiveMap.Load(); AddChild(map);
            var configuration = ActiveMap.ReadConfiguration(map);
            var host = new HostVehicleSession(310);
            host.RegisterSpawns(configuration);
            var presentation = new ItemSpawnPresentation(); AddChild(presentation); presentation.Initialize(configuration);
            ItemPublication Publication(bool active, ulong revision) => new(revision, host.Snapshot(), [], [], [],
                host.Spawns!.States.Select(s => active ? s : new ItemSpawnState(s.Id, false, 600, 1, revision, HeldItem.Missile)));
            Check(presentation.ActiveCount == 0, "All models hidden before confirmed publication");
            presentation.Apply(Publication(true, 1));
            Check(presentation.ActiveCount == 27, "Exactly one intact pickup per each of the 27 existing map markers");
            var nodes = Descendants(presentation).ToArray();
            Check(!nodes.Any(n => n is CollisionObject3D or CollisionShape3D), "No collision bodies, areas or shapes: no vehicle obstruction or projectile hit target");
            var model = nodes.OfType<Node3D>().First(n => n.Name == "CarnivalCrateStack");
            var crates = model.GetChildren().OfType<Node3D>().ToArray();
            Check(crates.Length == 3 && crates.All(c => c.Name.ToString().StartsWith("Crate", StringComparison.Ordinal)), "One asset retains exactly three named crate groups");
            var meshes = Descendants(model).OfType<MeshInstance3D>().ToArray();
            Aabb bounds = default; bool first = true;
            foreach (var mesh in meshes)
            {
                var box = (model.GlobalTransform.AffineInverse() * mesh.GlobalTransform) * mesh.GetAabb();
                bounds = first ? box : bounds.Merge(box); first = false;
            }
            Check(bounds.Size.X is > 2.8f and < 3.3f && bounds.Size.Y is > 2.8f and < 3.2f && bounds.Size.Z < 1.9f, $"Compact stack bounds: {bounds}");
            var copies = nodes.OfType<MeshInstance3D>().Where(n => n.Name == meshes[0].Name).ToArray();
            Check(copies.Length == 27 && copies.All(m => m.Mesh == copies[0].Mesh), "Repeated pickups share the imported mesh resource");
            await Frames(2);
            VerifyProjectilePassage(host, configuration.Items.Single(s => s.Id == "item-infield-06-turn").Position);
            for (ulong cycle = 0; cycle < 120; cycle++)
            {
                presentation.Apply(Publication(false, 2 + cycle * 2));
                Check(presentation.ActiveCount == 0 && nodes.OfType<GpuParticles3D>().All(p => !p.Emitting), "Cooldown hides all three crates and stops ambient particles", record: cycle == 0);
                presentation.Apply(Publication(true, 3 + cycle * 2));
                Check(presentation.ActiveCount == 27, "Confirmed respawn restores all three crates as one pickup", record: cycle == 0);
                if (cycle % 10 == 0) await Frames(1);
            }
            Check(Descendants(presentation).Count() == nodes.Length, "120 hide/respawn cycles do not accumulate nodes or visual owners");
            if (DisplayServer.GetName() != "headless")
            {
                var environment = new EnvironmentPresentation(); AddChild(environment);
                var camera = new Camera3D { Current = true, Fov = 55, Far = 1800 }; AddChild(camera);
                var marker = configuration.Items.Single(s => s.Id == "item-infield-06-turn");
                Vector3 position = VehicleBody.ToGodot(marker.Position);
                foreach (var preset in Enum.GetValues<EnvironmentPreset>())
                {
                    environment.Apply(preset);
                    foreach (int distance in new[] { 6, 16, 32 })
                    {
                        camera.Position = position + new Vector3(distance * .30f, 1.6f + distance * .15f, distance);
                        camera.LookAt(position + Vector3.Up * 1.45f);
                        await Capture($"{preset}-{distance}m");
                    }
                }
                environment.Apply(EnvironmentPreset.ClearBlue);
                foreach (var markerView in configuration.Items.Where(s => s.Id.StartsWith("item-infield-", StringComparison.Ordinal)))
                {
                    Vector3 point = VehicleBody.ToGodot(markerView.Position);
                    camera.Position = point + new Vector3(2, 4, 16); camera.LookAt(point + Vector3.Up * 1.45f);
                    await Capture(markerView.Id);
                }
                camera.Position = position + new Vector3(-2, 3, -7); camera.LookAt(position + Vector3.Up * 1.45f);
                await Capture("reverse-approach");
                camera.Position = position + new Vector3(4, 3, 7); camera.LookAt(position + Vector3.Up * 1.45f);
                await Frames(120); _measure = true; await Frames(300); _measure = false;
                double[] times = _frameTimes.Order().ToArray();
                _evidence.Add($"Rendered production map with 27 active stacks, 300 frames capped at 60 FPS: median={times[times.Length / 2]:F2} ms, p95={times[(int)(times.Length * .95)]:F2} ms, max={times[^1]:F2} ms. These are whole-frame timings on this machine, not isolated GPU cost.");
            }
            presentation.QueueFree(); await Frames(2);
            Check(!IsInstanceValid(presentation), "Scene teardown releases pickup owner");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            GD.Print("Carnival pickup checks passed: " + string.Join("; ", _evidence));
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static IEnumerable<Node> Descendants(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void VerifyProjectilePassage(HostVehicleSession host, N.Vector3 marker)
    {
        var before = host.Spawns!.States.ToArray();
        int sweeps = 0;
        foreach (float height in new[] { .8f, 2.2f })
        {
            N.Vector3 start = marker + new N.Vector3(0, height, 4);
            var missile = new MissileState(host.Items.TokenHighWater + 1, 1, start, new N.Vector3(0, 0, -30), 300);
            host.Items.Restore(new(host.Items.Revision + 1, host.Snapshot(), [], [missile], [], host.Spawns.States), host.Items.Revision + 1, missile.Id);
            float? Sweep(MissileState shot, N.Vector3 end)
            {
                sweeps++;
                Vector3 from = VehicleBody.ToGodot(shot.Position), to = VehicleBody.ToGodot(end);
                using var query = PhysicsRayQueryParameters3D.Create(from, to, uint.MaxValue);
                using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                return hit.Count == 0 ? null : from.DistanceTo(hit["position"].AsVector3()) / from.DistanceTo(to);
            }
            for (int tick = 0; tick < 24; tick++)
                host.Step(default, state => new VehicleObservation(state.Movement.Physics, N.Vector3.UnitY), Sweep);
            Check(host.Items.Missiles.Any(m => m.Id == missile.Id && m.Position.Z < marker.Z - 3), $"Native projectile sweeps pass through the intact stack at height {height} m");
            Check(host.Spawns.States.SequenceEqual(before) && host.Items.Slots.Count == 0, "Projectile passage neither consumes pickups nor awards items", record: height > 1);
        }
        Check(sweeps >= 48, "Both projectile paths exercised native physics queries");
    }

    private void Check(bool valid, string message, bool record = true)
    {
        if (!valid) throw new InvalidOperationException(message);
        if (record) _evidence.Add(message);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Capture(string name)
    {
        await Frames(15); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(System.IO.Path.Combine(_output, name + ".png")) != Error.Ok) throw new InvalidOperationException("Capture failed");
    }
}
