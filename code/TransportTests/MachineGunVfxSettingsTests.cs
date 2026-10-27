using Trackstorm.Client.Items;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class MachineGunVfxSettingsTests
{
    [TearDown]
    public void RestoreDefaults() => MachineGunVfxSettings.Current = new();

    [Test]
    public void InvalidVisualSettingsCannotEscapeAllocationAndPresentationBounds()
    {
        foreach (var invalid in new[] { new MachineGunVfxSettings { Speed = float.NaN }, new() { Speed = 299 },
            new() { TracerLength = 7 }, new() { BulletBrightness = 3 }, new() { TracerBrightness = .9f },
            new() { FlashScale = 2 }, new() { SmokeIntensity = 3 }, new() { Width = 0 }, new() { TracerBrightness = 5 } })
        { Assert.Throws<ArgumentException>(() => MachineGunVfxSettings.Current = invalid); }
        Assert.That(MachineGunVfxSettings.Current, Is.EqualTo(new MachineGunVfxSettings()));
    }

    [Test]
    public void EditingIntensityDoesNotChangeCapturedFlightSettings()
    {
        var captured = MachineGunVfxSettings.Current;
        MachineGunVfxSettings.Current = captured with { BulletBrightness = 2, TracerBrightness = 4 };
        Assert.That(captured.BulletBrightness, Is.EqualTo(1.35f));
        Assert.That(captured.TracerBrightness, Is.EqualTo(2.5f));
        Assert.That(MachineGunVfxSettings.Current.TracerBrightness, Is.EqualTo(4));
    }
}
