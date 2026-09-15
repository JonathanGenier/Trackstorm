using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructs confirmed pickups from existing CC0 geometry and native emissive particles.</summary>
internal sealed partial class ItemSpawnPresentation : Node3D
{
    private readonly Dictionary<string, Node3D> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label3D> _labels = new(StringComparer.Ordinal);
    private ItemPublication? _state;
    private float _age;

    /// <summary>Number of confirmed active visuals.</summary>
    internal int ActiveCount => _models.Values.Count(model => model.Visible);

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_state is null)
        {
            return;
        }

        _age += (float)delta;
        foreach (var model in _models.Values)
        {
            model.Rotation = new Vector3(0, _age, 0);
            model.Position = new Vector3(0, 1.6f + (MathF.Sin(_age * 2) * 0.15f), 0);
        }
    }

    /// <summary>Builds one non-colliding visual per actual registered marker, hidden until authority arrives.</summary>
    /// <param name="arena">Validated scene markers.</param>
    internal void Initialize(ArenaConfiguration arena)
    {
        var mesh = GD.Load<Mesh>("res://assets/items/kenney/weapons/ammo_rocket.obj");
        var material = GD.Load<StandardMaterial3D>("res://assets/items/materials/Pickup.tres");
        Aabb bounds = mesh.GetAabb();
        float scale = 1.8f / Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        foreach (var marker in arena.Items)
        {
            var root = new Node3D { Name = marker.Id, Position = VehicleBody.ToGodot(marker.Position) };
            AddChild(root);
            var model = new Node3D { Position = new Vector3(0, 1.6f, 0), Visible = false };
            root.AddChild(model);
            model.AddChild(new MeshInstance3D { Mesh = mesh, Scale = Vector3.One * scale, Position = -bounds.GetCenter() * scale, MaterialOverride = material });
            model.AddChild(new OmniLight3D { LightColor = new Color("ff9b40"), LightEnergy = 1.8f, OmniRange = 4 });
            model.AddChild(new GpuParticles3D
            {
                Amount = 16,
                Lifetime = 1.5,
                VisibilityAabb = new Aabb(new Vector3(-3, -3, -3), new Vector3(6, 6, 6)),
                ProcessMaterial = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.9f, Direction = Vector3.Up, Spread = 25, InitialVelocityMin = 0.4f, InitialVelocityMax = 0.8f, Gravity = Vector3.Zero, ScaleMin = 0.05f, ScaleMax = 0.1f },
                DrawPass1 = new SphereMesh { Radius = 0.5f, Height = 1, Material = material },
                Emitting = false,
            });
            var label = new Label3D { Position = new Vector3(0, 3, 0), Text = string.Empty, FontSize = 40, PixelSize = 0.012f, Modulate = new Color("ffc477"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 8 };
            root.AddChild(label);
            _models.Add(marker.Id, model);
            _labels.Add(marker.Id, label);
        }
    }

    /// <summary>Changes availability only from a confirmed complete publication.</summary>
    /// <param name="state">Host state accepted by the production driver.</param>
    internal void Apply(ItemPublication state)
    {
        _state = state;
        foreach (var pair in _models)
        {
            bool active = state.Spawns.Any(spawn => spawn.Id == pair.Key && spawn.Available);
            pair.Value.Visible = active;
            foreach (var particles in pair.Value.GetChildren().OfType<GpuParticles3D>())
            {
                particles.Emitting = active;
            }

            var spawn = state.Spawns.SingleOrDefault(spawn => spawn.Id == pair.Key);
            _labels[pair.Key].Text = active ? "ITEM" : spawn is null ? string.Empty : "RECHARGING";
        }
    }
}
