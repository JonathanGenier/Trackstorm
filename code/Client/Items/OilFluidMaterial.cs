using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Per-hazard fluid settings using one cached match shader, with owned material cleanup.</summary>
internal sealed class OilFluidMaterial : IDisposable
{
    internal const string ShaderPath = "res://assets/items/oil/OilFluid.gdshader";
    internal ShaderMaterial Material { get; }
    internal OilFluidMaterial(bool flowing)
    {
        Material = new ShaderMaterial { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(ShaderPath) };
        Material.SetShaderParameter("flowing", flowing);
    }
    internal void SetRoughness(float value) => Material.SetShaderParameter("roughness", value);
    public void Dispose() => Material.Dispose();
}
