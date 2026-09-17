using Godot;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Small local arena for exercising production movement against ramps, walls, vehicles, and movable props.</summary>
public sealed partial class VehicleArena : Node3D
{
    private readonly VehicleChaseCamera _camera = new() { Name = "ChaseCamera", Current = true, Fov = 65 };
    private readonly List<VehicleBody> _vehicles = new();
    private readonly VehicleDestructionEffects _destruction = new();
    private readonly Audio.ArenaAudio _audio = new();
    private MeshInstance3D? _blast;
    private float _blastSeconds;
    private Arenas.CombatArena? _layout;

    /// <summary>Retains the focused ramp/surface fixture for existing movement regression tests.</summary>
    internal bool LegacyTestLayout { get; init; }
    /// <summary>All eight production practice vehicles, or two in the focused fixture.</summary>
    internal IReadOnlyList<VehicleBody> Vehicles => _vehicles;

    /// <summary>The sole gameplay simulation, shared by every native adapter.</summary>
    internal Trackstorm.Core.Simulation.Simulation Simulation { get; private set; } = new(new Trackstorm.Core.Simulation.SimulationConfiguration(60));

    /// <summary>Controllable local vehicle.</summary>
    internal VehicleBody Player { get; private set; } = null!;
    /// <summary>Second physical vehicle for collision checks.</summary>
    internal VehicleBody Target { get; private set; } = null!;
    /// <summary>Movable prop for native interaction checks.</summary>
    internal RigidBody3D Crate { get; private set; } = null!;

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Only native vehicle bodies opt into physics interpolation; effects update in render time.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        if (!LegacyTestLayout)
        {
            Simulation = new Trackstorm.Core.Simulation.Simulation(new Trackstorm.Core.Simulation.SimulationConfiguration(60), new RespawnConfiguration());
        }

