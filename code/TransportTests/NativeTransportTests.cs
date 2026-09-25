using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

/// <summary>Real native UDP tests. Sequential because packet simulation and initialization are process-global.</summary>
[TestFixture]
[NonParallelizable]
[Category("Native")]
internal sealed class NativeTransportTests
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = [];

    /// <summary>Restores simulation and releases every native handle, including after a failed assertion.</summary>
    [TearDown]
    public void Cleanup()
    {
        if (_gateways.Count != 0)
        {
            _gateways[0].ConfigureSimulation(new());
        }

        foreach (GameNetworkingSocketsTransport gateway in _gateways.AsEnumerable().Reverse())
        {
            gateway.Dispose();
        }

        _gateways.Clear();
    }

    /// <summary>Exercises all player slots, two excess admissions, fresh reconnection and bidirectional payloads.</summary>
    [Test]
    public void HostAndSevenClientsRejectExcessAndReconnect()
    {
        GameNetworkingSocketsTransport host = Create();
        string address = AvailableAddress();
        host.Listen(TransportEndpoint.DirectIp(address));
        var clients = new List<GameNetworkingSocketsTransport>();
        for (int i = 0; i < 7; i++)
        {
            GameNetworkingSocketsTransport client = Create();
            clients.Add(client);
            client.Connect(TransportEndpoint.DirectIp(address));
        }

        PumpUntil(() => host.Connections.Count == 7 && host.Connections.Values.All(state => state == TransportConnectionState.Connected) &&
            clients.All(c => c.ConnectionState == TransportConnectionState.Connected));
        Assert.That(host.Connections.Values, Is.All.EqualTo(TransportConnectionState.Connected));
        for (int i = 0; i < 2; i++)
        {
            GameNetworkingSocketsTransport excess = Create();
            var events = new List<TransportConnectionChange>();
            excess.ConnectionChanged += events.Add;
            excess.Connect(TransportEndpoint.DirectIp(address));
            PumpUntil(() => events.Any(e => e.Reason == TransportDisconnectReason.SessionFull));
            Assert.That(excess.Connections, Is.Empty);
            Assert.That(host.Connections.Count, Is.EqualTo(7));
        }

        foreach (GameNetworkingSocketsTransport client in clients)
        {
            client.Send(new(client.Connections.Keys.Single(), new byte[] { 11, 22 }));
        }

        var receivedPeers = new HashSet<ulong>();
        PumpUntil(() =>
        {
            while (host.TryReceive(out TransportMessage message))
            {
                Assert.That(message.Payload.ToArray(), Is.EqualTo(new byte[] { 11, 22 }));
                receivedPeers.Add(message.RemotePeerId);
                host.Send(new(message.RemotePeerId, new byte[] { 33 }, TransportDelivery.Unreliable));
            }

            return receivedPeers.Count == 7;
        });
        var replies = new HashSet<GameNetworkingSocketsTransport>();
        PumpUntil(() =>
        {
            foreach (GameNetworkingSocketsTransport client in clients)
            {
                if (client.TryReceive(out TransportMessage reply))
                {
                    Assert.That(reply.Delivery, Is.EqualTo(TransportDelivery.Unreliable));
                    Assert.That(reply.Payload.ToArray(), Is.EqualTo(new byte[] { 33 }));
                    replies.Add(client);
                }
            }

            return replies.Count == 7;
        });
        ulong oldPeer = clients[0].Connections.Keys.Single();
        clients[0].Disconnect(oldPeer);
        PumpUntil(() => host.Connections.Count == 6);
        ulong newPeer = clients[0].Connect(TransportEndpoint.DirectIp(address));
        PumpUntil(() => host.Connections.Count == 7 && clients[0].ConnectionState == TransportConnectionState.Connected);
        Assert.That(newPeer, Is.GreaterThan(oldPeer));
        Assert.Throws<InvalidOperationException>(() => clients[0].Send(new(oldPeer, new byte[] { 1 })));
    }

    /// <summary>Reliable messages retain order while unreliable traffic loses packets under real impairment.</summary>
    [Test]
    public void AdversePacketsPreserveReliableOrderAndExposeDiagnostics()
    {
        GameNetworkingSocketsTransport host = Create();
        GameNetworkingSocketsTransport client = Create();
        string address = AvailableAddress();
        host.Listen(TransportEndpoint.DirectIp(address));
        ulong peer = client.Connect(TransportEndpoint.DirectIp(address));
        PumpUntil(() => client.ConnectionState == TransportConnectionState.Connected && host.ConnectionState == TransportConnectionState.Connected);
        client.ConfigureSimulation(new(30, 10, 25, 20, 50));
        var reliable = new List<int>();
        var unreliable = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            client.Send(new(peer, BitConverter.GetBytes(i), TransportDelivery.Reliable));
            client.Send(new(peer, BitConverter.GetBytes(i), TransportDelivery.Unreliable));
            PumpFor(12, () => Drain(host, reliable, unreliable));
        }

        PumpUntil(() =>
        {
            Drain(host, reliable, unreliable);
            return reliable.Count == 100;
        });
        PumpFor(600, () => Drain(host, reliable, unreliable));
        Assert.That(reliable, Is.EqualTo(Enumerable.Range(0, 100)));
        Assert.That(unreliable.Count, Is.InRange(1, 99));
        Assert.That(unreliable.Distinct().Count(), Is.EqualTo(unreliable.Count));
        PumpUntil(() => client.GetStatistics(peer).IncomingQuality.HasValue);
        TransportStatistics stats = client.GetStatistics(peer);
        Assert.That(stats.PingMilliseconds, Is.GreaterThanOrEqualTo(30));
        Assert.That(stats.IncomingLoss, Is.InRange(0f, 1f));
        TestContext.WriteLine($"Reliable={reliable.Count}/100; unreliable={unreliable.Count}/100; ping={stats.PingMilliseconds}ms; incoming loss={stats.IncomingLoss}; outgoing loss={stats.OutgoingLoss}");
        Assert.That(client.GetStatistics(ulong.MaxValue), Is.EqualTo(default(TransportStatistics)));
    }

    /// <summary>Initial and established blackholes both terminate with stable timeout reasons and release slots.</summary>
    [Test]
    public void ConnectionAndEstablishedTimeoutsReleaseResources()
    {
        GameNetworkingSocketsTransport client = Create(1000);
        var events = new List<TransportConnectionChange>();
        client.ConnectionChanged += events.Add;
        client.Connect(TransportEndpoint.DirectIp(AvailableAddress()));
        PumpUntil(() => events.Any(e => e.Reason == TransportDisconnectReason.Timeout));
        Assert.That(client.Connections, Is.Empty);
        GameNetworkingSocketsTransport host = Create(1000);
        string address = AvailableAddress();
        host.Listen(TransportEndpoint.DirectIp(address));
        client.Connect(TransportEndpoint.DirectIp(address));
        PumpUntil(() => client.ConnectionState == TransportConnectionState.Connected && host.ConnectionState == TransportConnectionState.Connected);
        events.Clear();
        client.ConfigureSimulation(new(lossPercent: 100));
        PumpUntil(() => events.Any(e => e.Reason == TransportDisconnectReason.Timeout) && host.Connections.Count == 0);
        Assert.That(client.Connections, Is.Empty);
    }

    /// <summary>Repeated hosting and joining reuses the same port with no remaining active connections.</summary>
    [Test]
    public void RepeatedHostJoinAndStopReleasesListenerAndPeers()
    {
        GameNetworkingSocketsTransport host = Create();
        GameNetworkingSocketsTransport client = Create();
        string address = AvailableAddress();
        for (int cycle = 0; cycle < 12; cycle++)
        {
            host.Listen(TransportEndpoint.DirectIp(address));
            client.Connect(TransportEndpoint.DirectIp(address));
            PumpUntil(() => client.ConnectionState == TransportConnectionState.Connected && host.ConnectionState == TransportConnectionState.Connected);
            host.Stop();
            PumpUntil(() => client.Connections.Count == 0);
            client.Stop();
            Assert.That(host.IsListening, Is.False);
            Assert.That(host.Connections, Is.Empty);
        }
    }

    /// <summary>Invalid requests and receive overflow fail explicitly without unbounded managed allocation.</summary>
    [Test]
    public void RejectsInvalidOperationsAndBoundsReceiveQueue()
    {
        GameNetworkingSocketsTransport host = Create();
        GameNetworkingSocketsTransport client = Create();
        Assert.Throws<ArgumentException>(() => client.Connect(TransportEndpoint.DirectIp("not-an-address")));
        Assert.Throws<InvalidOperationException>(() => client.Send(new(1, new byte[] { 1 })));
        string address = AvailableAddress();
        host.Listen(TransportEndpoint.DirectIp(address));
        ulong peer = client.Connect(TransportEndpoint.DirectIp(address));
        Assert.Throws<InvalidOperationException>(() => host.Connect(TransportEndpoint.DirectIp(address)));
        PumpUntil(() => client.ConnectionState == TransportConnectionState.Connected && host.ConnectionState == TransportConnectionState.Connected);
        Assert.Throws<ArgumentOutOfRangeException>(() => client.Send(new(peer, new byte[GameNetworkingSocketsTransport.MaximumPayloadBytes + 1])));
        var events = new List<TransportConnectionChange>();
        host.ConnectionChanged += events.Add;
        for (int i = 0; i < 300; i++)
        {
            client.Send(new(peer, new byte[] { 1 }));
        }

        PumpUntil(() => events.Any(e => e.Reason == TransportDisconnectReason.ReceiveOverflow));
        Assert.That(host.Connections, Is.Empty);
    }

    /// <summary>A real occupied UDP port stays owned by its socket and reports the native bind error.</summary>
    [Test]
    public void OccupiedListenerReportsBindErrorAndCanRecoverAfterOwnerCloses()
    {
        using var owner = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        owner.ExclusiveAddressUse = true;
        owner.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        string address = owner.LocalEndPoint!.ToString()!;
        GameNetworkingSocketsTransport host = Create();
        var error = Assert.Throws<InvalidOperationException>(() => host.Listen(TransportEndpoint.DirectIp(address)));
        Assert.That(error!.Message, Does.Contain(address).And.Contain("Failed to bind socket.").And.Contain("0x00002740"));
        Assert.That(host.IsListening, Is.False);
        Assert.That(host.Connections, Is.Empty);
        owner.Close();
        host.Listen(TransportEndpoint.DirectIp(address));
        Assert.That(host.IsListening, Is.True);
    }

    private static string AvailableAddress()
    {
        string ip = Environment.GetEnvironmentVariable("TRACKSTORM_TEST_ADDRESS") ?? "127.0.0.1";
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Parse(ip), 0));
        return socket.LocalEndPoint!.ToString()!;
    }

    private static void Drain(ITransportGateway gateway, List<int> reliable, List<int> unreliable)
    {
        while (gateway.TryReceive(out TransportMessage message))
        {
            (message.Delivery == TransportDelivery.Reliable ? reliable : unreliable).Add(BitConverter.ToInt32(message.Payload.Span));
        }
    }

    private GameNetworkingSocketsTransport Create(int timeout = 10000)
    {
        var gateway = new GameNetworkingSocketsTransport(timeout);
        _gateways.Add(gateway);
        return gateway;
    }

    private void PumpUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            foreach (GameNetworkingSocketsTransport gateway in _gateways)
            {
                gateway.Poll();
            }

            if (condition())
            {
                return;
            }

            Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(15), "Native condition timed out.");
            Thread.Sleep(5);
        }
    }

    private void PumpFor(int milliseconds, Action action)
    {
        var clock = Stopwatch.StartNew();
        PumpUntil(() =>
        {
            action();
            return clock.ElapsedMilliseconds >= milliseconds;
        });
    }
}
