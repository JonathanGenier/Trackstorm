using Trackstorm.Core.Sessions;

namespace Trackstorm.LeaseService.Tests;

/// <summary>Real durable store with a controlled clock, including concurrency and restart fencing.</summary>
[TestFixture]
internal sealed class LeaseStoreTests
{
    /// <summary>Verifies exclusive grants across the specified failure boundary.</summary>
    [Test]
    public void RenewalPartitionCrashAndSequentialTakeover()
    {
        using var fixture = new Fixture();
        var first = fixture.Store.Execute("create", new(fixture.Session, 0, string.Empty), "host")!;
        for (int i = 0; i < 20; i++)
        {
            fixture.Clock.Advance(2);
            first = fixture.Store.Execute("renew", Request(first), "host")!;
            Assert.That(first, Is.Not.Null);
            Assert.That(fixture.Store.Execute("takeover", Request(first), "client"), Is.Null, "A P2P partition does not affect the lease.");
        }

        fixture.Clock.Advance(10);
        Assert.That(fixture.Store.Execute("renew", Request(first), "host"), Is.Null);
        var second = fixture.Store.Execute("takeover", Request(first), "client")!;
        Assert.That(second.Epoch, Is.EqualTo(2));
        Assert.That(second.Token, Is.Not.EqualTo(first.Token));
        Assert.That(fixture.Store.Execute("renew", Request(first), "host"), Is.Null);
        Assert.That(fixture.Store.Execute("renew", Request(second), "host"), Is.Null);
        fixture.Clock.Advance(10);
        Assert.That(fixture.Store.Execute("takeover", Request(second), "host")!.Epoch, Is.EqualTo(3));
    }

    /// <summary>Verifies exclusive grants across the specified failure boundary.</summary>
    [Test]
    public void CompetingClaimsHaveExactlyOneWinner()
    {
        using var fixture = new Fixture();
        var first = fixture.Store.Execute("create", new(fixture.Session, 0, string.Empty), "host")!;
        fixture.Clock.Advance(10);
        var results = new AuthorityLease?[16];
        Parallel.For(0, results.Length, i => results[i] = fixture.Store.Execute("takeover", Request(first), "peer" + i));
        Assert.That(results.Count(value => value is not null), Is.EqualTo(1));
        Assert.That(fixture.Store.Read(fixture.Session)!.Epoch, Is.EqualTo(2));
    }

    /// <summary>Verifies exclusive grants across the specified failure boundary.</summary>
    [Test]
    public void RestartQuarantinesOldGrantAndNeverRenewsIt()
    {
        using var fixture = new Fixture();
        var first = fixture.Store.Execute("create", new(fixture.Session, 0, string.Empty), "host")!;
        Assert.Throws<IOException>(() => new LeaseStore(fixture.Path, fixture.Clock));
        fixture.Store.Dispose();
        using var restarted = new LeaseStore(fixture.Path, fixture.Clock);
        Assert.That(restarted.Execute("renew", Request(first), "host"), Is.Null);
        Assert.That(restarted.Execute("takeover", Request(first), "client"), Is.Null);
        fixture.Clock.Advance(9.999);
        Assert.That(restarted.Execute("takeover", Request(first), "client"), Is.Null);
        fixture.Clock.Advance(.001);
        Assert.That(restarted.Execute("takeover", Request(first), "client")!.Epoch, Is.EqualTo(2));
    }

    private static LeaseRequest Request(AuthorityLease lease) => new(lease.Session, lease.Epoch, lease.Token);

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(double seconds) => _ticks += (long)(seconds * TimeSpan.TicksPerSecond);
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture() => Store = new LeaseStore(Path, Clock);
        internal string Path { get; } = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "lease-" + Guid.NewGuid().ToString("N"), "ledger.json");
        internal string Session { get; } = new('A', 64);
        internal Clock Clock { get; } = new();
        internal LeaseStore Store { get; }
        public void Dispose()
        {
            Store.Dispose();
            Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, true);
        }
    }
}
