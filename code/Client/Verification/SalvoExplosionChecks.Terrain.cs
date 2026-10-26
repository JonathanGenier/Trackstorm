using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class SalvoExplosionChecks
{
    private N.Vector3? _terrainDirection;

    private async Task TerrainContacts()
    {
        var surface = new StaticBody3D { Position = new(500, 100, 500), CollisionLayer = 1 };
        var shape = new ConcavePolygonShape3D();
        shape.SetFaces([new(-20, 0, -20), new(20, 0, -20), new(20, 0, 20),
            new(-20, 0, -20), new(20, 0, 20), new(-20, 0, 20)]);
        surface.AddChild(new CollisionShape3D { Shape = shape });
        _arenas[0].AddChild(surface);
        await Frames(2);
        var failures = new List<string>();
        foreach (var (name, start, end) in new (string, N.Vector3, N.Vector3)[] {
            ("flat crossing", new(501, 101, 500), new(501, 99, 500)),
            ("flat exact endpoint", new(501, 101, 500), new(501, 100, 500)),
            ("flat boundary start", new(501, 100, 500), new(501, 99, 500)),
            ("diagonal seam", new(500, 101, 500), new(500, 99, 500)),
            ("shallow boundary", new(499, 100.01f, 500), new(501, 99.99f, 500)) })
        {
            var arc = new SalvoFlight(start, end, 1, 60, 0, Host.World.GetVehicle(1).LifeId);
            var round = new MissileState(9000, 1, start, end - start, 1800) { Arc = arc };
            float? hit = _arenas[0].Driver.CollideMissile!(round, end);
            GD.Print($"Terrain probe {name}: fraction={hit?.ToString() ?? "MISS"}");
            if (hit is null) { failures.Add(name); }
        }
        surface.QueueFree(); await Frames(2);
        Check(failures.Count == 0, "Five native terrain boundary contacts, including exact endpoint/start and seam: failures=" + string.Join(", ", failures));
        // Actual imported bank, dirt and structural collider coverage, including
        // exact surface starts. This is a geometry query, not a driving playtest.
        var map = ActiveMap.Load(); map.Position = new(500, 100, 500); _arenas[0].AddChild(map);
        await Frames(3);
        int samples = 0;
        for (int x = -210; x <= 210; x += 10)
        for (int z = -180; z <= 180; z += 20)
        {
            using var ray = PhysicsRayQueryParameters3D.Create(new(500 + x, 140, 500 + z), new(500 + x, 70, 500 + z), 1);
            using var hit = _arenas[0].GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count == 0) { continue; }
            var point = hit["position"].AsVector3();
            var normal = hit["normal"].AsVector3();
            if (normal.Y < .2f) { continue; }
            var tangent = (Vector3.Right - normal * Vector3.Right.Dot(normal)).Normalized();
            foreach (var offset in new[] { normal, (normal + tangent * 8).Normalized() })
            {
                var start = VehicleBody.ToCore(point);
                var end = VehicleBody.ToCore(point - offset * 4);
                var round = new MissileState(9000, 1, start, (end - start) * 60, 1799)
                { Arc = new(start + N.Vector3.UnitY, end, 1, 60, 1, Host.World.GetVehicle(1).LifeId) };
                Check(_arenas[0].Driver.CollideMissile!(round, end) is <= .01f,
                    $"Production ground surface start missed at {point}, direction {offset}", false);
                samples++;
            }
        }
        Check(samples > 300, $"{samples} production terrain surface-start contacts at steep/shallow angles");
        map.QueueFree(); await Frames(3);
    }

    private async Task MarkerDimensions()
    {
        var marker = new SalvoMarker(); _arenas[0].AddChild(marker);
        var local = Host.World.GetVehicle(1);
        var slot = new ItemSlot(1, local.LifeId, 9000, HeldItem.Salvo);
        var tuning = Host.Items.Configuration;
        void Refresh(ItemConfiguration configuration) => marker.Refresh(1, local, slot, null, configuration, true,
            p => (new Vector3(p.X, 100, p.Z), Vector3.Up));
        Refresh(tuning);
        var original = marker.SurfaceVertices.ToArray();
        var center = original.Aggregate(Vector3.Zero, (sum, p) => sum + p) / original.Length;
        float radius = original.Average(p => new Vector2(p.X - center.X, p.Z - center.Z).Length());
        Check(Math.Abs(radius - 5) < .001 && marker.RingCount == 1, "Original 5 m aiming-circle radius restored with 0.5 m ring width");
        Refresh(tuning with { SalvoBlastRadius = 30 });
        Check(marker.SurfaceVertices.SequenceEqual(original), "DevTools damage-radius changes do not resize or move the aiming circle");
        Refresh(tuning with { SalvoMarkerScale = 1 });
        Check(marker.SurfaceVertices.Average(p => new Vector2(p.X - center.X, p.Z - center.Z).Length()) > 9.99,
            "Independent marker-scale control retains its range");
        Refresh(tuning);
        Focus(center, new(14, 20, 20)); await Capture("09-original-aim-circle");
        marker.QueueFree(); await Frames(2);
    }

    private async Task TerrainVolleys()
    {
        var priorPositions = _positions.ToDictionary();
        var native = _arenas[0].Driver.CollideMissile!;
        var launched = new Dictionary<ulong, (ulong Owner, SalvoFlight Arc)>();
        var contacts = new Dictionary<ulong, N.Vector3>();
        _arenas[0].Driver.CollideMissile = (round, end) =>
        {
            if (round.Arc is { } arc) { launched.TryAdd(round.Id, (round.Owner, arc)); }
            var fraction = native(round, end);
            if (fraction is float f) { contacts.TryAdd(round.Id, N.Vector3.Lerp(round.Position, end, f)); }
            return fraction;
        };
        foreach (var gateway in _gateways) { gateway.ConfigureSimulation(new NetworkSimulation(150, 25, 3, 0, 0)); }
        foreach (var (terrain, speed, pitch, bearing) in new[] {
            (0, 150f, -.03f, 0f), (0, 200f, -.85f, .8f),
            (1, 20f, -.08f, -.6f), (1, 200f, -.6f, 1.4f),
            (2, 150f, -.04f, 0f), (2, 200f, -.8f, -1.5f) })
        {
            string name = $"terrain-{terrain}-speed-{speed}";
            float Height(float x, float z) => 100 + (terrain == 1 ? .18f * (z - 500) : terrain == 2 ?
                2 * MathF.Sin((x - 500) / 9) + 1.7f * MathF.Sin((z - 500) / 7) : 0);
            var ground = new List<Node3D>();
            foreach (var arena in _arenas)
            {
                var mesh = new ArrayMesh(); var vertices = new List<Vector3>();
                for (int x = 350; x < 650; x += 5)
                for (int z = 350; z < 650; z += 5)
                {
                    Vector3 P(int a, int b) => new(a, Height(a, b), b);
                    vertices.AddRange([P(x,z), P(x+5,z), P(x+5,z+5), P(x,z), P(x+5,z+5), P(x,z+5)]);
                }
                var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                var body = new StaticBody3D { CollisionLayer = 1 };
                body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
                body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = new StandardMaterial3D {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new(.3f, .34f, .28f) } });
                arena.AddChild(body); ground.Add(body);
            }
            foreach (ulong owner in new ulong[] { 1, 2, 3 })
            { float x = 490 + (owner - 1) * 20; _positions[owner] = new(x, Height(x, 550) + 1.2f, 550); }
            _terrainDirection = new(MathF.Sin(bearing) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(bearing) * MathF.Cos(pitch));
            Check(Host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_speed"] = speed }, out _), name + " speed configured");
            foreach (ulong owner in new ulong[] { 1, 3 })
            { Host.Items.RemovePlayer(owner); Check(Host.Items.Grant(Host.World, owner, HeldItem.Salvo), name + " full magazine"); }
            await Until(() => new[] { 0, 2 }.All(i => _arenas[i].Driver.LocalItem is { } slot &&
                _arenas[i].Driver.SalvoReady?.Invoke(slot) == true && Host.Items.SalvoReady(Host.Items.Slots.Single(s => s.Vehicle == slot.Vehicle))), name + " catapults ready", 1000);
            await Frames(90);
            launched.Clear(); contacts.Clear();
            int[] before = _impacts.Select(e => e.Count).ToArray();
            Focus(new(510, 104, 515), new(40, 25, 60));
            var timings = new List<double>(); _times = timings;
            Check(_arenas[0].Driver.RequestItemUse() && _arenas[2].Driver.RequestItemUse(), name + " concurrent five-round activation");
            await Until(() => _impacts.Select((e,i) => e.Count == before[i] + 10).All(v => v), name + " all ten impacts replicated", 1300);
            _times = null;
            Check(launched.Count == 10 && launched.Values.GroupBy(v => v.Owner).All(g => g.Count() == 5) && contacts.Count == 10,
                name + " each of ten launched rounds hit native ground, without lifetime fallback");
            for (int i = 0; i < 3; i++)
            {
                var outcomes = _impacts[i].Skip(before[i]).ToArray();
                Check(outcomes.Select(e => e.Token).ToHashSet().SetEquals(launched.Keys) && outcomes.Length == 10 &&
                    outcomes.All(e => N.Vector3.Distance(e.Position, contacts[e.Token]) < .001f), name + " unique matching outcomes peer " + i);
                var effects = Descendants(_arenas[i]).OfType<SalvoExplosion>().Where(e => launched.ContainsKey(e.Seed) && !e.IsQueuedForDeletion()).ToArray();
                Check(effects.Length == 10 && effects.Select(e => e.Seed).Distinct().Count() == 10,
                    name + " one live visual explosion for every authoritative impact peer " + i);
            }
            Check(Host.Items.Configuration.SalvoBlastRadius == .9144f, name + " enlarged VFX retain three-foot gameplay radius");
            await Capture("10-" + name); await Capture("10-remote-" + name, 2);
            Timing(name + " normal concurrent volleys, 150 ms / 25 ms jitter / 3% loss", timings);
            foreach (var body in ground) { body.QueueFree(); }
            await Frames(3);
        }
        _terrainDirection = null;
        // Reconstruct five off-map flights at their final original lifetime step.
        // The Core suite separately runs all 1,800 ticks from normal launches.
        int[] expiryBefore = _impacts.Select(e => e.Count).ToArray();
        ulong token = Host.Items.TokenHighWater;
        var expired = Enumerable.Range(0, 5).Select(i =>
        {
            var arc = new SalvoFlight(new(5000, 200, 5000 + i * 20), new(5000, 0, 5000 + i * 20), 12, 60, 1799, Host.World.GetVehicle(1).LifeId);
            return new MissileState(++token, 1, arc.At(1799), arc.VelocityAt(1799), 1) { Arc = arc };
        }).ToArray();
        var items = Host.Items;
        items.Restore(new(items.Revision + 1, Host.Snapshot(), items.Slots, expired, [], Host.Spawns?.States, items.Patches,
            items.OilContacts, Host.Spawns?.Balances, items.Mines, items.Shields, items.DiscardRevision, items.EmpAttachments), items.Revision + 1, token);
        await Until(() => _impacts.Select((events, i) => events.Count == expiryBefore[i] + 5).All(v => v), "Five off-map lifetime detonations converge after checkpoint continuation");
        await Frames(30);
        for (int i = 0; i < 3; i++)
        {
            var outcomes = _impacts[i].Skip(expiryBefore[i]).ToArray();
            Check(outcomes.Length == 5 && expired.All(round => outcomes.Count(e => e.Token == round.Id && e.Position == round.Arc!.At(1800)) == 1),
                "Original 30-second deadline detonates once per round at its final flight endpoint, peer " + i);
            Check(Descendants(_arenas[i]).OfType<SalvoExplosion>().Count(e => expired.Any(round => round.Id == e.Seed)) == 5,
                "Every off-map fallback also creates one effect, peer " + i);
        }
        _arenas[0].Driver.CollideMissile = native;
        foreach (var pair in priorPositions) { _positions[pair.Key] = pair.Value; }
        Check(Host.TryConfigure(0, new Dictionary<string, double> { ["items.salvo_speed"] = 150 }, out _), "Production launch speed restored after terrain fixtures");
        await Frames(3);
    }
}
