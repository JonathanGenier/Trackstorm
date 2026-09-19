using Trackstorm.Client.Development;
using Trackstorm.Core.Development;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Transport.Tests;

/// <summary>Deterministic draft isolation, authoritative defaults, validation and host-file recovery.</summary>
[TestFixture]
internal sealed class DeveloperOptionsTests
{
    private string _directory = string.Empty;
    private string _path = string.Empty;

    /// <summary>Classification compares typed values, never edit history or rounded display strings.</summary>
    [Test]
    public void DefaultsAndDirtyStateUseActualConfigurationValues()
    {
        var draft = new DeveloperOptionsDraft();
        foreach (var option in GameplayOptions.All)
        {
            Assert.That(draft.IsDefault(option), Is.True, option.Key);
        }

        Assert.That(draft.IsDirty, Is.False);
        Assert.That(draft.TryGetEdits(out var unchanged, out _), Is.True);
        Assert.That(unchanged, Is.Empty, "Displayed float precision must not create phantom edits.");
        var friction = GameplayOptions.All.Single(option => option.Key == "vehicle.tire_friction");
        draft.Set(friction.Key, "3.35");
        Assert.That(draft.IsDefault(friction), Is.True);
        Assert.That(draft.IsDirty, Is.False);
        draft.Set(friction.Key, "3.36");
        Assert.That(draft.IsDefault(friction), Is.False);
        Assert.That(draft.IsDirty, Is.True);
        draft.Set(friction.Key, "3.3500");
        Assert.That(draft.IsDirty, Is.False, "Restoring a value clears unapplied changes.");
        draft.Set(friction.Key, "NaN");
        Assert.That(draft.IsDefault(friction), Is.False);
        Assert.That(draft.IsDirty, Is.True);
        draft.ResetToDefaults();
        Assert.That(draft.IsDirty, Is.False, "Resetting already effective defaults needs no close decision.");
        var effective = GameplayConfiguration.HostedDefaults with { Vehicle = GameplayConfiguration.HostedDefaults.Vehicle with { TireFriction = 4 } };
        draft.Discard(effective);
        Assert.That(draft.IsDefault(friction), Is.False, "Persisted effective overrides are red before being edited.");
        Assert.That(draft.IsDirty, Is.False);
    }

