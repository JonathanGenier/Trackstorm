using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native static-contact regression scenarios through both production collision adapters.</summary>
public sealed partial class EnvironmentCollisionChecks : Node3D
{
    private int _failures;
    private string _directory = "";

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            _directory = ProjectSettings.GlobalizePath("res://.godot/environment-collision-checks");
            System.IO.Directory.CreateDirectory(_directory);
            foreach (bool network in new[] { false, true })
            {
                await Scenario(network, "scrape", new(3, 0, -35), 180);
                await Scenario(network, "direct", new(35, 0, 0), 90);
                await Scenario(network, "oblique", new(25, 0, -25), 120);
                await Scenario(network, "nitro-scrape", new(1, 0, -25), 180);
                await Scenario(network, "seams", new(3, 0, -35), 180);
                await Scenario(network, "corner", new(3, 0, -35), 180);
                await Scenario(network, "nitro-corner", new(1, 0, -25), 180);
                await Scenario(network, "exit", new(3, 0, -35), 180);
                for (int repeat = 0; repeat < 3; repeat++) { await Scenario(network, "repeated-direct-" + repeat, new(35, 0, 0), 90); }
                await Scenario(network, "rock", new(0, 0, -35), 100);
                await Scenario(network, "pillar", new(35, 0, 0), 100);
                await Scenario(network, "pillar-offcenter", new(35, 0, 0), 100);
                await Oval(network);
            }
            GD.Print($"Environment collision checks: failures={_failures}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Scenario(bool network, string name, Vector3 initialVelocity, int ticks)
    {
        var fixture = new Node3D();
        AddChild(fixture);
        var floor = Box(new(1000, -1, 0), new(500, 2, 500));
        floor.AddToGroup("landing_terrain");
        fixture.AddChild(floor);
        if (name == "rock")
        {
            var rock = GD.Load<PackedScene>("res://assets/environment/models/BoulderLow.glb").Instantiate<Node3D>();
            rock.Position = new(1000, 0, -12);
            rock.Scale = Vector3.One * 2;
            fixture.AddChild(rock);
        }
        else if (name.StartsWith("pillar", StringComparison.Ordinal))
        {
            var structure = GD.Load<PackedScene>("res://assets/maps/infield/infield_structures.glb").Instantiate<Node3D>();
            structure.Position = new(1000, 0, name == "pillar" ? -10 : -10.8f);
            fixture.AddChild(structure);
        }
        else if (name == "seams")
        {
            for (int segment = 0; segment < 30; segment++) { fixture.AddChild(Box(new(1003, 3, 5 - segment * 5), new(2, 8, 5.05f))); }
        }
        else { fixture.AddChild(Box(new(1003, 3, name == "exit" ? 90 : 0), new(2, 8, name == "exit" ? 200 : 400))); }
        if (name.Contains("corner", StringComparison.Ordinal)) { fixture.AddChild(Box(new(1000, 3, -35), new(20, 8, 2))); }
        var world = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60));
        var configuration = new VehicleConfiguration();
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        float yaw = -MathF.Atan2(initialVelocity.X, -initialVelocity.Z);
        var pose = new VehiclePhysicsState(new(1000, VehicleDimensions.RideHeight, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, yaw), VehicleBody.ToCore(initialVelocity), N.Vector3.Zero);
        world.AddVehicle(1, configuration, damage, pose);
        VehicleBody? body = null;
        NetworkVehicleBody? proxy = null;
        if (network)
        {
            proxy = new NetworkVehicleBody { VehicleId = 1 };
            fixture.AddChild(proxy);
            proxy.Apply(world.GetVehicle(1));
        }
        else
        {
            body = new VehicleBody { Position = VehicleBody.ToGodot(pose.Position), Quaternion = VehicleBody.ToGodot(pose.Orientation), LinearVelocity = initialVelocity, DamageConfiguration = damage };
            body.Initialize(world);
            fixture.AddChild(body);
        }
        float peakY = 0, peakAngular = 0;
        int contactTicks = 0;
        var trace = new List<object>();
        for (int tick = 0; tick < ticks; tick++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var input = new InputFrame(world.State.Tick + 1, 0, name.StartsWith("nitro", StringComparison.Ordinal) ? ushort.MaxValue : (ushort)0, 0, 0, 0, 0);
            VehicleObservation observation = network ? proxy!.Observe(world.GetVehicle(1)) : body!.Capture(input).Observation;
            if (observation.Contacts.Any(contact => contact.StaticObstacle)) { contactTicks++; }
            var request = new VehicleStepRequest(1, input, observation, nitro: tick == 0 && name.StartsWith("nitro", StringComparison.Ordinal) ? new NitroState(300, 2, 1.4f) : default);
            var result = world.Step(input, [request])[0];
            if (network) { proxy!.Apply(result.Snapshot); } else { body!.Apply(result); }
            var p = observation.Physics;
            peakY = Math.Max(peakY, p.LinearVelocity.Y);
            peakAngular = Math.Max(peakAngular, p.AngularVelocity.Length());
            trace.Add(new { tick, x = p.Position.X, y = p.Position.Y, z = p.Position.Z, vx = p.LinearVelocity.X, vy = p.LinearVelocity.Y, vz = p.LinearVelocity.Z, angular = p.AngularVelocity.Length(), hp = result.Snapshot.Damage.CurrentHP, contacts = observation.Contacts.Count });
        }
        var end = world.GetVehicle(1);
        string label = $"{(network ? "network" : "practice")}-{name}";
        GD.Print($"{label}: contactTicks={contactTicks}, speed={end.Speed:F2}, hp={end.Damage.CurrentHP:F1}, peakUp={peakY:F2}, peakAngular={peakAngular:F2}, position={end.ObservedPhysics.Position}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, label + ".json"), System.Text.Json.JsonSerializer.Serialize(trace));
        Check(contactTicks > 0, label + " contacts obstacle");
        Check(peakY < 5 && peakAngular < 3, label + " bounded lift/rotation");
        if (name.Contains("scrape", StringComparison.Ordinal) || name is "seams" or "exit")
        {
            Check(end.Damage.CurrentHP == 1000, label + " non-damaging");
            Check(end.ObservedPhysics.Position.Z < -25, label + " preserves along-surface travel");
        }
        else
        {
            Check(end.Damage.CurrentHP < 1000, label + " authoritative crash damage");
            if (name.Contains("direct", StringComparison.Ordinal) || name.Contains("corner", StringComparison.Ordinal)) { Check(end.Speed < 3, label + " stops"); }
        }
        fixture.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task Oval(bool network)
    {
        var map = GD.Load<PackedScene>("res://scenes/maps/oval_foundation.tscn").Instantiate<Node3D>();
        AddChild(map);
        using var data = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/maps/oval/measurements.json"));
        var sections = data.RootElement.GetProperty("sections_godot").EnumerateArray().ToArray();
        Vector3 Read(System.Text.Json.JsonElement point) => new(point[0].GetSingle(), point[1].GetSingle(), point[2].GetSingle());
        Vector3[] outer = sections.Select(section => Read(section[1])).ToArray();
        Vector3[] inner = sections.Select(section => Read(section[0])).ToArray();
        int progress = 60;
        Vector3 position = outer[progress].MoveToward(inner[progress], 1.6f) + Vector3.Up * VehicleDimensions.RideHeight;
        Vector3 tangent = (outer[progress + 2] - outer[progress]).Normalized();
        float yaw = MathF.Atan2(-tangent.X, -tangent.Z);
        var world = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60));
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        var pose = new VehiclePhysicsState(VehicleBody.ToCore(position), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, yaw), VehicleBody.ToCore(tangent * 40), N.Vector3.Zero);
        world.AddVehicle(1, new(), damage, pose);
        VehicleBody? body = null;
        NetworkVehicleBody? proxy = null;
        if (network) { proxy = new NetworkVehicleBody { VehicleId = 1 }; AddChild(proxy); proxy.Apply(world.GetVehicle(1)); }
        else { body = new VehicleBody { Position = position, Quaternion = VehicleBody.ToGodot(pose.Orientation), LinearVelocity = tangent * 40, DamageConfiguration = damage }; body.Initialize(world); AddChild(body); }
        int contacts = 0;
        float maxAngular = 0, maxNormal = 0;
        var trace = new List<object>();
        for (int tick = 0; tick < 600; tick++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var state = world.GetVehicle(1).Movement.Physics;
            position = VehicleBody.ToGodot(state.Position);
            for (int candidate = progress; candidate < progress + 12; candidate++)
            {
                if (position.DistanceSquaredTo(outer[candidate % outer.Length]) < position.DistanceSquaredTo(outer[progress % outer.Length])) { progress = candidate; }
            }
            int lookahead = (progress + 24) % outer.Length;
            Vector3 target = outer[lookahead].MoveToward(inner[lookahead], 0.8f) - position;
            Vector3 forward = VehicleBody.ToGodot(N.Vector3.Transform(-N.Vector3.UnitZ, state.Orientation));
            float angle = new Vector3(forward.X, 0, forward.Z).SignedAngleTo(new(target.X, 0, target.Z), Vector3.Up);
            var input = new InputFrame(world.State.Tick + 1, (short)(Math.Clamp(-angle * 3, -1, 1) * short.MaxValue), ushort.MaxValue, 0, 0, 0, 0);
            var observation = network ? proxy!.Observe(world.GetVehicle(1)) : body!.Capture(input).Observation;
            if (observation.Contacts.Any(contact => contact.StaticObstacle)) { contacts++; }
            var result = world.Step(input, [new VehicleStepRequest(1, input, observation)])[0];
            if (network) { proxy!.Apply(result.Snapshot); } else { body!.Apply(result); }
            maxAngular = Math.Max(maxAngular, observation.Physics.AngularVelocity.Length());
            if (observation.Support != N.Vector3.Zero) { maxNormal = Math.Max(maxNormal, Math.Abs(N.Vector3.Dot(observation.Physics.LinearVelocity, observation.Support))); }
            trace.Add(new { tick, progress, x = position.X, y = position.Y, z = position.Z, speed = result.Snapshot.Speed, hp = result.Snapshot.Damage.CurrentHP, angular = observation.Physics.AngularVelocity.Length(), contacts = observation.Contacts.Select(c => new { normal = c.Normal.ToString(), velocity = c.RelativeVelocity.ToString(), point = c.LocalPosition.ToString(), obstacle = c.StaticObstacle }).ToArray() });
        }
        string label = network ? "network-oval" : "practice-oval";
        var end = world.GetVehicle(1);
        GD.Print($"{label}: contactTicks={contacts}, sections={progress - 60}, speed={end.Speed:F2}, hp={end.Damage.CurrentHP:F1}, peakAngular={maxAngular:F2}, peakSupportSpeed={maxNormal:F2}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, label + ".json"), System.Text.Json.JsonSerializer.Serialize(trace));
        Check(contacts > 60 && progress > 130, label + " sustained barrier travel through turn");
        Check(end.Speed > 15, label + " maintains movement through sustained scrape");
        Check(end.Damage.CurrentHP == 1000 && maxAngular < 3 && maxNormal < 5, label + " harmless stable scrape");
        body?.QueueFree(); proxy?.QueueFree(); map.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string message)
    {
        if (!condition) { _failures++; GD.Print("FAIL: " + message); }
    }

    private static StaticBody3D Box(Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D { Position = position };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        return body;
    }
}
