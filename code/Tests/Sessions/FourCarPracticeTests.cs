using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Sessions;

[TestFixture]
internal sealed class FourCarPracticeTests
{
    [Test]
    public void ThirdCarStartsOneHundredRouteMetresBehindSecondAcrossLapSeam()
    {
        var first = PracticeVehicleInput.InitialPose(true, 0, 1.3f);
        var second = PracticeVehicleInput.InitialPose(true, 1, 1.3f);
        var third = PracticeVehicleInput.InitialPose(true, 2, 1.3f);
        Assert.That(first.Position, Is.EqualTo(new Vector3(-7, 1.3f, 91)));
        Assert.That(second.Position, Is.EqualTo(new Vector3(-17, 1.3f, 91)));
        float remainingArc = 91 * MathF.Atan2(-107 - third.Position.X, third.Position.Z);
        Assert.That(90 + remainingArc, Is.EqualTo(100).Within(.001f));
        Assert.That(Vector3.Transform(-Vector3.UnitZ, third.Orientation).Z, Is.GreaterThan(0));
    }

    [Test]
    public void MapSwitchPreservesPairAndReconcilesExtraTargetsWithoutReusingIds()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.AddPracticeCar();
        var original = lobby.State.Players.Where(p => p.PracticeCar).ToArray();
        Assert.That(lobby.SelectMap(0, MatchMap.OldMap), Is.True);
        Assert.That(lobby.State.Players.Where(p => p.PracticeCar), Is.EqualTo(original.Take(2)));
        Assert.That(lobby.SelectMap(0, MatchMap.NewMap), Is.True);
        lobby.AddPracticeCar();
        Assert.That(lobby.State.Players.Count, Is.EqualTo(5));
        Assert.That(lobby.State.Players.Where(p => p.PracticeCar).Take(2), Is.EqualTo(original.Take(2)));
        Assert.That(lobby.State.Players.Last().Id, Is.GreaterThan(original.Last().Id));
        Assert.That(lobby.SelectMap(0, MatchMap.OldMap), Is.True);
        for (ulong peer = 10; peer < 15; peer++) { lobby.Join(peer, GameVersion.Current.ToString(), "Human"); }
        Assert.That(lobby.SelectMap(0, MatchMap.NewMap), Is.False);
        Assert.That(lobby.State.Map, Is.EqualTo(MatchMap.OldMap));
    }

}
