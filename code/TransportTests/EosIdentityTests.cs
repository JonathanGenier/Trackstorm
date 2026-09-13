using System.Reflection;
using System.Text.Json;
using Trackstorm.Client.Online;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Transport.Tests;

/// <summary>Exercises identity lifecycle through controllable callbacks without network access.</summary>
[TestFixture]
internal sealed class EosIdentityTests
{
    /// <summary>Invalid configuration cannot reach EOS or leak submitted values in its error.</summary>
    /// <param name="property">Configuration field to invalidate.</param>
    /// <param name="value">Invalid input, which must not be echoed.</param>
    [TestCase("Environment", "production")]
    [TestCase("DeploymentName", "unsafe\nlabel")]
    [TestCase("ProductId", "")]
    [TestCase("SandboxId", "<sandbox>")]
    [TestCase("DeploymentId", " ")]
    [TestCase("ClientId", "invalid client")]
    [TestCase("ClientSecret", "private\nsecret")]
    public void RejectsInvalidConfiguration(string property, string value)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(Configuration()))!;
        values[property] = value;
        var config = JsonSerializer.Deserialize<EosConfiguration>(JsonSerializer.Serialize(values))!;
        var fake = new FakePlatform();
        using var service = new EosIdentityService(() => fake);
        service.Start(config);
        Assert.That(fake.Starts, Is.Zero);
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.Failed));
        Assert.That(service.Diagnostics, Does.Not.Contain("private\nsecret"));
    }

    /// <summary>SDK credential length boundaries and explicit null JSON values are rejected safely.</summary>
    [Test]
    public void ValidatesSdkLimitsAndNulls()
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(JsonSerializer.Serialize(Configuration()))!;
        foreach (string key in values.Keys.ToArray())
        {
            string? original = values[key];
            values[key] = null;
            Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<EosConfiguration>(JsonSerializer.Serialize(values))!.Validate());
            values[key] = original;
        }

        values["ClientSecret"] = new string('x', 65);
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Deserialize<EosConfiguration>(JsonSerializer.Serialize(values))!.Validate());
        values["ClientSecret"] = new string('x', 64);
        Assert.DoesNotThrow(() => JsonSerializer.Deserialize<EosConfiguration>(JsonSerializer.Serialize(values))!.Validate());
    }

    /// <summary>Repeated startup/login calls produce only one native operation.</summary>
    [Test]
    public void GuardsDuplicateOperationsAndCompletesLogout()
    {
        var fake = new FakePlatform();
        using var service = new EosIdentityService(() => fake);
        service.Start(Configuration());
        service.Start(Configuration());
        service.Login();
        service.Login();
        Assert.That(fake.Starts, Is.EqualTo(1));
        Assert.That(fake.Logins, Is.EqualTo(1));
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.LoggingIn));
        fake.CompleteLogin!(Identity(), null);
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.LoggedIn));
        fake.CompleteLogin!(null, "duplicate must be ignored");
        Assert.That(service.ProductUserId, Is.EqualTo(Identity()));
        service.Logout();
        Assert.That(service.ProductUserId, Is.Null);
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.LoggingOut));
        fake.CompleteLogout!(null);
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.Stopped));
        Assert.That(fake.Disposals, Is.EqualTo(1));
    }

    /// <summary>Callbacks from replaced or disposed platforms cannot resurrect identity.</summary>
    [Test]
    public void RejectsLateCallbacksAcrossReplacementAndDisposal()
    {
        var first = new FakePlatform();
        var second = new FakePlatform();
        var queue = new Queue<FakePlatform>([first, second]);
        var service = new EosIdentityService(() => queue.Dequeue());
        service.Start(Configuration());
        service.Login();
        service.Logout();
        service.Start(Configuration());
        service.Login();
        first.CompleteLogin!(Identity(), null);
        first.Lost!("old notification");
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.LoggingIn));
        second.CompleteLogin!(Identity(), null);
        service.Dispose();
        second.CompleteLogin!(Identity(), null);
        second.Lost!("late notification");
        service.Dispose();
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.Disposed));
        Assert.That(service.ProductUserId, Is.Null);
        Assert.That(first.Disposals, Is.EqualTo(1));
        Assert.That(second.Disposals, Is.EqualTo(1));
        Assert.Throws<ObjectDisposedException>(() => service.Start(Configuration()));
    }

    /// <summary>Monotonic timeout closes a pending login and ignores its eventual completion.</summary>
    [Test]
    public void TimesOutWithoutRetainingIdentityOrPlatform()
    {
        var fake = new FakePlatform();
        var clock = new FakeClock();
        using var service = new EosIdentityService(() => fake, clock);
        service.Start(Configuration());
        service.Login();
        clock.Advance(59);
        service.Tick();
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.LoggingIn));
        clock.Advance(1);
        service.Tick();
        fake.CompleteLogin!(Identity(), null);
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.Failed));
        Assert.That(service.Failure, Does.Contain("timed out"));
        Assert.That(service.PlatformInitialized, Is.False);
        Assert.That(service.ProductUserId, Is.Null);
    }

    /// <summary>Auth expiration cannot leave a usable stale PUID behind.</summary>
    [Test]
    public void ClearsExpiredIdentity()
    {
        var fake = new FakePlatform();
        using var service = new EosIdentityService(() => fake);
        service.Start(Configuration());
        service.Login();
        fake.CompleteLogin!(Identity(), null);
        fake.Lost!("credentials expired; log in again");
        Assert.That(service.ProductUserId, Is.Null);
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.Failed));
        Assert.That(fake.Disposals, Is.EqualTo(1));
    }

    /// <summary>Missing native binaries produce a recovery instruction and release partial ownership.</summary>
    [Test]
    public void HandlesMissingNativeLibrary()
    {
        var fake = new FakePlatform { StartError = new DllNotFoundException("sensitive native path") };
        using var service = new EosIdentityService(() => fake);
        service.Start(Configuration());
        Assert.That(service.Diagnostics, Does.Contain("setup-eos.ps1").And.Not.Contain("sensitive native path"));
        Assert.That(service.PlatformInitialized, Is.False);
        Assert.That(fake.Disposals, Is.EqualTo(1));
    }

    /// <summary>Core has no SDK reference, and PUIDs cannot convert to player IDs or expose raw identity.</summary>
    [Test]
    public void KeepsOnlineIdentityOutsideCoreAndRedactsDiagnostics()
    {
        Assert.That(typeof(LobbyAuthority).Assembly.GetReferencedAssemblies().Select(a => a.Name), Has.None.Contains("Epic"));
        Assert.That(typeof(LobbyAuthority).Assembly.GetReferencedAssemblies().Select(a => a.Name), Has.None.Contains("Client"));
        Type type = typeof(OnlineProductUserId);
        Assert.That(type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name is "op_Implicit" or "op_Explicit"), Is.Empty);
        Assert.That(typeof(IConvertible).IsAssignableFrom(type), Is.False);

        Assert.That(Identity().ToString(), Does.Not.Contain("1234567890abcdef1234567890abcdef"));
        Assert.That(Configuration().ToString(), Does.Not.Contain("test-secret"));
        Assert.Throws<ArgumentException>(() => new OnlineProductUserId("127.0.0.1"));
        Assert.Throws<ArgumentException>(() => new OnlineProductUserId("Player"));
        Assert.Throws<ArgumentException>(() => new OnlineProductUserId(new string('0', 32)));
    }

    /// <summary>Abandoned official-wrapper callbacks are released by owner without touching another platform.</summary>
    [Test]
    public void RemovesOnlyReleasedOwnersCallbackRegistrations()
    {
        Type helper = typeof(Epic.OnlineServices.Helper);
        MethodInfo add = helper.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "AddCallback" && method.GetParameters()[0].IsOut);
        var registrations = (System.Collections.IDictionary)helper.GetField("s_ClientDatas", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object first = new();
        object second = new();
        object?[] firstArgs = [IntPtr.Zero, first, new Delegate[] { new Action(() => { }) }];
        object?[] secondArgs = [IntPtr.Zero, second, new Delegate[] { new Action(() => { }) }];
        add.Invoke(null, firstArgs);
        add.Invoke(null, secondArgs);
        try
        {
            Epic.OnlineServices.Helper.ReleaseTrackstormCallbacks(first);
            Assert.That(registrations.Contains(firstArgs[0]!), Is.False);
            Assert.That(registrations.Contains(secondArgs[0]!), Is.True);
            Epic.OnlineServices.Helper.ReleaseTrackstormCallbacks(first);
        }
        finally
        {
            Epic.OnlineServices.Helper.ReleaseTrackstormCallbacks(first);
            Epic.OnlineServices.Helper.ReleaseTrackstormCallbacks(second);
        }
    }

    /// <summary>File parsing failures never echo JSON content or credentials.</summary>
    [Test]
    public void RedactsMalformedAndUnknownConfigurationFields()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"ClientSecret\": \"private-value\", malformed}");
            var error = Assert.Throws<InvalidOperationException>(() => EosConfiguration.Load(path));
            Assert.That(error!.Message, Does.Not.Contain("private-value").And.Contain("configuration"));
            File.WriteAllText(path, "{\"Unknown\": \"private-value\"}");
            Assert.Throws<InvalidOperationException>(() => EosConfiguration.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Logout failure is visible and cannot retain either online identity or the old platform.</summary>
    [Test]
    public void HandlesLogoutFailure()
    {
        var fake = new FakePlatform();
        using var service = new EosIdentityService(() => fake);
        service.Start(Configuration());
        service.Login();
        fake.CompleteLogin!(Identity(), null);
        service.Logout();
        fake.CompleteLogout!("logout failed; retry");
        fake.CompleteLogin!(Identity(), null);
        Assert.That(service.State, Is.EqualTo(OnlineIdentityState.Failed));
        Assert.That(service.ProductUserId, Is.Null);
        Assert.That(service.PlatformInitialized, Is.False);
    }

    private static EosConfiguration Configuration() => new()
    {
        Environment = "development",
        DeploymentName = "unit-test",
        ProductId = "test-product",
        SandboxId = "test-sandbox",
        DeploymentId = "test-deployment",
        ClientId = "test-client",
        ClientSecret = "test-secret",
    };

    private static OnlineProductUserId Identity() => new("1234567890abcdef1234567890abcdef");

    private sealed class FakeClock : TimeProvider
    {
        private long _seconds;

        public override long TimestampFrequency => 1;

        public override long GetTimestamp() => _seconds;

        public void Advance(long seconds) => _seconds += seconds;
    }

    private sealed class FakePlatform : IEosPlatform
    {
        public int Starts { get; private set; }

        public int Logins { get; private set; }

        public int Disposals { get; private set; }

        public Exception? StartError { get; init; }

        public Action<OnlineProductUserId?, string?>? CompleteLogin { get; private set; }

        public Action<string?>? CompleteLogout { get; private set; }

        public Action<string>? Lost { get; private set; }

        public void Start(EosConfiguration configuration)
        {
            ++Starts;
            if (StartError is not null)
            {
                throw StartError;
            }
        }

        public void Tick()
        {
        }

        public void Login(Action<OnlineProductUserId?, string?> completed)
        {
            ++Logins;
            CompleteLogin = completed;
        }

        public void Logout(Action<string?> completed) => CompleteLogout = completed;

        public void WatchIdentityLoss(Action<string> lost) => Lost = lost;

        public void Dispose() => ++Disposals;
    }
}
