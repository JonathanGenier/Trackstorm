using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native acceptance checks for the production arena, with optional rendered evidence.</summary>
public sealed partial class ArenaIntegrationChecks : Node3D
{
    private readonly List<string> _evidence = new();
    private VehicleArena _practice = null!;
    private CombatArena _layout = null!;
    private ulong _tick;
    private bool _advance;
    private string _output = string.Empty;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_advance)
        {
            _practice.Advance(new InputFrame(++_tick, 0, 0, 0, 0, 0, 0));
        }
    }

    /// <summary>Runs production local and synchronous network collision scenarios.</summary>
    public async void Run()
    {
        try
        {
            _output = OS.GetCmdlineUserArgs().FirstOrDefault(argument => argument.StartsWith("--arena-output=", StringComparison.Ordinal))?[15..] ?? ProjectSettings.GlobalizePath("res://.godot/arena-checks");
            System.IO.Directory.CreateDirectory(_output);
            Engine.PhysicsTicksPerSecond = 60;
            _practice = new VehicleArena();
            AddChild(_practice);
            _layout = _practice.GetNode<CombatArena>("PrototypeArena");
            _advance = true;
            await Frames(90);
            ArenaConfiguration actual = _layout.ValidateScene();
            Check(actual.Players.SequenceEqual(PrototypeArena.Configuration.Players) && actual.Items.SequenceEqual(PrototypeArena.Configuration.Items), "Scene extraction matches all stable Core spawn slots.");
            Check(_practice.Vehicles.Count == 8, "Eight native vehicles spawned simultaneously.");
            foreach (VehicleBody vehicle in _practice.Vehicles)
            {
                Check(vehicle.State.Grounded && vehicle.GlobalPosition.DistanceTo(VehicleBody.ToGodot(actual.Players[(int)vehicle.VehicleId - 1].Position)) < 1, $"Vehicle {vehicle.VehicleId} settles at its unobstructed spawn.");
                Check(vehicle.DamageState.CurrentHP == vehicle.DamageState.MaxHP, $"Vehicle {vehicle.VehicleId} spawns without impact damage.");
                Check(!_practice.Vehicles.Any(other => other != vehicle && other.GlobalPosition.DistanceTo(vehicle.GlobalPosition) < 5), $"Vehicle {vehicle.VehicleId} has non-overlapping clearance.");
            }

            await Capture("overview", new Vector3(72, 82, 84), Vector3.Zero);
            await VerifyNativeBoundaries();
            await VerifySurfaces();
            await VerifyProps();
            await VerifyNetworkCollisionAndAccessibility();
            await Capture("mud-and-industrial", new Vector3(-43, 12, 20), new Vector3(-26, 0, -5));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            _advance = false;
            _practice.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Task.Delay(150);
            GD.Print($"Arena integration passed: {_evidence.Count} assertions. Artifacts: {_output}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _advance = false;
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static VehiclePhysicsState Physics(Vector3 position, Vector3 velocity) => new(VehicleBody.ToCore(position), velocity.X != 0 || velocity.Z != 0 ? Numerics.Quaternion.CreateFromAxisAngle(Numerics.Vector3.UnitY, MathF.Atan2(-velocity.X, -velocity.Z)) : Numerics.Quaternion.Identity, VehicleBody.ToCore(velocity), Numerics.Vector3.Zero);

    private static VehicleSnapshot WithPhysics(VehicleSnapshot source, VehiclePhysicsState physics)
    {
        var simulation = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60));
        simulation.AddVehicle(source.VehicleId, new(), new(), physics);
        return simulation.GetVehicle(source.VehicleId);
    }

    private async Task Frames(int count)
    {
        for (int index = 0; index < count; index++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        _evidence.Add(message);
    }

    private async Task VerifyNativeBoundaries()
    {
        VehicleBody player = _practice.Player;
        foreach (Vector3 direction in new[] { Vector3.Left, Vector3.Right, Vector3.Forward, Vector3.Back })
        {
            Vector3 start = (direction * (direction.X == 0 ? 40 : 50)) + Vector3.Up;
            player.ResetBody(Physics(start, direction * 55));
            float furthest = 0;
            bool contained = true;
            for (int frame = 0; frame < 70; frame++)
            {
                await Frames(1);
                furthest = Math.Max(furthest, player.GlobalPosition.Dot(direction));
                contained &= Math.Abs(player.GlobalPosition.X) < 60 && Math.Abs(player.GlobalPosition.Z) < 50;
            }

            GD.Print($"Boundary {direction}: furthest={furthest:F2}, final={player.GlobalPosition}");
            Check(contained, $"Native boundary {direction} contains the entire 55 m/s impact trajectory.");
            Check(furthest > (direction.X == 0 ? 46 : 56), $"Vehicle actually reaches boundary {direction}.");
            player.ResetBody(Physics(start, direction * 35));
            await Frames(2);
            player.ApplyEffect(new DamageEffect(0, VehicleBody.ToCore((direction * 12000) + (Vector3.Up * 15000)), Numerics.Vector3.Zero), new DamageContext("explosion", 1, "arena-boundary-check"));
            await Frames(100);
            Check(Math.Abs(player.GlobalPosition.X) < 60 && Math.Abs(player.GlobalPosition.Z) < 50 && float.IsFinite(player.GlobalPosition.Y), $"Native boundary {direction} contains a normal combat blast launch.");
        }
    }

    private async Task VerifySurfaces()
    {
        VehicleBody player = _practice.Player;
        foreach (int side in new[] { -1, 1 })
        {
            foreach (bool intoMud in new[] { true, false })
            {
                player.ResetBody(Physics(new Vector3(side * (intoMud ? 39 : 33), 0.6f, 4), new Vector3(side * (intoMud ? -12 : 12), 0, 0)));
                await Frames(55);
                Check(player.State.CurrentSurface == (intoMud ? SurfaceType.Mud : SurfaceType.Concrete), $"Native surface crossing side {side}, intoMud={intoMud} resolves expected material (position {player.GlobalPosition}, surface {player.State.CurrentSurface}).");
            }
        }
    }

    private async Task VerifyProps()
    {
        var fixedBodies = _layout.GetChildren().OfType<SurfaceBody>().ToDictionary(body => body, body => body.Transform);
        RigidBody3D prop = _layout.Props[0];
        Vector3 initial = prop.GlobalPosition;
        _practice.Player.ResetBody(Physics(initial + new Vector3(0, 0, 7), new Vector3(0, 0, -18)));
        await Frames(60);
        Check(prop.GlobalPosition.DistanceTo(initial) > 0.2f, "Movable barrel reacts to a native vehicle impact.");
        initial = prop.GlobalPosition;
        _layout.Explode(initial + new Vector3(-2, 0, 0));
        await Frames(100);
        Check(prop.GlobalPosition.DistanceTo(initial) > 0.2f, "Movable barrel reacts to the Core explosion impulse helper.");
        foreach (RigidBody3D body in _layout.Props)
        {
            Check(body.GlobalPosition.Y > -1 && Math.Abs(body.GlobalPosition.X) < 60 && Math.Abs(body.GlobalPosition.Z) < 50 && body.LinearVelocity.Length() < 30 && body.AngularVelocity.Length() < 30, $"Prop {body.Name} remains finite, contained and bounded after impacts.");
        }

        foreach (var fixedBody in fixedBodies)
        {
            Check(fixedBody.Key.Transform == fixedBody.Value, $"Static collider {fixedBody.Key.Name} remains fixed.");
        }

        _layout.ResetProps();
        await Frames(60);
        Check(_layout.Props.Select((body, index) => body.GlobalPosition.DistanceTo(new Vector3(-8 + (index * 8), 1, -6)) < 1).All(value => value), "All three props reset and settle at their original locations.");
    }

    private async Task VerifyNetworkCollisionAndAccessibility()
    {
        var proxy = new NetworkVehicleBody { VehicleId = 99, PushProps = true };
        AddChild(proxy);
        var simulation = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60));
        simulation.AddVehicle(99, new(), new(), Physics(new Vector3(0, 1, 0), Vector3.Zero));
        await Frames(2);
        foreach (Vector3 direction in new[] { Vector3.Left, Vector3.Right, Vector3.Forward, Vector3.Back })
        {
            // Choose perimeter lanes which contain no interior obstacles.
            Vector3 start = direction.X == 0 ? new Vector3(55, 0.6f, direction.Z * 40) : new Vector3(direction.X * 50, 0.6f, 44);
            proxy.Apply(Physics(start, direction * 55));
            var observation = proxy.Observe(WithPhysics(simulation.GetVehicle(99), Physics(start, direction * 600)));
            Check(Math.Abs(observation.Physics.Position.X) < 60 && Math.Abs(observation.Physics.Position.Z) < 50, $"Network sweep contains high-speed boundary {direction} without tunneling.");
        }

        foreach (ArenaSpawn item in PrototypeArena.Configuration.Items)
        {
            Vector3 target = VehicleBody.ToGodot(item.Position) + new Vector3(0, 0.6f, 0);
            // A full vehicle-shaped sweep into every marker proves a drivable six metre approach.
            Vector3 start = target + new Vector3(0, 0, 6);
            proxy.Apply(Physics(start, Vector3.Zero));
            using var parameters = new PhysicsTestMotionParameters3D { From = proxy.GlobalTransform, Motion = target - start, Margin = 0.001f };
            using var result = new PhysicsTestMotionResult3D();
            bool blocked = PhysicsServer3D.BodyTestMotion(proxy.GetRid(), parameters, result);
            Check(!blocked, $"Item {item.Id} accepts a full vehicle approach and footprint.");
        }

        foreach (Vector3 obstacle in new[] { new Vector3(-17, 0.6f, 0), new Vector3(17, 0.6f, 0), new Vector3(0, 0.6f, -16), new Vector3(0, 0.6f, 16) })
        {
            var physics = Physics(obstacle + new Vector3(0, 0, 8), new Vector3(0, 0, -55));
            proxy.Apply(physics);
            var observation = proxy.Observe(WithPhysics(simulation.GetVehicle(99), physics));
            for (int step = 1; step < 12 && observation.Contacts.Count == 0; step++)
            {
                proxy.Apply(observation.Physics);
                observation = proxy.Observe(WithPhysics(simulation.GetVehicle(99), observation.Physics));
            }

            Check(observation.Contacts.Count > 0 && observation.Physics.Position.Z > obstacle.Z + 1, $"Network sweep collides predictably with fixed obstacle at {obstacle}.");
        }

        foreach (Vector3 point in new[] { new Vector3(-30, 0.55f, 0), new Vector3(30, 0.55f, 0), new Vector3(0, 0.55f, 0) })
        {
            var physics = Physics(point, Vector3.Zero);
            proxy.Apply(physics);
            var observation = proxy.Observe(WithPhysics(simulation.GetVehicle(99), physics));
            Check(observation.Surface == (point.X == 0 ? SurfaceType.Concrete : SurfaceType.Mud), $"Network support ray identifies surface at {point}.");
        }

        proxy.QueueFree();
    }

    private async Task Capture(string name, Vector3 eye, Vector3 target)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        var camera = new Camera3D { Position = eye, Current = true, Fov = 64 };
        AddChild(camera);
        camera.LookAt(target);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, name + ".png"));
        camera.QueueFree();
    }
}
