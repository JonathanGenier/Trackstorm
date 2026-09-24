using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Original project-authored magnetic mine with a pulsing red top beacon and no radius visualization.</summary>
internal sealed partial class ProxyMineVisual : Node3D
{
    private readonly StandardMaterial3D _beacon = new() { AlbedoColor = new Color(0.7f, 0.015f, 0.008f), EmissionEnabled = true, Emission = Colors.Red };
    private readonly OmniLight3D _light = new() { Position = new Vector3(0, 0.5f, 0), LightColor = Colors.Red, OmniRange = 2.2f, ShadowEnabled = false };
    private double _age;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.45f, BottomRadius = ProxyMineState.Radius, Height = ProxyMineState.HalfHeight * 2, RadialSegments = 32 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.15f, 0.16f), Metallic = 0.8f, Roughness = 0.4f },
        });
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6;
            AddChild(new MeshInstance3D { Position = new Vector3(Mathf.Cos(angle) * 0.45f, 0.18f, Mathf.Sin(angle) * 0.45f), Rotation = new Vector3(0, -angle, 0),
                Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.07f, 0.13f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.53f, 0.12f), Metallic = 0.5f } });
        }
        AddChild(new MeshInstance3D { Position = new Vector3(0, 0.3f, 0), Mesh = new SphereMesh { Radius = 0.15f, Height = 0.25f }, MaterialOverride = _beacon });
        AddChild(_light);
    }

    public override void _Process(double delta)
    {
        _age += delta;
        bool bright = _age % 0.8 < 0.22;
        _beacon.EmissionEnergyMultiplier = bright ? 5 : 0.15f;
        _light.LightEnergy = bright ? 2 : 0;
    }
}
