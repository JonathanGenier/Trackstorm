namespace Trackstorm.Core.Networking.Transport;

/// <summary>
/// Carries an opaque serialized message across the native transport boundary.
/// </summary>
/// <param name="RemotePeerId">The transport adapter's stable identifier for the remote peer.</param>
/// <param name="Payload">The immutable-for-publication payload owned by the caller or adapter.</param>
/// <param name="Delivery">The delivery guarantee; unreliable messages may be lost or reordered.</param>
public readonly record struct TransportMessage(ulong RemotePeerId, ReadOnlyMemory<byte> Payload, TransportDelivery Delivery = TransportDelivery.Reliable);
