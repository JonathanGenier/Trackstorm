using GnsSharp;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Transport.Tests;

/// <summary>Checks native flag and error conversions without starting the native runtime.</summary>
[TestFixture]
internal sealed class TransportConversionTests
{
    /// <summary>Modes choose distinct native guarantees and invalid modes are rejected.</summary>
    [Test]
    public void RoutesReliableAndUnreliableIndependently()
    {
        Assert.That(GnsConversions.SendFlags(TransportDelivery.Reliable), Is.EqualTo(ESteamNetworkingSendType.ReliableNoNagle));
        Assert.That(GnsConversions.SendFlags(TransportDelivery.Unreliable), Is.EqualTo(ESteamNetworkingSendType.UnreliableNoNagle));
        Assert.Throws<ArgumentOutOfRangeException>(() => GnsConversions.SendFlags((TransportDelivery)99));
    }

    /// <summary>Native timeout and closure codes have stable public meanings.</summary>
    [Test]
    public void ConvertsTimeoutRejectionAndUnknownFailures()
    {
        Assert.That(GnsConversions.DisconnectReason((int)ESteamNetConnectionEnd.Misc_Timeout, false), Is.EqualTo(TransportDisconnectReason.Timeout));
        Assert.That(GnsConversions.DisconnectReason((int)ESteamNetConnectionEnd.Remote_Timeout, false), Is.EqualTo(TransportDisconnectReason.Timeout));
        Assert.That(GnsConversions.DisconnectReason(GnsConversions.SessionFullCode, true), Is.EqualTo(TransportDisconnectReason.SessionFull));
        Assert.That(GnsConversions.DisconnectReason(GnsConversions.OverflowCode, true), Is.EqualTo(TransportDisconnectReason.ReceiveOverflow));
        Assert.That(GnsConversions.DisconnectReason(9999, false), Is.EqualTo(TransportDisconnectReason.Failure));
        Assert.That(GnsConversions.DisconnectReason(1000, true), Is.EqualTo(TransportDisconnectReason.RemoteRequest));
    }
}
