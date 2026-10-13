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
        // Broad merging lobes rise from a common wet skirt, then flatten into
        // the supported footprint. No long arched fingers or hooked terminal tips.
        const int Sectors = 48;
        const int Rings = 4;
        (Vector3 Position, Vector3 Normal) Point(float t, int sector)
        {
            float angle = sector * Mathf.Tau / Sectors;
            float lobe = .5f + .5f * MathF.Sin(angle * 7 + _seed);
            float irregular = .72f + .16f * MathF.Sin(angle * 3 + _seed * .7f) + .10f * lobe;
            float reach = _radius * Math.Min(1, _age * 5) * irregular;
            float height = Math.Max(0, _age * 2.1f - _age * _age * 5.8f);
            float crest = MathF.Sin(t * Mathf.Pi) * (.32f + .68f * lobe);
            Vector3 radial = new(MathF.Cos(angle), 0, MathF.Sin(angle));
            Vector3 position = radial * reach * t + Vector3.Up * (.008f + height * crest);
            Vector3 normal = (Vector3.Up - radial * (height * Mathf.Pi * MathF.Cos(t * Mathf.Pi) /
                Math.Max(.03f, reach))).Normalized();
            return (position, normal);
        }
        for (int ring = 0; ring < Rings; ring++)
        {
            float t = ring / (float)Rings, next = (ring + 1) / (float)Rings;
            for (int sector = 0; sector < Sectors; sector++)
            {
                var a = Point(t, sector); var b = Point(t, sector + 1);
                var c = Point(next, sector); var d = Point(next, sector + 1);
                Add(a); Add(b); Add(c); Add(b); Add(d); Add(c);
            }
        }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray(); arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
    public override void _ExitTree() { Mesh = null; _mesh.Dispose(); }
}
