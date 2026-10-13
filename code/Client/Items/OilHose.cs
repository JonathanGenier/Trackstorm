using Godot;

namespace Trackstorm.Client.Items;

/// <summary>A fixed-budget ribbed rubber conduit; four attachment controls never constrain head motion.</summary>
internal sealed partial class OilHose : MeshInstance3D
{
    private const int Rings = 96;
    private const int Sides = 8;
    private readonly ArrayMesh _mesh = new();
    private readonly Vector3[] _vertices = new Vector3[Rings * Sides * 6];
    private readonly Vector3[] _normals = new Vector3[Rings * Sides * 6];
    private readonly Color[] _colors = new Color[Rings * Sides * 6];
    private readonly Vector3[] _ringPoints = new Vector3[(Rings + 1) * Sides];
    private readonly Vector3[] _ringNormals = new Vector3[(Rings + 1) * Sides];
    private readonly Godot.Collections.Array _arrays = new();
    private readonly StandardMaterial3D _material = new() { VertexColorUseAsAlbedo = true, Roughness = .64f, Metallic = .16f };
    internal Vector3 End { get; private set; }

    internal OilHose()
    {
        Mesh = _mesh; MaterialOverride = _material; _arrays.Resize((int)Mesh.ArrayType.Max);
    }

    internal void Present(Vector3 start, Vector3 control1, Vector3 control2, Vector3 end)
    {
        End = end;
        Vector3 Point(float t) => start.BezierInterpolate(control1, control2, end, t);
        for (int ring = 0; ring <= Rings; ring++)
        {
            float t = ring / (float)Rings;
            Vector3 center = Point(t), direction = (Point(Math.Min(1, t + .002f)) - Point(Math.Max(0, t - .002f))).Normalized();
            Vector3 right = direction.Cross(Math.Abs(direction.Z) < .9f ? Vector3.Back : Vector3.Up).Normalized();
            Vector3 up = right.Cross(direction);
            // Four samples per bellows give visible shoulders and recessed roots,
            // instead of a tiny zigzag whose smooth normals hid the old ribs.
            float radius = ring % 4 is 1 or 2 ? .040f : .027f;
            float slope = ring % 4 is 0 or 1 ? -.42f : .42f;
            for (int side = 0; side < Sides; side++)
            {
                float angle = side * Mathf.Tau / Sides;
                Vector3 normal = right * MathF.Cos(angle) + up * MathF.Sin(angle);
                _ringPoints[ring * Sides + side] = center + normal * radius;
                _ringNormals[ring * Sides + side] = (normal + direction * slope).Normalized();
            }
        }
        int vertex = 0;
        void Add(int ring, int side)
        {
            int index = ring * Sides + side % Sides;
            _vertices[vertex] = _ringPoints[index]; _normals[vertex] = _ringNormals[index];
            // Stable authored-space grime, oxidized reinforcement and rubbed rib
            // edges move with the conduit; no texture swimming or new particles.
            float grain = .5f + .5f * MathF.Sin(ring * 2.399f + side * 7.17f);
            float worn = Math.Max(0, MathF.Sin(ring * .37f + side * 1.4f)) * (ring % 4 is 1 or 2 ? .85f : .08f);
            _colors[vertex++] = new Color(.025f + .055f * grain, .022f + .044f * grain, .017f + .029f * grain)
                .Lerp(new Color(.25f, .15f, .055f), worn);
        }
        for (int ring = 0; ring < Rings; ring++)
        {
            for (int side = 0; side < Sides; side++)
            { Add(ring, side); Add(ring + 1, side); Add(ring, side + 1); Add(ring, side + 1); Add(ring + 1, side); Add(ring + 1, side + 1); }
        }
        _mesh.ClearSurfaces();
        _arrays[(int)Godot.Mesh.ArrayType.Vertex] = _vertices; _arrays[(int)Godot.Mesh.ArrayType.Normal] = _normals;
        _arrays[(int)Godot.Mesh.ArrayType.Color] = _colors;
        _mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, _arrays);
    }

    public override void _ExitTree() { Mesh = null; _arrays.Dispose(); _mesh.Dispose(); _material.Dispose(); }
}
