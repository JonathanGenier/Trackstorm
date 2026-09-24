using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Repeat the collection choices through the actual hosted collision adapter.</summary>
public sealed partial class InfieldIntegrationChecks
{
    private async Task VerifyHostedPickupRoutes(ArenaConfiguration configuration, ArenaSpawn[] markers, System.Text.Json.JsonElement[] routes)
    {
        _advance = false;
        _vehicle.Freeze = true;
        _vehicle.CollisionLayer = 0;
        _companion!.Freeze = true;
        _companion.CollisionLayer = 0;
        Vector3[] Line(Vector3 a, Vector3 b) => Enumerable.Range(0, 41).Select(i => a.Lerp(b, i / 40f)).ToArray();
        Vector3[] Route(string id, int start, int count) => ReadPoints(routes.Single(route => route.GetProperty("id").GetString() == id)).Skip(start).Take(count).ToArray();
        var cases = new (int Marker, string Name, Vector3[] Path, float Speed, bool Expected)[]
        {
            (0, "easy-south", Route("SouthWestEntry", 5, 32), 14, true),
            (1, "easy-north", Route("NorthLink", 105, 34), 14, true),
            (2, "underpass", Line(new(0, 0, -30), new(0, 0, 30)), 14, true),
            (3, "water-edge", Line(new(52, 0, -43), new(102, 0, -43)), 12, true),
            (5, "west-turn", Route("WestLoop", 86, 38), 12, true),
            (6, "east-berm", Route("EastLoop", 106, 34), 12, true),
            (4, "skill-14", Line(new(-125, 0, 0), new(-25, 0, 0)), 14, true),
            (4, "skill-16", Line(new(-125, 0, 0), new(-25, 0, 0)), 16, true),
            (4, "skill-18", Line(new(-125, 0, 0), new(-25, 0, 0)), 18, true),
            (4, "ordinary-crossing", Line(new(-125, 0, 0), new(25, 0, 0)), 6, false),
            (4, "ordinary-tabletop", Line(new(-80, 0, 0), new(25, 0, 0)), 14, false),
            (4, "underpass-exclusion", Line(new(0, 0, -30), new(0, 0, 30)), 14, false),
        };
        foreach (var scenario in cases)
        {
            var proxy = new NetworkVehicleBody { VehicleId = 1 };
            AddChild(proxy);
            var world = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60), new RespawnConfiguration(), configuration);
            Vector3 origin = scenario.Path[0] with { Y = SurfaceHeight(scenario.Path[0].X, scenario.Path[0].Z) + 1 };
            Quaternion rotation = Basis.LookingAt(scenario.Path[1] - scenario.Path[0]).GetRotationQuaternion();
            world.AddVehicle(1, new VehicleConfiguration(), new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 },
                new VehiclePhysicsState(VehicleBody.ToCore(origin), new N.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), N.Vector3.Zero, N.Vector3.Zero));
            var items = new ItemAuthority();
            var pickups = new ItemSpawnAuthority(configuration, items, new ItemSelectionRandom(1));
            proxy.Apply(world.GetVehicle(1));
            await Frames(2);
            int progress = 0;
            bool collected = false;
            bool airborne = false;
            float depth = 0;
            float nearest = float.MaxValue;
            float deviation = 0;
            Vector3 awardedAt = Vector3.Zero;
            for (int frame = 0; frame < 3000 && progress < scenario.Path.Length - 2; frame++)
            {
                Vector3 position = proxy.Position with { Y = 0 };
                for (int i = progress; i < Math.Min(scenario.Path.Length, progress + 12); i++)
                {
                    if (position.DistanceTo(scenario.Path[i]) < position.DistanceTo(scenario.Path[progress])) { progress = i; }
                }
                deviation = Math.Max(deviation, position.DistanceTo(scenario.Path[progress]));
                Vector3 target = scenario.Path[Math.Min(scenario.Path.Length - 1, progress + 4)] - position;
                float angle = ((-proxy.Basis.Z) with { Y = 0 }).SignedAngleTo(target, Vector3.Up);
                float speed = world.GetVehicle(1).Movement.Physics.LinearVelocity.Length();
                float targetSpeed = Math.Abs(angle) > .35f ? 6 : scenario.Speed;
                var input = new InputFrame(world.State.Tick + 1, (short)(Math.Clamp(-angle * 2.5f, -1, 1) * short.MaxValue),
                    frame >= 90 && speed < targetSpeed ? (ushort)40000 : (ushort)0,
                    speed > targetSpeed + 1 ? (ushort)18000 : (ushort)0, 0, 0, 0);
                var observation = proxy.Observe(world.GetVehicle(1));
                depth = Math.Max(depth, observation.WaterDepth);
                world.Step(input, new[] { new VehicleStepRequest(1, input, observation) });
                proxy.Apply(world.GetVehicle(1));
                pickups.Advance(world);
                nearest = Math.Min(nearest, proxy.Position.DistanceTo(VehicleBody.ToGodot(markers[scenario.Marker].Position)));
                if (pickups.TryPickup(world, markers[scenario.Marker].Id, 1))
                {
                    collected = true;
                    airborne = !world.GetVehicle(1).Movement.Grounded;
                    awardedAt = proxy.Position;
                }
                if (_camera is not null)
                {
                    _camera.Position = proxy.Position + proxy.Basis.Z * 12 + Vector3.Up * 5;
                    _camera.LookAt(proxy.Position - proxy.Basis.Z * 8);
                }
                await Frames(1);
            }
            Check(progress >= scenario.Path.Length - 2 && collected == scenario.Expected && depth == 0 && world.GetVehicle(1).Damage.CurrentHP == 1000 && deviation < 6.6f,
                $"Hosted {scenario.Name}: progress {progress}/{scenario.Path.Length}, collected {collected} (expected {scenario.Expected}), nearest {nearest:F3} m, depth {depth:F3} m, HP {world.GetVehicle(1).Damage.CurrentHP}, deviation {deviation:F3} m; award {awardedAt}, airborne={airborne}.");
            if (scenario.Name.StartsWith("skill", StringComparison.Ordinal)) { Check(airborne, "Hosted skill award is airborne."); }
            proxy.QueueFree();
            await Frames(2);
        }
        _vehicle.Freeze = false;
        _vehicle.CollisionLayer = 1;
        _companion.Freeze = false;
        _companion.CollisionLayer = 1;
        _advance = true;
    }
}
