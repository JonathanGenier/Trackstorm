using GnsSharp;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Networking;

/// <summary>Contains explicit native-to-contract conversions, testable without opening sockets.</summary>
internal static class GnsConversions
{
    /// <summary>Application close code for rejected admission.</summary>
    internal const int SessionFullCode = 1001;
    /// <summary>Application close code for exhausted receive capacity.</summary>
    internal const int OverflowCode = 1002;

    /// <summary>Selects native delivery guarantees without coupling gameplay to native flags.</summary>
    /// <param name="delivery">The portable delivery mode.</param>
    /// <returns>Native send flags.</returns>
    internal static ESteamNetworkingSendType SendFlags(TransportDelivery delivery) => delivery switch
    {
        TransportDelivery.Reliable => ESteamNetworkingSendType.ReliableNoNagle,
        TransportDelivery.Unreliable => ESteamNetworkingSendType.UnreliableNoNagle,
        _ => throw new ArgumentOutOfRangeException(nameof(delivery)),
    };

    /// <summary>Maps application close codes and native timeout ranges into stable domain reasons.</summary>
    /// <param name="code">Native connection end code.</param>
    /// <param name="closedByPeer">Whether the remote endpoint initiated closure.</param>
    /// <returns>The stable disconnection reason.</returns>
    internal static TransportDisconnectReason DisconnectReason(int code, bool closedByPeer) => code switch
    {
        SessionFullCode => TransportDisconnectReason.SessionFull,
        OverflowCode => TransportDisconnectReason.ReceiveOverflow,
        (int)ESteamNetConnectionEnd.Remote_Timeout or (int)ESteamNetConnectionEnd.Misc_Timeout => TransportDisconnectReason.Timeout,
        _ => closedByPeer ? TransportDisconnectReason.RemoteRequest : TransportDisconnectReason.Failure,
    };
}
