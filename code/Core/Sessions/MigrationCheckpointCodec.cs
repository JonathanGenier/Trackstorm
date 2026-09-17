using System.Security.Cryptography;
using System.Text.Json;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Sessions;

/// <summary>Versioned bounded checkpoint with digest integrity and existing nested gameplay codecs.</summary>
public static class MigrationCheckpointCodec
{
    /// <summary>Leaves room for transport and control framing.</summary>
    public const int MaximumBytes = 64000;

    /// <summary>Serializes validated state without presentation events or native objects.</summary>
    /// <param name="checkpoint">Complete boundary.</param>
    /// <returns>Version, digest and bounded JSON body.</returns>
    public static byte[] Encode(MigrationCheckpoint checkpoint)
    {
        var lobby = checkpoint.Lobby;
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new Wire(checkpoint.Sequence, LobbyCodec.EncodeState(lobby.State, lobby.State.CurrentHostId), lobby.Tick, lobby.NextId, new Dictionary<ulong, string>(lobby.Subjects), new Dictionary<ulong, ulong>(lobby.Deadlines), Development.GameplayConfigurationCodec.Encode(lobby.State.Session, lobby.Configuration), checkpoint.Arena is null ? null : ResumeCheckpointCodec.Encode(checkpoint.Arena), checkpoint.Host));
        if (body.Length + 35 > MaximumBytes)
        {
            throw new ArgumentException("Migration checkpoint exceeds its bound.");
        }

        byte[] result = new byte[body.Length + 35];
        result[0] = (byte)'T';
        result[1] = (byte)'C';
        result[2] = 2;
        SHA256.HashData(body).CopyTo(result, 3);
        body.CopyTo(result, 35);
        return result;
    }

    /// <summary>Validates integrity, schema, nested contracts and complete host restore.</summary>
    /// <param name="bytes">Untrusted bounded payload.</param>
    /// <returns>Detached validated checkpoint.</returns>
    public static MigrationCheckpoint Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 36 or > MaximumBytes || bytes[0] != 'T' || bytes[1] != 'C' || bytes[2] != 2 ||
            !CryptographicOperations.FixedTimeEquals(bytes.Slice(3, 32), SHA256.HashData(bytes[35..])))
        {
            throw new ArgumentException("Corrupt or incompatible migration checkpoint.");
        }

        try
        {
            var wire = JsonSerializer.Deserialize<Wire>(bytes[35..], new JsonSerializerOptions { MaxDepth = 16, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new ArgumentException("Missing checkpoint.");
            if (wire.Lobby is null || wire.Subjects is null || wire.Deadlines is null || wire.Configuration is null)
            {
                throw new ArgumentException("Incomplete checkpoint.");
            }

            var state = LobbyCodec.DecodeState(wire.Lobby).State;
            var configuration = Development.GameplayConfigurationCodec.Decode(wire.Configuration);
            if (configuration.Session != state.Session)
            {
                throw new ArgumentException("Configuration belongs to another session.");
            }

            var lobby = new LobbyRestoreState(state, wire.Tick, wire.NextId, wire.Subjects, wire.Deadlines, configuration.State);
            return new MigrationCheckpoint(wire.Sequence, lobby, wire.Arena is null ? null : ResumeCheckpointCodec.Decode(wire.Arena), wire.Host);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or OverflowException or NullReferenceException)
        {
            throw new ArgumentException("Invalid migration continuation.", exception);
        }
    }

    /// <summary>Stable digest used to agree on exact bytes rather than only a sequence number.</summary>
    /// <param name="bytes">Validated encoded checkpoint.</param>
    /// <returns>Canonical uppercase digest.</returns>
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record Wire(ulong Sequence, byte[] Lobby, ulong Tick, ulong NextId, Dictionary<ulong, string> Subjects, Dictionary<ulong, ulong> Deadlines, byte[] Configuration, byte[]? Arena, HostRestoreState? Host);
}
