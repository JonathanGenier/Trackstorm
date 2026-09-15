using System.Text.Json;

namespace Trackstorm.Core.Sessions;

/// <summary>Bounded versioned reliable lobby protocol, separate from vehicle snapshots on the same transport.</summary>
public static class LobbyCodec
{
    /// <summary>Maximum complete packet size.</summary>
    public const int MaximumBytes = 4096;

    /// <summary>Identifies lobby packets before the vehicle decoder is consulted.</summary>
    /// <param name="data">Complete transport payload.</param>
    /// <returns>Whether the lobby magic matches.</returns>
    public static bool IsLobby(ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == 'T' && data[1] == 'L';

    /// <summary>Recognizes the bounded resume rejection control.</summary>
    /// <param name="data">Complete lobby message.</param>
    /// <returns>Whether this is a rejection envelope.</returns>
    public static bool IsRejection(ReadOnlySpan<byte> data) => data.Length >= 4 && IsLobby(data) && data[3] == 2;

    /// <summary>Encodes a stable resume failure without identity or credential detail.</summary>
    /// <param name="reason">Public failure state.</param>
    /// <returns>Reliable rejection.</returns>
    public static byte[] EncodeRejection(string reason) => Pack(2, JsonSerializer.SerializeToUtf8Bytes(reason));

    /// <summary>Decodes a public failure state.</summary>
    /// <param name="data">Complete rejection message.</param>
    /// <returns>Stable player-facing state.</returns>
    public static string DecodeRejection(ReadOnlySpan<byte> data)
    {
        using var document = Parse(data, 2);
        return document.RootElement.ValueKind == JsonValueKind.String && document.RootElement.GetString() == "Grace expired" ? "Grace expired" : "Resume rejected";
    }

    /// <summary>Encodes explicit departure acknowledgement before transport cleanup.</summary>
    /// <returns>Reliable acknowledgement.</returns>
    public static byte[] EncodeLeft() => Pack(3, [0]);

    /// <summary>Recognizes the exact versioned departure acknowledgement.</summary>
    /// <param name="data">Complete control payload.</param>
    /// <returns>Whether the host acknowledged departure.</returns>
    public static bool IsLeft(ReadOnlySpan<byte> data) => data.SequenceEqual(new byte[] { (byte)'T', (byte)'L', 2, 3, 0 });

    /// <summary>Encodes one complete host state and the recipient's assigned identity.</summary>
    /// <param name="state">Validated authoritative state.</param>
    /// <param name="player">Recipient identity.</param>
    /// <returns>Reliable packet.</returns>
    public static byte[] EncodeState(LobbySnapshot state, ulong player) => Pack(0, JsonSerializer.SerializeToUtf8Bytes(new { state.Session, state.Revision, state.Match, state.Phase, state.Players, state.GraceTicks, Player = player }));

    /// <summary>Encodes a sender-scoped intent with phase generation to reject stale commands.</summary>
    /// <param name="command">Requested action.</param>
    /// <param name="state">Last accepted state, absent for admission.</param>
    /// <param name="ready">Requested readiness.</param>
    /// <param name="name">Admission display name.</param>
    /// <returns>Reliable packet.</returns>
    /// <param name="player">Previous assigned identity for resume only.</param>
    /// <param name="generation">Previous connection generation for resume only.</param>
    public static byte[] EncodeCommand(LobbyCommand command, LobbySnapshot? state, bool ready = false, string name = "", ulong player = 0, ulong generation = 0) => Pack(1, JsonSerializer.SerializeToUtf8Bytes(new { Command = command, Session = state?.Session ?? 0, Match = state?.Match ?? 0, Phase = state?.Phase ?? SessionPhase.Lobby, Ready = ready, Name = PlayerName.Sanitize(name), Player = player, Generation = generation }));

    /// <summary>Encodes only the previous assignment; the host independently resolves authenticated identity.</summary>
    /// <param name="session">Expected logical session.</param>
    /// <param name="player">Previous player assignment.</param>
    /// <param name="generation">Last acknowledged connection generation.</param>
    /// <returns>Reliable resume intent.</returns>
    public static byte[] EncodeResume(ulong session, ulong player, ulong generation) => Pack(1, JsonSerializer.SerializeToUtf8Bytes(new { Command = LobbyCommand.Resume, Session = session, Match = session, Phase = SessionPhase.Lobby, Ready = false, Name = "Player", Player = player, Generation = generation }));

    /// <summary>Validates and decodes a host publication.</summary>
    /// <param name="data">Bounded reliable payload.</param>
    /// <returns>Detached state and recipient identity.</returns>
    public static (LobbySnapshot State, ulong Player) DecodeState(ReadOnlySpan<byte> data)
    {
        using JsonDocument document = Parse(data, 0);
        try
        {
            JsonElement root = document.RootElement;
            var players = root.GetProperty("Players").EnumerateArray().Take(9).Select(player => new SessionPlayer(player.GetProperty("Id").GetUInt64(), player.GetProperty("Name").GetString()!, player.GetProperty("Ready").GetBoolean(), player.GetProperty("Connected").GetBoolean(), player.GetProperty("Generation").GetUInt64())).ToArray();
            var state = new LobbySnapshot(root.GetProperty("Session").GetUInt64(), root.GetProperty("Revision").GetUInt64(), root.GetProperty("Match").GetUInt64(), (SessionPhase)root.GetProperty("Phase").GetInt32(), players, root.GetProperty("GraceTicks").GetUInt64());
            ulong id = root.GetProperty("Player").GetUInt64();
            if (!state.Players.Any(player => player.Id == id))
            {
                throw new ArgumentException("Recipient is absent from the lobby.");
            }

            return (state, id);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new ArgumentException("Malformed lobby state.", exception);
        }
    }

    /// <summary>Validates and decodes a client intent.</summary>
    /// <param name="data">Bounded reliable payload.</param>
    /// <returns>Sender-scoped action and phase guard.</returns>
    public static (LobbyCommand Command, ulong Session, ulong Match, SessionPhase Phase, bool Ready, string Name, ulong Player, ulong Generation) DecodeCommand(ReadOnlySpan<byte> data)
    {
        using JsonDocument document = Parse(data, 1);
        try
        {
            JsonElement root = document.RootElement;
            var command = (LobbyCommand)root.GetProperty("Command").GetInt32();
            if (!Enum.IsDefined(command))
            {
                throw new ArgumentException("Unknown lobby command.");
            }

            return (command, root.GetProperty("Session").GetUInt64(), root.GetProperty("Match").GetUInt64(), (SessionPhase)root.GetProperty("Phase").GetInt32(), root.GetProperty("Ready").GetBoolean(), PlayerName.Sanitize(root.GetProperty("Name").GetString()), root.GetProperty("Player").GetUInt64(), root.GetProperty("Generation").GetUInt64());
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new ArgumentException("Malformed lobby command.", exception);
        }
    }

    private static byte[] Pack(byte kind, byte[] body)
    {
        byte[] result = new byte[body.Length + 4];
        result[0] = (byte)'T';
        result[1] = (byte)'L';
        result[2] = 2;
        result[3] = kind;
        body.CopyTo(result, 4);
        if (result.Length > MaximumBytes)
        {
            throw new ArgumentException("Lobby payload too large.");
        }

        return result;
    }

    private static JsonDocument Parse(ReadOnlySpan<byte> data, byte kind)
    {
        if (data.Length is < 5 or > MaximumBytes || !IsLobby(data) || data[2] != 2 || data[3] != kind)
        {
            throw new ArgumentException("Invalid lobby envelope.");
        }

        try
        {
            return JsonDocument.Parse(data[4..].ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Invalid lobby JSON.", exception);
        }
    }
}
