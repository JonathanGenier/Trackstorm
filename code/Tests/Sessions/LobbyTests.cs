using System.Text;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Core.Tests.Sessions;

/// <summary>Deterministic lobby acceptance, ownership, lifecycle and wire-boundary regression checks.</summary>
[TestFixture]
internal sealed class LobbyTests
{
    /// <summary>Host reserves one slot and a ninth player cannot mutate a full roster.</summary>
    [Test]
    public void CapacityAndIdentityValidationAreAtomic()
    {
        var lobby = new LobbyAuthority(100, "Host");
        Assert.That(lobby.Add(0, 2, "A"), Is.False);
        Assert.That(lobby.Add(10, 0, "A"), Is.False);
        Assert.That(lobby.Add(10, 1, "A"), Is.False);
        Assert.That(lobby.Add(10, ulong.MaxValue, "A"), Is.False);
        Assert.That(lobby.Add(10, 2, "A"), Is.True);
        Assert.That(lobby.Add(11, 2, "B"), Is.False);
        Assert.That(lobby.Add(10, 3, "B"), Is.False);
        for (ulong peer = 11; peer <= 16; peer++)
        {
            Assert.That(lobby.Join(peer, "Same name"), Is.Not.Zero);
        }

        LobbySnapshot full = lobby.State;
        Assert.That(lobby.Join(17, "Excess"), Is.Zero);
        Assert.That(lobby.State, Is.SameAs(full));
        Assert.That(full.Players.Count, Is.EqualTo(8));
        Assert.That(lobby.Remove(10), Is.True);
        Assert.That(lobby.Add(20, 2, "Retired"), Is.False);
        Assert.That(lobby.Join(20, "New"), Is.GreaterThan(8));
    }

