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
            new() { TracerLength = 7 }, new() { RedPercent = 26 }, new() { GreenPercent = -1 },
            new() { FlashScale = 2 }, new() { SmokeIntensity = 3 }, new() { Width = 0 }, new() { Brightness = 2 } })
        { Assert.Throws<ArgumentException>(() => MachineGunVfxSettings.Current = invalid); }
        Assert.That(MachineGunVfxSettings.Current, Is.EqualTo(new MachineGunVfxSettings()));
    }

    [Test]
    public void DefaultColorMixIsRestrainedRepeatableAndAvailableToBothBarrels()
    {
        var settings = new MachineGunVfxSettings();
        var selected = Enumerable.Range(0, 400).Where(r => (r / 2 + r % 2) % 2 == 0).ToArray();
        var colors = selected.Select(r => settings.TracerColor(43, r)).ToArray();
        Assert.That(colors.Count(c => c == 0), Is.EqualTo(160));
        Assert.That(colors.Count(c => c == 1), Is.EqualTo(20));
        Assert.That(colors.Count(c => c == 2), Is.EqualTo(20));
        foreach (int side in new[] { 0, 1 })
        { Assert.That(selected.Where(r => r % 2 == side).Select(r => settings.TracerColor(43, r)).Distinct(), Is.EquivalentTo(new[] { 0, 1, 2 })); }
        Assert.That(selected.Select(r => new MachineGunVfxSettings().TracerColor(43, r)), Is.EqualTo(colors));
        Assert.That(selected.Select(r => (settings with { RedPercent = 0, GreenPercent = 0 }).TracerColor(43, r)), Is.All.Zero);
    }
}