    /// <summary>Uses an isolated file for each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "trackstorm-developer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "tuning.jsonl");
    }

    /// <summary>Removes only the test-owned directory.</summary>
    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    /// <summary>Production startup and missing persisted fields share the hosted preset, including multiplayer HP.</summary>
    [Test]
    public void HostStorageUsesProductionDefaultsForAbsentAndMissingValues()
    {
        Assert.That(new DeveloperSettingsStore(_path).Current, Is.EqualTo(GameplayConfiguration.HostedDefaults));
        Assert.That(GameplayConfiguration.HostedDefaults.Damage.MaxHP, Is.EqualTo(1000));
        AssertReleaseTuning(new DeveloperSettingsStore(_path).LoadForHost());
        Assert.That(new GameplayConfiguration().Damage.MaxHP, Is.EqualTo(100), "Core fixture defaults remain distinct.");
        File.WriteAllText(_path, "{\"schema\":1}\n{\"key\":\"vehicle.acceleration\",\"value\":7}\n");
        var loaded = new DeveloperSettingsStore(_path).Current;
        Assert.That(loaded, Is.EqualTo(GameplayConfiguration.HostedDefaults with { Vehicle = GameplayConfiguration.HostedDefaults.Vehicle with { Acceleration = 7 } }));
        Assert.That(loaded.Vehicle.Acceleration, Is.EqualTo(7));
        Assert.That(loaded.Vehicle.TireFriction, Is.EqualTo(3.35f));
        Assert.That(loaded.Items.MaximumDamage, Is.EqualTo(300));
    }

    /// <summary>Discard reads current authority, including updates after opening, without saving or revising it.</summary>
    [Test]
    public void DiscardRestoresCurrentAuthorityWithoutMutationOrPersistence()
    {
        var host = Host();
        var store = new DeveloperSettingsStore(_path);
        Assert.That(store.Save(host.Configuration.Configuration), Is.True);
        string persisted = File.ReadAllText(_path);
        var draft = new DeveloperOptionsDraft();
        draft.Discard(host.Configuration.Configuration);
        draft.Set("damage.max_hp", "2500");
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["damage.max_hp"] = 1200, ["spawns.seed"] = int.MaxValue }, out _), Is.True);
        var current = host.Configuration;

        draft.Discard(current.Configuration);

        Assert.That(draft.Get("damage.max_hp"), Is.EqualTo("1200"));
        Assert.That(draft.Get("spawns.seed"), Is.EqualTo("2147483647"), "Discard must retain every digit of integer values.");
        Assert.That(draft.IsDirty, Is.False);
        Assert.That(host.Configuration, Is.EqualTo(current));
        Assert.That(File.ReadAllText(_path), Is.EqualTo(persisted));
        Assert.That(store.Current.Damage.MaxHP, Is.EqualTo(1000));
    }

    /// <summary>Reset stages all production values; only normal authority application and saving change state.</summary>
    [Test]
    public void ResetStagesProductionDefaultsUntilApplyThenReplacesPersistedTuning()
    {
        var host = Host();
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["damage.max_hp"] = 1500, ["vehicle.acceleration"] = 7 }, out _), Is.True);
        var store = new DeveloperSettingsStore(_path);
        Assert.That(store.Save(host.Configuration.Configuration), Is.True);
        string persisted = File.ReadAllText(_path);
        var before = host.Configuration;
        var draft = new DeveloperOptionsDraft();
        draft.Discard(before.Configuration);
        draft.Set("vehicle.mass", "invalid pending text");

        draft.ResetToDefaults();

        Assert.That(draft.IsDirty, Is.True);
        Assert.That(draft.Get("damage.max_hp"), Is.EqualTo("1000"));
        Assert.That(draft.Get("vehicle.acceleration"), Is.EqualTo("12"));
        Assert.That(draft.Get("items.missile_speed"), Is.EqualTo("70"));
        Assert.That(host.Configuration, Is.EqualTo(before));
        Assert.That(store.Current, Is.EqualTo(before.Configuration));
        Assert.That(File.ReadAllText(_path), Is.EqualTo(persisted));
        Assert.That(draft.TryGetEdits(out var edits, out _), Is.True);
        Assert.That(edits.Count, Is.EqualTo(GameplayOptions.All.Count), "Reset requests the complete preset, even for unchanged baseline fields.");
        Assert.That(host.TryConfigure(0, edits, out _), Is.True);
        Assert.That(host.Configuration.Configuration, Is.EqualTo(GameplayConfiguration.HostedDefaults));
        Assert.That(host.Configuration.Revision, Is.EqualTo(before.Revision + 1));
        Assert.That(store.Save(host.Configuration.Configuration), Is.True);
        Assert.That(new DeveloperSettingsStore(_path).LoadForHost(), Is.EqualTo(GameplayConfiguration.HostedDefaults));
        AssertReleaseTuning(new DeveloperSettingsStore(_path).LoadForHost());
    }

    /// <summary>A reset still replaces fields that authority changed after the draft was opened.</summary>
    [Test]
    public void ResetDoesNotOmitDefaultsMatchingAnOutdatedEditorBaseline()
    {
        var host = Host();
        var draft = new DeveloperOptionsDraft();
        draft.Discard(host.Configuration.Configuration);
        draft.ResetToDefaults();
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["damage.max_hp"] = 2000 }, out _), Is.True);
        Assert.That(draft.TryGetEdits(out var edits, out _), Is.True);
        Assert.That(host.TryConfigure(0, edits, out _), Is.True);
        Assert.That(host.Configuration.Configuration, Is.EqualTo(GameplayConfiguration.HostedDefaults));
    }

    /// <summary>Invalid editor requests still fail through parsing or the owning authoritative rules.</summary>
    /// <param name="key">Setting under test.</param>
    /// <param name="text">Invalid editor input.</param>
    [TestCase("vehicle.mass", "not a number")]
    [TestCase("vehicle.mass", "-1")]
    [TestCase("vehicle.mass", "NaN")]
    [TestCase("match.minimum_players", "1.5")]
    public void ApplyRejectsInvalidDraftWithoutMutation(string key, string text)
    {
        var host = Host();
        var before = host.Configuration;
        var draft = new DeveloperOptionsDraft();
        draft.Discard(before.Configuration);
        draft.Set(key, text);
        bool accepted = draft.TryGetEdits(out var edits, out string error) && host.TryConfigure(0, edits, out error);
        Assert.That(accepted, Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(host.Configuration, Is.EqualTo(before));
        Assert.That(draft.Get(key), Is.EqualTo(text), "Rejected input remains available for correction.");
        Assert.That(File.Exists(_path), Is.False);
    }

    /// <summary>A failed save keeps live tuning and the old file; retrying identical values saves without a new revision.</summary>
    [Test]
    public void UnchangedAcceptedSettingsCanRetryFailedPersistence()
    {
        var host = Host();
        var store = new DeveloperSettingsStore(_path);
        Assert.That(store.Save(host.Configuration.Configuration), Is.True);
        string previousFile = File.ReadAllText(_path);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["vehicle.acceleration"] = 7 }, out _), Is.True);
        var accepted = host.Configuration;
        Directory.CreateDirectory(_path + ".tmp");
        Assert.That(store.Save(accepted.Configuration), Is.False);
        Assert.That(store.Current, Is.EqualTo(accepted.Configuration));
        Assert.That(File.ReadAllText(_path), Is.EqualTo(previousFile));
        Assert.That(store.Status, Does.Contain("saving failed").And.Contain("Apply Settings"));
        Directory.Delete(_path + ".tmp");

        var draft = new DeveloperOptionsDraft();
        draft.Discard(accepted.Configuration);
        Assert.That(draft.TryGetEdits(out var edits, out _), Is.True);
        Assert.That(host.TryConfigure(0, edits, out _), Is.True);
        Assert.That(host.Configuration, Is.EqualTo(accepted));
        Assert.That(store.Save(host.Configuration.Configuration), Is.True);
        Assert.That(store.Status, Is.EqualTo("Host tuning saved."));
        Assert.That(new DeveloperSettingsStore(_path).LoadForHost(), Is.EqualTo(accepted.Configuration));
    }

    /// <summary>Damaged records retain release fallbacks while independent overrides still load and save.</summary>
    [Test]
    public void MalformedSavedTuningPreservesReleaseFallbacksAndValidOverrides()
    {
        string text = """
            {"schema":1}
            {broken record
            {"key":"vehicle.tire_friction","value":-1}
            {"key":"items.missile_speed","value":90}
            {"key":"future.setting","value":{"enabled":true}}
            """;
        File.WriteAllText(_path, text);
        var store = new DeveloperSettingsStore(_path);
        Assert.That(store.Current, Is.EqualTo(GameplayConfiguration.HostedDefaults with
        {
            Items = GameplayConfiguration.HostedDefaults.Items with { MissileSpeed = 90 },
        }));
        Assert.That(store.Status, Does.Contain("ignored 2 invalid records"));
        Assert.That(store.Save(store.Current), Is.True);
        Assert.That(File.ReadAllText(_path), Does.Contain("future.setting"));
        Assert.That(new DeveloperSettingsStore(_path).LoadForHost(), Is.EqualTo(store.Current));
    }

    /// <summary>Unsupported or oversized files keep the release baseline and cannot be overwritten.</summary>
    /// <param name="oversized">Whether the file exceeds the bounded size instead of using a future schema.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void UnsupportedSavedTuningPreservesFileAndUsesReleaseDefaults(bool oversized)
    {
        string text = oversized ? new string(' ', 65537) : "{\"schema\":999}\n{\"key\":\"items.missile_speed\",\"value\":90}\n";
        File.WriteAllText(_path, text);
        var store = new DeveloperSettingsStore(_path);
        Assert.That(store.LoadForHost(), Is.EqualTo(GameplayConfiguration.HostedDefaults));
        AssertReleaseTuning(store.LoadForHost());
        Assert.That(store.Status, Does.Contain("persistence disabled"));
        Assert.That(store.Save(store.Current), Is.False);
        Assert.That(File.ReadAllText(_path), Is.EqualTo(text));
    }

    private static void AssertReleaseTuning(GameplayConfiguration configuration)
    {
        Assert.That(configuration.Vehicle.Acceleration, Is.EqualTo(12));
        Assert.That(configuration.Vehicle.TireFriction, Is.EqualTo(3.35f));
        Assert.That(configuration.Damage.CollisionScale, Is.EqualTo(5));
        Assert.That(configuration.Items.MissileSpeed, Is.EqualTo(70));
        Assert.That(configuration.Items.ExplosionRadius, Is.EqualTo(12));
        Assert.That(configuration.Items.MaximumDamage, Is.EqualTo(300));
    }

    private static HostVehicleSession Host() => new(9, configuration: GameplayConfiguration.HostedDefaults);
}
