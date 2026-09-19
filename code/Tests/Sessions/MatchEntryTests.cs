using Trackstorm.Core.Sessions;

namespace Trackstorm.Core.Tests.Sessions;

/// <summary>Authoritative map selection and bounded loading protocol invariants.</summary>
internal sealed class MatchEntryTests
{
    /// <summary>Selection survives state publication, match start, reconnect and return.</summary>
    /// <param name="map">Each supported release map.</param>
    [TestCase(MatchMap.OldMap)]
    [TestCase(MatchMap.NewMap)]
    public void HostSelectionSurvivesSessionLifecycle(MatchMap map)
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.Join(10, GameVersion.Current.ToString(), "Client", "subject");
        Assert.That(lobby.SelectMap(10, map), Is.False);
        Assert.That(lobby.SelectMap(0, (MatchMap)255), Is.False);
        Assert.That(lobby.SelectMap(0, map), Is.True);
        Assert.That(LobbyCodec.DecodeState(LobbyCodec.EncodeState(lobby.State, 2)).State.Map, Is.EqualTo(map));
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
        Assert.That(lobby.Start(0), Is.True);
        Assert.That(lobby.SelectMap(0, map), Is.False);
        lobby.Disconnect(10);
        Assert.That(lobby.Resume(11, GameVersion.Current.ToString(), 100, 2, 1, "subject"), Is.True);
        Assert.That(lobby.State.Map, Is.EqualTo(map));
        Assert.That(lobby.Capture("host").State.Map, Is.EqualTo(map));
        Assert.That(lobby.Return(0), Is.True);
        Assert.That(lobby.State.Map, Is.EqualTo(map));
    }

    /// <summary>No stale generation or malformed envelope can satisfy the entry barrier.</summary>
    [Test]
    public void LoadingProtocolRejectsStaleAndMalformedMessages()
    {
        byte[] packet = MatchEntryCodec.Encode(101, MatchEntryCodec.Loaded);
        Assert.That(MatchEntryCodec.Decode(packet, 101), Is.EqualTo(MatchEntryCodec.Loaded));
        Assert.Throws<ArgumentException>(() => MatchEntryCodec.Decode(packet, 102));
        Assert.Throws<ArgumentException>(() => MatchEntryCodec.Decode(packet[..^1], 101));
        packet[3] = 255;
        Assert.Throws<ArgumentException>(() => MatchEntryCodec.Decode(packet, 101));
    }
}
