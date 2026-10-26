using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Shared, bounded intact-pickup ribbon; cosmetic and independent of item authority.</summary>
internal static class CarnivalPickupAura
{
    internal const string ShaderPath = "res://assets/items/carnival-crate/PickupAura.gdshader";

    internal static ArrayMesh CreateMesh()
    {
        const int segments = 128;
        var vertices = new Vector3[(segments + 1) * 2];
        var uvs = new Vector2[vertices.Length];
        var indices = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.Tau * 1.5f;
            for (int edge = 0; edge < 2; edge++)
            {
                vertices[i * 2 + edge] = new Vector3(MathF.Cos(angle) * 1.68f, 0.3f + t * 2.45f + (edge - 0.5f) * 0.045f, MathF.Sin(angle) * 1.02f);
                uvs[i * 2 + edge] = new Vector2(t, edge);
            }
            if (i == segments) continue;
            int v = i * 2;
            int k = i * 6;
            indices[k] = v; indices[k + 1] = v + 1; indices[k + 2] = v + 2;
            indices[k + 3] = v + 1; indices[k + 4] = v + 3; indices[k + 5] = v + 2;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(ShaderPath) });
        return mesh;
    }
}
