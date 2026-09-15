using System.Buffers.Binary;

namespace Trackstorm.Core.Sessions;

/// <summary>Separates gameplay streams across connection generations without changing match or player identity.</summary>
public static class ConnectionEnvelope
{
    /// <summary>Wraps an existing gameplay payload in its recipient's connection boundary.</summary>
    /// <param name="session">Stable session identity.</param>
    /// <param name="generation">Current player connection generation.</param>
    /// <param name="payload">Existing bounded gameplay packet.</param>
    /// <returns>Detached wire envelope.</returns>
    public static byte[] Encode(ulong session, ulong generation, ReadOnlySpan<byte> payload)
    {
        if (session == 0 || generation == 0 || payload.Length > 65000)
        {
            throw new ArgumentException("Invalid connection boundary.");
        }

        byte[] bytes = new byte[19 + payload.Length];
        bytes[0] = (byte)'T';
        bytes[1] = (byte)'G';
        bytes[2] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(3), session);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(11), generation);
        payload.CopyTo(bytes.AsSpan(19));
        return bytes;
    }

    /// <summary>Validates the entire stream boundary before any nested codec or event consumer sees it.</summary>
    /// <param name="bytes">Complete wire message.</param>
    /// <param name="session">Expected session.</param>
    /// <param name="generation">Expected connection generation.</param>
    /// <returns>Detached nested payload.</returns>
    public static byte[] Decode(ReadOnlySpan<byte> bytes, ulong session, ulong generation)
    {
        if (bytes.Length is < 19 or > 65019 || bytes[0] != 'T' || bytes[1] != 'G' || bytes[2] != 1 ||
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[3..]) != session || BinaryPrimitives.ReadUInt64LittleEndian(bytes[11..]) != generation)
        {
            throw new ArgumentException("Retired or malformed connection generation.");
        }

        return bytes[19..].ToArray();
    }
}
