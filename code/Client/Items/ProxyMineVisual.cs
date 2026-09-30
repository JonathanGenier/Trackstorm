using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Shared Blender mine presentation for world and rack; no collision or gameplay authority.</summary>
internal sealed partial class ProxyMineVisual : Node3D
{
    internal const string AssetPath = "res://assets/items/proxy-mine/ProxyMine.glb";
    private readonly StandardMaterial3D _beacon = new() { AlbedoColor = new Color(0.7f, 0.015f, 0.008f), EmissionEnabled = true, Emission = Colors.Red };
    private readonly OmniLight3D _light = new() { Position = new Vector3(0, 0.5f, 0), LightColor = Colors.Red, OmniRange = 2.2f, ShadowEnabled = false };
    private double _age;

    public override void _Ready()
    {
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        // Only the lens material is per-instance. Geometry and body materials stay shared
        // across the rack and all deployed mines; one beacon cannot change another's pulse.
        var lens = (MeshInstance3D)model.FindChild("BeaconLens", true, false);
        lens.MaterialOverride = _beacon;
        AddChild(_light);
    }

    public override void _Process(double delta)
    {
        _age += delta;
        bool bright = _age % 0.8 < 0.22;
        _beacon.EmissionEnergyMultiplier = bright ? 5 : 0.15f;
        _light.LightEnergy = bright ? 2 : 0;
    }

    public override void _ExitTree()
    {
        _beacon.Dispose();
    }
}
