using System.Text.Json;
using Godot;

namespace Trackstorm.Client.Verification;

public sealed partial class OvalIntegrationChecks
{
    private async Task VerifyTerrainBake(MeshInstance3D terrain, ConcavePolygonShape3D baked)
    {
        using var audit = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/maps/infield/collision-audit.json"));
        foreach (var pair in new[] { ("source", "source_sha256"), ("render_source", "render_sha256"), ("collision_resource", "collision_sha256") })
        {
            string path = "res://assets/maps/infield/" + audit.RootElement.GetProperty(pair.Item1).GetString();
            Check(Godot.FileAccess.GetSha256(path) == audit.RootElement.GetProperty(pair.Item2).GetString(), "Terrain bake provenance matches " + path);
        }
        Check(baked.ResourcePath == "res://assets/maps/infield/TerrainCollision.res" && baked.GetFaces().Length / 3 <= 40500,
            "Production terrain uses the bounded baked collider, including after import-cache reuse.");
        Check(audit.RootElement.GetProperty("maximum_bidirectional_vertex_centroid_distance_m").GetDouble() < 0.04 &&
            audit.RootElement.GetProperty("maximum_boundary_distance_m").GetDouble() < 0.002, "Complete offline vertex/centroid and boundary audit meets geometry limits.");

        var body = terrain.GetChildren().OfType<StaticBody3D>().Single();
        var collider = body.GetChildren().OfType<CollisionShape3D>().Single();
        uint layer = body.CollisionLayer;
        body.CollisionLayer = 1u << 30;
        using var original = terrain.Mesh.CreateTrimeshShape();
        using var ray = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 1u << 30);
        ray.HitBackFaces = true;
        float maximum = 0;
        int samples = 0;
        try
        {
            foreach (var comparison in new[] { (terrain.Mesh.GetFaces(), baked), (baked.GetFaces(), original) })
            {
                collider.Shape = comparison.Item2;
                await Frames(2);
                Vector3[] faces = comparison.Item1;
                // Uniformly sample the entire source ordering in both directions.
                // Rim continuity is independently checked by VerifyCollision.
                for (int index = 0; index < faces.Length; index += 3 * 31)
                {
                    Vector3 point = (faces[index] + faces[index + 1] + faces[index + 2]) / 3;
                    Vector3 normal = (faces[index + 1] - faces[index]).Cross(faces[index + 2] - faces[index]).Normalized();
                    // Godot face buffers use clockwise winding; the authored
                    // height field has its collidable side above the terrain.
                    if (normal.Y < 0) { normal = -normal; }
                    ray.From = point + normal * 0.08f; ray.To = point - normal * 0.08f;
                    using var hit = body.GetWorld3D().DirectSpaceState.IntersectRay(ray);
                    if (hit.Count == 0) { Check(false, $"Terrain bake has native surface near sample {samples} at {point}, normal {normal}."); }
                    maximum = Math.Max(maximum, point.DistanceTo(hit["position"].AsVector3()));
                    samples++;
                }
            }
        }
        finally { collider.Shape = baked; body.CollisionLayer = layer; }
        await Frames(2);
        Check(maximum < 0.045f, $"Terrain bake native bidirectional normal-ray audit: {samples} centroids, maximum distance {maximum:F5} m (4 cm bake budget plus 5 mm ray/float tolerance).");
    }
}
