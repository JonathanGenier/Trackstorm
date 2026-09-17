using Trackstorm.Client.Online;

namespace Trackstorm.Transport.Tests;

/// <summary>Restart hints are bounded, user-specific and contain no authorization secret.</summary>
[TestFixture]
internal sealed class ResumeLocatorTests
{
    /// <summary>Malformed, oversized and mismatched-account hints are discarded.</summary>
    [Test]
    public void LoadsOnlyCurrentBoundedRoutingState()
    {
        string path = Path.Combine(Path.GetTempPath(), "trackstorm-resume-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new ResumeLocatorStore(path);
        var locator = new ResumeLocator("lobby", 100, 2, 1, "identity", 2, new string('a', 32));
        try
        {
            store.Save(locator);
            Assert.That(store.Load("identity"), Is.EqualTo(locator));
            Assert.That(store.Load("different"), Is.Null);
            Assert.That(File.Exists(path), Is.False);
            store.Save(locator);
            Assert.That(store.Load("identity"), Is.EqualTo(locator));
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { locator.Lobby, locator.Session, locator.Player, locator.Generation, locator.Identity, locator.AuthorityEpoch, locator.Host, Expires = DateTimeOffset.UnixEpoch, RetainedHost = false }));
            Assert.That(store.Load("identity"), Is.EqualTo(locator), "A legacy ordinary-client hint must not enforce its old local expiry.");
            Assert.That(store.Load("different"), Is.Null);
            foreach (string invalid in new[] { "broken", "null", "{}", new string('a', 4097), "{\"Identity\":\"identity\",\"Lobby\":null}" })
            {
                File.WriteAllText(path, invalid);
                Assert.That(store.Load("identity"), Is.Null);
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
