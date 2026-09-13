using System.Text.Json;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Core.Tests.Networking;

/// <summary>Checks admission, lifecycle, diagnostics and portable simulation contracts without native dependencies.</summary>
[TestFixture]
internal sealed class TransportLifecycleTests
{
    /// <summary>Pending connections reserve slots; closure releases one and cannot revive an old ID.</summary>
    [Test]
    public void AdmissionRejectsNinthPlayerAndReusesSlotWithFreshIdentity()
    {
        var connections = new TransportConnections();
        for (int i = 0; i < 7; i++)
        {
            Assert.That(connections.TryAdmit(out _), Is.True);
        }

        Assert.That(connections.TryAdmit(out ulong rejected), Is.False);
        Assert.That(rejected, Is.Zero);
        Assert.That(connections.TryTransition(1, TransportConnectionState.Disconnected), Is.True);
        Assert.That(connections.TryTransition(1, TransportConnectionState.Connected), Is.False);
        Assert.That(connections.TryAdmit(out ulong replacement), Is.True);
        Assert.That(replacement, Is.EqualTo(8));
        Assert.That(connections.Count, Is.EqualTo(7));
    }

    /// <summary>Invalid or duplicate transitions cannot mutate connection state.</summary>
    [Test]
    public void LifecycleRejectsInvalidTransitionsAndDetachedSnapshotMutation()
    {
        var connections = new TransportConnections();
        connections.TryAdmit(out ulong peer);
        var snapshot = (Dictionary<ulong, TransportConnectionState>)connections.Snapshot;
        snapshot.Clear();
        Assert.That(connections.TryTransition(peer, TransportConnectionState.Connecting), Is.False);
        Assert.That(connections.TryTransition(peer, (TransportConnectionState)99), Is.False);
        Assert.That(connections.IsConnected(peer), Is.False);
        Assert.That(connections.TryTransition(peer, TransportConnectionState.Connected), Is.True);
        Assert.That(connections.TryTransition(peer, TransportConnectionState.Connected), Is.False);
        Assert.That(connections.TryTransition(peer, TransportConnectionState.Connecting), Is.False);
        Assert.That(connections.IsConnected(peer), Is.True);
        Assert.That(connections.TryTransition(peer, TransportConnectionState.Disconnected), Is.True);
        Assert.That(connections.TryTransition(peer, TransportConnectionState.Disconnected), Is.False);
        Assert.That(connections.IsConnected(peer), Is.False);
    }

    /// <summary>Configuration clamps numeric bounds and remains portable through JSON.</summary>
    [Test]
    public void SimulationClampsAndRoundTrips()
    {
        var settings = new NetworkSimulation(-1, int.MaxValue, -3, 200, int.MaxValue);
        Assert.That(settings.LatencyMilliseconds, Is.Zero);
        Assert.That(settings.JitterMilliseconds, Is.EqualTo(1000));
        Assert.That(settings.LossPercent, Is.Zero);
        Assert.That(settings.ReorderPercent, Is.EqualTo(100));
        Assert.That(settings.ReorderMilliseconds, Is.EqualTo(5000));
        Assert.That(JsonSerializer.Deserialize<NetworkSimulation>(JsonSerializer.Serialize(settings)), Is.EqualTo(settings));
    }

    /// <summary>Non-finite percentages are rejected before any native configuration is changed.</summary>
    /// <param name="invalid">A non-finite percentage.</param>
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void SimulationRejectsNonFinitePercentages(float invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkSimulation(lossPercent: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkSimulation(reorderPercent: invalid));
    }

    /// <summary>Missing or invalid samples never appear as perfect connectivity.</summary>
    [Test]
    public void StatisticsPreserveUnknownAndConvertLoss()
    {
        Assert.That(TransportStatistics.FromSample(-1, float.NaN, -1), Is.EqualTo(default(TransportStatistics)));
        Assert.That(TransportStatistics.FromSample(-1, 2, float.PositiveInfinity), Is.EqualTo(default(TransportStatistics)));
        var sample = TransportStatistics.FromSample(0, 0.75f, 1);
        Assert.That(sample.PingMilliseconds, Is.Zero);
        Assert.That(sample.IncomingLoss, Is.EqualTo(0.25f));
        Assert.That(sample.OutgoingLoss, Is.Zero);
        Assert.That(JsonSerializer.Deserialize<TransportStatistics>(JsonSerializer.Serialize(sample)), Is.EqualTo(sample));
    }

    /// <summary>Messages and lifecycle events serialize without engine objects or native handles.</summary>
    [Test]
    public void PortableMessageAndFailureRoundTrip()
    {
        var message = new TransportMessage(42, new byte[] { 3, 7 }, TransportDelivery.Unreliable);
        TransportMessage decoded = JsonSerializer.Deserialize<TransportMessage>(JsonSerializer.Serialize(message));
        Assert.That(decoded.RemotePeerId, Is.EqualTo(42));
        Assert.That(decoded.Delivery, Is.EqualTo(TransportDelivery.Unreliable));
        Assert.That(decoded.Payload.ToArray(), Is.EqualTo(message.Payload.ToArray()));
        var change = new TransportConnectionChange(42, TransportConnectionState.Disconnected, TransportDisconnectReason.Timeout, "No reply");
        Assert.That(JsonSerializer.Deserialize<TransportConnectionChange>(JsonSerializer.Serialize(change)), Is.EqualTo(change));
    }
}
