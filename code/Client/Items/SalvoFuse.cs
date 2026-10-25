using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Bounded captive fuse sparkle. Its looping phase has no gameplay lifetime.</summary>
internal sealed partial class SalvoFuse : Node3D
{
    private readonly MeshInstance3D[] _sparks;
    private float _phase;

    internal SalvoFuse()
    {
        Name = "LoadedFuse";
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new(1, .85f, .22f), EmissionEnabled = true, Emission = new(1, .55f, .08f), EmissionEnergyMultiplier = 3 };
        var mesh = new SphereMesh { Radius = .018f, Height = .036f, RadialSegments = 6, Rings = 3, Material = material };
        _sparks = Enumerable.Range(0, 9).Select(_ => new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }).ToArray();
        foreach (var spark in _sparks) { AddChild(spark); }
    }

    internal void Present(float delta)
    {
        _phase = (_phase + delta) % 10;
        _sparks[0].Scale = Vector3.One * (1.5f + .3f * MathF.Sin(_phase * 67));
        for (int i = 1; i < _sparks.Length; i++)
        {
            float t = (_phase * 4 + i / 8f) % 1;
            float angle = i * 2.4f;
            _sparks[i].Position = new Vector3(MathF.Cos(angle), .5f + .5f * MathF.Sin(angle * 3), MathF.Sin(angle)) * (t * .18f);
            _sparks[i].Scale = Vector3.One * (1 - t);
        }
    }
}
