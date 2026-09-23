using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Map-sized pickup capacity is independent of the eight-player limit.</summary>
internal sealed class MapPickupCapacityTests
{
    /// <summary>Variable map counts register and round-trip without dropping marker state.</summary>
    /// <param name="count">Number of map pickups.</param>
    [TestCase(0)]
    [TestCase(8)]
    [TestCase(20)]
    public void MapCountsRoundTrip(int count)
    {
        var original = PrototypeArena.Configuration;
        var markers = Enumerable.Range(0, count).Select(i => new ArenaSpawn($"pickup-{i:00}", new Vector3(i, 1, 0), 0)).ToArray();
        var config = new ArenaConfiguration(original.Minimum, original.Maximum, original.Players, markers, original.Surfaces);
        var host = new HostVehicleSession(1);
        host.RegisterSpawns(config);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, host.Items.Events, host.Spawns!.States);
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(publication));
        Assert.That(decoded.Spawns, Is.EqualTo(publication.Spawns));
        Assert.That(decoded.Spawns.Select(spawn => spawn.Id), Is.EqualTo(markers.Select(marker => marker.Id)));
    }

    /// <summary>Capacity is bounded in both scene contracts and untrusted publications.</summary>
    [Test]
    public void ExcessiveCountsFail()
    {
        var original = PrototypeArena.Configuration;
        var markers = Enumerable.Range(0, 21).Select(i => new ArenaSpawn($"pickup-{i:00}", new Vector3(i, 1, 0), 0)).ToArray();
        Assert.Throws<ArgumentException>(() => new ArenaConfiguration(original.Minimum, original.Maximum, original.Players, markers, original.Surfaces));
        var host = new HostVehicleSession(1);
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, host.Items.Events,
            markers.Select(marker => new ItemSpawnState(marker.Id, true, 0, 0, 0, HeldItem.None))));
    }
}
