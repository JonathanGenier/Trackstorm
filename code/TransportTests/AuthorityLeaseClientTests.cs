using Trackstorm.Client.Online;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Client permission is conservative under delayed responses, suspension and outages.</summary>
[TestFixture]
internal sealed class AuthorityLeaseClientTests
{
    /// <summary>Retirement never grants permission from a queued response; failed release safely waits for service expiry.</summary>
    /// <param name="delay">Elapsed time before delivering the last renewal response.</param>
    [TestCase(0)]
    [TestCase(2.9)]
    [TestCase(3)]
    [TestCase(10)]
    public void RetiredRenewalCannotReviveAndSuccessorRejectsOldToken(double delay)
    {
        var clock = new Clock();
        using var store = new LeaseStore(clock);
        var transport = new LeaseTransport(store, "host");
        using var host = new AuthorityLeaseClient(transport, "host", clock);
        string session = new('C', 64);
        host.Poll(session, true, 1, false);
        host.Poll(session, true, 1, false);
        clock.Advance(2);
        transport.Delay = true;
        host.Poll(session, true, 1, false);
        var old = store.Read(session)!;
        host.Release(1);
        clock.Advance(delay);
        transport.Delay = false;
        transport.Pending!.SetResult(transport.Response);
        host.Poll(session, true, 1, false);
        Assert.That(host.Available(1), Is.False);
        var successorTransport = new LeaseTransport(store, "successor");
        using var successor = new AuthorityLeaseClient(successorTransport, "successor", clock);
        for (int frame = 0; frame < 60; frame++)
        {
            host.Poll(session, true, 1, false);
            successor.Poll(session, false, store.Read(session)!.Epoch, true);
            successor.Acquire(1);
            clock.Advance(0.25);
        }

        Assert.That(transport.Operations, Is.EqualTo(new[] { "create", "renew", "release" }));
        Assert.That(successorTransport.Operations.Count(operation => operation == "takeover"), Is.EqualTo(1));
        Assert.That(store.Read(session)!.Epoch, Is.EqualTo(2));
        Assert.That(successor.Available(2), Is.True);
        Assert.That(store.Execute("renew", new(session, old.Epoch, old.Token), "host"), Is.Null);
        Assert.That(host.Available(1), Is.False);
    }

    /// <summary>Dormant clients make no requests and cannot reuse cached expiry when a new observation period starts.</summary>
    [Test]
    public void ObserverRequiresFreshReadAfterEveryActivation()
    {
        var clock = new Clock();
        using var store = new LeaseStore(clock);
        string session = new('E', 64);
        var initial = store.Execute("create", new(session, 0, string.Empty), "host")!;
        var transport = new LeaseTransport(store, "client");
        using var client = new AuthorityLeaseClient(transport, "client", clock);
        for (int second = 0; second < 20; second++)
        {
            client.Poll(session, false, 1, false);
            clock.Advance(1);
        }

        Assert.That(transport.Operations, Is.Empty);
        client.Poll(session, false, 1, true);
        client.Poll(session, false, 1, true);
        Assert.That(client.Expired(1, initial.Holder), Is.True);
        Assert.That(client.HostProgressAt, Is.EqualTo(clock.GetTimestamp()), "Without a previous token, missed host renewal cannot be ruled out.");
        client.Poll(session, false, 1, false);
        Assert.That(client.Expired(1, initial.Holder), Is.False);
        transport.Offline = true;
        client.Poll(session, false, 1, true);
        client.Poll(session, false, 1, true);
        Assert.That(client.Expired(1, initial.Holder), Is.False);
        Assert.That(client.Acquire(1), Is.False);
        Assert.That(transport.Operations, Is.EqualTo(new[] { "read", "read" }));
    }

    /// <summary>A renewal response arriving after local expiry cannot revive a retired authority.</summary>
    [Test]
    public void LateRenewalAndServiceOutageFailClosed()
    {
        var clock = new Clock();
        using var store = new LeaseStore(clock);
        var transport = new LeaseTransport(store, "host");
        using var client = new AuthorityLeaseClient(transport, "host", clock);
        string session = new('B', 64);
        client.Poll(session, true, 1, false);
        client.Poll(session, true, 1, false);
        Assert.That(client.Available(1), Is.True);
        clock.Advance(2);
        transport.Delay = true;
        client.Poll(session, true, 1, false);
        clock.Advance(8);
        Assert.That(client.Available(1), Is.False);
        transport.Pending!.SetResult(transport.Response);
        transport.Delay = false;
        transport.Offline = true;
        client.Poll(session, true, 1, false);
        Assert.That(client.Available(1), Is.False);
        clock.Advance(30);
        client.Poll(session, true, 1, false);
        Assert.That(client.Available(1), Is.False);
        Assert.That(client.Acquire(1), Is.False);
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(double seconds) => _ticks += (long)(seconds * TimeSpan.TicksPerSecond);
    }
}
