using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Three expanding compressed-air pressure ripples; one reusable burst per launcher.</summary>
internal sealed partial class EmpClawDischarge : Node3D
{
    private readonly List<MeshInstance3D> _rings = [];
    private readonly List<StandardMaterial3D> _materials = [];
    private readonly TorusMesh _mesh = new() { InnerRadius = .90f, OuterRadius = 1, Rings = 32, RingSegments = 8 };
    private readonly MultiMesh _mist = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = 18 };
    private readonly QuadMesh _mistMesh = new() { Size = Vector2.One };
    private readonly StandardMaterial3D _mistMaterial = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, AlbedoColor = new(.85f, .90f, 1) };
    private float _age = 1;
    internal bool Active => _age < .34f;

    internal EmpClawDischarge()
    {
        Name = "AirDischarge";
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _mistMaterial.AlbedoTexture = Networking.MatchResourceLoader.LoadResource<Texture2D>("res://assets/items/kenney/particles/smoke_01.png");
        _mistMesh.Material = _mistMaterial; _mist.Mesh = _mistMesh;
        AddChild(new MultiMeshInstance3D { Multimesh = _mist, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _mist.VisibleInstanceCount = 0;
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
        _mist.VisibleInstanceCount = Active ? 18 : 0;
        for (int i = 0; i < _mist.VisibleInstanceCount; i++)
        {
            float angle = i * 2.39996f;
            float radius = .13f + _age * (1.1f + i % 3 * .22f);
            float size = .12f + _age * 1.6f;
            _mist.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * size),
                new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, -_age * (6 + i % 4))));
            _mist.SetInstanceColor(i, new Color(.8f, .88f, 1, Math.Max(0, 1 - _age / .34f) * .28f));
        }
        for (int i = 0; i < _rings.Count; i++)
        {
            float t = Math.Max(0, _age - i * .025f);
            _rings[i].Visible = Active && t > 0;
            float radius = (.18f + t * (3.8f - i * .6f)) * EmpClawVfxSettings.Current.ShockwaveScale;
            _rings[i].Scale = new(radius, .022f + t * .04f, radius);
            _rings[i].Position = new(0, 0, -t * (10 - i * 1.2f));
            _materials[i].AlbedoColor = new(.75f, .88f, 1, Math.Max(0, 1 - t / .29f) * .45f);
        }
    }

    public override void _ExitTree()
    {
        foreach (var instance in GetChildren().OfType<MultiMeshInstance3D>()) { instance.Multimesh = null; }
        _mist.Mesh = null; _mistMesh.Material = null; _mistMaterial.AlbedoTexture = null;
        _mist.Dispose(); _mistMesh.Dispose(); _mistMaterial.Dispose();
        foreach (var ring in _rings) { ring.Mesh = null; ring.MaterialOverride = null; }
        foreach (var material in _materials) { material.Dispose(); }
        _mesh.Dispose();
    }
}
