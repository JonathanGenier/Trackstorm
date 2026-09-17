using System.Buffers.Binary;

namespace Trackstorm.Core.Development;

/// <summary>Bounded versioned authoritative tuning; only the explicit gameplay allowlist crosses the wire.</summary>
public static class GameplayConfigurationCodec
{
    /// <summary>Recognizes a reliable configuration publication.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="bytes">Complete bounded wire payload.</param>
    public static bool IsConfiguration(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 'T' && bytes[1] == 'C';

    /// <summary>Encodes the entire validated revision in catalog order; schema changes require a wire version bump.</summary>
    /// <returns>Complete bounded configuration payload.</returns>
    /// <param name="session">Current nonzero arena generation.</param>
    /// <param name="state">Detached authoritative configuration boundary.</param>
    public static byte[] Encode(ulong session, GameplayConfigurationState state)
    {
        ArgumentOutOfRangeException.ThrowIfZero(session);
        state.Configuration.Validate();
        byte[] bytes = new byte[21 + (GameplayOptions.All.Count * 8)];
        bytes[0] = (byte)'T';
        bytes[1] = (byte)'C';
        bytes[2] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(3), session);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(11), state.Revision);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(19), (ushort)GameplayOptions.All.Count);
        for (int i = 0; i < GameplayOptions.All.Count; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(21 + (i * 8)), GameplayOptions.All[i].Read(state.Configuration));
        }

        return bytes;
    }

    /// <summary>Rejects incomplete, excessive, nonfinite or invalid tuning before returning a detached boundary.</summary>
    /// <returns>Validated session and configuration revision.</returns>
    /// <param name="bytes">Complete bounded wire payload.</param>
    public static (ulong Session, GameplayConfigurationState State) Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 21 + (GameplayOptions.All.Count * 8) || !IsConfiguration(bytes) || bytes[2] != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[19..]) != GameplayOptions.All.Count)
        {
            throw new ArgumentException("Invalid gameplay configuration message.");
        }

        ulong session = BinaryPrimitives.ReadUInt64LittleEndian(bytes[3..]);
        ArgumentOutOfRangeException.ThrowIfZero(session);
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < GameplayOptions.All.Count; i++)
        {
            values.Add(GameplayOptions.All[i].Key, BinaryPrimitives.ReadDoubleLittleEndian(bytes[(21 + (i * 8))..]));
        }

        if (!GameplayOptions.TryApply(new(), values, out var configuration, out var error))
        {
            throw new ArgumentException(error);
        }

        return (session, new GameplayConfigurationState(BinaryPrimitives.ReadUInt64LittleEndian(bytes[11..]), configuration));
    }
}
