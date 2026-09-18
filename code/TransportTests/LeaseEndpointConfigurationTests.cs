using Trackstorm.Client.Online;

namespace Trackstorm.Transport.Tests;

/// <summary>Players use bundled public configuration; explicit invalid overrides cannot silently fall back.</summary>
[TestFixture]
internal sealed class LeaseEndpointConfigurationTests
{
    /// <summary>The Client assembly carries the deployed public endpoint without local setup.</summary>
    [Test]
    [NonParallelizable]
    public void EmbeddedBundledAddress()
    {
        string? previous = Environment.GetEnvironmentVariable("TRACKSTORM_LEASE_URL");

        try
        {
            Environment.SetEnvironmentVariable("TRACKSTORM_LEASE_URL", null);
            Assert.That(LeaseEndpointConfiguration.Resolve().AbsoluteUri, Is.EqualTo("https://trackstorm-authority-leases.crypt-jo-g.workers.dev/"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACKSTORM_LEASE_URL", previous);
        }
    }

    /// <summary>Bundled configuration works without environment setup, and test overrides are explicit.</summary>
    [Test]
    public void BundledAddressAndOverride()
    {
        Assert.That(LeaseEndpointConfiguration.Resolve(null, "https://leases.example.test").AbsoluteUri, Is.EqualTo("https://leases.example.test/"));
        Assert.That(LeaseEndpointConfiguration.Resolve("https://qa.example.test/base", "https://leases.example.test").AbsoluteUri, Is.EqualTo("https://qa.example.test/base/"));
        Assert.Throws<InvalidOperationException>(() => LeaseEndpointConfiguration.Resolve(null, null));
    }

    /// <summary>Rejected addresses cannot receive the user's identity token.</summary>
    /// <param name="address">Untrusted or malformed explicit override.</param>
    [TestCase("")]
    [TestCase("http://leases.example.test")]
    [TestCase("https://user:password@leases.example.test")]
    [TestCase("https://leases.example.test/?credential=x")]
    [TestCase("https://leases.example.test/#fragment")]
    [TestCase(" https://leases.example.test")]
    public void InvalidOverrideFailsClosed(string address) => Assert.Throws<InvalidOperationException>(() => LeaseEndpointConfiguration.Resolve(address, "https://leases.example.test"));
}
