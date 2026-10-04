using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Fixtures emitted by the actual pre-rename main serializers, not by the current implementation.</summary>
internal sealed class ShieldBinaryCompatibilityTests
{
    [Test]
    public void HistoricalItemConfigurationAndResumeBytesRemainExact()
    {
        byte[] items = Fixture("ShieldItems-v21.bin");
        var publication = ItemCodec.DecodeState(items);
        Assert.That(ItemCodec.EncodeState(publication), Is.EqualTo(items));
        Assert.That(publication.Shields.Select(state => (state.Id, state.Stage, state.HP)), Is.EqualTo(new[]
        { (1ul, ShieldStage.WorldWall, 875f), (2ul, ShieldStage.RearShield, 925f) }));
        Assert.That(publication.Slots.Single().Item, Is.EqualTo(HeldItem.Shield));

        byte[] configuration = Fixture("ShieldConfiguration-v41.bin");
        var decoded = GameplayConfigurationCodec.Decode(configuration);
        Assert.That(GameplayConfigurationCodec.Encode(decoded.Session, decoded.State), Is.EqualTo(configuration));
        Assert.That(decoded.State.Configuration.Items.ShieldMass, Is.EqualTo(300));
        Assert.That(decoded.State.Configuration.Items.ShieldLifetimeSeconds, Is.EqualTo(90));
        Assert.That(decoded.State.Configuration.Spawns.Weights[HeldItem.Shield], Is.EqualTo(3));

        byte[] resume = Fixture("ShieldResume-v3.bin");
        var checkpoint = ResumeCheckpointCodec.Decode(resume);
        Assert.That(ResumeCheckpointCodec.Encode(checkpoint), Is.EqualTo(resume));
        Assert.That(checkpoint.Items.Shields, Is.EqualTo(publication.Shields));
        Assert.That(checkpoint.Configuration, Is.EqualTo(decoded.State));
    }

    private static byte[] Fixture(string name)
    {
        using var stream = typeof(ShieldBinaryCompatibilityTests).Assembly.GetManifestResourceStream("Trackstorm.Core.Tests.Items.Fixtures." + name)!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
