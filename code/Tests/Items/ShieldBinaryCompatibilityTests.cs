using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Fixtures emitted by the actual pre-rename main serializers, not by the current implementation.</summary>
internal sealed class ShieldBinaryCompatibilityTests
{
    [Test]
    public void HistoricalItemBodyRemainsExactAndRetiredProtocolsAreRejected()
    {
        byte[] items = Fixture("ShieldItems-v21.bin");
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(items));
        // TI22 extends accepted elevation; Shield's existing body layout is unchanged.
        items[2] = 22;
        var publication = ItemCodec.DecodeState(items);
        Assert.That(ItemCodec.EncodeState(publication), Is.EqualTo(items));
        Assert.That(publication.Shields.Select(state => (state.Id, state.Stage, state.HP)), Is.EqualTo(new[]
        { (1ul, ShieldStage.WorldWall, 875f), (2ul, ShieldStage.RearShield, 925f) }));
        Assert.That(publication.Slots.Single().Item, Is.EqualTo(HeldItem.Shield));

        byte[] configuration = Fixture("ShieldConfiguration-v41.bin");
        // TC42 retires the two aerial stabilization controls. Old checkpoints embed
        // TC41 and must be rejected rather than shifting every later catalog value.
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(configuration));

        byte[] resume = Fixture("ShieldResume-v3.bin");
        Assert.Throws<ArgumentException>(() => ResumeCheckpointCodec.Decode(resume));
    }

    private static byte[] Fixture(string name)
    {
        using var stream = typeof(ShieldBinaryCompatibilityTests).Assembly.GetManifestResourceStream("Trackstorm.Core.Tests.Items.Fixtures." + name)!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
