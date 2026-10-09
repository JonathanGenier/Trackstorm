using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class MissileTerrainChecks
{
    private async Task RunProduction()
    {
        bool diagnose = OS.GetCmdlineUserArgs().Contains("--missile-diagnose");
        string folder = System.IO.Path.Combine(_output, diagnose ? "production-before" : "production");
        System.IO.Directory.CreateDirectory(folder);
        using var gateway = new GameNetworkingSocketsTransport();
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
        reservation.Close(); gateway.Listen(TransportEndpoint.DirectIp(endpoint));
        var view = new SubViewport { OwnWorld3D = true, Size = new(1280, 720), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var display = new SubViewportContainer(); AddChild(display); display.AddChild(view);
        var arena = new NetworkVehicleArena(); arena.Initialize(gateway, 902, 0); view.AddChild(arena);
        var observer = new Camera3D { Far = 600 };
        arena.AddChild(observer);
        var traces = new List<object>();
        var results = new List<object>();
        string scenario = "survey";
        int frame = 0;
        object Point(Vector3 v) => new { x = v.X, y = v.Y, z = v.Z };
        object? Describe(Godot.Collections.Dictionary hit) => hit.Count == 0 ? null : new
        {
            point = Point(hit["position"].AsVector3()), normal = Point(hit["normal"].AsVector3()),
            collider = ((Node)hit["collider"].AsGodotObject()).GetPath().ToString(),
            material = SurfaceIdentityResolver.Resolve(hit["collider"].AsGodotObject(), hit["position"].AsVector3())?.ToString(),
        };
        Godot.Collections.Dictionary Ray(Vector3 a, Vector3 b)
        {
            using var ray = PhysicsRayQueryParameters3D.Create(a, b, 1); ray.HitFromInside = true;
            return arena.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        }
        async Task Step(InputFrame input = default)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            arena.Advance(input); frame++;
            Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
        }
        async Task CaptureProduction(string name)
        {
            if (DisplayServer.GetName() == "headless") { return; }
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            view.GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, name + ".png"));
        }
        try
        {
            await Step();
            var query = arena.Driver.QueryMissileTerrain!;
            arena.Driver.QueryMissileTerrain = (a, b) =>
            {
                var sample = query(a, b);
                using var raw = Ray(VehicleBody.ToGodot(a), VehicleBody.ToGodot(b));
                traces.Add(new { scenario, frame, kind = "probe", from = Point(VehicleBody.ToGodot(a)), to = Point(VehicleBody.ToGodot(b)), accepted = sample is not null, hit = Describe(raw) });
                return sample;
            };
            var collide = arena.Driver.CollideMissile!;
            string? impactMaterial = null, impactCollider = null;
            Vector3? impactPosition = null, launchOrigin = null;
            arena.Driver.CollideMissile = (missile, end) =>
            {
                launchOrigin ??= VehicleBody.ToGodot(missile.Position);
                float? fraction = collide(missile, end);
                if (fraction is float f)
                {
                    using var raw = Ray(VehicleBody.ToGodot(missile.Position), VehicleBody.ToGodot(end));
                    impactPosition = VehicleBody.ToGodot(N.Vector3.Lerp(missile.Position, end, f));
                    if (raw.Count > 0)
                    {
                        impactMaterial = SurfaceIdentityResolver.Resolve(raw["collider"].AsGodotObject(), raw["position"].AsVector3())?.ToString();
                        impactCollider = ((Node)raw["collider"].AsGodotObject()).GetPath().ToString();
                    }
                    traces.Add(new { scenario, frame, kind = "impact", point = Point(impactPosition.Value), hit = Describe(raw) });
                }
                return fraction;
            };
            foreach (int x in Enumerable.Range(-90, 181).Concat(Enumerable.Range(-210, 51)))
            {
                using var hit = Ray(new(x, 30, 0), new(x, -20, 0));
                traces.Add(new { scenario, frame, kind = "survey", x, hit = Describe(hit) });
            }
            var host = arena.Driver.Host!;
            var cases = new List<(string Name, float Start, float Fire, float Direction, float Z, float Speed)>();
            foreach (float speed in diagnose ? new[] { 120f } : new[] { 60f, 120f, 180f })
            {
                cases.Add(($"table-east-{speed}", -95, -63, 1, 0, speed));
                cases.Add(($"table-west-{speed}", 95, 63, -1, 0, speed));
                cases.Add(($"bank-{speed}", -175, -189, -1, 0, speed));
            }
            cases.Add(("table-offset-north", -95, -63, 1, -5, 120));
            cases.Add(("table-offset-south", 95, 63, -1, 5, 120));
            cases.Add(("bank-mid", -188, -195, -1, 0, 120));
            string? selected = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--missile-case=", StringComparison.Ordinal))?.Split('=')[1];
            foreach (var c in cases.Where(c => selected is null || c.Name.Contains(selected, StringComparison.Ordinal)))
            {
                observer.Current = false;
                scenario = c.Name; impactPosition = null; launchOrigin = null; impactMaterial = impactCollider = null;
                Require(host.TryConfigure(0, new Dictionary<string, double> { ["items.missile_speed"] = c.Speed }, out _), "Production speed tuning");
                using var ground = Ray(new(c.Start, 30, c.Z), new(c.Start, -20, c.Z));
                Require(ground.Count > 0, "Production route begins on real ground");
                Vector3 normal = ground["normal"].AsVector3();
                Vector3 forward = new Vector3(c.Direction, 0, 0).Slide(normal).Normalized();
                Quaternion rotation = Basis.LookingAt(forward, normal).GetRotationQuaternion();
                var pose = new VehiclePhysicsState(VehicleBody.ToCore(ground["position"].AsVector3() + Vector3.Up * 1.3f), new N.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), N.Vector3.Zero, N.Vector3.Zero);
                var old = host.World.State;
                var car = old.Vehicles.Single();
                host.World.Restore(new SimulationState(old.Tick, old.LastInput,
                    [new VehicleSnapshot(car.VehicleId, car.LifeId, new VehicleState(old.Tick, pose, false, false, 0, 0), car.Damage, pose)], old.Match));
                for (int i = 0; i < 90; i++) { await Step(); }
                int driven = 0;
                while (driven++ < 1500 && c.Direction * (host.World.State.Vehicles.Single().Movement.Physics.Position.X - c.Fire) < 0)
                {
                    var state = host.World.State.Vehicles.Single();
                    Vector3 direction = VehicleBody.ToGodot(N.Vector3.Transform(-N.Vector3.UnitZ, state.Movement.Physics.Orientation)) with { Y = 0 };
                    Vector3 target = new(c.Direction * 10, 0, c.Z - state.Movement.Physics.Position.Z);
                    float angle = direction.SignedAngleTo(target, Vector3.Up);
                    short steer = (short)(Math.Clamp(-angle * 2.5f, -1, 1) * short.MaxValue);
                    float speed = state.Movement.Physics.LinearVelocity.Length();
                    await Step(new InputFrame((ulong)frame, state.Movement.Grounded ? steer : (short)0,
                        state.Movement.Grounded && speed < 12 ? ushort.MaxValue : (ushort)0,
                        state.Movement.Grounded && speed > 13 ? (ushort)18000 : (ushort)0, 0, 0, 0));
                }
                Require(driven < 1500, $"{scenario}: native drive reached firing point");
                // Real route pickups must not select another item instead of the test missile.
                for (int slot = 0; slot < 2; slot++)
                {
                    arena.Driver.RequestItemDiscard(); await Step();
                    arena.Driver.RequestItemSwitch(); await Step();
                }
                Require(arena.Driver.GiveDeveloperItem(HeldItem.Missile), "Production missile grant");
                if (arena.Driver.LocalItem?.Active.Item != HeldItem.Missile) { arena.Driver.RequestItemSwitch(); await Step(); }
                Require(arena.Driver.LocalItem?.Active.Item == HeldItem.Missile, "Selected standard missile");
                var launch = host.World.State.Vehicles.Single().Movement.Physics;
                float launchPitch = Pitch(N.Vector3.Transform(-N.Vector3.UnitZ, launch.Orientation));
                await CaptureProduction(scenario + "-launch");
                Require(arena.Driver.RequestItemUse(), "Production missile launch");
                float furthest = c.Direction * launch.Position.X, minimum = float.MaxValue, rate = 0;
                N.Vector3? lastVelocity = null;
                float deckMinimum = float.MaxValue;
                bool seen = false;
                for (int tick = 0; tick < 330; tick++)
                {
                    await Step(new InputFrame((ulong)frame, 0, 0, 0, InputButtons.Drift, 0, 0));
                    if (host.Items.Missiles.FirstOrDefault() is { } missile)
                    {
                        seen = true; furthest = Math.Max(furthest, c.Direction * missile.Position.X);
                        observer.Current = true;
                        Vector3 focus = VehicleBody.ToGodot(missile.Position);
                        observer.Position = focus + new Vector3(-c.Direction * 9, 4, 10);
                        observer.LookAt(focus + new Vector3(c.Direction * 3, 0, 0));
                        using var support = Ray(VehicleBody.ToGodot(missile.Position), VehicleBody.ToGodot(missile.Position) + Vector3.Down * 30);
                        float? clearance = support.Count == 0 ? null : missile.Position.Y - support["position"].AsVector3().Y;
                        if (clearance is float h) { minimum = Math.Min(minimum, h); if (Math.Abs(missile.Position.X) < 74) { deckMinimum = Math.Min(deckMinimum, h); } }
                        if (lastVelocity is N.Vector3 previous)
                        {
                            rate = Math.Max(rate, Math.Abs(Pitch(missile.Velocity) - Pitch(previous)) * 60);
                            Require(rate <= host.Items.Configuration.MissileTerrain.TurnRate + 0.02f, "Production pitch rate stays bounded");
                            Require(Math.Abs(previous.X * missile.Velocity.Z - previous.Z * missile.Velocity.X) < 0.02f, "Production guidance never changes horizontal heading");
                        }
                        lastVelocity = missile.Velocity;
                        traces.Add(new { scenario, frame, kind = "flight", point = Point(VehicleBody.ToGodot(missile.Position)), pitch = Pitch(missile.Velocity), clearance });
                    }
                    if (scenario.StartsWith("bank", StringComparison.Ordinal) ? tick is 1 or 3 or 6 : tick is 12 or 35 or 60) { await CaptureProduction(scenario + "-flight-" + tick); }
                    if (seen && host.Items.Missiles.Count == 0) { break; }
                }
                bool tabletop = scenario.StartsWith("table", StringComparison.Ordinal);
                bool passed = seen && (tabletop ? furthest > 95 && deckMinimum > 0.6f : impactCollider?.Contains("PhysicalPerimeter", StringComparison.Ordinal) == true);
                results.Add(new { scenario, passed, speed = c.Speed, drivenFrames = driven, launch = Point(VehicleBody.ToGodot(launch.Position)), actualLaunchOrigin = launchOrigin.HasValue ? Point(launchOrigin.Value) : null, launchDirection = Point(VehicleBody.ToGodot(N.Vector3.Transform(-N.Vector3.UnitZ, launch.Orientation))), launchPitch, furthest, deckMinimum = tabletop ? (float?)deckMinimum : null, minimumClearance = minimum, peakPitchRate = rate, impact = impactPosition.HasValue ? Point(impactPosition.Value) : null, impactMaterial, impactCollider });
                GD.Print($"Production {scenario}: pass={passed}, launch={launch.Position}, pitch={launchPitch:F3}, furthest={furthest:F3}, min={minimum:F3}, peak={rate:F3}, impact={impactPosition}, material={impactMaterial}, collider={impactCollider}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "trace.json"), JsonSerializer.Serialize(traces));
                Require(host.Items.Missiles.Count == 0, "Production projectile cleanup");
                if (!diagnose) { Require(passed, $"{scenario}: traverse tabletop or impact top barrier, never premature ground"); }
            }
        }
        finally
        {
            arena.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            display.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GC.Collect(); GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
