using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Fixtures emitted by the actual pre-rename main serializers, not by the current implementation.</summary>
internal sealed class ShieldBinaryCompatibilityTests
{
    [Test]
    public void HistoricalItemAndConfigurationSchemasAreRejected()
    {
        byte[] items = Fixture("ShieldItems-v21.bin");
        // TI22 adds captured Missile deployment/stow boundaries. Old state cannot
        // silently decode with shifted slot fields; same-build peers are required.
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(items));

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
