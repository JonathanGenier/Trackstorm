using Trackstorm.Client.Items;

namespace Trackstorm.Transport.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CarnivalSmashSettingsTests
{
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(.49f)]
    [TestCase(1.51f)]
    public void InvalidSizeCannotReplaceTheCurrentPreview(float size)
    {
        var original = CarnivalSmashSettings.Current;
        try
        {
            CarnivalSmashSettings.Current = new() { Size = .75f };
            Assert.Throws<ArgumentException>(() => CarnivalSmashSettings.Current = new() { Size = size });
            Assert.That(CarnivalSmashSettings.Current.Size, Is.EqualTo(.75f));
        }
        finally { CarnivalSmashSettings.Current = original; }
    }
}
