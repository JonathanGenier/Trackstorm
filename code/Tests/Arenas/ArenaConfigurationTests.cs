using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Arenas;

/// <summary>Authored configuration invariants and authoritative slot integration.</summary>
internal sealed class ArenaConfigurationTests
{
    /// <summary>Both categories require exact capacity.</summary>
    /// <param name="players">Requested player count.</param>
    /// <param name="items">Requested item count.</param>
    [TestCase(7, 8)]
    [TestCase(9, 8)]
    [TestCase(8, 7)]
    [TestCase(8, 9)]
    [TestCase(0, 0)]
    public void CountsMustBeExactlyEight(int players, int items)
    {
        var configuration = PrototypeArena.Configuration;
        Assert.Throws<ArgumentException>(() => Copy(
            Enumerable.Range(0, players).Select(i => configuration.Players[i % 8]),
            Enumerable.Range(0, items).Select(i => configuration.Items[i % 8])));
    }

    /// <summary>IDs are canonical and unique across both categories.</summary>
    /// <param name="id">Invalid identity.</param>
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(" player-01")]
    [TestCase("player-02")]
    [TestCase("item-01")]
    public void MissingAndDuplicateIdsFail(string id)
    {
        var players = PrototypeArena.Configuration.Players.ToArray();
        players[0] = players[0] with { Id = id };
        Assert.Throws<ArgumentException>(() => Copy(players));
    }

    /// <summary>Both marker categories obey all volume axes.</summary>
    /// <param name="x">World X.</param>
    /// <param name="y">World Y.</param>
    /// <param name="z">World Z.</param>
    [TestCase(-61, 1, 0)]
    [TestCase(61, 1, 0)]
    [TestCase(0, -1, 0)]
    [TestCase(0, 31, 0)]
    [TestCase(0, 1, -51)]
    [TestCase(0, 1, 51)]
    [TestCase(float.NaN, 1, 0)]
    public void OutOfBoundsMarkersFail(float x, float y, float z)
    {
        foreach (bool player in new[] { true, false })
        {
            var players = PrototypeArena.Configuration.Players.ToArray();
            var items = PrototypeArena.Configuration.Items.ToArray();
            var markers = player ? players : items;
            markers[0] = markers[0] with { Position = new Vector3(x, y, z) };
            Assert.Throws<ArgumentException>(() => Copy(players, items));
        }
    }

    /// <summary>Unknown and missing surfaces cannot enter a scene contract.</summary>
    [Test]
    public void KnownSurfacesOnly()
    {
        Assert.DoesNotThrow(() => Copy(surfaces: new[] { SurfaceType.Concrete, SurfaceType.Mud }));
        Assert.Throws<ArgumentException>(() => Copy(surfaces: new[] { (SurfaceType)42 }));
        Assert.Throws<ArgumentException>(() => Copy(surfaces: Array.Empty<SurfaceType>()));
    }

    /// <summary>Player footprints and orientations remain valid.</summary>
    [Test]
    public void OverlappingSpawnsAndInvalidRotationsFail()
    {
        var players = PrototypeArena.Configuration.Players.ToArray();
        players[0] = players[0] with { Position = players[1].Position };
        Assert.Throws<ArgumentException>(() => Copy(players));
        players = PrototypeArena.Configuration.Players.ToArray();
        players[0] = players[0] with { Yaw = float.PositiveInfinity };
        Assert.Throws<ArgumentException>(() => Copy(players));
    }

    /// <summary>Configuration cannot be mutated through source arrays.</summary>
    [Test]
    public void ConfigurationCopiesInputsAndRejectsInvalidBounds()
    {
        var players = PrototypeArena.Configuration.Players.ToArray();
        var copy = Copy(players);
        players[0] = players[0] with { Id = "changed" };
        Assert.That(copy.Players[0].Id, Is.EqualTo("player-01"));
        Assert.Throws<ArgumentException>(() => new ArenaConfiguration(Vector3.One, Vector3.Zero, copy.Players, copy.Items, copy.Surfaces));
    }

    /// <summary>Host identity allocation uses reusable authored slots.</summary>
    [Test]
    public void EightPlayerHostUsesStableAuthoredSlotsIncludingRejoin()
    {
        var host = new HostVehicleSession(1);
        for (ulong peer = 1; peer < 8; peer++)
        {
            host.Join(peer);
        }

        Assert.That(host.World.State.Vehicles.Select(vehicle => vehicle.Movement.Physics.Position), Is.EquivalentTo(PrototypeArena.Configuration.Players.Select(spawn => spawn.Position)));
        host.Leave(3);
        ulong replacement = host.Join(20);
        Assert.That(host.World.GetVehicle(replacement).Movement.Physics.Position, Is.EqualTo(PrototypeArena.Configuration.Players[3].Position));
    }

    private static ArenaConfiguration Copy(IEnumerable<ArenaSpawn>? players = null, IEnumerable<ArenaSpawn>? items = null, IEnumerable<SurfaceType>? surfaces = null)
    {
        var source = PrototypeArena.Configuration;
        return new ArenaConfiguration(source.Minimum, source.Maximum, players ?? source.Players, items ?? source.Items, surfaces ?? source.Surfaces);
    }
}
