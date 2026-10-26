using Godot;
using Trackstorm.Client.Networking;

namespace Trackstorm.Client.Items;

/// <summary>One arena-owned set of immutable smash resources, retained across repeated collections.</summary>
internal sealed class CarnivalSmashResources
{
    internal Mesh[] Wood { get; }
    internal Mesh Foil { get; }
    internal Mesh[] GoldShapes { get; }
    internal Mesh Spark { get; }
    internal Mesh Flash { get; } = new QuadMesh { Size = new(2.6f, 2.6f) };
    internal Texture2D FlashTexture { get; } = MatchResourceLoader.LoadResource<Texture2D>("res://assets/items/kenney/particles/spark_01.png");
    internal Shader SurfaceShader { get; } = MatchResourceLoader.LoadResource<Shader>(CarnivalSmash.ShaderPath);
    internal Shader SparkShader { get; } = MatchResourceLoader.LoadResource<Shader>(CarnivalSmash.SparkShaderPath);
    internal Texture2D Red { get; } = WoodTexture("crimson wood");
    internal Texture2D Ivory { get; } = WoodTexture("ivory wood");
    internal Texture2D Raw { get; } = WoodTexture("exposed endgrain");

    internal CarnivalSmashResources()
    {
        var source = MatchResourceLoader.LoadResource<PackedScene>(CarnivalSmash.AssetPath).Instantiate<Node3D>();
        Wood = Enumerable.Range(0, 3).Select(i => source.GetNode<MeshInstance3D>("WoodShard"+i).Mesh).ToArray();
        Foil = source.GetNode<MeshInstance3D>("GoldFoil").Mesh;
        GoldShapes = new[] { "GoldStar", "GoldCoin", "GoldBolt" }.Select(name => source.GetNode<MeshInstance3D>(name).Mesh).ToArray();
        source.Free();
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int segment = 0; segment < 8; segment++)
        {
            float a = segment/8f, b = (segment+1)/8f;
            foreach (var uv in new[] { new Vector2(0,a), new Vector2(1,a), new Vector2(1,b), new Vector2(0,a), new Vector2(1,b), new Vector2(0,b) })
            {
                surface.SetUV(uv); surface.SetNormal(Vector3.Back); surface.AddVertex(new(uv.X-.5f, uv.Y, 0));
            }
        }
        Spark = surface.Commit(); surface.Dispose();
    }

    private static Texture2D WoodTexture(string name) => MatchResourceLoader.LoadResource<Texture2D>($"res://assets/items/carnival-crate/CarnivalCrate_Crate {name}.png");
}