        AddChild(_destruction);
        AddChild(_audio);
        _audio.Initialize(1);
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("172235"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b9d6ed"),
                AmbientLightEnergy = 0.65f,
            }

        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 1.4f, ShadowEnabled = true });
        if (LegacyTestLayout)
        {
            // Coplanar, non-overlapping floor tiles keep the mud boundary free of physical steps.
            AddStatic(new Vector3(4, 1, 80), new Vector3(-38, -0.5f, 0), new Color("303b4b"));
            AddStatic(new Vector3(68, 1, 80), new Vector3(6, -0.5f, 0), new Color("303b4b"));
            AddStatic(new Vector3(8, 1, 32), new Vector3(-32, -0.5f, 24), new Color("303b4b"));
            AddStatic(new Vector3(8, 1, 32), new Vector3(-32, -0.5f, -24), new Color("303b4b"));
            AddStatic(new Vector3(8, 1, 16), new Vector3(-32, -0.5f, 0), new Color("755039"), SurfaceType.Mud);
            AddStatic(new Vector3(1, 4, 80), new Vector3(-40, 1.5f, 0), new Color("bf6d39"));
            AddStatic(new Vector3(1, 4, 80), new Vector3(40, 1.5f, 0), new Color("bf6d39"));
            AddStatic(new Vector3(80, 4, 1), new Vector3(0, 1.5f, -40), new Color("bf6d39"));
            AddStatic(new Vector3(80, 4, 1), new Vector3(0, 1.5f, 40), new Color("bf6d39"));
            StaticBody3D ramp = AddStatic(new Vector3(8, 0.4f, 12), new Vector3(0, 0.95f, -5), new Color("607789"));
            ramp.Rotation = new Vector3(0.2f, 0, 0);
            AddStatic(new Vector3(5, 2, 5), new Vector3(-15, 1, -10), new Color("7b879a"));
            for (int z = -32; z <= 32; z += 8)
            {
                AddChild(VehicleBody.Box(new Vector3(0.15f, 0.02f, 3), new Vector3(-5, 0.02f, z), new Color("e4be60")));
                AddChild(VehicleBody.Box(new Vector3(0.15f, 0.02f, 3), new Vector3(5, 0.02f, z), new Color("e4be60")));
            }

            Player = new VehicleBody { Name = "PlayerVehicle", Position = new Vector3(0, 1, 20), VehicleId = 1 };
            Target = new VehicleBody { Name = "TargetVehicle", Position = new Vector3(12, 1, -15), VehicleId = 2, Paint = new Color("f28b46") };
            _vehicles.AddRange(new[] { Player, Target });
        }
        else
        {
            _layout = new Arenas.CombatArena { Name = "PrototypeArena" };
            AddChild(_layout);
            for (int slot = 0; slot < Core.Arenas.ArenaConfiguration.SpawnCount; slot++)
            {
                var spawn = Core.Arenas.PrototypeArena.Configuration.Spawn(slot);
                _vehicles.Add(new VehicleBody { Name = $"Vehicle{slot + 1}", VehicleId = (ulong)slot + 1, Position = VehicleBody.ToGodot(spawn.Position), Quaternion = VehicleBody.ToGodot(spawn.Orientation), Paint = Color.FromHsv(slot * 0.12f, 0.45f, 0.7f) });
            }

            Player = _vehicles[0];
            Target = _vehicles[1];
        }

        foreach (VehicleBody vehicle in _vehicles)
        {
            if (!LegacyTestLayout)
            {
                vehicle.DamageConfiguration = new DamageConfiguration { MaxHP = 1000 };
            }

            Simulation.AddVehicle(vehicle.VehicleId, vehicle.Configuration, vehicle.DamageConfiguration, new VehiclePhysicsState(VehicleBody.ToCore(vehicle.Position), new System.Numerics.Quaternion(vehicle.Quaternion.X, vehicle.Quaternion.Y, vehicle.Quaternion.Z, vehicle.Quaternion.W), System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero));
            vehicle.Initialize(Simulation);
            AddChild(vehicle);
        }

        if (LegacyTestLayout)
        {
            Crate = new RigidBody3D { Name = "MovableCrate", Position = new Vector3(12, 1.2f, 5), Mass = 150, ContinuousCd = true };
            Crate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.8f, 1.8f, 1.8f) } });
            Crate.AddChild(VehicleBody.Box(new Vector3(1.8f, 1.8f, 1.8f), Vector3.Zero, new Color("ddba5b")));
            AddChild(Crate);
        }
        else
        {
            Crate = _layout!.Props[0];
        }

        AddChild(_camera);
        _camera.Position = new Vector3(0, 8, 32);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _audio.Follow(Simulation.State.Vehicles, id => _vehicles.Single(vehicle => vehicle.VehicleId == id).GlobalPosition, Simulation.State.Tick);
        _camera.Follow(Player.GetGlobalTransformInterpolated(), Player.Snapshot, (float)delta);
        if (_blast is not null)
        {
            _blastSeconds -= (float)delta;
            _blast.Scale = Vector3.One * (1 + ((0.5f - _blastSeconds) * 10));
            if (_blastSeconds <= 0)
            {
                _blast.QueueFree();
                _blast = null;
            }

        }

    }

    /// <summary>Collects all native observations, advances Core once, then applies the complete accepted batch.</summary>
    /// <param name="input">Current fixed-step frame.</param>
    internal void Advance(InputFrame input)
    {
        var neutral = new InputFrame(input.Tick, 0, 0, 0, InputButtons.None, InputButtons.None, InputButtons.None);
        var requests = _vehicles.Select(vehicle => vehicle.Capture(vehicle == Player ? input : neutral)).ToArray();
        _camera.ObserveCollision(requests[0].Observation, Player.Configuration.Mass);
        IReadOnlyList<VehicleStepResult> results = Simulation.Step(input, requests);
        _destruction.Apply(Simulation.State.Vehicles);
        _audio.ApplyVehicles(Simulation.State.Vehicles);
        for (int index = 0; index < _vehicles.Count; index++)
        {
            _vehicles[index].Apply(results[index]);
        }

        foreach (VehicleBody vehicle in _vehicles)
        {
            vehicle.Publish();
        }

    }

    /// <summary>Local authority demonstration of the item-independent Core explosion helper.</summary>
    /// <param name="center">World-space blast center.</param>
    internal void Explode(Vector3 center)
    {
        if (!Player.Snapshot.CanInteract)
        {
            return;
        }

        foreach (VehicleBody vehicle in _vehicles)
        {
            DamageEffect effect = VehicleDamageMath.Explosion(VehicleBody.ToCore(center), VehicleBody.ToCore(vehicle.GlobalPosition), 8, 55, 15000, new System.Numerics.Vector3(0.7f, 0.25f, -0.6f));
            vehicle.ApplyEffect(effect, new DamageContext("explosion", Player.VehicleId, "local-arena-blast"));
        }

        _layout?.Explode(center);
        _audio.PracticeExplosion(center);
        _blast?.QueueFree();
        _blast = new MeshInstance3D
        {
            Position = center,
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1, 0.45f, 0.1f, 0.3f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        _blastSeconds = 0.5f;
        AddChild(_blast);
    }

    /// <summary>Queues normal new-life resets for local practice vehicles.</summary>
    internal void ResetVehicles()
    {
        if (!LegacyTestLayout)
        {
            _layout!.ResetProps();
            for (int slot = 0; slot < _vehicles.Count; slot++)
            {
                _vehicles[slot].ResetBody(Core.Arenas.PrototypeArena.Configuration.Spawn(slot));
            }

            return;
        }

        Player.ResetBody(new VehiclePhysicsState(new System.Numerics.Vector3(0, 1, 20), System.Numerics.Quaternion.Identity, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero));
        Target.ResetBody(new VehiclePhysicsState(new System.Numerics.Vector3(12, 1, -15), System.Numerics.Quaternion.Identity, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero));
    }

    private StaticBody3D AddStatic(Vector3 size, Vector3 position, Color color, SurfaceType surface = SurfaceType.Concrete)
    {
        var body = new SurfaceBody { Position = position, Surface = surface };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(VehicleBody.Box(size, Vector3.Zero, color));
        AddChild(body);
        return body;
    }

}
