using System.Numerics;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Pure presentation sampling checks with no native engine initialization.</summary>
[TestFixture]
internal sealed class RemoteInterpolationTests
{
    /// <summary>Endpoints, midpoint and out-of-range cursors follow the explicit no-extrapolation policy.</summary>
    /// <param name="tick">Requested presentation time.</param>
    /// <param name="position">Expected X position.</param>
    [TestCase(0, 0)]
    [TestCase(10, 0)]
    [TestCase(15, 5)]
    [TestCase(20, 10)]
    [TestCase(30, 10)]
    public void SamplesAndClamps(double tick, float position)
    {
        var history = new SnapshotHistory(99);
        history.Add(Snapshot(10, 0));
        history.Add(Snapshot(20, 10));
        var result = RemoteInterpolation.Sample(history, 1, tick)!.Value;
        Assert.That(result.Position.X, Is.EqualTo(position));
        Assert.That(result.LinearVelocity.X, Is.EqualTo(position));
        Assert.That(result.Orientation.LengthSquared(), Is.EqualTo(1).Within(0.00001));
        Assert.That(RemoteInterpolation.Sample(history, 2, tick), Is.Null);
    }

    /// <summary>New lives remain discrete rather than sweeping through a reset teleport.</summary>
    [Test]
    public void LifeChangesDoNotInterpolate()
    {
        var history = new SnapshotHistory(99);
        history.Add(Snapshot(10, 0));
        history.Add(Snapshot(20, 100, 2));
        Assert.That(RemoteInterpolation.Sample(history, 1, 19)!.Value.Position.X, Is.Zero);
        Assert.That(RemoteInterpolation.Sample(history, 1, 20)!.Value.Position.X, Is.EqualTo(100));
    }

    /// <summary>Jittered arrivals and lost snapshots cannot rewind the render cursor or extrapolate past authority.</summary>
    [Test]
    public void PresentationClockIsMonotonicAndHoldsAtLatest()
    {
        var history = new SnapshotHistory(99);
        var clock = new RemoteInterpolation();
        history.Add(Snapshot(60, 0));
        clock.Advance(history, 0, 0);
        Assert.That(clock.RenderTick, Is.EqualTo(54));
        Assert.That(clock.DelayMilliseconds, Is.EqualTo(100));
        double previous = clock.RenderTick;
        for (int frame = 1; frame <= 120; frame++)
        {
            clock.Advance(history, 1.0 / 60, frame / 60.0);
            Assert.That(clock.RenderTick, Is.InRange(previous, 60));
            previous = clock.RenderTick;
        }

        Assert.That(clock.RenderTick, Is.EqualTo(60));
        history.Add(Snapshot(66, 6));
        clock.Advance(history, 1.0 / 60, 0);
        Assert.That(clock.RenderTick, Is.InRange(60, 61.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(history, -1, 0));
    }

    private static WorldSnapshot Snapshot(ulong tick, float x, ulong life = 1)
    {
        var physics = new VehiclePhysicsState(new Vector3(x, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, x / 10), new Vector3(x, 0, 0), Vector3.Zero);
        var state = new VehicleSnapshot(1, life, new VehicleState(tick, physics, true, false, 0, 0), new VehicleDamageState(100, 100, null, null), physics);
        return new WorldSnapshot(99, tick, [new(state, 0)]);
    }
}
