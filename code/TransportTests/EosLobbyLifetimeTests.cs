using Trackstorm.Client.Online;

namespace Trackstorm.Transport.Tests;

/// <summary>Native-free ownership and identity presentation regression checks.</summary>
[TestFixture]
internal sealed class EosLobbyLifetimeTests
{
    /// <summary>A completed operation releases once; canceled operations remain owned until pre-platform teardown.</summary>
    [Test]
    public void PendingHandlesSurviveConsumerCancellationAndReleaseExactlyOnce()
    {
        var events = new List<string>();
        var handles = new EosPendingHandles();
        var completed = handles.Retain(() => events.Add("completed"));
        var canceled = handles.Retain(() => events.Add("canceled"));
        Assert.That(events, Is.Empty);
        completed.Dispose();
        completed.Dispose();
        Assert.That(events, Is.EqualTo(new[] { "completed" }));
        handles.Dispose();
        events.Add("platform released");
        handles.Dispose();
        canceled.Dispose();
        Assert.That(events, Is.EqualTo(new[] { "completed", "canceled", "platform released" }));
        Assert.Throws<ObjectDisposedException>(() => handles.Retain(() => events.Add("invalid")));
    }

    /// <summary>Each replaced provider's still-pending resources remain owned by the same platform.</summary>
    [Test]
    public void RepeatedOperationsDoNotDropEarlierPendingOwnership()
    {
        int released = 0;
        using (var handles = new EosPendingHandles())
        {
            for (int i = 0; i < 20; i++)
            {
                var operation = handles.Retain(() => released++);
                if (i % 2 == 0)
                {
                    operation.Dispose();
                }
            }

            Assert.That(released, Is.EqualTo(10));
        }

        Assert.That(released, Is.EqualTo(20));
    }

    /// <summary>Authentication alone cannot advertise readiness before the native coordinator exists.</summary>
    [Test]
    public void OnlyAuthenticatedCoordinatorEnablesHosting()
    {
        foreach (var state in Enum.GetValues<OnlineIdentityState>())
        {
            Assert.That(EosLobbyStatus.FromIdentity(state, false, true, null).Online, Is.False);
            Assert.That(EosLobbyStatus.FromIdentity(state, true, true, null).Online, Is.EqualTo(state == OnlineIdentityState.LoggedIn));
        }
    }

    /// <summary>Initialization failures, login failures and signed-out state have distinct recovery guidance.</summary>
    [Test]
    public void FailuresAndSignedOutStateExplainRecovery()
    {
        var unavailable = EosLobbyStatus.FromIdentity(OnlineIdentityState.Failed, false, false, "Run setup-eos.ps1.");
        var authentication = EosLobbyStatus.FromIdentity(OnlineIdentityState.Failed, false, true, "Check deployment.");
        var signedOut = EosLobbyStatus.FromIdentity(OnlineIdentityState.Stopped, false, false, null);
        Assert.That(unavailable.Text, Does.Contain("EOS unavailable").And.Contain("setup-eos.ps1"));
        Assert.That(authentication.Text, Does.Contain("Authentication failed").And.Contain("Check deployment"));
        foreach (var status in new[] { unavailable, authentication, signedOut })
        {
            Assert.That(status.CanRetry, Is.True);
            Assert.That(status.HostReason, Is.Not.Empty);
            Assert.That(status.Online, Is.False);
        }
    }
}
