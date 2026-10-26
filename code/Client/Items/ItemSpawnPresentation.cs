using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructs one non-colliding carnival stack per confirmed pickup marker.</summary>
internal sealed partial class ItemSpawnPresentation : Node3D
{
    internal const string AssetPath = "res://assets/items/carnival-crate/CarnivalCrate.glb";
    private readonly Dictionary<string, Node3D> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label3D> _labels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _floating = new(StringComparer.Ordinal);

    /// <summary>Number of confirmed active visuals.</summary>
    internal int ActiveCount => _models.Values.Count(model => model.Visible);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        // The static map has entered the physics world by this boundary. Placement
        // is cosmetic and happens once; confirmed spawn centres remain untouched.
        foreach (var pair in _models)
        {
            var model = pair.Value;
            if (!_floating.Contains(pair.Key))
            {
                if (PickupGroundPose.Resolve(GetWorld3D().DirectSpaceState, model.GlobalPosition, model.GlobalBasis) is { } pose)
                    model.GlobalTransform = pose;
                else
                    GD.PushWarning($"No terrain support for pickup {pair.Key}.");
            }

            // Billboard upright, with clearance above the tilted upper crate.
            // Both texts share the same stable anchor through cooldown/respawn.
            Vector3 top = model.ToGlobal(new Vector3(0, 2.88f, 0));
            float tiltedTop = MathF.Abs(model.GlobalBasis.X.Y) * .75f + MathF.Abs(model.GlobalBasis.Z.Y) * .8f;
            // Looking up at a floating pickup must not hide the billboard behind
            // its upper front edge. Grounded approach views need less separation.
            float clearance = _floating.Contains(pair.Key) ? .95f : .42f;
            _labels[pair.Key].GlobalPosition = top + Vector3.Up * (clearance + tiltedTop);
        }
        SetPhysicsProcess(false);
    }

    /// <summary>Builds one non-colliding visual per actual registered marker, hidden until authority arrives.</summary>
    /// <param name="arena">Validated scene markers.</param>
    /// <param name="authoredMarkers">Map-owned presentation guidance; never changes the Core spawn contract.</param>
    internal void Initialize(ArenaConfiguration arena, Node3D authoredMarkers)
    {
        var scene = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath);
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color("ffd478"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true, Emission = new Color("ffc04a"), EmissionEnergyMultiplier = 1.2f,
        };
        var spark = new SphereMesh { Radius = 0.5f, Height = 1, RadialSegments = 6, Rings = 3, Material = material };
        var aura = CarnivalPickupAura.CreateMesh();
        foreach (var marker in arena.Items)
        {
            if (authoredMarkers.GetNode<Marker3D>(marker.Id).GetMeta("pickup_floating", false).AsBool())
                _floating.Add(marker.Id);
            var root = new Node3D { Name = marker.Id, Position = VehicleBody.ToGodot(marker.Position) };
            AddChild(root);
            // Marker -Z follows the authored route; the star faces its approach (+Z).
            var model = new Node3D { Name = "IntactPickup", Rotation = new Vector3(0, marker.Yaw, 0), Visible = false };
            root.AddChild(model);
            model.AddChild(scene.Instantiate<Node3D>());
            model.AddChild(new MeshInstance3D { Mesh = aura, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            model.AddChild(new OmniLight3D { Position = new Vector3(0, 1.5f, 1.1f), LightColor = new Color("ffd17a"), LightEnergy = 0.65f, OmniRange = 3.6f, DistanceFadeEnabled = true, DistanceFadeBegin = 30, DistanceFadeLength = 15 });
            model.AddChild(new GpuParticles3D
            {
                Amount = 16,
                Lifetime = 2.0,
                VisibilityAabb = new Aabb(new Vector3(-3, -3, -3), new Vector3(6, 6, 6)),
                Position = new Vector3(0, 1.3f, 0),
                ProcessMaterial = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(1.65f, 1.25f, 0.9f), Direction = Vector3.Up, Spread = 15, InitialVelocityMin = 0.12f, InitialVelocityMax = 0.25f, Gravity = Vector3.Zero, ScaleMin = 0.018f, ScaleMax = 0.035f },
                DrawPass1 = spark,
                Emitting = false,
            });
            var label = new Label3D { Position = new Vector3(0, 3.3f, 0), Text = string.Empty, FontSize = 40, PixelSize = 0.012f, Modulate = new Color("ffc477"), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 8 };
            root.AddChild(label);
            _models.Add(marker.Id, model);
            _labels.Add(marker.Id, label);
        }
        SetPhysicsProcess(true);
    }

    /// <summary>Changes availability only from a confirmed complete publication.</summary>
    /// <param name="state">Host state accepted by the production driver.</param>
    internal void Apply(ItemPublication state)
    {
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
