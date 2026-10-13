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
    private OilStreamVisual? _stream;
    internal Func<Vector3?>? Mouth { get; init; }
    internal float StreamWidth { get; set; } = .24f;
    internal Vector3? StreamMouth { get; private set; }
    private double _clock;
    private double _tick;
    private Vector3 _source;
    private Vector3 _sourceVelocity;
    private readonly List<(MeshInstance3D Node, Vector3 Velocity, float Age)> _splashes = new();
    private readonly List<OilImpactVisual> _crowns = new();
    private float _flightAge;
    internal const float FadeSeconds = 2;
    internal bool Retired { get; private set; }
    internal float FadeProgress { get; private set; }
    internal ulong HazardId => _patch?.Id ?? 0;
    internal void ObserveTick(ulong tick) { _tick = Math.Max(_tick, tick); }
    internal bool Retire(ulong tick)
    {
        if (_patch is null || tick < _patch.ExpiresAtTick) { return false; }
        // An old locally extrapolated clock must not skip the fade after a slow
        // simulation/capture interval. The removal publication owns this boundary.
        Retired = true; _tick = tick;
        if (_stream is not null) { _stream.Visible = false; }
        foreach (var splash in _splashes) { splash.Node.QueueFree(); }
        _splashes.Clear();
        foreach (var crown in _crowns.Where(GodotObject.IsInstanceValid)) { crown.QueueFree(); }
        _crowns.Clear();
        return true;
    }
    // The rack and smoothed vehicle model must finish before sampling the mouth.
    public OilHazardVisual() { ProcessPriority = 100; }
    private readonly OilFluidMaterial _streamMaterial = new(true);
    private readonly OilFluidMaterial _surfaceMaterial = new(false);
    private readonly StandardMaterial3D _material = new() { AlbedoColor = new Color(.006f, .0055f, .005f), Metallic = .04f, MetallicSpecular = .3f, Roughness = .16f, ClearcoatEnabled = true, Clearcoat = .35f, ClearcoatRoughness = .2f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
    private readonly SphereMesh _dropMesh = new() { Radius = .075f, Height = .15f, RadialSegments = 12, Rings = 6 };
    internal float Roughness { set { _material.Roughness = value; _streamMaterial.SetRoughness(value); _surfaceMaterial.SetRoughness(value); } }

    internal void Apply(OilPatch patch, float roughness = .16f, ulong tick = 0, Trackstorm.Core.Vehicles.VehiclePhysicsState? owner = null)
    {
        Roughness = roughness;
        _tick = tick;
        if (owner is { } pose && patch.Pour is { } sourcePour)
        {
            _source = VehicleBody.ToGodot(pose.Position + System.Numerics.Vector3.Transform(OilPour.Mouth(sourcePour.HeadOffset), pose.Orientation));
            _sourceVelocity = VehicleBody.ToGodot(pose.LinearVelocity);
        }
        if (_patch is not null && ((_patch.Pour is { Deposited: false } && patch.Pour is { Deposited: true }) ||
            patch.AdditionalSegments.Segments.Count > _patch.AdditionalSegments.Segments.Count))
        {
            var impact = patch.AdditionalSegments.Segments.LastOrDefault(s => s.Radius >= patch.Radius * .45f);
            Vector3 position = VehicleBody.ToGodot(impact.Radius > 0 ? impact.End : patch.Position);
            Vector3 normal = VehicleBody.ToGodot(impact.Radius > 0 ? impact.Normal : patch.Normal);
            _crowns.RemoveAll(c => !GodotObject.IsInstanceValid(c) || c.IsQueuedForDeletion());
            if (_crowns.Count < 6)
            {
                var crown = new OilImpactVisual(position, normal, impact.Radius > 0 ? impact.Radius : patch.Radius, _streamMaterial.Material);
                AddChild(crown); _crowns.Add(crown);
            }
            Vector3 tangent = normal.Cross(Math.Abs(normal.Z) < .9f ? Vector3.Back : Vector3.Right).Normalized();
            for (int i = 0; i < 8 && _splashes.Count < 24; i++)
            {
                var splash = new MeshInstance3D { Position = position + normal * .02f, Mesh = _dropMesh, MaterialOverride = _material,
                    Scale = new Vector3(.6f, 1.5f, .6f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(splash);
                float angle = i * Mathf.Tau / 8 + patch.Position.X;
                _splashes.Add((splash, normal * (1.8f + .8f * MathF.Sin(i * 2.4f)) + (tangent * Mathf.Cos(angle) + normal.Cross(tangent) * Mathf.Sin(angle)) * 2, 0));
            }
        }
        if (_patch is null || _patch.Position != patch.Position || _patch.Normal != patch.Normal ||
            _patch.Radius != patch.Radius || !_patch.AdditionalSegments.Equals(patch.AdditionalSegments) || _patch.Pour?.Deposited != patch.Pour?.Deposited)
        {
            if (_surface is not null) { RemoveChild(_surface); _surface.QueueFree(); _surface = null; }
            _mesh?.Dispose(); _mesh = null;
            if (patch.Pour is not { Deposited: false })
            {
                var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var colors = new List<Color>(); var uvs = new List<Vector2>();
                int layer = 0;
                void Segment(OilSegment piece)
                {
                    Vector3 normal = VehicleBody.ToGodot(piece.Normal);
                    Vector3 tangent = normal.Cross(Math.Abs(normal.Z) < .9f ? Vector3.Back : Vector3.Right).Normalized();
                    Vector3 bitangent = normal.Cross(tangent);
                    Vector3 start = VehicleBody.ToGodot(piece.Start), end = VehicleBody.ToGodot(piece.End);
                    float height = .012f + layer++ * .0001f;
                    Vector3 center = (start + end) / 2 + normal * height;
                    Vector3 Point(int index)
                    {
                        float angle = index * Mathf.Tau / 64;
                        Vector3 radial = tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle);
                        return (radial.Dot(end - start) >= 0 ? end : start) + radial * piece.EdgeRadius(angle) + normal * height;
                    }
                    for (int i = 0; i < 64; i++)
                    {
                        void Vertex(Vector3 point)
                        {
                            vertices.Add(point);
                            float x = point.Dot(tangent), y = point.Dot(bitangent);
                            uvs.Add(new Vector2(x, y));
                            normals.Add((normal + tangent * (.012f * MathF.Sin(x * 4 + y * 2)) + bitangent * (.012f * MathF.Cos(y * 3 - x))).Normalized());
                            colors.Add(new Color(.78f, .78f, .78f));
                        }
                        Vertex(center); Vertex(Point(i + 1)); Vertex(Point(i));
                    }
                }
                Segment(new(patch.Position, patch.Position, patch.Normal, patch.Radius));
                foreach (var piece in patch.AdditionalSegments.Segments) { Segment(piece); }
                using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray(); arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
                arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
                var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                _mesh = mesh;
                _material.VertexColorUseAsAlbedo = true;
                _surface = new MeshInstance3D { Mesh = mesh, MaterialOverride = _surfaceMaterial.Material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(_surface);
            }
        }
        _flightAge = 0;
        if (_stream is null && patch.Pour is not null) { _stream = new OilStreamVisual(_streamMaterial.Material); AddChild(_stream); }
        _patch = patch;
    }

    public override void _Process(double delta)
    {
        _flightAge = Math.Min(.1f, _flightAge + (float)delta);
        _clock += delta; _tick += delta * 60;
        if (Retired)
        {
            FadeProgress = Mathf.Clamp((float)(_tick - _patch!.ExpiresAtTick) / (60 * FadeSeconds), 0, 1);
            _surfaceMaterial.SetFade(FadeProgress);
            if (FadeProgress >= 1) { QueueFree(); }
            return;
        }
        bool firing = _patch?.Pour is { Cancelled: false } pour && _tick < pour.StartTick + (ulong)pour.DurationTicks;
        Vector3? mouth = firing ? Mouth?.Invoke() : null;
        StreamMouth = mouth;
        Vector3 correction = mouth.HasValue ? mouth.Value - (_source + _sourceVelocity * _flightAge) : Vector3.Zero;
        _stream?.Present(_patch?.Pour, _flightAge, mouth, correction, StreamWidth, _clock);
        for (int i = _splashes.Count - 1; i >= 0; i--)
        {
            var splash = _splashes[i]; float age = splash.Age + (float)delta;
            if (age > .45f) { splash.Node.QueueFree(); _splashes.RemoveAt(i); continue; }
            Vector3 velocity = splash.Velocity - Vector3.Up * ((float)delta * 9.81f);
            splash.Node.Position += velocity * (float)delta;
            splash.Node.Scale = new Vector3(.6f, 1.5f, .6f) * (1 - age / .45f);
            _splashes[i] = (splash.Node, velocity, age);
        }
    }

    public override void _ExitTree() { _mesh?.Dispose(); _material.Dispose(); _dropMesh.Dispose(); _streamMaterial.Dispose(); _surfaceMaterial.Dispose(); }
}
