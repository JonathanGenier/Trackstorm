using System.Buffers.Binary;

namespace Trackstorm.Core.Sessions;

/// <summary>Separates gameplay streams across connection generations without changing match or player identity.</summary>
public static class ConnectionEnvelope
{
    /// <summary>Wraps an existing gameplay payload in its recipient's connection boundary.</summary>
    /// <param name="session">Stable session identity.</param>
    /// <param name="generation">Current player connection generation.</param>
    /// <param name="payload">Existing bounded gameplay packet.</param>
    /// <param name="authorityEpoch">Current authority fence.</param>
    /// <returns>Detached wire envelope.</returns>
    public static byte[] Encode(ulong session, ulong generation, ReadOnlySpan<byte> payload, ulong authorityEpoch = 1)
    {
        if (session == 0 || generation == 0 || authorityEpoch == 0 || payload.Length > 65000)
        {
            throw new ArgumentException("Invalid connection boundary.");
        }

        byte[] bytes = new byte[27 + payload.Length];
        bytes[0] = (byte)'T';
        bytes[1] = (byte)'G';
        bytes[2] = 2;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(3), session);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(11), generation);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(19), authorityEpoch);
        payload.CopyTo(bytes.AsSpan(27));
        return bytes;
    }

    /// <summary>Validates the entire stream boundary before any nested codec or event consumer sees it.</summary>
    /// <param name="bytes">Complete wire message.</param>
    /// <param name="session">Expected session.</param>
    /// <param name="generation">Expected connection generation.</param>
    /// <param name="authorityEpoch">Expected authority fence.</param>
    /// <returns>Detached nested payload.</returns>
    public static byte[] Decode(ReadOnlySpan<byte> bytes, ulong session, ulong generation, ulong authorityEpoch = 1)
    {
        if (bytes.Length is < 27 or > 65027 || bytes[0] != 'T' || bytes[1] != 'G' || bytes[2] != 2 ||
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[3..]) != session || BinaryPrimitives.ReadUInt64LittleEndian(bytes[11..]) != generation ||
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[19..]) != authorityEpoch)
        {
            throw new ArgumentException("Retired or malformed connection generation.");
        }

        return bytes[27..].ToArray();
    }
}
