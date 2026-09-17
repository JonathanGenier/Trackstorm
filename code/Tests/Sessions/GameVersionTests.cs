using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Core.Tests.Sessions;

/// <summary>Source-derived build identity and atomic compatibility gates.</summary>
[TestFixture]
internal sealed class GameVersionTests
{
    /// <summary>Canonical values preserve all four components.</summary>
    /// <param name="revision">Build revision.</param>
    [TestCase(0)]
    [TestCase(8)]
    [TestCase(65534)]
    public void CanonicalValuesRoundTrip(int revision)
    {
        var version = new GameVersion(revision);
        Assert.That(GameVersion.Parse(version.ToString()), Is.EqualTo(version));
        Assert.That(version.ToString(), Is.EqualTo($"0.0.1.{revision}"));
    }

    /// <summary>Malformed or absent wire values never match.</summary>
    /// <param name="value">Untrusted version.</param>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("0.0.1")]
    [TestCase("0.0.1.-1")]
    [TestCase("0.0.1.+1")]
    [TestCase("0.0.1.01")]
    [TestCase("0.0.1.65535")]
    [TestCase("0.0.1.999999999999999")]
    [TestCase("0.0.1.1 ")]
    [TestCase(" 0.0.1.1")]
    [TestCase("0.0.1.1-beta")]
    [TestCase("0.0.1.١")]
    [TestCase("0.0.2.1")]
    [TestCase("1.0.1.1")]
    public void InvalidVersionsFailClosed(string? value)
    {
        Assert.That(GameVersion.TryParse(value, out var parsed), Is.False);
        Assert.That(parsed, Is.Null);
        Assert.Throws<ArgumentException>(() => GameVersion.Parse(value!));
        Assert.That(GameVersion.Current.IsCompatible(value), Is.False);
        Assert.That(GameVersion.Current.MismatchMessage(value), Does.Contain("unknown/invalid"));
    }

    /// <summary>Revision bounds match generated assembly metadata.</summary>
    /// <param name="revision">Invalid revision.</param>
    [TestCase(-1)]
    [TestCase(65535)]
    public void RevisionMustFitAssemblyMetadata(int revision) => Assert.Throws<ArgumentOutOfRangeException>(() => new GameVersion(revision));

    /// <summary>Runtime and ordinary assembly versions share the canonical repository source.</summary>
    [Test]
    public void RuntimeAndAssemblyMetadataMatchRepositorySource()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null);
        string canonical = XDocument.Load(Path.Combine(directory!.FullName, "Directory.Build.props")).Descendants("TrackstormVersion").Single().Value;
        Assembly assembly = typeof(GameVersion).Assembly;
        Assert.That(GameVersion.Current.ToString(), Is.EqualTo(canonical));
        Assert.That(assembly.GetName().Version!.ToString(), Is.EqualTo(canonical));
        Assert.That(assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version, Is.EqualTo(canonical));
        Assert.That(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, Is.EqualTo(canonical));
    }

    /// <summary>Rejection consumes no identity, slot, event or reservation.</summary>
    /// <param name="remote">Incompatible joining runtime.</param>
    [TestCase("0.0.1.3")]
    [TestCase("0.0.1.5")]
    [TestCase("")]
    [TestCase("invalid")]
    public void RejectedFreshJoinAllocatesNothing(string remote)
    {
        var host = new LobbyAuthority(100, "Host", gameVersion: new GameVersion(4));
        var before = host.State;
        ulong sequence = host.Events.LastSequence;
        Assert.That(host.Join(10, remote, "Guest", "subject"), Is.Zero);
        Assert.That(host.Add(10, remote, 2, "Guest"), Is.False);
        Assert.That(host.State, Is.SameAs(before));
        Assert.That(host.Events.LastSequence, Is.EqualTo(sequence));
        Assert.That(host.Peers, Is.Empty);
        Assert.That(host.PlayerId(10), Is.Zero);
        Assert.That(host.FindPlayer("subject"), Is.Zero);
        Assert.That(host.Disconnect(10), Is.False);
        Assert.That(host.Join(10, "0.0.1.4", "Guest", "subject"), Is.EqualTo(2));
        for (ulong peer = 11; peer < 17; peer++)
        {
            Assert.That(host.Join(peer, "0.0.1.4", "Guest"), Is.Not.Zero);
        }

        Assert.That(host.State.Players.Count, Is.EqualTo(8));
    }

    /// <summary>Mismatch cannot rebind in either phase; a later compatible attempt still succeeds.</summary>
    /// <param name="arena">Whether the retained player is in an arena.</param>
    /// <param name="remote">Incompatible returning runtime.</param>
    [TestCase(false, "0.0.1.3")]
    [TestCase(false, "0.0.1.5")]
    [TestCase(true, "0.0.1.3")]
    [TestCase(true, "0.0.1.5")]
    public void ResumeMismatchPreservesReservationAndCannotRestoreControl(bool arena, string remote)
    {
        var host = new LobbyAuthority(100, "Host", gameVersion: new GameVersion(4));
        ulong player = host.Join(10, "0.0.1.4", "Guest", "subject");
        if (arena)
        {
            host.SetReady(0, true);
            host.SetReady(10, true);
            Assert.That(host.Start(0), Is.True);
        }

        host.Disconnect(10);
        var before = host.State;
        Assert.That(host.Resume(20, remote, 100, player, 1, "subject"), Is.False);
        Assert.That(host.Join(21, remote, "Late", "new-subject"), Is.Zero);
        Assert.That(host.State, Is.SameAs(before));
        Assert.That(host.Peers, Is.Empty);
        Assert.That(host.FindPlayer("subject"), Is.EqualTo(player));
        Assert.That(host.Resume(20, "0.0.1.4", 100, player, 1, "subject"), Is.True);
        Assert.That(host.State.Players.Single(value => value.Id == player).Generation, Is.EqualTo(2));
    }

    /// <summary>Admission intents transmit canonical versions and old omitted fields stay incompatible.</summary>
    [Test]
    public void AdmissionWireCarriesVersionAndMissingVersionFailsClosed()
    {
        Assert.That(LobbyCodec.DecodeCommand(LobbyCodec.EncodeCommand(LobbyCommand.Join, null)).GameVersion, Is.EqualTo(GameVersion.Current.ToString()));
        Assert.That(LobbyCodec.DecodeCommand(LobbyCodec.EncodeResume(100, 2, 1)).GameVersion, Is.EqualTo(GameVersion.Current.ToString()));
        byte[] legacy = [(byte)'T', (byte)'L', 2, 1, .. Encoding.UTF8.GetBytes("{\"Command\":0,\"Session\":0,\"Match\":0,\"Phase\":0,\"Ready\":false,\"Name\":\"Guest\",\"Player\":0,\"Generation\":0}")];
        Assert.That(LobbyCodec.DecodeCommand(legacy).GameVersion, Is.Empty);
        var version = new GameVersion(4);
        byte[] rejection = LobbyCodec.EncodeVersionMismatch(version);
        Assert.That(LobbyCodec.IsVersionMismatch(rejection), Is.True);
        Assert.That(LobbyCodec.DecodeVersionMismatch(rejection), Is.EqualTo(version.ToString()));
    }
}
