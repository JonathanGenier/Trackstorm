using Trackstorm.Client.Online;

namespace Trackstorm.Transport.Tests;

/// <summary>Client permission is conservative under delayed responses, suspension and outages.</summary>
[TestFixture]
internal sealed class AuthorityLeaseClientTests
{
    /// <summary>A renewal response arriving after local expiry cannot revive a retired authority.</summary>
    [Test]
    public void LateRenewalAndServiceOutageFailClosed()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "lease-" + Guid.NewGuid().ToString("N"), "ledger.json");
        var clock = new Clock();
        try
        {
            using var store = new LeaseService.LeaseStore(path, clock);
            var transport = new LeaseTransport(store, "host");
            using var client = new AuthorityLeaseClient(transport, "host", clock);
            string session = new('B', 64);
            client.Poll(session, true, 1);
            client.Poll(session, true, 1);
            Assert.That(client.Available(1), Is.True);
            clock.Advance(2);
            transport.Delay = true;
            client.Poll(session, true, 1);
            clock.Advance(8);
            Assert.That(client.Available(1), Is.False);
            transport.Pending!.SetResult(transport.Response);
            transport.Delay = false;
            transport.Offline = true;
            client.Poll(session, true, 1);
            Assert.That(client.Available(1), Is.False);
            clock.Advance(30);
            client.Poll(session, true, 1);
            Assert.That(client.Available(1), Is.False);
            Assert.That(client.Acquire(1), Is.False);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(double seconds) => _ticks += (long)(seconds * TimeSpan.TicksPerSecond);
    }
}
