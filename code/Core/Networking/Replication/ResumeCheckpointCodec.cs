using System.Buffers.Binary;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Bounded compound reliable checkpoint using the existing gameplay codecs.</summary>
public static class ResumeCheckpointCodec
{
    /// <summary>Recognizes a checkpoint before ordinary gameplay decoding.</summary>
    /// <param name="bytes">Complete nested payload.</param>
    /// <returns>Whether checkpoint magic matches.</returns>
    public static bool IsCheckpoint(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 'T' && bytes[1] == 'R';

    /// <summary>Serializes one complete state boundary.</summary>
    /// <param name="checkpoint">Validated resume state.</param>
    /// <returns>Reliable compound payload.</returns>
    public static byte[] Encode(ResumeCheckpoint checkpoint)
    {
        byte[] items = ItemCodec.EncodeState(checkpoint.Items);
        byte[] match = MatchCodec.Encode(checkpoint.Items.World.Session, checkpoint.Match);
        byte[] props = checkpoint.Props is null ? [] : VehicleNetworkCodec.EncodeProps(checkpoint.Props);
        byte[] result = new byte[15 + items.Length + match.Length + props.Length];
        if (result.Length > 65000)
        {
            throw new ArgumentException("Resume checkpoint exceeds transport bounds.");
        }

        result[0] = (byte)'T';
        result[1] = (byte)'R';
        result[2] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(3), items.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(7), match.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(11), props.Length);
        items.CopyTo(result, 15);
        match.CopyTo(result, 15 + items.Length);
        props.CopyTo(result, 15 + items.Length + match.Length);
        return result;
    }

    /// <summary>Validates all nested components before returning a reset boundary.</summary>
    /// <param name="bytes">Complete reliable payload.</param>
    /// <returns>Detached checkpoint.</returns>
    public static ResumeCheckpoint Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 15 or > 65000 || !IsCheckpoint(bytes) || bytes[2] != 1)
        {
            throw new ArgumentException("Invalid resume checkpoint.");
        }

        int itemsLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[3..]);
        int matchLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[7..]);
        int propsLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[11..]);
        if (itemsLength <= 0 || matchLength <= 0 || propsLength < 0 || (long)itemsLength + matchLength + propsLength != bytes.Length - 15)
        {
            throw new ArgumentException("Invalid checkpoint lengths.");
        }

        var items = ItemCodec.DecodeState(bytes.Slice(15, itemsLength));
        var match = MatchCodec.Decode(bytes.Slice(15 + itemsLength, matchLength));
        if (match.Session != items.World.Session)
        {
            throw new ArgumentException("Checkpoint session mismatch.");
        }

        return new ResumeCheckpoint(items, match.State, propsLength == 0 ? null : VehicleNetworkCodec.DecodeProps(bytes[(15 + itemsLength + matchLength)..]));
    }
}
