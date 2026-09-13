namespace Trackstorm.Core.Networking.Transport;

/// <summary>Portable lifecycle notification. Peer IDs are local connection identities, never entity IDs.</summary>
public readonly record struct TransportConnectionChange(ulong RemotePeerId, TransportConnectionState State, TransportDisconnectReason Reason, string Detail);
