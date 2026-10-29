using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WorldWallChecks
{
    private bool TerrainStability => OS.GetCmdlineUserArgs().Contains("--world-wall-terrain");
    private readonly Dictionary<ulong, N.Vector3> _settled = new();
    private float _peakDrift, _peakSpeed;
    private long _soakFits;
    private int _terrainCycles;
    private static Vector3 TerrainOrigin(int scenario, int player) => new(300 + scenario * 45 + player * 16, 200, 0);

    private static void AddTerrainFixtures(NetworkVehicleArena arena)
    {
        AddFloor(arena, new(800, 200, 0), new(14, 1, 30), new(0, 0, Mathf.DegToRad(45)));
        AddFloor(arena, new(1000, 200, 0), new(60, 1, 60), new(Mathf.DegToRad(15), 0, Mathf.DegToRad(-20)));
        for (int scenario = 0; scenario < 8; scenario++)
        for (int player = 0; player < 2; player++)
        {
            var origin = TerrainOrigin(scenario, player);
            var rotation = scenario switch
            {
                1 => new Vector3(Mathf.DegToRad(20), 0, 0),
                2 => new Vector3(0, 0, Mathf.DegToRad(30)),
                3 => new Vector3(Mathf.DegToRad(15), 0, Mathf.DegToRad(-20)),
                6 => new Vector3(0, 0, Mathf.DegToRad(35)),
                _ => Vector3.Zero,
            };
            AddFloor(arena, origin + (scenario == 5 ? new Vector3(-.4f, 0, 0) : Vector3.Zero),
                scenario == 5 ? new Vector3(5.6f, 1, 30) : new Vector3(14, 1, 30), rotation);
            if (scenario is 4 or 7)
            {
                // A small rise under a wing, outside the former +/- depth/2 probes.
                AddFloor(arena, origin + new Vector3(2.4f, .55f, -.7f), new(1, .2f, 1), Vector3.Zero);
                if (scenario == 7) { AddFloor(arena, origin + new Vector3(-1, .52f, 0), new(1, .1f, 1), Vector3.Zero); }
            }
        }
    }

    private static void AddFloor(Node parent, Vector3 position, Vector3 size, Vector3 rotation)
    {
        var floor = new StaticBody3D { Position = position, Rotation = rotation, CollisionLayer = 1 };
        floor.AddToGroup("landing_terrain");
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        floor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.25f, .3f, .25f) } });
        parent.AddChild(floor);
    }

    private void AdvanceTerrain()
    {
        var host = _arenas[0].Driver.Host!;
        int elapsed = _frame - _boundary;
        Check(elapsed < 4000, $"Terrain stage {_stage}, scenario {_scenario} timeout");
        if (_stage == 2)
        {
            for (ulong id = 1; id <= 2; id++)
            {
                if (host.Items.Shields.Any(s => s.Attached && s.Owner == id)) { PositionTerrainVehicle(id); }
                else { Position(id, new(0, 202, -100 - (int)id * 12), N.Vector3.Zero); }
            }
        }
        for (int i = 0; i < _arenas.Count; i++)
        {
            bool use = _stage == 2 && elapsed == 1;
            _arenas[i].Advance(new((ulong)_frame, 0, 0, 0, use ? InputButtons.UseItem : 0, use ? InputButtons.UseItem : 0, 0));
            Check(_arenas[i].Driver.Failure.Length == 0, _arenas[i].Driver.Failure);
        }
        _boundaries[host.World.State.Tick] = host.Items.Shields.ToArray();
        foreach (var wall in host.Items.Shields.Where(s => !s.Attached))
        {
            Check(!wall.Tipping && N.Vector3.Transform(N.Vector3.UnitY, wall.Orientation).Y >= .76f, "Terrain cannot tip a standing wall");
            Check(float.IsFinite(wall.Position.Y) && wall.LinearVelocity.Length() < 3, $"No terrain launch: case {_scenario}, wall {wall.Id}, pose {wall.Position}, velocity {wall.LinearVelocity}");
            if (_settled.TryGetValue(wall.Id, out var start))
            {
                _peakDrift = Math.Max(_peakDrift, N.Vector3.Distance(start, wall.Position));
                _peakSpeed = Math.Max(_peakSpeed, wall.LinearVelocity.Length());
                Check(N.Vector3.Distance(start, wall.Position) < .06f, $"Settled wall {wall.Id} drift: {N.Vector3.Distance(start, wall.Position)}");
                Check(wall.LinearVelocity.Length() < .1f, "No persistent native jitter");
            }
        }
        switch (_stage)
        {
            case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2):
                Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1,
                    ["items.shield_lifetime"] = 120, ["damage.max_hp"] = 100000 }, out _), "Configure terrain fixture with long-lived setup vehicles");
                Next(1); break;
            case 1 when host.World.State.Match?.Phase == Core.Matches.MatchPhase.Active:
                if (_scenario == 0 && !ProductionMap) { VerifyTerrainBoundaries(); }
                for (ulong id = 1; id <= 2; id++)
                {
                    host.Items.RemovePlayer(id);
                    Check(host.Items.Grant(host.World, id, HeldItem.Shield), "Repeated terrain shield grant");
                    PositionTerrainVehicle(id);
                }
                var camera = _arenas[0].GetNode<Camera3D>("WallCamera");
                var target = ProductionMap ? VehicleBody.ToGodot(host.World.GetVehicle(1).ObservedPhysics.Position) : TerrainOrigin(_scenario, 0);
                camera.Position = target + new Vector3(10, 7, 10); camera.LookAt(target + Vector3.Up);
                Next(10); break;
            case 10 when elapsed > 100 && _arenas.All(a => a.Driver.LocalItem is { } inventory && a.Bodies[a.Driver.LocalVehicleId].CanDeployShield(inventory)):
                // Place the vehicle at the selected terrain again after unfolding, so
                // slope acceleration does not choose a different test location.
                for (ulong id = 1; id <= 2; id++)
                {
                    PositionTerrainVehicle(id);
                }
                Next(2); break;
            case 2 when elapsed > 120:
                Check(host.Items.Shields.Count(s => !s.Attached) == (_scenario + 1) * 2, $"Both players deploy on terrain case {_scenario}");
                foreach (var wall in host.Items.Shields.Where(s => !s.Attached && !_settled.ContainsKey(s.Id)))
                {
                    _settled[wall.Id] = wall.Position;
                    VerifyFootClearance(_arenas[0].Walls.Bodies[wall.Id], wall);
                }
                VerifyBoundaries(); Capture($"terrain-{_scenario}.png");
                Record($"Terrain {_scenario}: two normal-use deployments, full panel footing clear, upright, exact UDP state.");
                if (++_scenario < 8) { Next(1); } else { _soakFits = ShieldGround.Fits; Next(3); }
                break;
            case 3 when elapsed >= 1800:
                Check(_arenas.All(a => a.Walls.Bodies.Count == 16), "All sixteen terrain walls survive the soak on both peers");
                VerifyBoundaries();
                Record($"Sixteen varied-terrain walls, 1800-frame final soak; peak settled drift {_peakDrift:F6} m, speed {_peakSpeed:F6} m/s.");
                if (ShieldGround.Profile) { Check(ShieldGround.Fits - _soakFits < 32, "Settled walls do not repeat terrain fitting every tick"); }
                foreach (var wall in host.Items.Shields.ToArray())
                { host.Items.DamageShield(host.World, wall.Id, wall.DamageSequence + 1, 1000, new("world", 0, "fixture-cleanup")); }
                Next(4); break;
            case 4 when elapsed >= 180:
                Check(_arenas.All(a => a.Walls.Bodies.Count == 0 && a.Walls.GetChildCount() == 0), "All wall bodies, shapes, visuals and timed debris are released");
                Check(Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) == 0, "No orphan nodes after repeated wall destruction");
                Record($"Cycle {_terrainCycles + 1}: all sixteen walls and destruction effects removed on both peers; zero orphan nodes.");
                _settled.Clear();
                if (++_terrainCycles < 2) { _scenario = 0; Next(1); break; }
                System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "terrain-evidence.txt"), _evidence);
                GD.Print("World wall integration passed: varied terrain, repeated deployment, sixteen-wall stability soak and UDP convergence.");
                Finish(0); break;
        }
    }

    private void VerifyTerrainBoundaries()
    {
        var space = _arenas[0].GetWorld3D().DirectSpaceState;
        var ground = new ShieldGround(new(6.6f, 2.5f, .6f));
        Check(ground.Seat(space, new(9000, 202, 0), Vector3.Back) is null, "Missing terrain rejects placement");
        Check(ground.Seat(space, new(800, 202, 0), Vector3.Back) is null, "Excessive slope rejects placement");
        Check(ground.Seat(space, TerrainOrigin(5, 0) + new Vector3(3, 2, 0), Vector3.Back) is null, "Large unsupported overhang rejects placement");
        foreach (var size in new[] { new Vector3(3, 2, .3f), new Vector3(12, 6, 2), new Vector3(8, 3, 1.2f) })
        foreach (float yaw in new[] { 0f, .7f, 2.5f })
        {
            var heading = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
            var seat = new ShieldGround(size).Seat(space, new(1000, 203, 0), heading);
            Check(seat.HasValue, "Captured custom dimensions fit a compound slope");
            var pose = seat!.Value;
            Check(new Vector3(pose.Basis.Z.X, 0, pose.Basis.Z.Z).Normalized().Dot(heading) > .99999f, "Compound support preserves heading at arbitrary yaw");
            float minGap = float.PositiveInfinity, maxGap = float.NegativeInfinity;
            foreach (var point in ShieldGeometry.DeployedCorners(VehicleBody.ToCore(size)).SelectMany(c => c.Where(p => p.Y < 0)))
            {
                var foot = pose * VehicleBody.ToGodot(point);
                using var ray = PhysicsRayQueryParameters3D.Create(foot + Vector3.Up, foot - Vector3.Up, 1);
                using var hit = space.IntersectRay(ray);
                Check(hit.Count > 0, "Custom footing has support");
                float gap = foot.Y - hit["position"].AsVector3().Y;
                minGap = Math.Min(minGap, gap); maxGap = Math.Max(maxGap, gap);
            }
            Check(minGap > .01f && maxGap < .03f, $"Complete custom footing seats within 1-3 cm: {minGap}, {maxGap}");
        }
        var inverted = new Core.Vehicles.VehiclePhysicsState(new(300, 201.65f, -4.4f),
            N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, MathF.PI), N.Vector3.Zero, N.Vector3.Zero);
        var placed = _arenas[0].Walls.Place(inverted, _arenas[0].Driver.Host!.Items.Configuration, _arenas[0].Bodies[1]);
        Check(placed is { } p && N.Vector3.Transform(N.Vector3.UnitY, p.Orientation).Y > .999f, "Vehicle roll cannot deploy a sideways/upside-down wall");
        Record("Boundary checks: missing/steep/excess-overhang rejection, nine size/yaw compound-slope fits, inverted-vehicle upright placement.");
    }

    private void PositionTerrainVehicle(ulong id)
    {
        var point = TerrainOrigin(_scenario, (int)id - 1) + new Vector3(0, 0, -4.4f);
        if (ProductionMap)
        {
            var markers = _arenas[0].MapConfiguration.Items.Where(s => s.Id.StartsWith("item-infield-", StringComparison.Ordinal)).ToArray();
            var spawn = _scenario < 4 ? _arenas[0].MapConfiguration.Players[_scenario * 2 + (int)id - 1] : markers[((_scenario - 4) * 2 + (int)id - 1) % markers.Length];
            point = VehicleBody.ToGodot(spawn.Position);
        }
        using var ray = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 10, point - Vector3.Up * 10, 1);
        using var hit = _arenas[0].GetWorld3D().DirectSpaceState.IntersectRay(ray);
        Check(hit.Count != 0, "Vehicle test support exists");
        var normal = hit["normal"].AsVector3();
        // The map has seven infield markers. The sixteenth accumulated wall
        // uses the opposite side of the repeated marker, avoiding an existing wall.
        float direction = ProductionMap && _scenario == 7 && id == 2 ? -1 : 1;
        var back = new Vector3(0, -direction * normal.Z / normal.Y, direction).Normalized();
        var rotation = new Basis(normal.Cross(back), normal, back).GetRotationQuaternion();
        Position(id, VehicleBody.ToCore(hit["position"].AsVector3() + Vector3.Up * (Core.Vehicles.VehicleDimensions.RideHeight / normal.Y)),
            N.Vector3.Zero, new(rotation.X, rotation.Y, rotation.Z, rotation.W));
    }

    private static void VerifyFootClearance(ShieldWallBody body, ShieldState wall)
    {
        var space = body.GetWorld3D().DirectSpaceState;
        foreach (var corners in ShieldGeometry.DeployedCorners(wall.WallSize))
        {
            using var shape = new ConvexPolygonShape3D { Points = corners.Select(VehicleBody.ToGodot).ToArray() };
            using var query = new PhysicsShapeQueryParameters3D { Shape = shape, Transform = body.GlobalTransform, CollisionMask = 1, Margin = .001f };
            Check(space.IntersectShape(query, 1).Count == 0, "Full native panel volume clears the uneven terrain");
        }
    }
}
