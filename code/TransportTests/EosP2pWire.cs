using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

/// <summary>Deterministic authenticated datagrams for transport and online-composition tests.</summary>
internal sealed class EosP2pWire(OnlineProductUserId local) : IEosP2p
{
    private readonly HashSet<OnlineProductUserId> _accepted = new();
    /// <summary>Routes for multi-peer tests.</summary>
    internal Dictionary<OnlineProductUserId, EosP2pWire>? Routes { get; set; }
    /// <summary>Direct route for two-peer tests.</summary>
    internal EosP2pWire? Other { get; set; }
    /// <summary>Injected native connection notification.</summary>
    internal Action<OnlineProductUserId, TransportConnectionState, TransportDisconnectReason>? Changed { get; set; }
    /// <summary>Authenticated packets awaiting delivery.</summary>
    internal Queue<(OnlineProductUserId Peer, byte[] Bytes, TransportDelivery Delivery)> Packets { get; } = new();
    /// <summary>Observed native closures.</summary>
    internal List<OnlineProductUserId> Closed { get; } = new();
    /// <summary>Observed adapter disposals.</summary>
    internal int Disposals { get; private set; }
    /// <summary>Receive attempts.</summary>
    internal int Reads { get; private set; }
    /// <summary>Drops all outgoing packets.</summary>
    internal bool DropOutgoing { get; set; }
    /// <summary>Selectively partitioned recipients.</summary>
    internal HashSet<OnlineProductUserId> DropRecipients { get; } = new();
    /// <inheritdoc />
    public void Start(string socket, Action<OnlineProductUserId, TransportConnectionState, TransportDisconnectReason> changed) => Changed = changed;
    /// <inheritdoc />
    public bool Accept(OnlineProductUserId peer)
    {
        _accepted.Add(peer);
        var other = Routes?.GetValueOrDefault(peer) ?? Other!;
        if (other._accepted.Contains(local))
        {
            Changed!(peer, TransportConnectionState.Connected, TransportDisconnectReason.None);
            other.Changed!(local, TransportConnectionState.Connected, TransportDisconnectReason.None);
        }
        else
        {
            other.Changed!(local, TransportConnectionState.Connecting, TransportDisconnectReason.None);
        }

        return true;
    }

    /// <inheritdoc />
    public bool Send(OnlineProductUserId peer, ArraySegment<byte> data, TransportDelivery delivery)
    {
        if (!DropOutgoing && !DropRecipients.Contains(peer))
        {
            (Routes?.GetValueOrDefault(peer) ?? Other!).Packets.Enqueue((local, data.ToArray(), delivery));
        }

        return true;
    }

    /// <inheritdoc />
    public bool Receive(byte[] buffer, out OnlineProductUserId? peer, out int length, out TransportDelivery delivery)
    {
        Reads++;
        if (!Packets.TryDequeue(out var packet))
        {
            peer = null;
            length = 0;
            delivery = default;
            return false;
        }

        peer = packet.Peer;
        length = packet.Bytes.Length;
        delivery = packet.Delivery;
        packet.Bytes.CopyTo(buffer, 0);
        return true;
    }

    /// <inheritdoc />
    public void Close(OnlineProductUserId peer)
    {
        _accepted.Remove(peer);
        Closed.Add(peer);
    }

    /// <inheritdoc />
    public void Stop()
    {
        _accepted.Clear();
        Packets.Clear();
        Changed = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Disposals++;
        Stop();
    }

    /// <summary>Reorders unreliable traffic while preserving reliable packet positions.</summary>
    internal void ReverseUnreliable()
    {
        var packets = Packets.ToArray();
        var reversed = new Queue<(OnlineProductUserId Peer, byte[] Bytes, TransportDelivery Delivery)>(packets.Where(packet => packet.Delivery == TransportDelivery.Unreliable).Reverse());
        Packets.Clear();
        foreach (var packet in packets)
        {
            Packets.Enqueue(packet.Delivery == TransportDelivery.Unreliable ? reversed.Dequeue() : packet);
        }
    }
}
