using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>One scene owner for the exact organic coverage and bounded flight parcels of a hazard.</summary>
internal sealed partial class OilHazardVisual : Node3D
{
    private OilPatch? _patch;
    private MeshInstance3D? _surface;
    private ArrayMesh? _mesh;
    private readonly List<MeshInstance3D> _drops = new();
    private readonly List<(MeshInstance3D Node, Vector3 Velocity, float Age)> _splashes = new();
    private OilFlight _flight = OilFlight.Empty;
    private float _flightAge;
    private readonly StandardMaterial3D _material = new() { AlbedoColor = new Color(.012f, .011f, .01f), Metallic = .32f, Roughness = .16f, ClearcoatEnabled = true, Clearcoat = .8f, ClearcoatRoughness = .12f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
    private readonly SphereMesh _dropMesh = new() { Radius = .075f, Height = .15f, RadialSegments = 12, Rings = 6 };
    internal float Roughness { set => _material.Roughness = value; }

    internal void Apply(OilPatch patch, float roughness = .16f)
    {
        _material.Roughness = roughness;
        if (_patch is not null && ((_patch.Pour is { Deposited: false } && patch.Pour is { Deposited: true }) ||
            patch.AdditionalSegments.Segments.Count > _patch.AdditionalSegments.Segments.Count))
        {
            var impact = patch.AdditionalSegments.Segments.LastOrDefault();
            Vector3 position = VehicleBody.ToGodot(impact.Radius > 0 ? impact.End : patch.Position);
            Vector3 normal = VehicleBody.ToGodot(impact.Radius > 0 ? impact.Normal : patch.Normal);
            Vector3 tangent = normal.Cross(Math.Abs(normal.Z) < .9f ? Vector3.Back : Vector3.Right).Normalized();
            for (int i = 0; i < 4 && _splashes.Count < 12; i++)
            {
                var splash = new MeshInstance3D { Position = position + normal * .02f, Mesh = _dropMesh, MaterialOverride = _material,
                    Scale = Vector3.One * .35f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(splash);
                float angle = i * Mathf.Tau / 4 + patch.Position.X;
                _splashes.Add((splash, normal * 1.3f + (tangent * Mathf.Cos(angle) + normal.Cross(tangent) * Mathf.Sin(angle)) * .8f, 0));
            }
        }
        if (_patch is null || _patch.Position != patch.Position || _patch.Normal != patch.Normal ||
            _patch.Radius != patch.Radius || !_patch.AdditionalSegments.Equals(patch.AdditionalSegments) || _patch.Pour?.Deposited != patch.Pour?.Deposited)
        {
            if (_surface is not null) { RemoveChild(_surface); _surface.QueueFree(); _surface = null; }
            _mesh?.Dispose(); _mesh = null;
            if (patch.Pour is not { Deposited: false })
            {
                var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var colors = new List<Color>();
                int layer = 0;
                void Segment(OilSegment piece)
                {
                    Vector3 normal = VehicleBody.ToGodot(piece.Normal);
                    Vector3 tangent = normal.Cross(Math.Abs(normal.Z) < .9f ? Vector3.Back : Vector3.Right).Normalized();
                    Vector3 bitangent = normal.Cross(tangent);
                    Vector3 start = VehicleBody.ToGodot(piece.Start), end = VehicleBody.ToGodot(piece.End);
                    float height = .014f + layer++ * .0002f;
                    Vector3 center = (start + end) / 2 + normal * height;
                    Vector3 Point(int index)
                    {
                        float angle = index * Mathf.Tau / 64;
                        Vector3 radial = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
                        return (radial.Dot(end - start) >= 0 ? end : start) + radial * piece.EdgeRadius(angle) + normal * height;
                    }
                    for (int i = 0; i < 64; i++)
                    {
                        vertices.Add(center); vertices.Add(Point(i + 1)); vertices.Add(Point(i));
                        for (int j = 0; j < 3; j++) { normals.Add(normal); colors.Add(new Color(.7f + .25f * MathF.Sin(i * .6f + piece.Start.X), .7f, .7f)); }
                    }
                }
                Segment(new(patch.Position, patch.Position, patch.Normal, patch.Radius));
                foreach (var piece in patch.AdditionalSegments.Segments) { Segment(piece); }
                using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray(); arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
                var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                _mesh = mesh;
                _material.VertexColorUseAsAlbedo = true;
                _surface = new MeshInstance3D { Mesh = mesh, MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(_surface);
            }
        }
        var flight = patch.Pour?.Drops ?? OilFlight.Empty;
        _flight = flight; _flightAge = 0;
        while (_drops.Count < flight.Length)
        {
            var node = new MeshInstance3D { Mesh = _dropMesh, MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _drops.Add(node); AddChild(node);
        }
        for (int i = 0; i < _drops.Count; i++)
        {
            _drops[i].Visible = i < flight.Length;
            if (i >= flight.Length) { continue; }
            _drops[i].Position = VehicleBody.ToGodot(flight[i].Position);
            Vector3 velocity = VehicleBody.ToGodot(flight[i].Velocity);
            _drops[i].Basis = velocity.LengthSquared() > .001f ? Basis.LookingAt(velocity, Math.Abs(velocity.Normalized().Dot(Vector3.Up)) > .95f ? Vector3.Right : Vector3.Up) : Basis.Identity;
            _drops[i].Scale = new Vector3(1, 1, Math.Clamp(velocity.Length() * .09f, 1, 6));
        }
        _patch = patch;
    }

    public override void _Process(double delta)
    {
        _flightAge = Math.Min(.1f, _flightAge + (float)delta);
        for (int i = 0; i < _flight.Length; i++)
        {
            _drops[i].Position = VehicleBody.ToGodot(_flight[i].Position + _flight[i].Velocity * _flightAge) -
                Vector3.Up * ((_patch?.Pour?.Gravity ?? 9.81f) * _flightAge * _flightAge * .5f);
        }
        for (int i = _splashes.Count - 1; i >= 0; i--)
        {
            var splash = _splashes[i]; float age = splash.Age + (float)delta;
            if (age > .25f) { splash.Node.QueueFree(); _splashes.RemoveAt(i); continue; }
            Vector3 velocity = splash.Velocity - Vector3.Up * ((float)delta * 9.81f);
            splash.Node.Position += velocity * (float)delta;
            splash.Node.Scale = Vector3.One * (.35f * (1 - age / .25f));
            _splashes[i] = (splash.Node, velocity, age);
        }
    }

    public override void _ExitTree() { _mesh?.Dispose(); _material.Dispose(); _dropMesh.Dispose(); }
}
