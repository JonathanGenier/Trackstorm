using Godot;

namespace Trackstorm.Client.Items;

/// <summary>The exact authored paper round used in the handle magazine and spoon.
/// Extracting its rigid subtree keeps a single model/material source for every stage.</summary>
internal static class SalvoFirework
{
    internal static readonly Vector3 FusePosition = new(.235f, .04f, .015f);

    internal static Node3D Create()
    {
        var mechanism = Networking.MatchResourceLoader.LoadResource<PackedScene>(SalvoCatapult.AssetPath).Instantiate<Node3D>();
        var round = (Node3D)mechanism.FindChild("Round_0", true, false);
        round.GetParent().RemoveChild(round);
        round.Transform = Transform3D.Identity;
        round.Name = "PaperFirework";
        mechanism.Free();
        return round;
    }
}
