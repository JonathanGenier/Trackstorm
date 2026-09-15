using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructable Kenney projectile and particle effects; no collision or gameplay mutation.</summary>
internal sealed partial class ItemPresentation : Node3D
{
    private readonly AudioStreamWav _launch = VehicleFeedback.CreateCue(false);
    private readonly AudioStreamWav _blast = VehicleFeedback.CreateCue(true);
    private readonly Dictionary<ulong, Node3D> _missiles = new();
    private readonly Dictionary<ulong, (Node3D Node, HeldItem Item)> _held = new();
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

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        foreach (var burst in _bursts)
        {
            foreach (var audio in burst.Node.GetChildren().OfType<AudioStreamPlayer3D>())
            {
                audio.Stop();
                audio.Stream = null;
            }
        }

        _launch.Dispose();
        _blast.Dispose();
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
        foreach (ulong id in _missiles.Keys.Except(state.Missiles.Select(missile => missile.Id)).ToArray())
        {
            _missiles[id].QueueFree();
            _missiles.Remove(id);
        }

        foreach (var missile in state.Missiles)
        {
            if (!_missiles.TryGetValue(missile.Id, out var node))
            {
                node = Rocket(false);
                AddChild(node);
                node.AddChild(Particles("smoke_01", false, 0.5f));
                _missiles.Add(missile.Id, node);
            }

            node.Position = VehicleBody.ToGodot(missile.Position);
            node.Quaternion = new Quaternion(Vector3.Forward, VehicleBody.ToGodot(missile.Velocity).Normalized());
        }

        foreach (ulong id in _held.Keys.Except(state.Slots.Where(slot => slot.Item != HeldItem.None && _held.TryGetValue(slot.Vehicle, out var held) && held.Item == slot.Item).Select(slot => slot.Vehicle)).ToArray())
        {
            _held[id].Node.QueueFree();
            _held.Remove(id);
        }

        foreach (var slot in state.Slots.Where(slot => slot.Item != HeldItem.None && !_held.ContainsKey(slot.Vehicle)))
        {
            Node3D node = slot.Item == HeldItem.Missile ? Rocket(true) : Wrench();
            AddChild(node);
            node.Position = VehicleBody.ToGodot(state.World.Vehicles.Single(vehicle => vehicle.State.VehicleId == slot.Vehicle).State.Movement.Physics.Position) + new Vector3(0, 2.2f, 0);
            _held.Add(slot.Vehicle, (node, slot.Item));
        }

        foreach (var outcome in state.Events)
        {
            var burst = new Node3D { Position = VehicleBody.ToGodot(outcome.Position) };
            AddChild(burst);
            var audio = new AudioStreamPlayer3D { Stream = outcome.Impact ? _blast : _launch, MaxDistance = 65, UnitSize = 12, VolumeDb = -12, Bus = AudioServer.GetBusIndex("SFX") >= 0 ? "SFX" : "Master" };
            burst.AddChild(audio);
            audio.Play();
            string texture = outcome.Impact ? "fire_01" : "spark_01";
            burst.AddChild(Particles(texture, true, outcome.Impact ? 0.7f : 0.25f));
            if (outcome.Impact)
            {
                burst.AddChild(Particles("smoke_01", true, 1.1f));
            }

            _bursts.Add((burst, 0, outcome.Impact ? 1.3f : 0.5f));
        }
    }

    /// <summary>Follows displayed vehicles without feeding render poses into authority.</summary>
    /// <param name="vehicle">Stable ID.</param>
    /// <param name="position">Displayed vehicle position.</param>
    /// <param name="active">Whether the authoritative vehicle may show a held item.</param>
    internal void Follow(ulong vehicle, Vector3 position, bool active = true)
    {
        if (_held.TryGetValue(vehicle, out var node))
        {
            node.Node.Position = position + new Vector3(0, 2.2f, 0);
            node.Node.Visible = active;
        }
    }

    private static Node3D Rocket(bool held)
    {
        var root = new Node3D();
        var mesh = GD.Load<Mesh>("res://assets/items/kenney/weapons/ammo_rocket.obj");
        Aabb bounds = mesh.GetAabb();
        float scale = 1.5f / Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        root.AddChild(new MeshInstance3D { Mesh = mesh, Scale = Vector3.One * scale, Position = -bounds.GetCenter() * scale, MaterialOverride = GD.Load<StandardMaterial3D>(held ? "res://assets/items/materials/Pickup.tres" : "res://assets/items/materials/Projectile.tres") });
        return root;
    }

    private static Node3D Wrench()
    {
        var root = new Node3D();
        var material = GD.Load<StandardMaterial3D>("res://assets/items/materials/Pickup.tres");
        foreach (var part in new[] { (new Vector3(0.18f, 0.18f, 0.9f), Vector3.Zero), (new Vector3(0.5f, 0.18f, 0.18f), new Vector3(0, 0, -0.45f)), (new Vector3(0.16f, 0.18f, 0.3f), new Vector3(-0.2f, 0, -0.6f)), (new Vector3(0.16f, 0.18f, 0.3f), new Vector3(0.2f, 0, -0.6f)) })
        {
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = part.Item1 }, Position = part.Item2, MaterialOverride = material });
        }

        return root;
    }

}
