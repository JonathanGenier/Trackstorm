using Trackstorm.Client.Online;

namespace Trackstorm.Transport.Tests;

/// <summary>Restart hints are bounded, short-lived, user-specific and contain no authorization secret.</summary>
[TestFixture]
internal sealed class ResumeLocatorTests
{
    /// <summary>Malformed, oversized, expired and mismatched-account hints are discarded.</summary>
    [Test]
    public void LoadsOnlyCurrentBoundedRoutingState()
    {
        string path = Path.Combine(Path.GetTempPath(), "trackstorm-resume-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new ResumeLocatorStore(path);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var locator = new ResumeLocator("lobby", 100, 2, 1, "identity", 2, new string('a', 32), now.AddMinutes(2));
        try
        {
            store.Save(locator);
            Assert.That(store.Load("identity", now), Is.EqualTo(locator));
            Assert.That(store.Load("different", now), Is.Null);
            Assert.That(File.Exists(path), Is.False);
            store.Save(locator);
            Assert.That(store.Load("identity", now.AddMinutes(2)), Is.Null);
            var retained = locator with { RetainedHost = true };
            store.Save(retained);
            Assert.That(store.Load("identity", now.AddHours(1)), Is.EqualTo(retained), "Core decides same-match eligibility; local cleanup must not discard former-host routing.");
            Assert.That(store.Load("different", now.AddHours(1)), Is.Null);
            foreach (string invalid in new[] { "broken", "null", "{}", new string('a', 4097), "{\"Identity\":\"identity\",\"Lobby\":null}" })
            {
                File.WriteAllText(path, invalid);
                Assert.That(store.Load("identity", now), Is.Null);
            }

            store.Save(locator);
            store.Clear();
            store.Clear();
            Assert.That(File.Exists(path), Is.False);
        }
        finally
        {
            store.Clear();
        }
    }
}
