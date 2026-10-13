using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Irregular connected liquid and a fixed spray batch over authoritative ballistic samples.</summary>
internal sealed partial class OilStreamVisual : MeshInstance3D
{
    private readonly ArrayMesh _mesh = new();
    private readonly List<Vector3> _vertices = new(36000);
    private readonly List<Vector3> _normals = new(36000);
    private readonly List<Vector2> _uvs = new(36000);
    private readonly List<(Vector3 Point, float Age)> _points = new(50);
    private readonly List<(Vector3 Point, float Age)> _curve = new(150);
    private const int Sides = 6;
    private readonly (Vector3 Point, Vector3 Normal, Vector2 Uv)[] _rings = new (Vector3, Vector3, Vector2)[150 * 9];
    private readonly MultiMesh _spray = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D };
    private readonly SphereMesh _sprayShape = new() { Radius = 1, Height = 2, RadialSegments = 6, Rings = 3 };
    private MultiMeshInstance3D? _sprayNode;
    private readonly Material _material;
    private Transform3D? _inverseOwner;
    internal int OccludedQuads { get; private set; }

    internal OilStreamVisual(Material material)
    {
        Mesh = _mesh; MaterialOverride = material;
        _material = material;
        CastShadow = ShadowCastingSetting.Off;
    }

    public override void _Ready()
    {
        _spray.Mesh = _sprayShape; _spray.InstanceCount = 120; _spray.VisibleInstanceCount = 0;
        _sprayNode = new MultiMeshInstance3D { Multimesh = _spray, MaterialOverride = _material, CastShadow = ShadowCastingSetting.Off };
        AddChild(_sprayNode);
    }

    internal void Present(OilPour? pour, float age, Vector3? mouth, Vector3 sourceCorrection, float width, double clock, Transform3D? owner = null)
    {
        if (pour is null || pour.Drops.Length == 0) { Visible = false; return; }
        _inverseOwner = owner?.AffineInverse(); OccludedQuads = 0;
        _mesh.ClearSurfaces(); _vertices.Clear(); _normals.Clear(); _uvs.Clear(); _points.Clear();
        // Only a newly emitted parcel can connect to the mouth. Missing/retired
        // packets cannot create a long folded bridge back to the moving vehicle.
        if (mouth.HasValue && (180 - pour.Drops.Drops[^1].RemainingTicks) / 60f + age < .20f)
        { _points.Add((mouth.Value, 0)); }
        OilDrop? previous = null;
        int spray = 0;
        foreach (var drop in pour.Drops.Drops.Reverse())
        {
            Vector3 point = VehicleBody.ToGodot(drop.Position + drop.Velocity * age) - Vector3.Up * (pour.Gravity * age * age * .5f);
            float flightSeconds = (180 - drop.RemainingTicks) / 60f + age;
            point += sourceCorrection.LimitLength(3) * MathF.Exp(-flightSeconds * 5);
            if (owner is { } frame && mouth is { } outlet && flightSeconds < .45f)
            {
                // Reliable flight is sampled at 10 Hz while the car/head render
                // smoothly. A decaying translation alone can drag that old jet
                // through today's box. Reconstruct the short ejection neck from
                // the displayed mouth using the same safe ballistic rule, then
                // join accepted world flight only beyond the rear hardware.
                Vector3 localMouth = frame.AffineInverse() * outlet;
                Vector3 gravity = frame.Basis.Inverse() * (Vector3.Down * pour.Gravity);
                int ordinal = Math.Max(0, pour.Emitted - 1 - (int)((180 - drop.RemainingTicks) / pour.IntervalTicks));
                Vector3 velocity = VehicleBody.ToGodot(OilJet.Velocity(VehicleBody.ToCore(localMouth), pour.Speed,
                    VehicleBody.ToCore(gravity), ordinal, 0));
                Vector3 neck = localMouth + velocity * flightSeconds + gravity * (.5f * flightSeconds * flightSeconds);
                Vector3 accepted = frame.AffineInverse() * point;
                float join = Mathf.SmoothStep(3.10f, 4.3f, Math.Min(neck.Z, accepted.Z));
                point = frame * neck.Lerp(accepted, join);
            }
            // Fractional cadence rounds emission ticks; a larger hole is an actual retired parcel.
            if (previous is not null && (previous.RemainingTicks - drop.RemainingTicks > MathF.Ceiling(pour.IntervalTicks) ||
                (_points.Count > 0 && point.DistanceTo(_points[^1].Point) > 2)))
            { Build(width, clock); _points.Clear(); }
            _points.Add((point, flightSeconds));
            // Two to three small lobes per parcel, always inside a fixed 120-instance
            // batch. They are cosmetic breakup, never additional hits or hazards.
            for (int i = 0; i < 3 && spray < 120 && flightSeconds > .05f; i++)
            {
                float seed = drop.RemainingTicks * 2.399f + i * 17.7f;
                float spread = width * (1 + Math.Min(flightSeconds * 4, 3));
                Vector3 scatter = new(MathF.Sin(seed) * spread, MathF.Cos(seed * .77f) * spread * .65f, MathF.Sin(seed * .53f) * spread);
                float size = .011f + .029f * (.5f + .5f * MathF.Sin(seed * .37f));
                Vector3 velocity = VehicleBody.ToGodot(drop.Velocity);
                if (BehindHardware(point + scatter, size * 2.8f)) { continue; }
                var basis = new Basis(new Quaternion(Vector3.Up, velocity.Normalized())).Scaled(new(size, size * 2.8f, size));
                _spray.SetInstanceTransform(spray++, new Transform3D(basis, point + scatter));
            }
            previous = drop;
        }
        // Never append a retired impact to an extrapolated flight chain: that
        // backward connection caused the previous folded V at burst shutdown.
        Build(width, clock);
        _spray.VisibleInstanceCount = spray;
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
        Vector3 Smoothed(int i) => i == 0 || i == _points.Count - 1 ? _points[i].Point :
            (_points[i - 1].Point + _points[i].Point * 2 + _points[i + 1].Point) / 4;
        for (int i = 0; i < _points.Count - 1; i++)
        {
            var a = _points[i]; var b = _points[i + 1];
            for (int j = 0; j < 3; j++)
            {
                float t = j / 3f;
                _curve.Add((Smoothed(i).Lerp(Smoothed(i + 1), t), Mathf.Lerp(a.Age, b.Age, t)));
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
                float flow = age * (57 + strand * 11) - (float)clock * 37 + strand * 2.399f;
                float turn = strand * 2.399f + age * 5 + .45f * MathF.Sin(flow * .37f);
                float free = Mathf.SmoothStep(.06f, .20f, age);
                if (_inverseOwner is { } inverse) { free *= Mathf.SmoothStep(2.5f, 3.1f, (inverse * center).Z); }
                float neckWidth = Mathf.Lerp(Math.Min(width, .18f), width, free);
                float spread = neckWidth * (.10f + free * (.42f + .15f * MathF.Sin(flow * .31f)));
                center += (right * MathF.Cos(turn) + up * MathF.Sin(turn)) * spread;
                center += (right * MathF.Sin(flow) + up * MathF.Cos(flow * .81f)) * neckWidth * .16f * free;
                // Five distinct narrow rivulets leave one dense throat. Rounded
                // lobes, irregular necks and offset breakup avoid a dominant cord.
                float tips = MathF.Sqrt(Math.Min(1, Math.Min(index / 1.5f, (_curve.Count - 1 - index) / 1.5f)));
                float neck = .78f + .38f * MathF.Sin(flow) + .20f * MathF.Sin(flow * 1.71f);
                if (age > .1f) { neck *= .18f + .82f * Mathf.SmoothStep(-.85f, .1f, MathF.Sin(flow * .43f + strand)); }
                float radius = neckWidth * (.22f + .025f * MathF.Sin(strand * 7)) * neck * tips;
                float angle = side * Mathf.Tau / Sides;
                Vector3 normal = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                radius *= 1 + .15f * MathF.Sin(angle * 3 + flow * .71f);
                return (center + normal * radius, normal, new Vector2(side / (float)Sides, age * 5 + strand));
            }
            for (int i = 0; i < _curve.Count; i++)
            { for (int side = 0; side <= Sides; side++) { _rings[i * 9 + side] = Ring(i, side); } }
            for (int i = 0; i < _curve.Count - 1; i++)
            {
                for (int side = 0; side < Sides; side++)
                {
                    var a = _rings[i * 9 + side]; var b = _rings[i * 9 + side + 1]; var c = _rings[(i + 1) * 9 + side]; var d = _rings[(i + 1) * 9 + side + 1];
                    // During a hard spin the vehicle can sweep through older world
                    // flight. Do not draw a bridge through the current hardware;
                    // authoritative flight and the impacted terrain stay unchanged.
                    if (_inverseOwner is { } inverse)
                    {
                        var bounds = new Aabb(inverse * a.Point, Vector3.Zero).Expand(inverse * b.Point).Expand(inverse * c.Point).Expand(inverse * d.Point);
                        if (IntersectsHardware(bounds)) { OccludedQuads++; continue; }
                    }
                    Vertex(a); Vertex(b); Vertex(c); Vertex(b); Vertex(d); Vertex(c);
                }
            }
        }
    }

    // Raised box, tank/rack and rear body, in the unchanged vehicle frame.
    // Conservative bounds also protect fine spray without world collision cost.
    private static readonly Aabb[] Hardware =
    [
        new(new(-.51f, 1.42f, 1.33f), new(1.02f, .74f, 1.02f)),
        new(new(-.80f, .71f, 1.25f), new(1.60f, .72f, 1.42f)),
        new(new(-.95f, .10f, -2.3f), new(1.90f, 1.12f, 5.35f)),
    ];

    private bool BehindHardware(Vector3 point, float radius)
    {
        if (_inverseOwner is not { } inverse) { return false; }
        point = inverse * point;
        foreach (var box in Hardware) { if (box.Grow(radius).HasPoint(point)) { return true; } }
        return false;
    }

    private static bool IntersectsHardware(Aabb bounds)
    {
        foreach (var box in Hardware) { if (box.Intersects(bounds)) { return true; } }
        return false;
    }

    public override void _ExitTree() { Mesh = null; _mesh.Dispose(); _spray.Dispose(); _sprayShape.Dispose(); }
}
