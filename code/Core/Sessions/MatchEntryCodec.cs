using System.Buffers.Binary;

namespace Trackstorm.Core.Sessions;

/// <summary>Bounded loading handshake inside the authenticated session/epoch/connection envelope.</summary>
public static class MatchEntryCodec
{
    /// <summary>Client has loaded the selected map and requests the complete checkpoint.</summary>
    public const byte Loaded = 1;
    /// <summary>Client installed the checkpoint and acknowledges the initial barrier.</summary>
    public const byte Synchronized = 2;
    /// <summary>Host completed the initial barrier and permits match entry.</summary>
    public const byte Released = 3;

    /// <summary>Identifies this protocol without accepting its contents.</summary>
    /// <param name="data">Complete inner payload.</param>
    /// <returns>Whether the protocol magic matches.</returns>
    public static bool IsEntry(ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == 'T' && data[1] == 'E';

    /// <summary>Encodes a generation-bound handshake.</summary>
    /// <param name="match">Nonzero match generation.</param>
    /// <param name="kind">Allowlisted handshake action.</param>
    /// <returns>Reliable inner payload.</returns>
    public static byte[] Encode(ulong match, byte kind)
    {
        if (match == 0 || kind is < Loaded or > Released)
        {
            throw new ArgumentException("Invalid match entry message.");
        }

        byte[] data = new byte[12];
        data[0] = (byte)'T';
        data[1] = (byte)'E';
        data[2] = 1;
        data[3] = kind;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(4), match);
        return data;
    }

    /// <summary>Validates exact length, version, action and match generation.</summary>
    /// <param name="data">Inner payload.</param>
    /// <param name="match">Expected match generation.</param>
    /// <returns>Validated action.</returns>
    public static byte Decode(ReadOnlySpan<byte> data, ulong match)
    {
        if (data.Length != 12 || !IsEntry(data) || data[2] != 1 || data[3] is < Loaded or > Released || match == 0 || BinaryPrimitives.ReadUInt64LittleEndian(data[4..]) != match)
        {
            throw new ArgumentException("Invalid match entry envelope.");
        }

        return data[3];
    }
}
