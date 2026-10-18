using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Three expanding compressed-air pressure ripples; one reusable burst per launcher.</summary>
internal sealed partial class EmpClawDischarge : Node3D
{
    private readonly List<MeshInstance3D> _rings = [];
    private readonly List<StandardMaterial3D> _materials = [];
    private readonly TorusMesh _mesh = new() { InnerRadius = .90f, OuterRadius = 1, Rings = 32, RingSegments = 8 };
    private float _age = 1;
    internal bool Active => _age < .34f;

    internal EmpClawDischarge()
    {
        Name = "AirDischarge";
        for (int i = 0; i < 3; i++)
        {
            var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new(.75f, .88f, 1, 0), NoDepthTest = false };
            var ring = new MeshInstance3D { Mesh = _mesh, MaterialOverride = material, Rotation = new(MathF.PI / 2, 0, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
            AddChild(ring); _rings.Add(ring); _materials.Add(material);
        }
    }

    internal void Fire(Transform3D muzzle)
    {
        TopLevel = true;
        GlobalTransform = muzzle;
        _age = 0;
        Advance(0);
    }

    internal void Advance(float delta)
    {
        _age += delta;
        for (int i = 0; i < _rings.Count; i++)
        {
            float t = Math.Max(0, _age - i * .025f);
            _rings[i].Visible = Active && t > 0;
            float radius = .18f + t * (3.8f - i * .6f);
            _rings[i].Scale = new(radius, .022f + t * .04f, radius);
            _rings[i].Position = new(0, 0, -t * (10 - i * 1.2f));
            _materials[i].AlbedoColor = new(.75f, .88f, 1, Math.Max(0, 1 - t / .29f) * .45f);
        }
    }

    public override void _ExitTree()
    {
        foreach (var ring in _rings) { ring.Mesh = null; ring.MaterialOverride = null; }
        foreach (var material in _materials) { material.Dispose(); }
        _mesh.Dispose();
    }
}
