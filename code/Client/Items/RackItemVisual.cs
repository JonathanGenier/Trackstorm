using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Existing item art or explicitly approved labelled boxes; no new weapon art.</summary>
internal static class RackItemVisual
{
    internal static Node3D Create(HeldItem item)
    {
        if (item == HeldItem.Nitro) { throw new ArgumentException("Nitro uses the rack's persistent Boost jet.", nameof(item)); }
        if (item == HeldItem.ProxyMine) { return new ProxyMineRack { Name = "RackItem_ProxyMine" }; }
        if (item == HeldItem.Tombstone)
        {
            return new TombstoneRack();
        }
        var root = new Node3D { Name = "RackItem_" + item };
        var definition = ItemRegistry.Find(item) ?? throw new ArgumentOutOfRangeException(nameof(item));
        if (item == HeldItem.Missile)
        {
            Mesh mesh = Networking.MatchResourceLoader.LoadResource<Mesh>("res://assets/items/kenney/weapons/ammo_rocket.obj");
            Aabb bounds = mesh.GetAabb();
            float scale = 0.65f / Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
            root.AddChild(new MeshInstance3D { Mesh = mesh, Scale = Vector3.One * scale,
                Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z) * scale,
                MaterialOverride = Networking.MatchResourceLoader.LoadResource<StandardMaterial3D>("res://assets/items/materials/Projectile.tres") });
        }
        else
        {
            Color color = item switch
            {
                HeldItem.Wrench => new Color(0.25f, 0.75f, 0.40f),
                HeldItem.Oil => new Color(0.30f, 0.20f, 0.42f),
                HeldItem.Salvo => new Color(0.90f, 0.52f, 0.13f),
                _ => new Color(0.38f, 0.42f, 0.45f),
            };
            root.AddChild(new MeshInstance3D { Name = "TemporaryBox", Position = new Vector3(0, 0.16f, 0),
                Mesh = new BoxMesh { Size = new Vector3(0.56f, 0.32f, 0.56f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.65f } });
        }
        root.AddChild(new Label3D { Name = "ItemIdentity", Text = definition.DisplayName, Position = new Vector3(0, 0.72f, 0),
            FontSize = 40, PixelSize = 0.004f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = Colors.White, OutlineSize = 10, NoDepthTest = false });
        return root;
    }
}
