using System.Buffers.Binary;
using System.Text;

namespace Trackstorm.Core.Development;

/// <summary>Bounded edits and acknowledgements inside the session's authenticated connection envelope.</summary>
public static class ConfigurationRequestCodec
{
    /// <summary>Encodes only requested allowlisted fields, never a client-owned configuration.</summary>
    public static byte[] Encode(ulong request, ulong match, IReadOnlyDictionary<string, double> edits)
    {
        ArgumentOutOfRangeException.ThrowIfZero(request);
        if (edits.Count > GameplayOptions.All.Count) throw new ArgumentException("Too many configuration edits.");
        byte[] bytes = new byte[19 + edits.Count * 10];
        bytes[0] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(1), request);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(9), match);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(17), (ushort)edits.Count);
        int offset = 19;
        foreach (var (key, value) in edits)
        {
            int index = GameplayOptions.All.ToList().FindIndex(option => option.Key == key);
            if (index < 0 || !double.IsFinite(value)) throw new ArgumentException("Unknown or nonfinite configuration value.");
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), (ushort)index);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(offset + 2), value);
            offset += 10;
        }
        return bytes;
    }

    /// <summary>Rejects malformed, repeated or unknown fields before authoritative validation.</summary>
    public static (ulong Request, ulong Match, Dictionary<string, double> Edits) Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 19 || bytes[0] != 1) throw new ArgumentException("Invalid configuration request.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[17..]);
        if (count > GameplayOptions.All.Count || bytes.Length != 19 + count * 10) throw new ArgumentException("Invalid configuration request size.");
        ulong request = BinaryPrimitives.ReadUInt64LittleEndian(bytes[1..]);
        ArgumentOutOfRangeException.ThrowIfZero(request);
        var edits = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int offset = 19 + i * 10;
            int index = BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
            double value = BinaryPrimitives.ReadDoubleLittleEndian(bytes[(offset + 2)..]);
            if (index >= GameplayOptions.All.Count || !double.IsFinite(value) || !edits.TryAdd(GameplayOptions.All[index].Key, value))
                throw new ArgumentException("Invalid configuration field.");
        }
        return (request, BinaryPrimitives.ReadUInt64LittleEndian(bytes[9..]), edits);
    }

    /// <summary>Empty error denotes acceptance at the supplied authoritative revision.</summary>
    public static byte[] EncodeResult(ulong request, ulong revision, string error)
    {
        byte[] text = Encoding.UTF8.GetBytes(error);
        if (text.Length > 2048) throw new ArgumentException("Configuration feedback is too long.");
        byte[] bytes = new byte[17 + text.Length];
        bytes[0] = 2;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(1), request);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(9), revision);
        text.CopyTo(bytes, 17);
        return bytes;
    }

    /// <summary>Reads bounded host feedback.</summary>
    public static (ulong Request, ulong Revision, string Error) DecodeResult(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 17 || bytes.Length > 2065 || bytes[0] != 2) throw new ArgumentException("Invalid configuration result.");
        return (BinaryPrimitives.ReadUInt64LittleEndian(bytes[1..]), BinaryPrimitives.ReadUInt64LittleEndian(bytes[9..]), new UTF8Encoding(false, true).GetString(bytes[17..]));
    }
}
