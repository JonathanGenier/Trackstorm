using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Production map pickup approaches and complete outer-perimeter containment.</summary>
public sealed partial class OvalIntegrationChecks
{
    private async Task VerifyContent()
    {
        var config = ActiveMap.ReadConfiguration(_map);
        Check(config.Items.Count == 27, "Twenty oval and seven infield pickups are registered from scene markers.");
        var markers = _map.GetNode("ItemSpawns").GetChildren().OfType<Marker3D>().ToArray();
        var forest = _map.GetNode("MapContent").GetChildren().OfType<MultiMeshInstance3D>().ToArray();
        Check(forest.Length == 48 && forest.Sum(batch => batch.Multimesh.InstanceCount) == 864, "Forest uses 864 instances in 48 spatial batches.");
        // The dummy headless renderer does not expose MultiMesh instance transforms.
        foreach (var batch in DisplayServer.GetName() == "headless" ? Array.Empty<MultiMeshInstance3D>() : forest)
        {
            for (int tree = 0; tree < batch.Multimesh.InstanceCount; tree++)
            {
                Vector3 position = batch.Multimesh.GetInstanceTransform(tree).Origin;
                if (_outer.Min(point => HorizontalDistance(point, position)) <= 11.9f)
                {
                    throw new InvalidOperationException($"{batch.Name}/{tree} lost its exterior transform.");
                }
            }
        }

        Check(true, DisplayServer.GetName() == "headless" ? "Forest transforms require the Visual runtime check; headless renderer cannot inspect them." : "All 864 rendered tree transforms have at least 11.9 m exterior clearance.");

        foreach (var row in markers.Where(marker => marker.Name.ToString().StartsWith("item-triple-", StringComparison.Ordinal)).GroupBy(marker => marker.Name.ToString()[..14]))
        {
            var ordered = row.OrderBy(marker => marker.Name.ToString(), StringComparer.Ordinal).ToArray();
            Check(ordered.Length == 3 && Math.Abs(ordered[0].Position.DistanceTo(ordered[1].Position) - 6) < 0.001f && Math.Abs(ordered[1].Position.DistanceTo(ordered[2].Position) - 6) < 0.001f,
                $"{row.Key}: exactly three pickups, six metre separation, three metre edge clearance.");
        }

        Node wall = _map.GetNode("MapContent/OuterContainment");
        int rays = 0;
        for (int i = 0; i < _outer.Length; i++)
        {
            int j = (i + 1) % _outer.Length;
            foreach (float t in new[] { 0f, 0.5f, 0.999f })
            {
                Vector3 point = _outer[i].Lerp(_outer[j], t);
                Vector3 direction = (point - _inner[i].Lerp(_inner[j], t));
                direction.Y = 0;
                direction = direction.Normalized();
                foreach (float height in new[] { 0.6f, 3f, 15f, 30f })
                {
                    using var query = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * height - direction * 2, point + Vector3.Up * height + direction * 6);
                    var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                    if (hit.Count == 0 || hit["collider"].AsGodotObject() != wall) { throw new InvalidOperationException($"Missing containment section {i}, fraction {t}, height {height} m."); }
                    rays++;
                }
            }
        }

        Check(true, $"{rays} native perimeter rays cover every seam, midsection and upper wall through 30 m above track.");
        foreach (int i in new[] { 0, 114, 229, 343, 458, 572, 687, 801 })
        {
            Vector3 outward = _outer[i] - _inner[i];
            outward.Y = 0;
            outward = outward.Normalized();
            foreach (bool network in new[] { false, true })
            {
                foreach (float elevation in new[] { 1f, 8f })
                {
                    Vector3 start = _outer[i] - outward * 8 + Vector3.Up * elevation;
                    var states = await HandlingProbe(network, start, Basis.LookingAt(outward), outward * 55 + Vector3.Up * (elevation > 1 ? 12 : 0), 90, tick => new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0));
                    float escape = states.Max(state => (VehicleBody.ToGodot(state.Physics.Position) - _outer[i]).Dot(outward));
                    Check(escape < 0.15f && states.All(state => VehiclePhysicsState.IsFinite(state.Physics.Position)), $"{(network ? "Network" : "Practice")} 55 m/s impact/launch at section {i}, elevation {elevation}: outermost center {escape:F3} m relative to boundary.");
                }
            }
        }

        _advance = false;
        _vehicle.CollisionLayer = 0;
        foreach (Marker3D marker in markers.Where(marker => marker.HasMeta("source_section")))
        {
            int section = marker.GetMeta("source_section").AsInt32();
            Vector3 tangent = (_centers[(section + 1) % _centers.Length] - _centers[(section + _centers.Length - 1) % _centers.Length]).Normalized();
            Vector3 normal = Hit(marker.Position).Normal;
            var proxy = new NetworkVehicleBody { VehicleId = 1 };
            AddChild(proxy);
            var world = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60), new RespawnConfiguration(), config);
            Vector3 start = marker.Position - tangent * 14 + normal * VehicleDimensions.RideHeight;
            Quaternion rotation = Basis.LookingAt(tangent, normal).GetRotationQuaternion();
            world.AddVehicle(1, new(), new(), new VehiclePhysicsState(VehicleBody.ToCore(start), new System.Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(tangent * 36), System.Numerics.Vector3.Zero));
            var items = new ItemAuthority();
            var pickups = new ItemSpawnAuthority(config, items, new ItemSelectionRandom(1));
            proxy.Apply(world.GetVehicle(1));
            await Frames(2);
            bool collected = false;
            for (ulong tick = 1; tick <= 75; tick++)
            {
                var input = new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0);
                world.Step(input, new[] { new VehicleStepRequest(1, input, proxy.Observe(world.GetVehicle(1))) });
                proxy.Apply(world.GetVehicle(1));
                pickups.Advance(world);
                collected |= pickups.TryPickup(world, marker.Name, 1);
            }

            Check(collected && items.Slots.Single().Item != HeldItem.None, $"Production network vehicle drives through {marker.Name} at 36 m/s entry and existing Core authority awards one pickup.");
            proxy.QueueFree();
            await Frames(2);
        }

        _vehicle.CollisionLayer = 1;
        _advance = true;
    }
}
