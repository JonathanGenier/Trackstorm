using Godot;

namespace Trackstorm.Client.Items;

/// <summary>A short, surface-oriented crown and outward splash; owned by one Oil hazard.</summary>
internal sealed partial class OilImpactVisual : MeshInstance3D
{
    private readonly ArrayMesh _mesh = new();
    private float _age;
    private readonly float _radius;
    private readonly float _seed;
    internal OilImpactVisual(Vector3 position, Vector3 normal, float radius, Material material)
    {
        Position = position + normal * .018f;
        Quaternion = new Quaternion(Vector3.Up, normal);
        _radius = radius; _seed = position.X + position.Z;
        Mesh = _mesh; MaterialOverride = material; CastShadow = ShadowCastingSetting.Off;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age > .48f) { QueueFree(); return; }
        _mesh.ClearSurfaces();
        var vertices = new List<Vector3>(384); var normals = new List<Vector3>(384); var uvs = new List<Vector2>(384);
        void Add(Vector3 p) { vertices.Add(p); normals.Add(new Vector3(p.X * .15f, 1, p.Z * .15f).Normalized()); uvs.Add(new Vector2(p.X, p.Z)); }
        for (int petal = 0; petal < 9; petal++)
        {
            float angle = petal * Mathf.Tau / 9 + _seed;
            float reach = _radius * Math.Min(1, _age * 4) * (.72f + .24f * MathF.Sin(petal * 2.399f + _seed));
            float height = Math.Max(0, _age * 3.3f - _age * _age * 7) * (.65f + .35f * MathF.Sin(petal * 1.7f));
            Vector3 Point(float t, float side)
            {
                float a = angle + side * .28f * (1 - t * .52f) + .04f * MathF.Sin(petal * 7 + t * 11);
                return new Vector3(MathF.Cos(a) * reach * t, height * MathF.Sin(t * 3.0f) * (1 + .18f * side), MathF.Sin(a) * reach * t);
            }
            for (int part = 0; part < 3; part++)
            {
                float t = part / 3f, next = (part + 1) / 3f;
                var a = Point(t, -1); var b = Point(t, 1); var c = Point(next, -1); var d = Point(next, 1);
                Add(a); Add(b); Add(c); Add(b); Add(d); Add(c);
            }
        }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray(); arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
    public override void _ExitTree() { Mesh = null; _mesh.Dispose(); }
}
