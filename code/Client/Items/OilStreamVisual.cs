using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Five interwoven liquid strands over bounded authoritative flight; no extra projectiles.</summary>
internal sealed partial class OilStreamVisual : MeshInstance3D
{
    private readonly ArrayMesh _mesh = new();
    private readonly List<Vector3> _vertices = new(36000);
    private readonly List<Vector3> _normals = new(36000);
    private readonly List<Vector2> _uvs = new(36000);
    private readonly List<(Vector3 Point, float Age)> _points = new(50);
    private readonly List<(Vector3 Point, float Age)> _curve = new(150);
    private const int Sides = 8;
    private readonly (Vector3 Point, Vector3 Normal, Vector2 Uv)[] _rings = new (Vector3, Vector3, Vector2)[150 * 9];

    internal OilStreamVisual(Material material)
    {
        Mesh = _mesh; MaterialOverride = material;
        CastShadow = ShadowCastingSetting.Off;
    }

    internal void Present(OilPour? pour, float age, Vector3? mouth, Vector3 sourceCorrection, float width, double clock, Vector3? impact)
    {
        if (pour is null || pour.Drops.Length == 0) { Visible = false; return; }
        _mesh.ClearSurfaces(); _vertices.Clear(); _normals.Clear(); _uvs.Clear(); _points.Clear();
        if (mouth.HasValue) { _points.Add((mouth.Value, 0)); }
        OilDrop? previous = null;
        foreach (var drop in pour.Drops.Drops.Reverse())
        {
            Vector3 point = VehicleBody.ToGodot(drop.Position + drop.Velocity * age) - Vector3.Up * (pour.Gravity * age * age * .5f);
            float flightSeconds = (180 - drop.RemainingTicks) / 60f + age;
            point += sourceCorrection.LimitLength(8) * MathF.Exp(-Math.Max(0, flightSeconds - .18f) * 2.5f);
            // Fractional cadence rounds emission ticks; a larger hole is an actual retired parcel.
            if (previous is not null && previous.RemainingTicks - drop.RemainingTicks > MathF.Ceiling(pour.IntervalTicks))
            { Build(width, clock); _points.Clear(); }
            _points.Add((point, flightSeconds));
            previous = drop;
        }
        if (impact.HasValue && _points.Count > 0 && _points[^1].Point.DistanceTo(impact.Value) < 1.7f)
        { _points.Add((impact.Value, _points[^1].Age)); }
        Build(width, clock);
        Visible = _vertices.Count > 0;
        if (!Visible) { return; }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs.ToArray();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }

    private void Build(float width, double clock)
    {
        if (_points.Count < 2) { return; }
        _curve.Clear();
        for (int i = 0; i < _points.Count - 1; i++)
        {
            var a = _points[i]; var b = _points[i + 1];
            for (int j = 0; j < 3; j++)
            {
                float t = j / 3f;
                _curve.Add((a.Point.CubicInterpolate(b.Point, _points[Math.Max(0, i - 1)].Point, _points[Math.Min(_points.Count - 1, i + 2)].Point, t), Mathf.Lerp(a.Age, b.Age, t)));
            }
        }
        _curve.Add(_points[^1]);
        void Vertex((Vector3 Point, Vector3 Normal, Vector2 Uv) v) { _vertices.Add(v.Point); _normals.Add(v.Normal); _uvs.Add(v.Uv); }
        for (int strand = 0; strand < 5; strand++)
        {
            (Vector3 Point, Vector3 Normal, Vector2 Uv) Ring(int index, int side)
            {
                Vector3 center = _curve[index].Point;
                int before = Math.Max(0, index - 1), after = Math.Min(index + 1, _curve.Count - 1);
                Vector3 direction = (_curve[after].Point - _curve[before].Point).Normalized();
                if (direction.LengthSquared() < .5f) { direction = Vector3.Down; }
                Vector3 right = direction.Cross(Math.Abs(direction.Y) < .9f ? Vector3.Up : Vector3.Right).Normalized();
                Vector3 up = right.Cross(direction);
                float age = _curve[index].Age;
                float flow = age * 37 - (float)clock * 23 + strand * 2.399f;
                float turn = strand * Mathf.Tau / 5 + age * 9 + .35f * MathF.Sin(flow * .45f);
                float spread = width * (.19f + Math.Min(age * .75f, .75f));
                center += (right * MathF.Cos(turn) + up * MathF.Sin(turn)) * spread;
                center += (right * MathF.Sin(flow) + up * MathF.Cos(flow * .81f)) * width * .21f * Math.Min(age * 8, 1);
                // Soft tips and liquid necks replace cut ends; three inner strands
                // keep the burst cohesive while the outer two tear into irregular lobes.
                float tips = Math.Min(1, Math.Min(index / 2f, (_curve.Count - 1 - index) / 4f));
                float neck = .76f + .38f * MathF.Sin(flow) + .13f * MathF.Sin(flow * 1.71f);
                if (age > .12f) { neck *= .16f + .84f * Mathf.SmoothStep(-.95f, .1f, MathF.Sin(flow * .43f + strand)); }
                if (strand >= 3 && age > .18f) { neck *= Mathf.SmoothStep(-.7f, .15f, MathF.Sin(flow * .58f)); }
                float radius = width * .30f * (1 + Math.Min(age, .8f)) * neck * tips;
                float angle = side * Mathf.Tau / Sides;
                Vector3 normal = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                return (center + normal * radius, normal, new Vector2(side / (float)Sides, age * 5 + strand));
            }
            for (int i = 0; i < _curve.Count; i++)
            { for (int side = 0; side <= Sides; side++) { _rings[i * 9 + side] = Ring(i, side); } }
            for (int i = 0; i < _curve.Count - 1; i++)
            {
                for (int side = 0; side < Sides; side++)
                {
                    var a = _rings[i * 9 + side]; var b = _rings[i * 9 + side + 1]; var c = _rings[(i + 1) * 9 + side]; var d = _rings[(i + 1) * 9 + side + 1];
                    Vertex(a); Vertex(b); Vertex(c); Vertex(b); Vertex(d); Vertex(c);
                }
            }
        }
    }

    public override void _ExitTree() { Mesh = null; _mesh.Dispose(); }
}
