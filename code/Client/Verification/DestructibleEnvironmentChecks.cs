using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native staged-rock and soft-cover interaction through both production vehicle adapters.</summary>
public sealed partial class DestructibleEnvironmentChecks : Node3D
{
    private int _failures;
    private string _directory = "";
    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            _directory = ProjectSettings.GlobalizePath("res://.godot/destructible-checks");
            System.IO.Directory.CreateDirectory(_directory);
            AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
            AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightEnergy = 1.4f });
            var camera = new Camera3D { Current = true, Position = new(10, 10, 16) };
            AddChild(camera); camera.LookAt(Vector3.Zero);
            var production = ActiveMap.Load();
            AddChild(production);
            var configuration = ActiveMap.ReadConfiguration(production);
            Check(configuration.Environment is { Rocks.Count: 147, Plants.Count: 2447 }, "actual production rock/plant coverage");
            var productionView = new DestructibleEnvironment(production);
            var productionAuthority = new EnvironmentAuthority(configuration.Environment!);
            productionView.Apply(productionAuthority.Snapshot(1, 0), true);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            production.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (bool network in new[] { false, true })
            {
                await Scenario(network, 1, 22);
                await Scenario(network, 2, 12);
                await Scenario(network, 3, 12);
                await Scenario(network, 3, 22);
                await Scenario(network, 3, 35);
            }
            await NetworkConvergence();
            GD.Print($"Destructible environment checks: failures={_failures}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Scenario(bool network, byte stage, float speed)
    {
        var fixture = new Node3D(); AddChild(fixture);
        var floor = new StaticBody3D(); floor.AddToGroup("landing_terrain");
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(1000, 2, 1000) }, Position = new(0, -1, 0) });
        floor.AddChild(VehicleBody.Box(new(1000, 2, 1000), new(0, -1, 0), new Color("80634b")));
        fixture.AddChild(floor);
        var dressing = new Node3D { Name = "EnvironmentDressing" }; fixture.AddChild(dressing);
        var rock = GD.Load<PackedScene>("res://assets/environment/models/BoulderLow.glb").Instantiate<Node3D>();
        rock.Name = "BoulderLow_000"; dressing.AddChild(rock);
        // Retain a real smallest-production-size reference away from the driving line.
        var small = GD.Load<PackedScene>("res://assets/environment/models/BoulderLow.glb").Instantiate<Node3D>();
        small.Name = "BoulderLow_001"; small.Position = new(8, 0, 0); small.Scale = Vector3.One * 0.25f; dressing.AddChild(small);
        var cover = new Node3D { Name = "GroundCover" }; dressing.AddChild(cover);
        var plant = GD.Load<PackedScene>("res://assets/environment/models/ScrubLow.glb").Instantiate<Node3D>();
        var plantMesh = plant.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().First().Mesh;
        var batch = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = plantMesh, InstanceCount = 1 } };
        batch.Multimesh.Buffer = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 6]; cover.AddChild(batch); plant.Free();
        var layout = DestructibleEnvironment.ReadLayout(fixture)!;
        var authority = new EnvironmentAuthority(layout);
        authority.Restore(new(1, 0, [new(stage, 0, 0, default, default), new(3, 0, 0, default, default)], [false]));
        var presentation = new DestructibleEnvironment(fixture);
        presentation.Apply(authority.Snapshot(1, 0), true);
        var world = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60));
        var damage = new DamageConfiguration { MaxHP = 10000, CollisionScale = 5 };
        var pose = new VehiclePhysicsState(new(stage == 3 ? 0.8165f : 0, VehicleDimensions.RideHeight, 16), N.Quaternion.Identity, new(0, 0, -speed), N.Vector3.Zero);
        world.AddVehicle(1, new(), damage, pose);
        VehicleBody? body = null; NetworkVehicleBody? proxy = null;
        if (network) { proxy = new() { VehicleId = 1 }; fixture.AddChild(proxy); proxy.Apply(world.GetVehicle(1)); }
        else { body = new() { Position = VehicleBody.ToGodot(pose.Position), LinearVelocity = VehicleBody.ToGodot(pose.LinearVelocity), DamageConfiguration = damage }; body.Initialize(world); fixture.AddChild(body); }
        float up = 0, rotation = 0;
        bool plantCleared = false; int contacts = 0;
        var traces = new List<object>();
        var items = new ItemAuthority(new() { MaximumDamage = 300 });
        for (int frame = 0; frame < 240; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var input = new InputFrame(world.State.Tick + 1, 0, ushort.MaxValue, 0, 0, 0, 0);
            var observation = network ? proxy!.Observe(world.GetVehicle(1)) : body!.Capture(input).Observation;
            var request = new VehicleStepRequest(1, input, observation);
            var result = world.Step(input, [request])[0];
            authority.Advance(input.Tick, [request], [], items);
            presentation.Apply(authority.Snapshot(1, input.Tick));
            if (network) { proxy!.Apply(result.Snapshot); } else { body!.Apply(result); }
            up = Math.Max(up, observation.Physics.LinearVelocity.Y);
            rotation = Math.Max(rotation, observation.Physics.AngularVelocity.Length());
            contacts += observation.Contacts.Count(c => c.EnvironmentRock == 1);
            plantCleared |= authority.Snapshot(1, input.Tick).Plants[0];
            traces.Add(new { frame, position = observation.Physics.Position.ToString(), up = observation.Physics.LinearVelocity.Y, angular = observation.Physics.AngularVelocity.Length(), stage = authority.Snapshot(1, input.Tick).Rocks[0].Stage });
            if (frame == 90 && DisplayServer.GetName() != "headless")
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_directory, $"{network}-{stage}-{speed}.png"));
            }
        }
        var final = authority.Snapshot(1, world.State.Tick);
        Check(plantCleared, $"{network} plant drive-over clears");
        Check(batch.Multimesh.GetInstanceTransform(0).Basis.Scale.LengthSquared() == 0, "plant presentation cleared");
        if (stage == 1)
        {
            Check(contacts > 0 && final.Rocks[0].Damage > 0, $"{network} actual static-rock impact contributes damage");
            authority.Advance(world.State.Tick + 1, [], [new(1, 1, HeldItem.Missile, N.Vector3.Zero, true)], items);
            Check(authority.Snapshot(1, world.State.Tick + 1).Rocks[0].Stage == 2, "weapon and vehicle share staged damage");
        }
        if (stage == 2) { Check(final.Rocks[0].Stage == 3, $"{network} movable rock breaks on drive-over"); }
        if (stage == 3)
        {
            Check(contacts == 0 && up < 1.5f && rotation < 1 && world.GetVehicle(1).ObservedPhysics.Position.Z < -15 && world.GetVehicle(1).ObservedPhysics.Position.Y > 1, $"{network} smallest rock stable drive-over at {speed}");
        }
        GD.Print($"destructible native network={network} stage={stage} speed={speed}: up={up:F3} angular={rotation:F3} contactCount={contacts} end={world.GetVehicle(1).ObservedPhysics.Position} rock={final.Rocks[0]} plant={plantCleared}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_directory, $"{network}-{stage}-{speed}.json"), System.Text.Json.JsonSerializer.Serialize(traces));
        presentation.Apply(new EnvironmentAuthority(layout).Snapshot(2, 0), true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (DisplayServer.GetName() != "headless") { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
        var restoredPlant = batch.Multimesh.GetInstanceTransform(0);
        Check(rock.Visible && restoredPlant.IsEqualApprox(new Transform3D(Basis.Identity, new Vector3(0, 0, 6))), $"native reset restores authored rock and plant: visible={rock.Visible} plant={restoredPlant}");
        fixture.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool condition, string label) { if (!condition) { _failures++; GD.Print("FAIL: " + label); } }

    private async Task NetworkConvergence()
    {
        using var socket = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((System.Net.IPEndPoint)socket.Client.LocalEndPoint!).Port}"; socket.Close();
        var gateways = new List<GameNetworkingSocketsTransport>();
        var arenas = new List<NetworkVehicleArena>();
        var views = new List<SubViewport>();
        void Join()
        {
            var gateway = new GameNetworkingSocketsTransport(); ulong server = 0;
            if (gateways.Count == 0) { gateway.Listen(Core.Networking.Transport.TransportEndpoint.DirectIp(endpoint)); gateway.ConfigureSimulation(new Core.Networking.Transport.NetworkSimulation(30, 5, 2, 0, 0)); }
            else { server = gateway.Connect(Core.Networking.Transport.TransportEndpoint.DirectIp(endpoint)); }
            gateways.Add(gateway);
            var view = new SubViewport { OwnWorld3D = true, Size = new(320, 180) }; AddChild(view); views.Add(view);
            var arena = new NetworkVehicleArena(); arena.Initialize(gateway, gateways.Count == 1 ? 162ul : 0, server); view.AddChild(arena); arenas.Add(arena);
        }
        async Task Frames(int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                foreach (var arena in arenas) { arena.Advance(default); }
            }
        }
        try
        {
            Join(); Join(); await Frames(180);
            Check(arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2), "two native UDP peers admitted");
            EnvironmentRecoveryFixture.Seed(arenas[0]); await Frames(90);
            EnvironmentRecoveryFixture.Verify(arenas[1]);
            Join(); await Frames(180);
            Check(arenas.All(a => a.Driver.Latest?.Vehicles.Count == 3), "late peer admitted");
            EnvironmentRecoveryFixture.Verify(arenas[2]);
            var host = arenas[0].Driver.Host!;
            var current = host.Environment!.Snapshot(host.SessionId, host.World.State.Tick);
            host.Environment.Restore(new(current.Session, current.Tick, current.Rocks.Select(_ => new EnvironmentRockState(3, 0, 0, default, default)), current.Plants.Select(_ => true)));
            await Frames(600);
            foreach (var arena in arenas)
            {
                var state = arena.Driver.EnvironmentState!;
                Check(state.Rocks.All(r => r.Stage == 3) && state.Plants.All(p => p), "complete destruction converges and stays cleared under loss");
                Check(arena.Map.FindChildren("*", "RigidBody3D", true, false).Count == 0, "no unbounded dynamic debris");
            }
            GD.Print("Environment UDP convergence: three native worlds, late join, 30ms delay/5ms jitter/2% loss, all 147 rocks and 2447 plants, 600-tick sustained cleared state.");
        }
        finally
        {
            foreach (var view in views) { view.QueueFree(); }
            foreach (var gateway in gateways) { gateway.Dispose(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
