using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Input-driven collection, vertical exclusion and shoreline risk on the authored map.</summary>
public sealed partial class InfieldIntegrationChecks
{
    private Core.Arenas.ArenaConfiguration? _routeConfiguration;
    private ItemSpawnAuthority? _routePickups;
    private ItemAuthority? _routeItems;
    private ItemSpawnPresentation? _pickupPresentation;
    private EnvironmentPresentation? _pickupEnvironment;
    private string _routePickupId = string.Empty;
    private float _pickupDistance;
    private float _routeWaterDepth;
    private bool _routeCollected;
    private bool _collectedAirborne;
    private Vector3 _collectionPosition;

    private void ObservePickupRoute()
    {
        if (_routePickups is null || !_drive) { return; }
        _routeWaterDepth = Math.Max(_routeWaterDepth, WaterObservation.Observe(_vehicle, _vehicle.GlobalTransform));
        var marker = _routeConfiguration!.Items.Single(spawn => spawn.Id == _routePickupId);
        _pickupDistance = Math.Min(_pickupDistance, _vehicle.Position.DistanceTo(VehicleBody.ToGodot(marker.Position)));
        _routePickups.Advance(_simulation);
        if (_routePickups.TryPickup(_simulation, _routePickupId, 1))
        {
            _routeCollected = true;
            _collectedAirborne = !_vehicle.State.Grounded;
            _collectionPosition = _vehicle.Position;
            _pickupPresentation!.Apply(new ItemPublication(_simulation.State.Tick + 1,
                new Core.Networking.Replication.WorldSnapshot(1, _simulation.State.Tick, _simulation.State.Vehicles.Select(vehicle => new Core.Networking.Replication.ReplicatedVehicle(vehicle, 0))), _routeItems!.Slots,
                _routeItems.Missiles, _routeItems.Events, _routePickups.States));
        }
    }

