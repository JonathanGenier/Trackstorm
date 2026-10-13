using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>A bounded liquid skin joining consecutive authoritative ballistic samples; no extra collision or emission.</summary>
internal sealed partial class OilStreamVisual : MeshInstance3D
{
    private readonly ArrayMesh _mesh = new();
    private readonly List<Vector3> _vertices = new(15000);
    private readonly List<Vector3> _normals = new(15000);
    private readonly List<Vector2> _uvs = new(15000);
    private readonly List<(Vector3 Point, float Age)> _points = new(146);
    private const int Sides = 10;

    internal OilStreamVisual(Material material)
    {
        Mesh = _mesh; MaterialOverride = material;
        CastShadow = ShadowCastingSetting.Off;
    }

    internal void Present(OilPour? pour, float age, Vector3? mouth, Vector3 sourceCorrection, float width, double clock, Vector3? impact)
    {
        if (pour is null || pour.Drops.Length == 0) { Visible = false; return; }
        _mesh.ClearSurfaces(); _vertices.Clear(); _normals.Clear(); _uvs.Clear(); _points.Clear();
        Visible = true;
        // Missing parcel ordinals split the skin. It never bridges a collided gap.
        if (mouth.HasValue) { _points.Add((mouth.Value, 0)); }
        OilDrop? previous = null;
        foreach (var drop in pour.Drops.Drops.Reverse())
        {
            Vector3 point = VehicleBody.ToGodot(drop.Position + drop.Velocity * age) - Vector3.Up * (pour.Gravity * age * age * .5f);
            float flightSeconds = (180 - drop.RemainingTicks) / 60f + age;
            // Vehicle models have their own presentation smoothing. Join that rendered
            // socket near the mouth, fading to the exact world flight and impact path.
            point += sourceCorrection.LimitLength(8) * MathF.Exp(-Math.Max(0, flightSeconds - .18f) * 2.5f);
            if (previous is not null && previous.RemainingTicks - drop.RemainingTicks != pour.IntervalTicks)
            { Build(width, clock); _points.Clear(); }
            if (_points.Count > 0)
            {
                var last = _points[^1];
                for (int i = 1; i <= 3; i++) { _points.Add((last.Point.Lerp(point, i / 3f), Mathf.Lerp(last.Age, flightSeconds, i / 3f))); }
            }
            else { _points.Add((point, flightSeconds)); }
            previous = drop;
        }
        if (impact.HasValue && _points.Count > 0 && _points[^1].Point.DistanceTo(impact.Value) < 1.7f)
        { _points.Add((impact.Value, _points[^1].Age)); }
        Build(width, clock);
        if (_vertices.Count == 0) { Visible = false; return; }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs.ToArray();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }

    private void Build(float width, double clock)
    {
        if (_points.Count < 2) { return; }
        (Vector3 Point, Vector3 Normal, Vector2 Uv) Ring(int index, int side)
        {
            Vector3 direction = (_points[Math.Min(index + 1, _points.Count - 1)].Point - _points[Math.Max(0, index - 1)].Point).Normalized();
            if (direction.LengthSquared() < .5f) { direction = Vector3.Down; }
            Vector3 right = direction.Cross(Math.Abs(direction.Y) < .9f ? Vector3.Up : Vector3.Right).Normalized();
            Vector3 up = right.Cross(direction);
            float angle = side * Mathf.Tau / Sides;
            Vector3 normal = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            float age = _points[index].Age;
            float radius = width * .5f * (1 + Math.Min(age * 1.3f, .9f));
            float flow = age * 28 - (float)clock * 22;
            radius *= 1 + .28f * MathF.Sin(flow + angle * 3) + .15f * MathF.Sin(age * 49 + angle * 5);
            Vector3 ripple = (right * MathF.Sin(flow) + up * MathF.Cos(flow * .7f)) * (width * .12f * Math.Min(age * 5, 1));
            return (_points[index].Point + ripple + normal * radius, (normal + direction * (.18f * MathF.Cos(flow + angle * 3))).Normalized(), new(side / (float)Sides, age * 6));
        }
        void Vertex((Vector3 Point, Vector3 Normal, Vector2 Uv) v) { _vertices.Add(v.Point); _normals.Add(v.Normal); _uvs.Add(v.Uv); }
        for (int i = 0; i < _points.Count - 1; i++)
        {
            for (int side = 0; side < Sides; side++)
            {
                var a = Ring(i, side); var b = Ring(i, side + 1); var c = Ring(i + 1, side); var d = Ring(i + 1, side + 1);
                Vertex(a); Vertex(b); Vertex(c); Vertex(b); Vertex(d); Vertex(c);
            }
        }
        // Thin torn sheets peel from the coherent core. They share its ballistic
        // samples and bounded mesh, rather than multiplying simulated droplets.
        for (int strand = 0; strand < 5; strand++)
        {
            for (int i = 1; i < _points.Count - 1; i++)
            {
                if (_points[i].Age < .12f || MathF.Sin(_points[i].Age * 31 - (float)clock * 15 + strand * 2.4f) < -.65f) { continue; }
                (Vector3 Point, Vector3 Normal, Vector2 Uv) Edge(int index, int edge)
                {
                    var ring = Ring(index, strand * 2);
                    float age = _points[index].Age;
                    float wave = MathF.Sin(age * 23 - (float)clock * 17 + strand * 2.4f);
                    float spread = width * Math.Min(age * 2, 1.5f) * (.6f + .3f * wave);
                    Vector3 side = ring.Normal.Cross(Vector3.Down).Normalized();
                    return (ring.Point + ring.Normal * spread + side * (edge * width * (.05f + .025f * wave)), ring.Normal, ring.Uv);
                }
                var a = Edge(i, -1); var b = Edge(i, 1); var c = Edge(i + 1, -1); var d = Edge(i + 1, 1);
                Vertex(a); Vertex(b); Vertex(c); Vertex(b); Vertex(d); Vertex(c);
            }
        }
    }

    public override void _ExitTree() { Mesh = null; _mesh.Dispose(); }
}
