using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

/// <summary>Checks HUD ping selection without loading the native transport runtime.</summary>
[TestFixture]
internal sealed class TransportDiagnosticsTests
{
    /// <summary>An inactive transport has no ping.</summary>
    [Test]
    public void MissingGatewayHasNoPing()
    {
        Assert.That(TransportDiagnostics.GetPing(null), Is.Null);
    }

    /// <summary>Only connected peers with available samples contribute a ping.</summary>
    /// <param name="states">The peer states in enumeration order.</param>
    /// <param name="pings">The corresponding available or unavailable samples.</param>
    /// <param name="expected">The first eligible ping, if any.</param>
    /// <param name="sampledPeers">The peers whose statistics should be queried.</param>
    [TestCaseSource(nameof(PingCases))]
    public void SelectsFirstAvailableConnectedPing(TransportConnectionState[] states, int?[] pings, int? expected, ulong[] sampledPeers)
    {
        using var gateway = new DiagnosticGateway(states, pings);
        Assert.That(TransportDiagnostics.GetPing(gateway), Is.EqualTo(expected));
        Assert.That(gateway.SampledPeers, Is.EqualTo(sampledPeers));
    }

    private static IEnumerable<TestCaseData> PingCases()
    {
        var connecting = TransportConnectionState.Connecting;
        var connected = TransportConnectionState.Connected;
        yield return new TestCaseData(Array.Empty<TransportConnectionState>(), Array.Empty<int?>(), null, Array.Empty<ulong>()).SetName("NoPeersHasNoPing");
        yield return new TestCaseData(new[] { connecting }, new int?[] { 10 }, null, Array.Empty<ulong>()).SetName("ConnectingPeerIsNotSampled");
        yield return new TestCaseData(new[] { connected }, new int?[] { 25 }, 25, new ulong[] { 0 }).SetName("ConnectedPeerProvidesPing");
        yield return new TestCaseData(new[] { connecting, connected }, new int?[] { null, 25 }, 25, new ulong[] { 1 }).SetName("ConnectingPeerDoesNotHideConnectedPing");
        yield return new TestCaseData(new[] { connected, connected }, new int?[] { null, 25 }, 25, new ulong[] { 0, 1 }).SetName("UnavailableConnectedSampleIsSkipped");
        yield return new TestCaseData(new[] { connected }, new int?[] { null }, null, new ulong[] { 0 }).SetName("UnavailableConnectedSampleHasNoPing");
        yield return new TestCaseData(new[] { connected, connected }, new int?[] { 0, 25 }, 0, new ulong[] { 0 }).SetName("FirstAvailablePingIncludesZero");
    }

    private sealed class DiagnosticGateway(TransportConnectionState[] states, int?[] pings) : ITransportGateway
    {
        public event Action<TransportConnectionChange>? ConnectionChanged
        {
            add { }
            remove { }
        }

        public bool IsListening => false;

        public IReadOnlyDictionary<ulong, TransportConnectionState> Connections { get; } = states.Select((state, index) => new KeyValuePair<ulong, TransportConnectionState>((ulong)index, state)).ToDictionary();

        public TransportConnectionState ConnectionState => throw new NotSupportedException();

        public List<ulong> SampledPeers { get; } = [];

        public TransportStatistics GetStatistics(ulong peerId)
        {
            SampledPeers.Add(peerId);
            return new TransportStatistics(pings[peerId], null, null);
        }

        public void Dispose()
        {
        }

        public void Listen(string address) => throw new NotSupportedException();

        public ulong Connect(string address) => throw new NotSupportedException();

        public void Disconnect(ulong peerId) => throw new NotSupportedException();

        public void Poll() => throw new NotSupportedException();

        public void Stop() => throw new NotSupportedException();

        public void ConfigureSimulation(NetworkSimulation simulation) => throw new NotSupportedException();

        public void Send(TransportMessage message) => throw new NotSupportedException();

        public bool TryReceive(out TransportMessage message) => throw new NotSupportedException();
    }
}
