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
        var vertices = new List<Vector3>(2016); var normals = new List<Vector3>(2016); var uvs = new List<Vector2>(2016);
        void Add((Vector3 Position, Vector3 Normal) p) { vertices.Add(p.Position); normals.Add(p.Normal); uvs.Add(new Vector2(p.Position.X, p.Position.Z)); }
        for (int petal = 0; petal < 7; petal++)
        {
            float angle = petal * 2.399963f + _seed;
            float reach = _radius * Math.Min(1, _age * 4.5f) * (.58f + .26f * MathF.Sin(petal * 2.399f + _seed));
            float height = Math.Max(0, _age * 2.6f - _age * _age * 6) * (.65f + .35f * MathF.Sin(petal * 1.7f));
            (Vector3 Position, Vector3 Normal) Point(float t, int side)
            {
                float a = angle + .16f * MathF.Sin(petal * 7 + t * 4);
                Vector3 radial = new(MathF.Cos(a), 0, MathF.Sin(a));
                Vector3 across = new(-radial.Z, 0, radial.X);
                float ring = side * Mathf.Tau / 8;
                Vector3 normal = across * MathF.Cos(ring) + Vector3.Up * MathF.Sin(ring);
                float radius = (.035f + _radius * .045f) * MathF.Sqrt(Math.Max(0, MathF.Sin(t * Mathf.Pi))) *
                    (.8f + .2f * MathF.Sin(t * 9 + petal)) * (1 - _age / .48f);
                Vector3 center = radial * reach * t + Vector3.Up * (height * MathF.Sin(t * 2.9f) + radius);
                // Rounded, bending liquid fingers collapse into the supported
                // patch; no rigid two-triangle wedge is exposed from the side.
                return (center + normal * radius, normal);
            }
            for (int part = 0; part < 6; part++)
            {
                float t = part / 6f, next = (part + 1) / 6f;
                for (int side = 0; side < 8; side++)
                {
                    var a = Point(t, side); var b = Point(t, side + 1); var c = Point(next, side); var d = Point(next, side + 1);
                    Add(a); Add(b); Add(c); Add(b); Add(d); Add(c);
                }
            }
        }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray(); arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
    public override void _ExitTree() { Mesh = null; _mesh.Dispose(); }
}