    /// <summary>Readiness targets sender ownership; departure removes its entire record without touching others.</summary>
    [Test]
    public void ReadyAndRemovalTargetOnlyTheSender()
    {
        var lobby = new LobbyAuthority(100, "Host");
        ulong first = lobby.Join(10, "A");
        ulong second = lobby.Join(20, "B");
        LobbySnapshot before = lobby.State;
        Assert.That(lobby.SetReady(10, true), Is.True);
        Assert.That(lobby.State.Players.Where(player => player.Ready).Select(player => player.Id), Is.EqualTo(new[] { first }));
        Assert.That(before.Players.All(player => !player.Ready), Is.True, "Published snapshots must remain immutable.");
        Assert.That(lobby.SetReady(10, false), Is.True);
        Assert.That(lobby.SetReady(20, true), Is.True);
        Assert.That(lobby.SetReady(999, true), Is.False);
        Assert.That(lobby.Remove(20), Is.True);
        Assert.That(lobby.Remove(20), Is.False);
        Assert.That(lobby.State.Players.Any(player => player.Id == second || player.Ready), Is.False);
        Assert.That(lobby.SetReady(20, true), Is.False);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(2));
    }

    /// <summary>Lobby loss removes immediately, while arena loss retains authenticated state for resume.</summary>
    [Test]
    public void DisconnectPolicyIsExplicitlyPhaseSpecific()
    {
        var lobby = new LobbyAuthority(100, "Host");
        Assert.That(lobby.State.ReconnectPolicy, Is.EqualTo(SessionReconnectPolicy.FreshJoin));
        Assert.That(lobby.Join(10, "Client", "client"), Is.EqualTo(2));
        Assert.That(lobby.Disconnect(10), Is.True);
        Assert.That(lobby.State.Players.Any(player => player.Id == 2), Is.False);
        Assert.That(lobby.FindPlayer("client"), Is.Zero);
        Assert.That(lobby.Resume(20, 100, 2, 1, "client"), Is.False);
        Assert.That(lobby.Join(20, "Client", "client"), Is.EqualTo(3), "A returning lobby member is a fresh admission.");

        lobby.SetReady(0, true);
        lobby.SetReady(20, true);
        Assert.That(lobby.Start(0, [20]), Is.True);
        Assert.That(lobby.State.ReconnectPolicy, Is.EqualTo(SessionReconnectPolicy.RetainedResume));
        Assert.That(lobby.Disconnect(20), Is.True);
        Assert.That(lobby.State.Players.Single(player => player.Id == 3).Connected, Is.False);
        Assert.That(lobby.Resume(30, 100, 3, 1, "client"), Is.True);
        Assert.That(lobby.State.Players.Single(player => player.Id == 3).Generation, Is.EqualTo(2));
    }

    /// <summary>Only a host with every admitted connected player ready can enter a fresh match.</summary>
    [Test]
    public void StartReturnAndRepeatFollowAuthorityAndConnectedRosterRules()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.Join(10, "Client");
        Assert.That(lobby.Start(0), Is.False);
        lobby.SetReady(0, true);
        Assert.That(lobby.Start(0), Is.False);
        lobby.SetReady(10, true);
        Assert.That(lobby.Start(10), Is.False);
        Assert.That(lobby.Start(999), Is.False);
        Assert.That(lobby.Start(0, Array.Empty<ulong>()), Is.False, "A departed transport cannot remain in the starting roster.");
        Assert.That(lobby.Start(0, new ulong[] { 10, 20 }), Is.False, "Pending admission must complete before start.");
        Assert.That(lobby.Start(0, new ulong[] { 10 }), Is.True);
        Assert.That(lobby.State.Phase, Is.EqualTo(SessionPhase.Arena));
        Assert.That(lobby.State.Match, Is.EqualTo(101));
        Assert.That(lobby.Start(0), Is.False);
        Assert.That(lobby.SetReady(10, false), Is.False);
        Assert.That(lobby.Join(20, "Late"), Is.EqualTo(3));
        Assert.That(lobby.IsPendingJoin(20), Is.True);
        Assert.That(lobby.Return(10), Is.False);
        Assert.That(lobby.Return(0), Is.True);
        Assert.That(lobby.State.Players.All(player => !player.Ready), Is.True);
        Assert.That(lobby.State.Players.Select(player => player.Id), Is.EqualTo(new ulong[] { 1, 2 }));
        Assert.That(lobby.Return(0), Is.False);
        lobby.SetReady(0, true);
        lobby.SetReady(10, true);
        Assert.That(lobby.Start(0), Is.True);
        Assert.That(lobby.State.Match, Is.EqualTo(102));
        Assert.That(lobby.Remove(10), Is.True);
        Assert.That(lobby.State.Players.Single(player => player.Id == 2).Connected, Is.False);
        Assert.That(lobby.State.Players.Count, Is.EqualTo(2));
        Assert.That(lobby.State.Phase, Is.EqualTo(SessionPhase.Arena));
    }

    /// <summary>One explicitly ready host is a valid development session.</summary>
    [Test]
    public void SoloHostRequiresReadiness()
    {
        var lobby = new LobbyAuthority(7, "Host");
        Assert.That(lobby.Start(0), Is.False);
        lobby.SetReady(0, true);
        Assert.That(lobby.Start(0), Is.True);
    }

    /// <summary>Names have deterministic fallback, plain text and bounded Unicode scalar length.</summary>
    /// <param name="input">Untrusted requested name.</param>
    /// <param name="expected">Canonical displayed result.</param>
    [TestCase(null, "Player")]
    [TestCase(" \t\r\n ", "Player")]
    [TestCase("<>&\u202e\u0000", "Player")]
    [TestCase("  A\t B\nC  ", "A B C")]
    [TestCase("Zoë-车_42", "Zoë-车_42")]
    [TestCase("<Alice>\u202e", "Alice")]
    public void NamesAreSanitized(string? input, string expected)
    {
        Assert.That(PlayerName.Sanitize(input), Is.EqualTo(expected));
        Assert.That(PlayerName.Sanitize(expected), Is.EqualTo(expected));
    }

    /// <summary>Length boundaries never split supplementary Unicode letters or retain trailing spaces.</summary>
    [Test]
    public void NameLengthIsBoundedWithoutSplittingUnicode()
    {
        foreach (int length in new[] { 23, 24, 25, 100 })
        {
            string input = string.Concat(Enumerable.Repeat("𐐀", length));
            string name = PlayerName.Sanitize(input);
            Assert.That(name.EnumerateRunes().Count(), Is.EqualTo(Math.Min(length, 24)));
            Assert.That(name.Contains(Rune.ReplacementChar.ToString(), StringComparison.Ordinal), Is.False);
        }

        Assert.That(PlayerName.Sanitize(new string('a', 23) + " b"), Is.EqualTo(new string('a', 23)));
    }

    /// <summary>Complete roster publications round trip and malformed envelopes cannot construct state.</summary>
    [Test]
    public void CodecValidatesCompleteStateAndIdentity()
    {
        var lobby = new LobbyAuthority(100, " Host ");
        lobby.Join(10, " Client ");
        lobby.SetReady(10, true);
        byte[] bytes = LobbyCodec.EncodeState(lobby.State, 2);
        var decoded = LobbyCodec.DecodeState(bytes);
        Assert.That(decoded.Player, Is.EqualTo(2));
        Assert.That(decoded.State.Players, Is.EqualTo(lobby.State.Players));
        Assert.That(decoded.State.Session, Is.EqualTo(100));
        Assert.That(decoded.State.Revision, Is.EqualTo(lobby.State.Revision));
        Assert.Throws<ArgumentException>(() => LobbyCodec.DecodeState(LobbyCodec.EncodeState(lobby.State, 99)));
        Assert.Throws<ArgumentException>(() => LobbyCodec.DecodeState(bytes[..^1]));
        bytes[2] = 1;
        Assert.Throws<ArgumentException>(() => LobbyCodec.DecodeState(bytes));
        Assert.Throws<ArgumentException>(() => LobbyCodec.DecodeState(new byte[4097]));
        Assert.Throws<ArgumentException>(() => new LobbySnapshot(1, 1, 1, SessionPhase.Lobby, new[] { new SessionPlayer(1, "Invalid<", false) }));
        Assert.Throws<ArgumentException>(() => new LobbySnapshot(1, 1, 1, SessionPhase.Lobby, new[] { new SessionPlayer(1, "Host", false), new SessionPlayer(1, "Duplicate", false) }));
        var command = LobbyCodec.DecodeCommand(LobbyCodec.EncodeCommand(LobbyCommand.Ready, lobby.State, true));
        Assert.That(command.Command, Is.EqualTo(LobbyCommand.Ready));
        Assert.That(command.Ready, Is.True);
        Assert.That(command.Session, Is.EqualTo(100));
    }

    /// <summary>Replica state cannot be replaced by another sender, stale revisions or reassigned identities.</summary>
    [Test]
    public void ReplicaRejectsStaleForgedAndReassignedPublications()
    {
        var host = new LobbyAuthority(100, "Host");
        host.Join(10, "Client");
        var replica = new LobbyReplica();
        Assert.That(replica.Accept(host.State, 2, 41, 42), Is.False);
        Assert.That(replica.Accept(host.State, 99, 42, 42), Is.False);
        Assert.That(replica.State, Is.Null);
        Assert.That(replica.Accept(host.State, 2, 42, 42), Is.True);
        LobbySnapshot old = host.State;
        host.SetReady(10, true);
        Assert.That(replica.Accept(host.State, 1, 42, 42), Is.False);
        Assert.That(replica.Accept(host.State, 2, 42, 42), Is.True);
        Assert.That(replica.Accept(old, 2, 42, 42), Is.False);
        Assert.That(replica.Accept(host.State, 2, 42, 42), Is.False);
        var other = new LobbySnapshot(101, 1000, 101, SessionPhase.Lobby, host.State.Players);
        Assert.That(replica.Accept(other, 2, 42, 42), Is.False);
        Assert.That(replica.State, Is.SameAs(host.State));
    }

    /// <summary>Delayed commands from an earlier arena or lifecycle phase cannot ready or restart the current lobby.</summary>
    [Test]
    public void CommandsMustMatchCurrentSessionGenerationAndPhase()
    {
        var host = new LobbyAuthority(100, "Host");
        host.Join(10, "Client");
        ulong[] peers = { 10 };
        Assert.That(host.Execute(10, LobbyCommand.Ready, 99, 100, SessionPhase.Lobby, true, peers), Is.False);
        Assert.That(host.Execute(10, LobbyCommand.Ready, 100, 100, SessionPhase.Lobby, true, peers), Is.True);
        host.SetReady(0, true);
        host.Start(0);
        Assert.That(host.Execute(10, LobbyCommand.Ready, 100, 100, SessionPhase.Lobby, true, peers), Is.False);
        LobbySnapshot arena = host.State;
        host.Return(0);
        Assert.That(host.Execute(10, LobbyCommand.Ready, arena.Session, arena.Match, arena.Phase, true, peers), Is.False);
        Assert.That(host.State.Players.All(player => !player.Ready), Is.True);
    }

    /// <summary>Malformed nested JSON, invalid flags and missing fields fail as protocol errors.</summary>
    /// <param name="json">Untrusted state body.</param>
    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("{\"Session\":-1}")]
    [TestCase("{\"Players\":[null]}")]
    [TestCase("{\"Players\":[{\"Id\":1,\"Name\":null,\"Ready\":false}],\"Session\":1,\"Revision\":1,\"Match\":1,\"Phase\":0,\"Player\":1}")]
    public void InvalidStateBodiesAreRejected(string json)
    {
        byte[] payload = new byte[] { (byte)'T', (byte)'L', 1, 0 }.Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        Assert.Throws<ArgumentException>(() => LobbyCodec.DecodeState(payload));
    }
}
