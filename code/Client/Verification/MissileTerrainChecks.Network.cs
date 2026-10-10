using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class MissileTerrainChecks
{
    private async Task RunNetwork()
    {
        var gateways = new List<GameNetworkingSocketsTransport>();
        var arenas = new List<NetworkVehicleArena>();
        var views = new List<SubViewport>();
        int[] motions = new int[3];
        bool[] curved = new bool[3];
        int[] uses = new int[3];
        try
        {
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            reservation.Close();
            for (int i = 0; i < 3; i++)
            {
                var gateway = new GameNetworkingSocketsTransport(); gateways.Add(gateway);
                ulong server = 0;
                if (i == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
                else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
                var view = new SubViewport { OwnWorld3D = true, Size = new(320, 180), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
                AddChild(view); views.Add(view);
                var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
                arena.Initialize(gateway, i == 0 ? 88ul : 0, server); view.AddChild(arena); arenas.Add(arena);
                var floor = new StaticBody3D { Position = new(0, 99, -200), CollisionLayer = 1 };
                floor.SetMeta("surface_identity", "Asphalt");
                floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 2, 600) } }); arena.AddChild(floor);
                int index = i;
                arena.Driver.ProjectileMotionReceived += missiles => { motions[index]++; curved[index] |= missiles.Any(m => m.Velocity.Y < -0.01f); };
                arena.Driver.ItemsReceived += p => uses[index] += p.Events.Count(e => e.Item == HeldItem.Missile && !e.Impact);
            }
            async Task Frames(int count)
            {
                for (int frame = 0; frame < count; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    foreach (var arena in arenas) { arena.Advance(default); Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure); }
                }
            }
            for (int i = 0; i < 600 && !arenas.All(a => a.Driver.Latest?.Vehicles.Count == 3); i++) { await Frames(1); }
            Require(arenas.All(a => a.Driver.Latest?.Vehicles.Count == 3), "Three native peers connected");
            var host = arenas[0].Driver.Host!;
            Require(host.TryConfigure(0, new Dictionary<string, double> { ["items.missile_lifetime_ticks"] = 90, ["items.missile_response"] = 6, ["items.missile_turn_rate"] = 30 }, out _), "Host terrain tuning accepted");
            var world = host.World;
            var states = world.State.Vehicles.Select(v =>
            {
                var pose = new VehiclePhysicsState(new N.Vector3((v.VehicleId - 1) * 30, 102, 20), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
                return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(world.State.Tick, pose, false, false, 0, 0), v.Damage, pose);
            }).ToArray();
            world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, states, world.State.Match));
            await Frames(60);
            for (int wave = 0; wave < 12; wave++)
            {
                Require(host.Items.Grant(host.World, 2, HeldItem.Missile), "Repeated remote missile grant");
                for (int i = 0; i < 360 && !(arenas[1].Driver.LocalItem is { } held &&
                    held.Active.Item == HeldItem.Missile && arenas[1].Bodies[2].Rack.IsMissileReady(held)); i++) { await Frames(1); }
                Require(arenas[1].Driver.RequestItemUse(), "Remote missile request under latency");
                await Frames(35);
            }
            await Frames(150);
            Require(uses.All(count => count == 12), "All peers see exactly twelve remote launches");
            Require(curved.All(value => value), "Host and both remote peers receive curved flight");
            Require(motions.All(count => count > 10), "Repeated motion publication survives impairment");
            Require(arenas.All(a => a.Driver.ItemState!.Missiles.Count == 0), "Expiry reliably clears every peer");
            Require(arenas.All(a => a.Driver.Configuration == arenas[0].Driver.Configuration), "Terrain configuration matches on all peers");
            _results.Add(new { name = "three-peer-impaired", uses, motions, curved, delayMilliseconds = 30, jitterMilliseconds = 5, lossPercent = 2 });
            GD.Print($"Missile native network: launches={string.Join(',', uses)}, motion={string.Join(',', motions)}, curved={string.Join(',', curved)}, cleanup confirmed");
        }
        finally
        {
            foreach (var arena in arenas) { arena.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            foreach (var gateway in gateways) { gateway.ConfigureSimulation(new()); gateway.Dispose(); }
            foreach (var view in views) { view.QueueFree(); }
        }
    }
    private async Task RunOval()
    {
        var gateway = new GameNetworkingSocketsTransport();
        var view = new SubViewport { OwnWorld3D = true, Size = new(1280, 720), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var display = new SubViewportContainer(); AddChild(display); display.AddChild(view);
        NetworkVehicleArena? arena = null;
        try
        {
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            reservation.Close(); gateway.Listen(TransportEndpoint.DirectIp(endpoint));
            arena = new NetworkVehicleArena(); arena.Initialize(gateway, 701, 0); view.AddChild(arena);
            int queries = 0, suitable = 0, impacts = 0; bool moved = false;
            var query = arena.Driver.QueryMissileTerrain!;
            arena.Driver.QueryMissileTerrain = (a, b) => { queries++; var value = query(a, b); if (value is not null) { suitable++; } return value; };
            arena.Driver.ItemsReceived += p => impacts += p.Events.Count(e => e.Impact && e.Item == HeldItem.Missile);
            for (int tick = 0; tick < 540; tick++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                arena.Advance(default);
                Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
                if (tick == 60) { Require(arena.Driver.GiveDeveloperItem(HeldItem.Missile), "Oval grant"); }
                if (tick == 190) { Require(arena.Driver.RequestItemUse(), "Oval ready launch"); }
                moved |= arena.Driver.Host!.Items.Missiles.Count > 0;
                if (tick == 220 && DisplayServer.GetName() != "headless")
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    view.GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, "production-oval.png"));
                }
            }
            Require(moved && queries > 10 && suitable > 10, "Production oval supplies authored terrain during actual missile flight");
            Require(arena.Driver.Host!.Items.Missiles.Count == 0, "Oval shot eventually impacts or expires");
            Require(impacts <= 1, "Oval shot impacts at most once");
            _results.Add(new { name = "production-oval", queries, suitable, impacts });
            GD.Print($"Missile production oval: {queries} probes, {suitable} suitable hits, {impacts} impacts, cleanup confirmed");
        }
        finally
        {
            arena?.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            gateway.Dispose(); display.QueueFree();
        }
    }

}
