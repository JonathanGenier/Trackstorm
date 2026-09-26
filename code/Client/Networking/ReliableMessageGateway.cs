using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

/// <summary>Shared ordered transfer of large reliable boundaries over the existing bounded native packets.</summary>
internal sealed class ReliableMessageGateway : ITransportGateway
{
    internal const int ChunkBytes = 60000;
    private const int WindowChunks = 2;
    internal const int MaximumBytes = MigrationCheckpointCodec.MaximumBytes + 3;
    private const int Header = 20;
    private const long QueueBytes = 128L * 1024 * 1024;
    private static readonly ConditionalWeakTable<ITransportGateway, ReliableMessageGateway> Instances = new();
    private readonly ITransportGateway _inner;
    private readonly TimeProvider _time;
    private readonly Dictionary<ulong, Queue<Transfer>> _outgoing = new();
    private readonly Dictionary<ulong, Transfer> _incoming = new();
    private readonly Dictionary<ulong, ulong> _received = new();
    private ulong _sequence;
    private long _queuedBytes;
    private long _assemblyBytes;
    private int _queuedMessages;

    internal ReliableMessageGateway(ITransportGateway inner, TimeProvider? time = null)
    {
        _inner = inner;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>All drivers sharing a receive stream must share its assembly and send ordering.</summary>
    internal static ITransportGateway For(ITransportGateway gateway) => gateway is ReliableMessageGateway ? gateway : Instances.GetValue(gateway, inner => new(inner));

    public event Action<TransportConnectionChange>? ConnectionChanged { add => _inner.ConnectionChanged += value; remove => _inner.ConnectionChanged -= value; }
    public bool IsListening => _inner.IsListening;
    public IReadOnlyDictionary<ulong, TransportConnectionState> Connections => _inner.Connections;
    public TransportConnectionState ConnectionState => _inner.ConnectionState;
    public string Name => _inner.Name;
    public TransportCapabilities Capabilities => _inner.Capabilities;
    public void Listen(TransportEndpoint endpoint) => _inner.Listen(endpoint);
    public ulong Connect(TransportEndpoint endpoint) => _inner.Connect(endpoint);
    public TransportStatistics GetStatistics(ulong peerId) => _inner.GetStatistics(peerId);
    public void ConfigureSimulation(NetworkSimulation simulation) => _inner.ConfigureSimulation(simulation);

    public void Send(TransportMessage message)
    {
        if (!Enum.IsDefined(message.Delivery) || message.Payload.Length > MaximumBytes) { throw new ArgumentException("Invalid reliable boundary."); }
        if (message.Delivery == TransportDelivery.Reliable && message.Payload.Length > ChunkBytes)
        {
            byte[] compressed = new byte[message.Payload.Length];
            if (BrotliEncoder.TryCompress(message.Payload.Span, compressed.AsSpan(8), out int written, quality: 1, window: 22) && written + 8 < message.Payload.Length)
            {
                compressed[0] = (byte)'T'; compressed[1] = (byte)'Z'; compressed[2] = 1; compressed[3] = 2;
                BinaryPrimitives.WriteInt32LittleEndian(compressed.AsSpan(4), message.Payload.Length);
                message = message with { Payload = compressed.AsSpan(0, written + 8).ToArray() };
            }
        }
        if (message.Delivery != TransportDelivery.Reliable || (message.Payload.Length <= ChunkBytes && !_outgoing.ContainsKey(message.RemotePeerId)))
        {
            _inner.Send(message);
            return;
        }
        if (_queuedMessages >= 256 || _queuedBytes + message.Payload.Length > QueueBytes)
        {
            Disconnect(message.RemotePeerId);
            throw new InvalidOperationException("Reliable boundary backlog exceeded its bounded capacity.");
        }
        if (!_outgoing.TryGetValue(message.RemotePeerId, out var queue)) { _outgoing.Add(message.RemotePeerId, queue = new()); }
        queue.Enqueue(new(checked(++_sequence), message.Payload, _time.GetTimestamp()));
        _queuedMessages++;
        _queuedBytes += message.Payload.Length;
        if (queue.Count == 1) { SendNext(message.RemotePeerId); }
    }

    private void SendNext(ulong peer)
    {
        var queue = _outgoing[peer];
        while (queue.Count > 0)
        {
            var transfer = queue.Peek();
            if (transfer.Data.Length > ChunkBytes)
            {
                while (transfer.Offset < transfer.Data.Length && transfer.Offset - transfer.Acknowledged < WindowChunks * ChunkBytes)
                {
                    int count = Math.Min(ChunkBytes, transfer.Data.Length - transfer.Offset);
                    byte[] packet = Frame(0, transfer.Id, transfer.Data.Length, transfer.Offset, count);
                    transfer.Data.Span.Slice(transfer.Offset, count).CopyTo(packet.AsSpan(Header));
                    _inner.Send(new(peer, packet));
                    transfer.Offset += count;
                }
                transfer.ProgressAt = _time.GetTimestamp();
                return; // Two chunks per peer stay below the native EOS queue budget across seven recipients.
            }
            _inner.Send(new(peer, transfer.Data));
            Dequeue(queue);
        }
        _outgoing.Remove(peer);
    }

    public bool TryReceive(out TransportMessage message)
    {
        while (_inner.TryReceive(out var packet))
        {
            try
            {
                var bytes = packet.Payload.Span;
                if (bytes.Length < 2 || bytes[0] != 'T' || bytes[1] != 'Z')
                {
                    if (packet.Delivery == TransportDelivery.Reliable && _incoming.ContainsKey(packet.RemotePeerId)) { throw new ArgumentException("Interrupted reliable boundary."); }
                    message = packet;
                    return true;
                }
                if (!Connections.TryGetValue(packet.RemotePeerId, out var state) || state != TransportConnectionState.Connected) { continue; }
                if (bytes.Length >= 4 && bytes[3] == 2)
                {
                    if (packet.Delivery != TransportDelivery.Reliable || _incoming.ContainsKey(packet.RemotePeerId)) { throw new ArgumentException("Interrupted compressed boundary."); }
                    message = packet with { Payload = Expand(packet.Payload) };
                    return true;
                }
                if (packet.Delivery != TransportDelivery.Reliable || bytes.Length < Header || bytes[2] != 1 || bytes[3] > 1) { throw new ArgumentException("Invalid boundary chunk."); }
                ulong id = BinaryPrimitives.ReadUInt64LittleEndian(bytes[4..]);
                int length = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
                int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
                if (id == 0 || length is <= ChunkBytes or > MaximumBytes || offset < 0 || offset > length) { throw new ArgumentException("Invalid boundary length."); }
                ulong peer = packet.RemotePeerId;
                if (bytes[3] == 1)
                {
                    if (bytes.Length != Header) { throw new ArgumentException("Invalid acknowledgement."); }
                    if (!_outgoing.TryGetValue(peer, out var queue) || id < queue.Peek().Id) { continue; }
                    var sent = queue.Peek();
                    if (id != sent.Id || length != sent.Data.Length || offset > sent.Offset || (offset != length && offset % ChunkBytes != 0)) { throw new ArgumentException("Invalid acknowledgement boundary."); }
                    if (offset <= sent.Acknowledged) { continue; }
                    sent.Acknowledged = offset;
                    if (offset == length) { Dequeue(queue); }
                    SendNext(peer);
                    continue;
                }
                if (offset == length || bytes.Length != Header + Math.Min(ChunkBytes, length - offset)) { throw new ArgumentException("Invalid chunk size."); }
                if (id <= _received.GetValueOrDefault(peer)) { continue; }
                if (!_incoming.TryGetValue(peer, out var incoming))
                {
                    if (offset != 0 || _assemblyBytes + length > QueueBytes) { throw new ArgumentException("Invalid or excessive boundary assembly."); }
                    incoming = new(id, new byte[length], _time.GetTimestamp());
                    _incoming.Add(peer, incoming);
                    _assemblyBytes += length;
                }
                if (incoming.Id != id || incoming.Data.Length != length || incoming.Offset != offset) { throw new ArgumentException("Inconsistent boundary chunks."); }
                bytes[Header..].CopyTo(incoming.Buffer.Span[offset..]);
                incoming.Offset += bytes.Length - Header;
                incoming.ProgressAt = _time.GetTimestamp();
                _inner.Send(new(peer, Frame(1, id, length, incoming.Offset)));
                if (incoming.Offset != length) { continue; }
                _incoming.Remove(peer);
                _assemblyBytes -= length;
                _received[peer] = id;
                message = new(peer, Expand(incoming.Data));
                return true;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                Disconnect(packet.RemotePeerId);
            }
        }
        message = default;
        return false;
    }

    private static ReadOnlyMemory<byte> Expand(ReadOnlyMemory<byte> payload)
    {
        var bytes = payload.Span;
        if (bytes.Length < 4 || bytes[0] != 'T' || bytes[1] != 'Z' || bytes[3] != 2) { return payload; }
        if (bytes.Length < 8 || bytes[2] != 1) { throw new ArgumentException("Invalid compressed boundary."); }
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        if (length is <= ChunkBytes or > MaximumBytes || bytes.Length >= length) { throw new ArgumentException("Invalid expanded boundary length."); }
        byte[] expanded = new byte[length];
        using var decoder = new BrotliDecoder();
        if (decoder.Decompress(bytes[8..], expanded, out int consumed, out int written) != System.Buffers.OperationStatus.Done || consumed != bytes.Length - 8 || written != length)
        { throw new ArgumentException("Invalid compressed boundary data."); }
        return expanded;
    }

    public void Poll()
    {
        _inner.Poll();
        foreach (ulong peer in _outgoing.Keys.Concat(_incoming.Keys).Concat(_received.Keys).Distinct().ToArray())
        {
            if (!Connections.TryGetValue(peer, out var state) || state == TransportConnectionState.Disconnected) { Clear(peer); continue; }
            bool Expired(Transfer transfer) => _time.GetElapsedTime(transfer.ProgressAt).TotalSeconds > 30;
            if ((_outgoing.TryGetValue(peer, out var queue) && Expired(queue.Peek())) || (_incoming.TryGetValue(peer, out var incoming) && Expired(incoming))) { Disconnect(peer); }
        }
    }

    public void Disconnect(ulong peerId) { Clear(peerId); _inner.Disconnect(peerId); }
    public void Stop() { ClearAll(); _inner.Stop(); }
    public void Dispose() { ClearAll(); _inner.Dispose(); }

    private void Dequeue(Queue<Transfer> queue)
    {
        _queuedBytes -= queue.Dequeue().Data.Length;
        _queuedMessages--;
    }

    private void Clear(ulong peer)
    {
        if (_outgoing.Remove(peer, out var queue)) { while (queue.Count > 0) { Dequeue(queue); } }
        if (_incoming.Remove(peer, out var incoming)) { _assemblyBytes -= incoming.Data.Length; }
        _received.Remove(peer);
    }

    private void ClearAll()
    {
        _outgoing.Clear(); _incoming.Clear(); _received.Clear();
        _queuedBytes = 0; _assemblyBytes = 0; _queuedMessages = 0;
    }

    private static byte[] Frame(byte kind, ulong id, int length, int offset, int count = 0)
    {
        byte[] bytes = new byte[Header + count];
        bytes[0] = (byte)'T'; bytes[1] = (byte)'Z'; bytes[2] = 1; bytes[3] = kind;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4), id);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), offset);
        return bytes;
    }

    private sealed class Transfer(ulong id, ReadOnlyMemory<byte> data, long progressAt)
    {
        internal ulong Id { get; } = id;
        internal ReadOnlyMemory<byte> Data { get; } = data;
        internal Memory<byte> Buffer => System.Runtime.InteropServices.MemoryMarshal.AsMemory(Data);
        internal int Offset { get; set; }
        internal int Acknowledged { get; set; }
        internal long ProgressAt { get; set; } = progressAt;
    }
}
