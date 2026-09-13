using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Arenas;

/// <summary>Shared production combat yard. Render meshes never generate gameplay collision.</summary>
public sealed partial class CombatArena : Node3D
{
    private readonly List<RigidBody3D> _props = new();
    private readonly List<SurfaceBody> _surfaces = new();

    /// <summary>Movable bodies in stable prop ID order.</summary>
    internal IReadOnlyList<RigidBody3D> Props => _props;
    /// <summary>Frozen replicas on network clients, native bodies on the host and in practice.</summary>
    internal bool Replica { get; init; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        var asphalt = GD.Load<StandardMaterial3D>("res://assets/arena/materials/Asphalt.tres");
        var concrete = GD.Load<StandardMaterial3D>("res://assets/arena/materials/Concrete.tres");
        var rust = GD.Load<StandardMaterial3D>("res://assets/arena/materials/Rust.tres");
        var sheet = GD.Load<StandardMaterial3D>("res://assets/arena/materials/Sheet.tres");
        var mud = GD.Load<StandardMaterial3D>("res://assets/arena/materials/Mud.tres");
        AddChild(new MeshInstance3D { Position = new Vector3(0, -2.05f, 0), Mesh = new BoxMesh { Size = new Vector3(180, 0.1f, 150) }, MaterialOverride = asphalt });
        // A grid partition produces flush, disjoint floor colliders, including both mud regions.
        float[] xs = { -60, -36, -24, 24, 36, 60 };
        float[] zs = { -50, -12, 12, 50 };
        for (int x = 0; x < xs.Length - 1; x++)
        {
            for (int z = 0; z < zs.Length - 1; z++)
            {
                bool soft = (x == 1 || x == 3) && z == 1;
                AddSolid($"Ground{x}_{z}", new Vector3(xs[x + 1] - xs[x], 2, zs[z + 1] - zs[z]), new Vector3((xs[x] + xs[x + 1]) / 2, -1, (zs[z] + zs[z + 1]) / 2), soft ? mud : asphalt, soft ? SurfaceType.Mud : SurfaceType.Concrete);
            }
        }

        // Tall continuous collision contains launches; visible retaining wall is four metres high.
        foreach (int side in new[] { -1, 1 })
        {
            AddSolid($"BoundaryX{side}", new Vector3(4, 32, 108), new Vector3(side * 62, 14, 0), concrete, visibleHeight: 4);
            AddSolid($"BoundaryZ{side}", new Vector3(120, 32, 4), new Vector3(0, 14, side * 52), concrete, visibleHeight: 4);
        }

        AddObstacle("ContainerWest", new Vector3(10, 3, 4), new Vector3(-17, 1.5f, 0), "kenney/city/shipping-container-a.glb", rust);
        AddObstacle("ContainerEast", new Vector3(10, 3, 4), new Vector3(17, 1.5f, 0), "kenney/city/shipping-container-a.glb", sheet);
        AddObstacle("BarrierNorth", new Vector3(7, 1.6f, 1.2f), new Vector3(0, 0.8f, -16), "polyhaven/concrete_road_barrier/concrete_road_barrier_1k.gltf", null);
        AddObstacle("BarrierSouth", new Vector3(7, 1.6f, 1.2f), new Vector3(0, 0.8f, 16), "polyhaven/concrete_road_barrier/concrete_road_barrier_1k.gltf", null);

        // A low salvage ramp at the edge leaves the central combat floor flat.
        var ramp = new SurfaceBody { Name = "SalvageRamp", Position = new Vector3(49, 0, -20) };
        ramp.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = new[] { new Vector3(-3, 0, -4), new Vector3(3, 0, -4), new Vector3(-3, 1, -4), new Vector3(3, 1, -4), new Vector3(-3, 0, 4), new Vector3(3, 0, 4) } } });
        AddChild(ramp);
        AddModel(ramp, "kenney/racing/ramp.glb", new Vector3(6, 1, 8), new Vector3(0, 0.5f, 0), sheet);
        _surfaces.Add(ramp);

        for (int index = 0; index < 3; index++)
        {
            var prop = new RigidBody3D
            {
                Name = $"prop-{index + 1:00}",
                Position = new Vector3(-8 + (index * 8), 1, -6),
                Mass = 180,
                ContinuousCd = true,
                LinearDamp = 1.2f,
                AngularDamp = 2,
                Freeze = Replica,
                FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic,
                PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.8f, Bounce = 0 },
            };
            prop.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.2f, 1.6f, 1.2f) } });
            AddChild(prop);
            AddModel(prop, "polyhaven/Barrel_01/Barrel_01_1k.gltf", new Vector3(1.2f, 1.6f, 1.2f), Vector3.Zero, null);
            _props.Add(prop);
        }

        AddMarkers("PlayerSpawns", PrototypeArena.Configuration.Players, new Color("d1a66b"));
        AddMarkers("ItemSpawns", PrototypeArena.Configuration.Items, new Color("74c4ba"));
        // Non-colliding skyline remains outside the sealed playable volume.
        for (int index = 0; index < 5; index++)
        {
            AddModel(this, "kenney/city/building-a.glb", new Vector3(16, 9 + ((index % 2) * 5), 12), new Vector3(-48 + (index * 24), 4.5f + ((index % 2) * 2.5f), -63), concrete);
        }

        AddModel(this, "kenney/city/chimney-large.glb", new Vector3(5, 23, 5), new Vector3(-44, 11.5f, -64), rust);
        AddModel(this, "kenney/factory/machine.glb", new Vector3(10, 5, 6), new Vector3(67, 2.5f, 8), rust);
        ValidateScene();
    }

    /// <summary>Extracts actual scene markers and supporting surfaces through the Core validator.</summary>
    /// <returns>The validated scene contract.</returns>
    internal ArenaConfiguration ValidateScene()
    {
        ArenaSpawn[] Extract(string group) => GetNode<Node3D>(group).GetChildren().OfType<Marker3D>().Select(marker => new ArenaSpawn(marker.Name, VehicleBody.ToCore(marker.Position), marker.Rotation.Y)).ToArray();
        return new ArenaConfiguration(PrototypeArena.Configuration.Minimum, PrototypeArena.Configuration.Maximum, Extract("PlayerSpawns"), Extract("ItemSpawns"), _surfaces.Select(surface => surface.Surface));
    }

    /// <summary>Applies the existing Core explosion falloff to the limited native prop set.</summary>
    /// <param name="center">World-space blast origin.</param>
    internal void Explode(Vector3 center)
    {
        if (Replica)
        {
            return;
        }

        foreach (RigidBody3D prop in _props)
        {
            DamageEffect effect = VehicleDamageMath.Explosion(VehicleBody.ToCore(center), VehicleBody.ToCore(prop.GlobalPosition), 8, 0, 1800, System.Numerics.Vector3.Zero);
            prop.ApplyCentralImpulse(VehicleBody.ToGodot(effect.Impulse));
        }
    }

    /// <summary>Restores the three props for repeatable local combat trials.</summary>
    internal void ResetProps()
    {
        if (Replica)
        {
            return;
        }

        for (int index = 0; index < _props.Count; index++)
        {
            RigidBody3D prop = _props[index];
            prop.Transform = new Transform3D(Basis.Identity, new Vector3(-8 + (index * 8), 1, -6));
            prop.LinearVelocity = Vector3.Zero;
            prop.AngularVelocity = Vector3.Zero;
            prop.Sleeping = false;
        }
    }

    private static void AddModel(Node3D parent, string asset, Vector3 size, Vector3 center, Material? material)
    {
        Node3D model = GD.Load<PackedScene>("res://assets/arena/" + asset).Instantiate<Node3D>();
        var wrapper = new Node3D();
        parent.AddChild(wrapper);
        wrapper.AddChild(model);
        var meshes = model.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
        Aabb? bounds = null;
        foreach (MeshInstance3D mesh in meshes)
        {
            Aabb box = (wrapper.GlobalTransform.AffineInverse() * mesh.GlobalTransform) * mesh.GetAabb();
            bounds = bounds is Aabb existing ? existing.Merge(box) : box;
            if (material is not null)
            {
                mesh.MaterialOverride = material;
            }
        }

        if (bounds is not Aabb extent || extent.Size.X <= 0 || extent.Size.Y <= 0 || extent.Size.Z <= 0)
        {
            throw new InvalidOperationException($"Asset has no usable visual bounds: {asset}");
        }

        wrapper.Scale = size / extent.Size;
        wrapper.Position = center - (extent.GetCenter() * wrapper.Scale);
    }

    private SurfaceBody AddSolid(string name, Vector3 size, Vector3 position, Material material, SurfaceType surface = SurfaceType.Concrete, float visibleHeight = 0)
    {
        var body = new SurfaceBody { Name = name, Position = position, Surface = surface };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        Vector3 visualSize = visibleHeight > 0 ? new Vector3(size.X, visibleHeight, size.Z) : size;
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = visualSize }, MaterialOverride = material, Position = visibleHeight > 0 ? new Vector3(0, (visibleHeight / 2) - position.Y, 0) : Vector3.Zero });
        AddChild(body);
        _surfaces.Add(body);
        return body;
    }

    private void AddObstacle(string name, Vector3 size, Vector3 position, string asset, Material? material)
    {
        var body = new SurfaceBody { Name = name, Position = position };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
        AddModel(body, asset, size, Vector3.Zero, material);
        _surfaces.Add(body);
    }

    private void AddMarkers(string name, IReadOnlyList<ArenaSpawn> spawns, Color color)
    {
        var parent = new Node3D { Name = name };
        AddChild(parent);
        foreach (ArenaSpawn spawn in spawns)
        {
            var marker = new Marker3D { Name = spawn.Id, Position = VehicleBody.ToGodot(spawn.Position), Rotation = new Vector3(0, spawn.Yaw, 0) };
            parent.AddChild(marker);
            marker.AddChild(new MeshInstance3D { Position = new Vector3(0, 0.025f - spawn.Position.Y, 0), Mesh = new TorusMesh { InnerRadius = 1.6f, OuterRadius = 1.8f, Rings = 24, RingSegments = 8 }, Scale = new Vector3(1, 0.05f, 1), MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f } });
            marker.AddChild(new Label3D { Text = spawn.Id, Position = new Vector3(0, 0.06f - spawn.Position.Y, 0), RotationDegrees = new Vector3(-90, 0, 0), FontSize = 40, PixelSize = 0.025f, Modulate = color });
        }
    }

}
