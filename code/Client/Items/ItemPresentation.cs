using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructable Kenney projectile and particle effects; no collision or gameplay mutation.</summary>
internal sealed partial class ItemPresentation : Node3D
{
    private readonly Dictionary<ulong, Node3D> _mines = new();
    private readonly Dictionary<ulong, Node3D> _oil = new();
    private readonly Dictionary<ulong, Node3D> _missiles = new();
    private readonly List<(Node3D Node, float Age, float Lifetime)> _bursts = new();

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var burst = _bursts[i];
            burst.Age += (float)delta;
            if (burst.Age >= burst.Lifetime)
            {
                burst.Node.QueueFree();
                _bursts.RemoveAt(i);
            }
            else
            {
                _bursts[i] = burst;
            }
        }
    }

    /// <summary>Builds a presentation-only emitter using the already acquired CC0 Particle Pack.</summary>
    /// <param name="texture">Acquired texture stem.</param>
    /// <param name="burst">One-shot emission or continuous trail.</param>
    /// <param name="lifetime">Particle lifetime in presentation seconds.</param>
    /// <returns>Caller-owned native emitter.</returns>
    internal static GpuParticles3D Particles(string texture, bool burst, float lifetime)
    {
        var material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = GD.Load<Texture2D>($"res://assets/items/kenney/particles/{texture}.png"),
            AlbedoColor = texture == "smoke_01" ? new Color(0.3f, 0.32f, 0.35f, 0.5f) : new Color(1, 0.35f, 0.05f),
        };
        return new GpuParticles3D
        {
            Amount = burst ? 24 : 16,
            Lifetime = lifetime,
            OneShot = burst,
            Explosiveness = burst ? 1 : 0,
            LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-12, -12, -12), new Vector3(24, 24, 24)),
            ProcessMaterial = new ParticleProcessMaterial { Direction = Vector3.Up, Spread = 180, InitialVelocityMin = burst ? 2 : 0.1f, InitialVelocityMax = burst ? 7 : 0.5f, Gravity = new Vector3(0, 0.5f, 0), ScaleMin = 0.15f, ScaleMax = burst ? 1.4f : 0.4f },
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = material },
            Emitting = true,
        };
    }

    /// <summary>Consumes a new reliable publication exactly once.</summary>
    /// <param name="state">Accepted authority state.</param>
    internal void Apply(ItemPublication state)
    {
        foreach (ulong id in _mines.Keys.Except(state.Mines.Select(mine => mine.Id)).ToArray())
        {
            _mines[id].QueueFree();
            _mines.Remove(id);
        }
        foreach (var mine in state.Mines)
        {
            if (!_mines.TryGetValue(mine.Id, out var node))
            {
                node = new ProxyMineVisual();
                AddChild(node);
                _mines.Add(mine.Id, node);
            }
            node.Position = VehicleBody.ToGodot(mine.Position);
            node.Quaternion = new Quaternion(Vector3.Up, VehicleBody.ToGodot(mine.Normal));
        }
        foreach (ulong id in _oil.Keys.Except(state.Patches.Select(patch => patch.Id)).ToArray())
        {
            _oil[id].QueueFree();
            _oil.Remove(id);
        }
        foreach (var patch in state.Patches)
        {
            if (!_oil.TryGetValue(patch.Id, out var node))
            {
                node = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = patch.Radius, BottomRadius = patch.Radius, Height = 0.012f, RadialSegments = 64 },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.035f, 0.022f, 0.055f), Metallic = 0.65f, Roughness = 0.18f },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(node);
                _oil.Add(patch.Id, node);
            }
            node.Position = VehicleBody.ToGodot(patch.Position + patch.Normal * 0.07f);
            node.Quaternion = new Quaternion(Vector3.Up, VehicleBody.ToGodot(patch.Normal));
        }

        foreach (ulong id in _missiles.Keys.Except(state.Missiles.Where(missile => missile.Launched).Select(missile => missile.Id)).ToArray())
        {
            _missiles[id].QueueFree();
            _missiles.Remove(id);
        }

        foreach (var missile in state.Missiles.Where(missile => missile.Launched))
        {
            if (!_missiles.TryGetValue(missile.Id, out var node))
            {
                node = Rocket();
                AddChild(node);
                node.AddChild(Particles("smoke_01", false, 0.5f));
                _missiles.Add(missile.Id, node);
            }

            node.Position = VehicleBody.ToGodot(missile.Position);
            node.Quaternion = new Quaternion(Vector3.Forward, VehicleBody.ToGodot(missile.Velocity).Normalized());
        }

        foreach (var outcome in state.Events)
        {
            if (outcome.Item == HeldItem.MachineGun)
            {
                if (outcome.Tracer) { Tracer(outcome); }
                if (outcome.Impact) { BulletImpact(outcome); }
                continue;
            }
            var definition = ItemRegistry.Find(outcome.Item)!;
            string? texture = outcome.Impact ? definition.ImpactVfx : definition.UseVfx;
            if (texture is null)
            {
                continue;
            }

            var burst = new Node3D { Position = VehicleBody.ToGodot(outcome.Position) };
            AddChild(burst);
            burst.AddChild(Particles(texture, true, outcome.Impact ? 0.7f : 0.25f));
            if (outcome.Impact)
            {
                burst.AddChild(Particles("smoke_01", true, 1.1f));
            }

            _bursts.Add((burst, 0, outcome.Impact ? 1.3f : 0.5f));
        }
    }

    private void Tracer(ItemEvent outcome)
    {
        Vector3 start = VehicleBody.ToGodot(outcome.Origin);
        Vector3 end = VehicleBody.ToGodot(outcome.Position);
        Vector3 segment = end - start;
        // Bounded presentation only: delivery catch-up cannot create an unbounded burst pool.
        if (_bursts.Count >= 128) { return; }
        var root = new Node3D();
        AddChild(root);
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 0.76f, 0.25f), EmissionEnabled = true, Emission = new Color(1, 0.4f, 0.08f) };
        if (segment.Length() > 0.01f)
        {
            root.AddChild(new MeshInstance3D { Position = (start + end) / 2, Quaternion = new Quaternion(Vector3.Up, segment.Normalized()),
                Mesh = new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.018f, Height = segment.Length(), RadialSegments = 6 }, MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        root.AddChild(new MeshInstance3D { Position = start + segment.Normalized() * Math.Min(2.2f, segment.Length()), Mesh = new SphereMesh { Radius = 0.10f, Height = 0.20f }, MaterialOverride = material });
        if (outcome.Impact) { root.AddChild(new MeshInstance3D { Position = end, Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f }, MaterialOverride = material }); }
        _bursts.Add((root, 0, 0.055f));
    }

    private void BulletImpact(ItemEvent outcome)
    {
        if (_bursts.Count >= 128) { return; }
        Vector3 incoming = VehicleBody.ToGodot(outcome.Position - outcome.Origin).Normalized();
        var root = new Node3D { Position = VehicleBody.ToGodot(outcome.Position) - incoming * 0.025f };
        AddChild(root);
        var sparks = Particles("spark_01", true, 0.22f);
        sparks.Name = "BulletImpactSparks";
        sparks.Amount = 6;
        // The publication has a hit point, not a surface normal: scatter back along the incoming ray.
        var process = (ParticleProcessMaterial)sparks.ProcessMaterial;
        process.Direction = -incoming;
        process.Spread = 65;
        process.InitialVelocityMin = 1.5f;
        process.InitialVelocityMax = 4;
        process.Gravity = new Vector3(0, -8, 0);
        process.ScaleMin = 0.04f;
        process.ScaleMax = 0.11f;
        process.ColorRamp = new GradientTexture1D { Gradient = new Gradient
        {
            Colors = new[] { new Color(1, 1, 0.8f, 1), new Color(1, 0.6f, 0.15f, 0.9f), new Color(1, 0.25f, 0.02f, 0) },
            Offsets = new[] { 0f, 0.35f, 1f },
        } };
        root.AddChild(sparks);
        _bursts.Add((root, 0, 0.3f));
    }

    private static Node3D Rocket()
    {
        var root = new Node3D();
        var mesh = GD.Load<Mesh>("res://assets/items/kenney/weapons/ammo_rocket.obj");
        Aabb bounds = mesh.GetAabb();
        float scale = 1.5f / Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        root.AddChild(new MeshInstance3D { Mesh = mesh, Scale = Vector3.One * scale, Position = -bounds.GetCenter() * scale, MaterialOverride = GD.Load<StandardMaterial3D>("res://assets/items/materials/Projectile.tres") });
        return root;
    }

}
