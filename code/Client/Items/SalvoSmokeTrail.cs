using Godot;

namespace Trackstorm.Client.Items;

/// <summary>One fixed world-space segment pool per arena. Segments keep their own
/// birth time after impact; saturation declines new smoke rather than popping old trails.</summary>
internal sealed partial class SalvoSmokeTrail : MultiMeshInstance3D
{
    internal const int Capacity = 16384;
    internal const float Lifetime = 15;
    private readonly float[] _born = new float[Capacity];
    private readonly ShaderMaterial _material;
    private int _head;
    private int _tail;
    private int _count;
    private int _highWater;
    private float _clock;
    internal int ActiveSegments => _count;
    internal int DroppedSegments { get; private set; }

    internal SalvoSmokeTrail()
    {
        Name = "SalvoSmokeTrails";
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _material = new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>("res://assets/items/salvo/SalvoSmoke.gdshader") };
        _material.SetShaderParameter("smoke_texture", Networking.MatchResourceLoader.LoadResource<Texture2D>("res://assets/items/kenney/particles/smoke_01.png"));
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = _material }, InstanceCount = Capacity, VisibleInstanceCount = 0 };
        CastShadow = ShadowCastingSetting.Off;
        // The ballistic continuation may leave the map; shader discards expired slots.
        CustomAabb = new(new(-10000000, -10000000, -10000000), new(20000000, 20000000, 20000000));
        for (int i = 0; i < Capacity; i++) { Multimesh.SetInstanceCustomData(i, new(-Lifetime * 2, 0, 0, 0)); }
    }

    internal void Emit(Vector3 from, Vector3 to, ulong id)
    {
        Vector3 segment = to - from;
        if (segment.LengthSquared() < .000001f) { return; }
        if (_count == Capacity) { DroppedSegments++; return; }
        float seed = ((_head * 17 + (int)(id % 8191)) % 997) / 997f;
        float width = 1.1f + seed * .4f;
        Vector3 axis = segment.Normalized();
        Vector3 right = (Math.Abs(axis.Dot(Vector3.Up)) > .95f ? Vector3.Right : Vector3.Up).Cross(axis).Normalized();
        // Soft texture edges need overlapping coverage, including multi-metre
        // steps at launch speed. Rotation of the paper round never rotates this wake.
        Multimesh.SetInstanceTransform(_head, new(new Basis(axis * (segment.Length() * 2.5f + width), right * width, axis.Cross(right)), (from + to) / 2));
        Multimesh.SetInstanceCustomData(_head, new(_clock, seed, width, 1));
        _born[_head] = _clock;
        _highWater = Math.Max(_highWater, _head + 1);
        Multimesh.VisibleInstanceCount = _highWater;
        _head = (_head + 1) % Capacity;
        _count++;
    }

    public override void _Process(double delta) => Advance((float)delta);

    internal void Advance(float seconds)
    {
        _clock += Math.Max(0, seconds);
        _material.SetShaderParameter("smoke_clock", _clock);
        while (_count > 0 && _clock - _born[_tail] >= Lifetime)
        {
            _tail = (_tail + 1) % Capacity;
            _count--;
        }
        if (_count == 0) { Multimesh.VisibleInstanceCount = 0; }
    }
}
