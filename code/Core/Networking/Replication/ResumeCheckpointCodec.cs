using System.Buffers.Binary;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Bounded compound reliable checkpoint using the existing gameplay codecs.</summary>
public static class ResumeCheckpointCodec
{
    /// <summary>Complete item boundary plus existing bounded configuration, match and environment state.</summary>
    public const int MaximumBytes = ItemCodec.MaximumBytes + 65536;
    /// <summary>Recognizes a checkpoint before ordinary gameplay decoding.</summary>
    /// <returns>Whether checkpoint magic matches.</returns>
    /// <param name="bytes">Complete nested payload.</param>
    public static bool IsCheckpoint(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 'T' && bytes[1] == 'R';

    /// <summary>Serializes one complete state boundary.</summary>
    /// <returns>Reliable compound payload.</returns>
    /// <param name="checkpoint">Validated resume state.</param>
    public static byte[] Encode(ResumeCheckpoint checkpoint)
    {
        byte[] items = ItemCodec.EncodeState(checkpoint.Items);
        byte[] match = MatchCodec.Encode(checkpoint.Items.World.Session, checkpoint.Match);
        byte[] props = checkpoint.Props is null ? [] : VehicleNetworkCodec.EncodeProps(checkpoint.Props);
        byte[] configuration = Development.GameplayConfigurationCodec.Encode(checkpoint.Items.World.Session, checkpoint.Configuration);
        byte[] environment = checkpoint.Environment is null ? [] : Arenas.EnvironmentCodec.Encode(checkpoint.Environment);
        byte[] result = new byte[23 + items.Length + match.Length + props.Length + configuration.Length + environment.Length];
        if (result.Length > MaximumBytes)
        {
            throw new ArgumentException("Resume checkpoint exceeds transport bounds.");
        }

        result[0] = (byte)'T';
        result[1] = (byte)'R';
        result[2] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(3), items.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(7), match.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(11), props.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(15), configuration.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(19), environment.Length);
        items.CopyTo(result, 23);
        match.CopyTo(result, 23 + items.Length);
        props.CopyTo(result, 23 + items.Length + match.Length);
        configuration.CopyTo(result, 23 + items.Length + match.Length + props.Length);
        environment.CopyTo(result, 23 + items.Length + match.Length + props.Length + configuration.Length);
        return result;
    }

    /// <summary>Validates all nested components before returning a reset boundary.</summary>
    /// <returns>Detached checkpoint.</returns>
    /// <param name="bytes">Complete reliable payload.</param>
    public static ResumeCheckpoint Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 23 or > MaximumBytes || !IsCheckpoint(bytes) || bytes[2] != 3)
        {
            throw new ArgumentException("Invalid resume checkpoint.");
        }

        int itemsLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[3..]);
        int matchLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[7..]);
        int propsLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[11..]);
        int configurationLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[15..]);
        int environmentLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[19..]);
        if (environmentLength < 0 || itemsLength <= 0 || matchLength <= 0 || propsLength < 0 || configurationLength <= 0 || (long)itemsLength + matchLength + propsLength + configurationLength + environmentLength != bytes.Length - 23)
        {
            throw new ArgumentException("Invalid checkpoint lengths.");
        }

        var items = ItemCodec.DecodeState(bytes.Slice(23, itemsLength));
        var match = MatchCodec.Decode(bytes.Slice(23 + itemsLength, matchLength));
        var configuration = Development.GameplayConfigurationCodec.Decode(bytes.Slice(23 + itemsLength + matchLength + propsLength, configurationLength));
        if (match.Session != items.World.Session || configuration.Session != items.World.Session)
        {
            throw new ArgumentException("Checkpoint session mismatch.");
        }

        return new ResumeCheckpoint(items, match.State, propsLength == 0 ? null : VehicleNetworkCodec.DecodeProps(bytes.Slice(23 + itemsLength + matchLength, propsLength)), configuration.State, environmentLength == 0 ? null : Arenas.EnvironmentCodec.Decode(bytes[(23 + itemsLength + matchLength + propsLength + configurationLength)..]));
    }
}
