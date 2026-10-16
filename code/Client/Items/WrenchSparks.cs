using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Bounded vehicle-local mechanical flecks; owned and cleared with the arm.</summary>
internal sealed partial class WrenchSparks : Node3D
{
    private readonly List<MeshInstance3D> _flecks = [];
    private float _age = 1;
    private float _intensity;
    private StandardMaterial3D _material = null!;
    private BoxMesh _mesh = null!;
    internal bool Active => _age < .16f && _intensity > 0;
    internal float Age => _age;

    public override void _Ready()
    {
        _material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1, .78f, .2f), EmissionEnabled = true, Emission = new Color(1, .45f, .06f),
            EmissionEnergyMultiplier = 3 };
        _mesh = new BoxMesh { Size = new Vector3(.022f, .06f, .022f), Material = _material };
        for (int i = 0; i < 16; i++)
        {
            var fleck = new MeshInstance3D { Mesh = _mesh, Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(fleck); _flecks.Add(fleck);
        }
    }

    internal void Burst(Vector3 contact, float intensity)
    {
        Position = contact;
        _age = 0;
        _intensity = Mathf.Clamp(intensity, 0, 2);
        Advance(0);
    }

    internal void Clear() { _age = 1; Advance(0); }

    public override void _ExitTree()
    {
        foreach (var fleck in _flecks) { fleck.Mesh = null; }
        _mesh.Material = null;
        _mesh.Dispose();
        _material.Dispose();
    }

    internal void Advance(float delta)
    {
        _age += delta;
        for (int i = 0; i < _flecks.Count; i++)
        {
            var fleck = _flecks[i];
            fleck.Visible = Active && i < Math.Min(16, (int)MathF.Ceiling(10 * _intensity));
            if (!fleck.Visible) { continue; }
            float angle = i * 2.39996f;
            Vector3 velocity = new(MathF.Cos(angle) * (3 + i % 3 * .35f), 7 + i % 4, MathF.Sin(angle) * 1.1f);
            fleck.Position = Vector3.Up * .03f + velocity * _age + Vector3.Down * (4 * _age * _age);
            fleck.Rotation = new Vector3(angle, 0, -angle);
            fleck.Scale = Vector3.One * Math.Min(1, (.16f - _age) / .04f);
        }
    }
}
