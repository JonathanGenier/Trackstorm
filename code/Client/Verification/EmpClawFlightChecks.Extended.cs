using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class EmpClawFlightChecks
{
    private async Task LongFlight()
    {
        Configure(new() { ["items.emp_claw_lifetime"] = 60 });
        _positions[1] = new(0, 110, 0); _positions[2] = new(0, 111.81f, -90); _positions[3] = new(80, 110, 50);
        ulong id = await Fire(0, 2);
        ulong deadline = Host.Items.Missiles.Single(p => p.Id == id).EmpClaw!.ExpiresAtTick;
        _positions[2] = new(0, 111.81f, 90);
        await Frames(120);
        var returning = Host.Items.Missiles.Single(p => p.Id == id);
        Check(returning.Velocity.Z > 0, "A retained target behind the flight path causes a bounded physical turn.");
        _positions[1] = returning.Position + N.Vector3.Normalize(returning.Velocity) * .2f;
        await Frames(2);
        Check(Host.Items.Missiles.Any(p => p.Id == id), "Returning guided shot passes through its owner without impact.");
        await Capture("04-returning-owner-immunity", VehicleBody.ToGodot(returning.Position + new N.Vector3(12, 6, 5)), VehicleBody.ToGodot(returning.Position));
        _positions[1] = new(0, 110, 0);
        // A faster synthetic rival keeps the shot alive; it remains the original selected identity.
        while (Host.World.State.Tick < deadline - 1)
        {
            var shot = Host.Items.Missiles.Single(p => p.Id == id);
            _positions[2] = shot.Position + N.Vector3.Normalize(shot.Velocity) * 90;
            await Frames(1);
        }
        Check(Host.Items.Missiles.Single(p => p.Id == id).RemainingTicks == 1, "Claw remains alive through the penultimate 60-second tick.");
        await Frames(1);
        Check(Host.Items.Missiles.All(p => p.Id != id), "Exact 3,600-tick deadline removes the claw.");
        await Frames(60);
        Check(_arenas.All(a => a.Driver.ItemState?.Missiles.Count == 0), "Long-lived expiry converges on all three peers.");
        Check(_events.All(es => es.All(e => e.Token != id || !e.Impact)), "Lifetime expiry never fabricates a contact.");
        Record(new { scenario = "sixty-seconds-return-loop", id, deadline, returningVelocity = returning.Velocity, passed = true });
    }

    private async Task ProductionTerrain()
    {
        using var gateway = new GameNetworkingSocketsTransport();
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
        reservation.Close(); gateway.Listen(TransportEndpoint.DirectIp(endpoint));
        var view = new SubViewport { OwnWorld3D = true, Size = new(1280, 720), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var display = new SubViewportContainer(); AddChild(display); display.AddChild(view);
        var arena = new NetworkVehicleArena(); arena.Initialize(gateway, 2971, 0); view.AddChild(arena);
        var observer = new Camera3D { Far = 3000 }; arena.AddChild(observer);
        int frame = 0;
        string? impactCollider = null;
        var native = arena.Driver.CollideMissile!;
        arena.Driver.CollideMissile = (shot, end) =>
        {
            var fraction = native(shot, end);
            if (fraction.HasValue)
            {
                using var hit = Ray(VehicleBody.ToGodot(shot.Position), VehicleBody.ToGodot(end));
                impactCollider = hit.Count == 0 ? "unknown" : ((Node)hit["collider"].AsGodotObject()).GetPath().ToString();
            }
            return fraction;
        };
        async Task Step(InputFrame input = default)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (arena.Driver.LocalState is { } car)
            { arena.Driver.DesiredAim = N.Vector3.Transform(-N.Vector3.UnitZ, car.ObservedPhysics.Orientation); }
            arena.Advance(input); frame++;
            Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
        }
        Godot.Collections.Dictionary Ray(Vector3 from, Vector3 to)
        {
            using var ray = PhysicsRayQueryParameters3D.Create(from, to, 1); ray.HitFromInside = true;
            return arena.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        }
        try
        {
            await Step(); var host = arena.Driver.Host!;
            Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.minimum_players"] = 1, ["match.countdown_ticks"] = 1, ["items.emp_claw_lifetime"] = 12 }, out _), "Production flight tuning.");
            for (int i = 0; i < 120; i++) { await Step(); }
            foreach (var c in new[] { (Name: "table-east", Start: -95f, Fire: -63f, Direction: 1f), (Name: "table-west", Start: 95f, Fire: 63f, Direction: -1f), (Name: "bank", Start: -175f, Fire: -189f, Direction: -1f) })
            {
                if (OS.GetCmdlineUserArgs().Contains("--emp-bank") && c.Name != "bank") { continue; }
                impactCollider = null;
                using var ground = Ray(new(c.Start, 30, 0), new(c.Start, -20, 0));
                Check(ground.Count > 0, "Authored production route has native support.");
                Vector3 normal = ground["normal"].AsVector3();
                Vector3 forward = new Vector3(c.Direction, 0, 0).Slide(normal).Normalized();
                Quaternion rotation = Basis.LookingAt(forward, normal).GetRotationQuaternion();
                var pose = new VehiclePhysicsState(VehicleBody.ToCore(ground["position"].AsVector3() + Vector3.Up * 1.3f), new(rotation.X, rotation.Y, rotation.Z, rotation.W), N.Vector3.Zero, N.Vector3.Zero);
                var old = host.World.State; var car = old.Vehicles.Single();
                host.World.Restore(new SimulationState(old.Tick, old.LastInput, [new VehicleSnapshot(car.VehicleId, car.LifeId, new VehicleState(old.Tick, pose, false, false, 0, 0), car.Damage, pose)], old.Match));
                for (int i = 0; i < 90; i++) { await Step(); }
                for (int slot = 0; slot < 2; slot++) { arena.Driver.RequestItemDiscard(); await Step(); arena.Driver.RequestItemSwitch(); await Step(); }
                Check(arena.Driver.GiveDeveloperItem(HeldItem.EmpClaw), "Production EMP grant.");
                if (arena.Driver.LocalItem?.Active.Item != HeldItem.EmpClaw) { arena.Driver.RequestItemSwitch(); await Step(); }
                int driven = 0;
                while (driven++ < 1500 && c.Direction * (host.World.State.Vehicles.Single().Movement.Physics.Position.X - c.Fire) < 0)
                {
                    var state = host.World.State.Vehicles.Single();
                    Vector3 direction = VehicleBody.ToGodot(N.Vector3.Transform(-N.Vector3.UnitZ, state.Movement.Physics.Orientation)) with { Y = 0 };
                    Vector3 target = new(c.Direction * 10, 0, -state.Movement.Physics.Position.Z);
                    short steer = (short)(Math.Clamp(-direction.SignedAngleTo(target, Vector3.Up) * 2.5f, -1, 1) * short.MaxValue);
                    await Step(new InputFrame((ulong)frame, steer, 20000, 0, InputButtons.None, InputButtons.None, InputButtons.None));
                }
                Check(driven < 1500, "Native driving reaches production firing point.");
                for (int i = 0; i < 500 && !(arena.Driver.LocalItem is { } slot && arena.Bodies[1].Rack.IsEmpClawReady(slot)); i++) { await Step(); }
                Check(arena.Driver.RequestItemUse(), "Production ready EMP use.");
                await Step();
                Check(host.Items.Missiles.Count == 1, "Production shot entered authoritative flight.");
                var launch = host.Items.Missiles.Single();
                float farthest = launch.Position.X;
                ItemEvent? impact = null;
                for (int tick = 0; tick < 800 && host.Items.Missiles.Count > 0; tick++)
                {
                    var shot = host.Items.Missiles.Single();
                    farthest = c.Direction > 0 ? Math.Max(farthest, shot.Position.X) : Math.Min(farthest, shot.Position.X);
                    await Step();
                    impact ??= host.Items.Events.FirstOrDefault(e => e.Impact && e.Item == HeldItem.EmpClaw);
                    if (tick == (c.Name == "bank" ? 10 : 70) && DisplayServer.GetName() != "headless")
                    {
                        observer.Position = VehicleBody.ToGodot(shot.Position) + new Vector3(6, 4, 6);
                        observer.LookAt(VehicleBody.ToGodot(shot.Position)); observer.Current = true;
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        view.GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, "production-" + c.Name + ".png"));
                    }
                }
                bool passed = c.Name == "bank" ? impactCollider?.Contains("PhysicalPerimeter", StringComparison.Ordinal) == true : c.Direction * farthest > 60;
                Record(new { scenario = "production-" + c.Name, launch = launch.Position, farthestX = farthest, impact = impact?.Position, impactCollider, passed });
                Check(passed, c.Name + " traverses the authored tabletop/bank without a premature ground hit.");
                Check(host.Items.Missiles.Count == 0, "Production flight cleans up.");
                observer.Current = false;
            }
        }
        finally
        {
            arena.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            display.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
