using System.Buffers.Binary;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class ReliableMessageGatewayTests
{
    [Test]
    public void LargeBoundaryIsAtomicPacedAndPreservesFollowingReliableOrder()
    {
        var a = new Wire(); var b = new Wire(); a.Other = b; b.Other = a;
        var sender = ReliableMessageGateway.For(a); var receiver = ReliableMessageGateway.For(b);
        Assert.That(ReliableMessageGateway.For(a), Is.SameAs(sender));
        byte[] payload = RandomPayload(200000);
        sender.Send(new(1, payload));
        sender.Send(new(1, new byte[] { 7, 8 }));
        sender.Send(new(1, new byte[] { 9 }, TransportDelivery.Unreliable));
        Assert.That(a.Sent.Count, Is.EqualTo(3), "Two chunks are outstanding; the later reliable message waits, unreliable remains independent.");
        Assert.That(receiver.TryReceive(out var independent), Is.True);
        Assert.That(independent.Payload.ToArray(), Is.EqualTo(new byte[] { 9 }));
        Assert.That(receiver.TryReceive(out _), Is.False, "Partial boundary never reaches gameplay.");
        var received = new List<byte[]>();
        for (int i = 0; i < 10; i++)
        {
            Assert.That(sender.TryReceive(out _), Is.False);
            while (receiver.TryReceive(out var message)) { received.Add(message.Payload.ToArray()); }
        }
        Assert.That(received.Count, Is.EqualTo(2));
        Assert.That(received[0], Is.EqualTo(payload));
        Assert.That(received[1], Is.EqualTo(new byte[] { 7, 8 }));
        Assert.That(a.Sent.All(message => message.Payload.Length <= 60020), Is.True);
        b.Received.Enqueue(a.Sent[0]);
        Assert.That(receiver.TryReceive(out _), Is.False, "Completed message replay cannot publish twice.");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void InvalidOrInconsistentChunksDisconnectWithoutPublishing(int corruption)
    {
        var a = new Wire(); var b = new Wire(); a.Other = b; b.Other = a;
        var sender = new ReliableMessageGateway(a); var receiver = new ReliableMessageGateway(b);
        sender.Send(new(1, RandomPayload(180000)));
        var packet = b.Received.Dequeue();
        b.Received.Clear();
        byte[] bytes = packet.Payload.ToArray();
        switch (corruption)
        {
            case 0: BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), int.MaxValue); break;
            case 1: BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1); break;
            case 2: packet = packet with { Delivery = TransportDelivery.Unreliable }; break;
            case 3:
                b.Received.Enqueue(packet);
                Assert.That(receiver.TryReceive(out _), Is.False);
                break;
        }
        b.Received.Enqueue(packet with { Payload = bytes });
        Assert.That(receiver.TryReceive(out _), Is.False);
        Assert.That(b.ConnectionState, Is.EqualTo(TransportConnectionState.Disconnected));
    }

    [Test]
    public void IncompleteTransfersExpireAndStopClearsContinuation()
    {
        var clock = new Clock();
        var a = new Wire(); var b = new Wire(); a.Other = b; b.Other = a;
        var sender = new ReliableMessageGateway(a, clock); var receiver = new ReliableMessageGateway(b, clock);
        sender.Send(new(1, RandomPayload(180000)));
        Assert.That(receiver.TryReceive(out _), Is.False);
        clock.Seconds = 31;
        sender.Poll(); receiver.Poll();
        Assert.That(a.ConnectionState, Is.EqualTo(TransportConnectionState.Disconnected));
        Assert.That(b.ConnectionState, Is.EqualTo(TransportConnectionState.Disconnected));
        sender.Stop(); receiver.Stop();
        a.Connect(TransportEndpoint.DirectIp("127.0.0.1:1")); b.Connect(TransportEndpoint.DirectIp("127.0.0.1:1"));
        sender.Send(new(1, new byte[] { 3 }));
        Assert.That(receiver.TryReceive(out var message), Is.True);
        Assert.That(message.Payload.ToArray(), Is.EqualTo(new byte[] { 3 }));
    }

    private static byte[] RandomPayload(int length) { var bytes = new byte[length]; new Random(172).NextBytes(bytes); return bytes; }

    [TestCase(false)]
    [TestCase(true)]
    public void CompressedBoundariesPreserveExactBytesAcrossSingleAndMultiplePackets(bool multiple)
    {
        var a = new Wire(); var b = new Wire(); a.Other = b; b.Other = a;
        var sender = new ReliableMessageGateway(a); var receiver = new ReliableMessageGateway(b);
        byte[] payload = new byte[400000];
        if (multiple) { RandomPayload(160000).CopyTo(payload, 0); }
        sender.Send(new(1, payload));
        TransportMessage received = default;
        bool complete = false;
        for (int i = 0; i < 10 && !complete; i++)
        {
            complete = receiver.TryReceive(out received);
            sender.TryReceive(out _);
        }
        Assert.That(complete, Is.True);
        Assert.That(received.Payload.ToArray(), Is.EqualTo(payload));
        Assert.That(a.Sent.Sum(message => message.Payload.Length), Is.LessThan(payload.Length));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void MalformedCompressionCannotPublishOrAllocateBeyondTheBoundary(int corruption)
    {
        var a = new Wire(); var b = new Wire(); a.Other = b; b.Other = a;
        var sender = new ReliableMessageGateway(a); var receiver = new ReliableMessageGateway(b);
        sender.Send(new(1, new byte[180000]));
        var packet = b.Received.Dequeue();
        byte[] bytes = packet.Payload.ToArray();
        if (corruption == 0) { BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), int.MaxValue); }
        if (corruption == 1) { bytes = bytes[..^1]; }
        if (corruption == 2) { bytes = [.. bytes, 0]; }
        b.Received.Enqueue(packet with { Payload = bytes });
        Assert.That(receiver.TryReceive(out _), Is.False);
        Assert.That(b.ConnectionState, Is.EqualTo(TransportConnectionState.Disconnected));
    }

    private sealed class Clock : TimeProvider
    {
        internal long Seconds { get; set; }
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Seconds;
    }

    private sealed class Wire : ITransportGateway
    {
        internal Wire Other { get; set; } = null!;
        internal Queue<TransportMessage> Received { get; } = new();
        internal List<TransportMessage> Sent { get; } = new();
        private readonly Dictionary<ulong, TransportConnectionState> _connections = new() { [1] = TransportConnectionState.Connected };
        public event Action<TransportConnectionChange>? ConnectionChanged { add { } remove { } }
        public bool IsListening => false;
        public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => _connections;
        public TransportConnectionState ConnectionState => _connections[1];
        public void Listen(TransportEndpoint endpoint) { }
        public ulong Connect(TransportEndpoint endpoint) { _connections[1] = TransportConnectionState.Connected; return 1; }
        public void Disconnect(ulong peerId) => _connections[peerId] = TransportConnectionState.Disconnected;
        public void Poll() { }
        public void Stop() { Received.Clear(); _connections[1] = TransportConnectionState.Disconnected; }
        public void Dispose() => Stop();
        public TransportStatistics GetStatistics(ulong peerId) => default;
        public void ConfigureSimulation(NetworkSimulation simulation) { }
        public void Send(TransportMessage message) { Assert.That(message.Payload.Length, Is.LessThanOrEqualTo(65536)); Sent.Add(message); Other.Received.Enqueue(message); }
        public bool TryReceive(out TransportMessage message) => Received.TryDequeue(out message);
    }
}
