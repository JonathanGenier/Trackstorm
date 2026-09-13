namespace Trackstorm.Core.Networking.Transport;

/// <summary>Identifies independent reliable ordered and unreliable delivery paths.</summary>
public enum TransportDelivery
{
    /// <summary>Deliver in order, retransmitting missing data.</summary>
    Reliable,

    /// <summary>Deliver without retransmission or ordering guarantees.</summary>
    Unreliable,
}
