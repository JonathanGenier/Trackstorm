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
    private readonly string _output = ProjectSettings.GlobalizePath("res://.godot/ts-310/round-2/presentation");
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
            var presentation = new ItemSpawnPresentation(); AddChild(presentation); presentation.Initialize(configuration, map.GetNode<Node3D>("ItemSpawns"));
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
            VerifyPlacement(presentation, configuration);
            var poses = presentation.GetChildren().OfType<Node3D>().Select(n => n.GetNode<Node3D>("IntactPickup").GlobalTransform).ToArray();
            VerifyProjectilePassage(host, configuration.Items.Single(s => s.Id == "item-infield-06-turn").Position);
            for (ulong cycle = 0; cycle < 120; cycle++)
            {
                presentation.Apply(Publication(false, 2 + cycle * 2), seed: true);
                Check(presentation.ActiveCount == 0 && nodes.OfType<GpuParticles3D>().All(p => !p.Emitting), "Cooldown hides all three crates and stops ambient particles", record: cycle == 0);
                presentation.Apply(Publication(true, 3 + cycle * 2));
                Check(presentation.ActiveCount == 27, "Confirmed respawn restores all three crates as one pickup", record: cycle == 0);
                if (cycle % 10 == 0) await Frames(1);
            }
            Check(Descendants(presentation).Count() == nodes.Length, "120 hide/respawn cycles do not accumulate nodes or visual owners");
            Check(presentation.GetChildren().OfType<Node3D>().Select(n => n.GetNode<Node3D>("IntactPickup").GlobalTransform).SequenceEqual(poses), "Grounded poses remain fixed through elapsed frames and all 120 respawns; no hover returns");
            if (DisplayServer.GetName() != "headless")
            {
                var environment = new EnvironmentPresentation(); AddChild(environment);
                var camera = new Camera3D { Current = true, Fov = 55, Far = 1800 }; AddChild(camera);
                var marker = configuration.Items.Single(s => s.Id == "item-infield-06-turn");
                Node3D viewed = presentation.GetNode<Node3D>(marker.Id + "/IntactPickup");
                Vector3 position = viewed.GlobalPosition;
                foreach (var preset in Enum.GetValues<EnvironmentPreset>())
                {
                    environment.Apply(preset);
                    foreach (int distance in new[] { 6, 16, 32 })
                    {
                        camera.Position = position + viewed.GlobalBasis.Z * distance + viewed.GlobalBasis.X * (distance * .30f) + Vector3.Up * (1.6f + distance * .15f);
                        camera.LookAt(position + Vector3.Up * 1.45f);
                        await Capture($"{preset}-{distance}m");
                    }
                }
                environment.Apply(EnvironmentPreset.ClearBlue);
                foreach (var markerView in configuration.Items)
                {
                    var pickup = presentation.GetNode<Node3D>(markerView.Id + "/IntactPickup");
                    Vector3 point = pickup.GlobalPosition;
                    // Ground-level route approach, including under the bridge.
                    Vector3 direction = new Vector3(pickup.GlobalBasis.Z.X, 0, pickup.GlobalBasis.Z.Z).Normalized();
                    camera.Position = point + direction * 12 + Vector3.Up * 2.6f;
                    if (!markerView.Id.StartsWith("item-infield-", StringComparison.Ordinal) || map.GetNode<Marker3D>("ItemSpawns/" + markerView.Id).GetMeta("pickup_floating", false).AsBool())
                    {
                        using var groundQuery = PhysicsRayQueryParameters3D.Create(camera.Position + Vector3.Up * 15, camera.Position + Vector3.Down * 30, 1);
                        using var ground = GetWorld3D().DirectSpaceState.IntersectRay(groundQuery);
                        Check(ground.Count != 0, "Banked approach camera has terrain support", record: false);
                        camera.Position = new Vector3(camera.Position.X, ground["position"].AsVector3().Y + 2.6f, camera.Position.Z);
                    }
                    camera.LookAt(point + pickup.GlobalBasis.Y * 1.45f);
                    await Capture(markerView.Id);
                }
                camera.Position = position - viewed.GlobalBasis.Z * 7 + Vector3.Up * 3; camera.LookAt(position + Vector3.Up * 1.45f);
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

    private void VerifyPlacement(ItemSpawnPresentation presentation, Core.Arenas.ArenaConfiguration configuration)
    {
        using var measurements = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://assets/maps/oval/measurements.json")));
        var sections = measurements.RootElement.GetProperty("sections_godot").EnumerateArray().ToArray();
        var map = GetChildren().OfType<Node3D>().Single(n => n.HasNode("ItemSpawns"));
        var source = GD.Load<PackedScene>("res://scenes/maps/infield_pickups.tscn").Instantiate<Node3D>();
        foreach (var authored in source.GetChildren().OfType<Marker3D>())
        {
            var marker = configuration.Items.Single(s => s.Id == authored.Name);
            Check(VehicleBody.ToGodot(marker.Position).IsEqualApprox(authored.Position) && MathF.Abs(Mathf.AngleDifference(marker.Yaw, authored.Rotation.Y)) < .0001f,
                "Baked infield pickup positions and facing match the authoring scene", record: false);
            Check(authored.GetMeta("pickup_floating", false).AsBool() == map.GetNode<Marker3D>("ItemSpawns/" + marker.Id).GetMeta("pickup_floating", false).AsBool(),
                "Baked floating designation matches the authoring scene", record: false);
        }
        source.Free();
        float worstGap = float.NegativeInfinity, worstPenetration = 0;
        int groundedCount = 0, floatingCount = 0;
        foreach (var marker in configuration.Items)
        {
            var root = presentation.GetNode<Node3D>(marker.Id);
            var pickup = root.GetNode<Node3D>("IntactPickup");
            Check(root.Position.IsEqualApprox(VehicleBody.ToGodot(marker.Position)), "Presentation roots preserve registered spawn centres", record: false);
            Check(pickup.Scale.IsEqualApprox(Vector3.One), "User-approved size remains unchanged at every marker", record: false);
            Vector3 forward = -new Basis(Vector3.Up, marker.Yaw).Z;
            var authoredMarker = map.GetNode<Marker3D>("ItemSpawns/" + marker.Id);
            if (authoredMarker.HasMeta("source_section"))
            {
                int index = authoredMarker.GetMeta("source_section").AsInt32();
                float lane = authoredMarker.GetMeta("surface_lane_m").AsSingle() / 18;
                Vector3 Section(int i)
                {
                    var ends = sections[(i + sections.Length) % sections.Length].EnumerateArray().ToArray();
                    Vector3 Read(System.Text.Json.JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
                    return Read(ends[0]).Lerp(Read(ends[1]), lane);
                }
                Vector3 tangent = Section(index + 1) - Section(index - 1); tangent.Y = 0;
                Check(forward.Dot(tangent.Normalized()) > .999f, $"{marker.Id} follows measured oval travel direction", record: false);
            }
            Check((-pickup.GlobalBasis.Z).Dot(forward.Slide(pickup.GlobalBasis.Y).Normalized()) > .999f, $"{marker.Id} follows authored travel direction on its terrain plane", record: false);
            bool floating = authoredMarker.GetMeta("pickup_floating", false).AsBool();
            float minGap = float.PositiveInfinity, maxGap = float.NegativeInfinity;
            if (floating)
            {
                floatingCount++;
                Check(pickup.GlobalPosition.IsEqualApprox(VehicleBody.ToGodot(marker.Position)) && pickup.GlobalBasis.Y.IsEqualApprox(Vector3.Up),
                    $"{marker.Id} retains designated floating height at {pickup.GlobalPosition}; no terrain projection");
            }
            else
            {
                groundedCount++;
                foreach (float x in new[] { -1.4f, -.08f, .08f, 1.4f })
                foreach (float z in new[] { -.65f, .65f })
                {
                    Vector3 foot = pickup.ToGlobal(new Vector3(x, .025f, z));
                    using var query = PhysicsRayQueryParameters3D.Create(foot + Vector3.Up * .5f, foot + Vector3.Down * 2, 1);
                    using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                    Check(hit.Count != 0, $"{marker.Id} has terrain under both bottom crates", record: false);
                    float gap = foot.Y - hit["position"].AsVector3().Y;
                    minGap = MathF.Min(minGap, gap); maxGap = MathF.Max(maxGap, gap);
                }
                worstGap = MathF.Max(worstGap, maxGap); worstPenetration = MathF.Max(worstPenetration, -minGap);
                Check(maxGap <= .035f && minGap >= -.12f, $"{marker.Id} planted support: signed vertical clearance {minGap:F4}..{maxGap:F4} m", record: false);
                _evidence.Add($"{marker.Id}: ground={pickup.GlobalPosition}, up={pickup.GlobalBasis.Y}, heading={marker.Yaw:F4} rad, foot clearance={minGap:F4}..{maxGap:F4} m, marker={marker.Position}.");
            }
            var label = root.GetChildren().OfType<Label3D>().Single();
            var stack = Descendants(pickup).OfType<Node3D>().Single(n => n.Name == "CarnivalCrateStack");
            float top = Descendants(stack).OfType<MeshInstance3D>().Max(m => (m.GlobalTransform * m.GetAabb()).End.Y);
            Check(label.GlobalPosition.Y >= top + .10f, $"{marker.Id} label clears the tilted stack", record: false);
        }
        Check(groundedCount == 26 && floatingCount == 1, $"All 27 stacks preserve scale/marker centres and route facing. {groundedCount} grounded stacks, eight foot samples each: maximum gap={worstGap:F4} m, maximum penetration={worstPenetration:F4} m. One authored floating stack stays aloft. Labels clear the upper bounds for both placement modes.");
        var jump = presentation.GetNode<Node3D>("item-infield-05-jump/IntactPickup");
        var tunnel = presentation.GetNode<Node3D>("item-infield-03-tunnel/IntactPickup");
        Check(MathF.Abs(jump.GlobalBasis.Z.X) > .999f && MathF.Abs(tunnel.GlobalBasis.Z.Z) > .999f && tunnel.GlobalPosition.Y < .1f, "Jump faces east/west approach; tunnel faces north/south underpass and seats on its floor rather than bridge deck");
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
