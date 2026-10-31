using Godot;

namespace Trackstorm.Client.Items;

internal sealed partial class TwinFangVisual
{
    internal const string SkinShaderPath = "res://assets/items/machine-gun/Harlequin.gdshader";
    internal const string SkinArtworkPath = "res://assets/items/machine-gun/Harlequin.svg";

    private static ShaderMaterial CreateSkin(MeshInstance3D mesh, StandardMaterial3D original)
    {
        // Imported material groups retain their authored rest coordinates. Mapping
        // in that space keeps the paint attached during pitch, yaw and recoil.
        string parent = mesh.GetParent().Name.ToString();
        int assembly = parent.StartsWith("Gun_", StringComparison.Ordinal) ? 1 :
            parent.StartsWith("Drum_", StringComparison.Ordinal) ? 2 : parent == "FangYaw" ? 3 : 0;
        if (assembly == 0)
        {
            var metal = new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(MetalShaderPath) };
            metal.SetShaderParameter("base_color", original.AlbedoColor.Darkened(.45f));
            return metal;
        }

        string material = original.ResourceName;
        var skin = new ShaderMaterial
        {
            Shader = Networking.MatchResourceLoader.LoadResource<Shader>(SkinShaderPath),
        };
        skin.SetShaderParameter("artwork", Networking.MatchResourceLoader.LoadResource<Texture2D>(SkinArtworkPath));
        skin.SetShaderParameter("rest_transform", mesh.Transform);
        skin.SetShaderParameter("assembly", assembly);
        skin.SetShaderParameter("side", parent.Contains("-1", StringComparison.Ordinal) ? -1f : 1f);
        skin.SetShaderParameter("finish", material.Contains("Edges", StringComparison.Ordinal) ? 2 :
            material.Contains("Recess", StringComparison.Ordinal) ? 3 : material.Contains("Steel", StringComparison.Ordinal) ? 1 : 0);
        return skin;
    }
}
