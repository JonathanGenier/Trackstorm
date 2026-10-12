using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    // Presentation-only native surface probes; the marker suite separately exercises
    // selection, input, actual vehicle state and lifecycle through the arena owner.
    private async Task CheckMarkerSurfaces()
    {
        var arena = _arenas[1];
        var probe = new AssistedTargetIndicator(); arena.AddChild(probe);
        var camera = new Camera3D { Fov = 65 }; arena.AddChild(camera); camera.MakeCurrent();
        foreach (float degrees in new[] { 0f, 20f, 35f, 45f })
        {
            var slope = new StaticBody3D { Position = new(0, 40, 0), Rotation = new(0, 0, Mathf.DegToRad(degrees)), CollisionLayer = 1 };
            slope.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(18, .5f, 18) } });
            slope.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(18, .5f, 18) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.16f, .19f, .22f) } });
            arena.AddChild(slope); await Frames(2);
            Vector3 normal = slope.GlobalBasis.Y;
            Vector3 surface = slope.GlobalPosition + normal * .25f;
            var pose = new Transform3D(slope.GlobalBasis, surface + normal * VehicleDimensions.RideHeight);
            camera.Position = surface + normal * 8 + slope.GlobalBasis.X * 6 + Vector3.Back * 10; camera.LookAt(surface);
            probe.Present(1, pose, camera, true, true);
            Require(probe.RingVisible, $"Supported {degrees}-degree bank must show the footprint");
            CheckSurface($"bank-{degrees}"); await Capture($"surface-bank-{degrees}");
            probe.Present(1, pose.Translated(Vector3.Up * 6), camera, true, false);
            Require(!probe.RingVisible && probe.ChevronVisible, "Airborne state hides the ring even above a bank");
            probe.Clear(); slope.Free(); await Frames(2);
        }
        // A curved bank-to-flat transition defeats a single tangent-plane projection.
        var vertices = new List<Vector3>();
        for (int x = -24; x < 24; x++)
        for (int z = -24; z < 24; z++)
        {
            Vector3 a = Point(x, z), b = Point(x + 1, z), c = Point(x, z + 1), d = Point(x + 1, z + 1);
            vertices.AddRange([a, c, b, b, c, d]);
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var uneven = new StaticBody3D { CollisionLayer = 1 };
        uneven.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = vertices.ToArray(), BackfaceCollision = true } });
        uneven.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.2f, .24f, .16f), CullMode = BaseMaterial3D.CullModeEnum.Disabled } });
        arena.AddChild(uneven); await Frames(2);
        for (int i = 0; i <= 8; i++)
        {
            Vector3 contact = Point(i - 4, 0);
            var pose = new Transform3D(Basis.Identity, contact + Vector3.Up * VehicleDimensions.RideHeight);
            camera.Position = contact + new Vector3(8, 6, 10); camera.LookAt(contact);
            probe.Present(1, pose, camera, i % 2 == 0, true);
            Require(probe.RingVisible && probe.SurfacePoints.Count >= 160, "Moving footprint survives curved elevation and cross-slope changes");
            CheckSurface($"curve-{i}");
            if (i is 0 or 4 or 8) { await Capture($"surface-curve-{i}"); }
        }
        probe.Clear(); uneven.Free(); await Frames(2);
        if (_oval)
        {
            var locations = new List<Vector3> { new(195, 30, 0), new(-195, 30, 0), new(-95, 30, 48) };
            for (int step = 0; step < 9; step++)
            {
                float angle = Mathf.DegToRad(-40 + step * 10);
                locations.Add(new(107 + 88 * MathF.Cos(angle), 30, 88 * MathF.Sin(angle)));
            }
            foreach (Vector3 location in locations)
            {
                var hit = Ground(location, 60); Require(hit.HasValue, "Production bank/roller support exists");
                var (point, normal) = hit!.Value;
                Vector3 forward = Vector3.Forward.Slide(normal).Normalized();
                var pose = new Transform3D(new Basis(forward.Cross(normal), normal, -forward), point + normal * VehicleDimensions.RideHeight);
                camera.Position = point + normal * 8 + pose.Basis.X * 6 + pose.Basis.Z * 10; camera.LookAt(point);
                probe.Present(1, pose, camera, true, true);
                Require(probe.RingVisible, "Actual production map support retains the ring");
                CheckSurface($"production-{location.X:F0}-{location.Z:F0}"); await Frames(1);
                if (location.Z is 0 or 48) { await Capture($"surface-production-{location.X:F0}"); }
            }
        }
        probe.QueueFree(); camera.QueueFree(); Camera.MakeCurrent();

        static Vector3 Point(int x, int z)
        {
            float px = x * .25f, pz = z * .25f;
            return new(px, 40 + .12f * px * px + .35f * MathF.Sin(pz), pz);
        }
        (Vector3 Point, Vector3 Normal)? Ground(Vector3 from, float distance)
        {
            using var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * distance, 1);
            using var hit = arena.GetWorld3D().DirectSpaceState.IntersectRay(query);
            return hit.Count == 0 ? null : (hit["position"].AsVector3(), hit["normal"].AsVector3());
        }
        void CheckSurface(string name)
        {
            float maximum = 0;
            foreach (Vector3 point in probe.SurfacePoints)
            {
                var hit = Ground(point + Vector3.Up * .15f, .4f);
                RequireShot(hit.HasValue, "Every emitted surface point has local support");
                maximum = Math.Max(maximum, Math.Abs(point.Y - hit!.Value.Point.Y));
            }
            GD.Print($"MARKER_SURFACE name={name} vertices={probe.SurfacePoints.Count} maxVerticalGap={maximum:F4}m");
            Require(probe.SurfacePoints.Count >= 160 && maximum < .08f, "Ground geometry conforms across the whole ring, not just its centre");
        }
    }
}
