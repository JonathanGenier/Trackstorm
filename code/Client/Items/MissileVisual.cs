using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Blender-authored rigid telescopes and four equal fin hinges.</summary>
internal sealed partial class MissileVisual : Node3D
{
    internal const string AssetPath = "res://assets/items/missile/Missile.glb";
    private readonly Node3D _body;
    private readonly Node3D _tail;
    private readonly Node3D[] _fins;
    internal MissileVisual()
    {
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        _body = (Node3D)model.FindChild("ForwardBody", true, false);
        _tail = (Node3D)model.FindChild("TailExtension", true, false);
        _fins = Enumerable.Range(0, 4).Select(i => (Node3D)model.FindChild("FinHinge_" + i, true, false)).ToArray();
        SetDeployment(0, 0);
    }

    internal void SetDeployment(float extension, float fins)
    {
        _body.Position = new(0, 0, -.36f * Mathf.Clamp(extension, 0, 1));
        _tail.Position = new(0, 0, .24f * Mathf.Clamp(extension, 0, 1));
        foreach (Node3D fin in _fins) { fin.Rotation = new(0, 0, (1 - Mathf.Clamp(fins, 0, 1)) * MathF.PI / 2); }
    }
}