    private async Task VerifyPickupRoutes(Node3D map, System.Text.Json.JsonElement[] routes)
    {
        if (_caseFilter.Length != 0 && _caseFilter != "Pickup") { return; }
        var configuration = ActiveMap.ReadConfiguration(map);
        _routeConfiguration = configuration;
        var markers = configuration.Items.Where(marker => marker.Id.StartsWith("item-infield-", StringComparison.Ordinal)).ToArray();
        Check(configuration.Items.Count == 27 && markers.Length == 7, "Exactly seven infield singles extend the twenty oval locations.");
        var source = GD.Load<PackedScene>("res://scenes/maps/infield_pickups.tscn").Instantiate<Node3D>();
        Check(source.GetChildren().OfType<Marker3D>().Select(marker => new Core.Arenas.ArenaSpawn(marker.Name, VehicleBody.ToCore(marker.Position)))
            .SequenceEqual(markers), "Baked map markers exactly match the seven-marker authoring scene.");
        source.Free();
        _pickupPresentation = new ItemSpawnPresentation();
        AddChild(_pickupPresentation);
        _pickupPresentation.Initialize(configuration);

        Vector3[] Line(Vector3 start, Vector3 end) => Enumerable.Range(0, 41).Select(i => start.Lerp(end, i / 40f)).ToArray();
        Vector3[] Route(string id, int start, int count) => ReadPoints(routes.Single(route => route.GetProperty("id").GetString() == id)).Skip(start).Take(count).ToArray();

        void Begin(int index)
        {
            _routeItems = new ItemAuthority();
            _routePickups = new ItemSpawnAuthority(configuration, _routeItems, new ItemSelectionRandom(1));
            _routePickupId = markers[index].Id;
            _pickupDistance = float.MaxValue;
            _routeWaterDepth = 0;
            _routeCollected = false;
            _collectedAirborne = false;
            _pickupPresentation.Apply(new ItemPublication(_simulation.State.Tick + 1,
                new Core.Networking.Replication.WorldSnapshot(1, _simulation.State.Tick, _simulation.State.Vehicles.Select(vehicle => new Core.Networking.Replication.ReplicatedVehicle(vehicle, 0))), _routeItems.Slots,
                _routeItems.Missiles, _routeItems.Events, _routePickups.States));
        }

        async Task Collect(int index, string name, Vector3[] path, float speed, bool jump = false)
        {
            Begin(index);
            await Drive("Pickup" + name, path, 16, speed, jump);
            Check(_routeCollected && _routeItems!.Slots.Single().Item != HeldItem.None,
                $"{name}: authoritative award at {_collectionPosition}, minimum distance {_pickupDistance:F3} m, airborne={_collectedAirborne}, maximum immersion={_routeWaterDepth:F3} m.");
            Check(_routeWaterDepth == 0, name + ": complete collection route remains dry.");
            if (jump) { Check(_collectedAirborne, name + ": awarded during the intended airborne trajectory."); }
        }

        await Collect(0, "EasySouth", Route("SouthWestEntry", 5, 32), 14);
        await Collect(1, "EasyNorth", Route("NorthLink", 105, 34), 14);
        // Start beyond the deck so ordinary top-down setup selects the lower route.
        await Collect(2, "Underpass", Line(new(0, 0, -30), new(0, 0, 30)), 14);
        await Collect(3, "WaterEdge", Line(new(52, 0, -43), new(102, 0, -43)), 12);
        await Collect(5, "WestTurn", Route("WestLoop", 86, 38), 12);
        await Collect(6, "EastBerm", Route("EastLoop", 106, 34), 12);
        foreach (float speed in new[] { 14f, 16f, 18f })
        {
            await Collect(4, $"SkillJump{speed}", Enumerable.Range(0, 51).Select(i => new Vector3(-125 + i * 2, 0, 0)).ToArray(), speed, true);
        }
        foreach (float speed in new[] { 6f, 10f })
        {
            Begin(4);
            await Drive($"PickupOrdinaryCrossing{speed}", Enumerable.Range(0, 126).Select(i => new Vector3(-125 + i * 2, 0, 0)).ToArray(), 16, speed);
            Check(!_routeCollected, $"Ordinary tabletop crossing at {speed} m/s cannot collect skill pickup; nearest {_pickupDistance:F3} m exceeds three-metre radius.");
        }
        Begin(4);
        await Drive("PickupUnderpassExclusion", Line(new(0, 0, -30), new(0, 0, 30)), 16, 14);
        Check(!_routeCollected, "Underpass traversal cannot collect elevated skill pickup.");

        // Deliberately continue the dry shoreline line toward the basin, with ordinary input.
        Begin(3);
        _drive = false;
        _path = Line(new(77, 0, -49), new(77, 0, -32));
        _progress = 0;
        _targetSpeed = 14;
        var rotation = Basis.LookingAt(Vector3.Back).GetRotationQuaternion();
        _vehicle.ResetBody(new Core.Vehicles.VehiclePhysicsState(new Numerics.Vector3(77, SurfaceHeight(77, -49) + 1, -49),
            new Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        await Frames(90);
        _drive = true;
        for (int frame = 0; frame < 240 && _routeWaterDepth < 1; frame++) { await Frames(1); }
        _drive = false;
        Check(_routeCollected && _routeWaterDepth > 0, $"Poor shoreline line collects then enters actual water: immersion {_routeWaterDepth:F3} m at {_vehicle.Position}.");
        _routePickups = null;

        if (_camera is not null)
        {
            Begin(0);
            _routePickups = null;
            foreach (var preset in Enum.GetValues<Core.Development.EnvironmentPreset>())
            {
                _pickupEnvironment!.Apply(preset);
                foreach (var marker in markers)
                {
                    Vector3 point = VehicleBody.ToGodot(marker.Position);
                    Vector3 offset = marker.Id.Contains("tunnel", StringComparison.Ordinal) ? new(0, 2.8f, -18) : new(0, 4, 19);
                    await View($"pickup-{preset}-{marker.Id}", point + offset, point + Vector3.Up * 1.6f);
                }
            }
            _pickupEnvironment!.Apply(Core.Development.EnvironmentPreset.ClearBlue);
        }
        _pickupPresentation.QueueFree();
        await VerifyHostedPickupRoutes(configuration, markers, routes);
    }
}
