using System.Text;
using System.Text.Json;

namespace Trackstorm.Core.Sessions;

/// <summary>Bounded versioned reliable lobby protocol, separate from vehicle snapshots on the same transport.</summary>
public static class LobbyCodec
{
    /// <summary>Maximum complete packet size.</summary>
    public const int MaximumBytes = 30000;
    private const byte Version = 8;

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
        if (document.RootElement.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("Invalid rejection reason.");
        }

        return document.RootElement.GetString() switch
        {
            "Session full" => "Session full",
            "Session unavailable" => "Session unavailable",
            "Join bootstrap failed" => "Join bootstrap failed",
            "Access denied" => "Access denied",
            "Removed by host" => "Removed by host",
            _ => "Resume rejected",
        };
    }

    /// <summary>Encodes a stable compatibility rejection containing the canonical host version.</summary>
    /// <returns>Reliable rejection.</returns>
    /// <param name="version">Canonical host version.</param>
    public static byte[] EncodeVersionMismatch(GameVersion version) => Pack(4, JsonSerializer.SerializeToUtf8Bytes(version.ToString()));

    /// <summary>Recognizes a compatibility rejection.</summary>
    /// <param name="data">Complete payload.</param>
    /// <returns>Whether this is a version rejection.</returns>
    public static bool IsVersionMismatch(ReadOnlySpan<byte> data) => data.Length >= 4 && IsLobby(data) && data[3] == 4;

    /// <summary>Decodes the hosted version for safe local presentation.</summary>
    /// <param name="data">Complete rejection.</param>
    /// <returns>Hosted version.</returns>
    public static string DecodeVersionMismatch(ReadOnlySpan<byte> data)
    {
        using var document = Parse(data, 4);
        return document.RootElement.ValueKind == JsonValueKind.String ? GameVersion.Parse(document.RootElement.GetString()!).ToString() : throw new ArgumentException("Invalid version rejection.");
    }

    /// <summary>Encodes explicit departure acknowledgement before transport cleanup.</summary>
    /// <returns>Reliable acknowledgement.</returns>
    public static byte[] EncodeLeft() => Pack(3, [0]);

    /// <summary>Recognizes the exact versioned departure acknowledgement.</summary>
    /// <param name="data">Complete control payload.</param>
    /// <returns>Whether the host acknowledged departure.</returns>
    public static bool IsLeft(ReadOnlySpan<byte> data) => data.SequenceEqual(new byte[] { (byte)'T', (byte)'L', Version, 3, 0 });

    /// <summary>Identifies a reservation response without granting a player assignment.</summary>
    /// <param name="data">Complete reliable control payload.</param>
    /// <returns>Whether it is a reservation response.</returns>
    public static bool IsReservation(ReadOnlySpan<byte> data) => data.Length >= 4 && IsLobby(data) && data[3] == 5;

    /// <summary>Encodes an exact request-bound reservation result.</summary>
    /// <param name="session">Session lifetime.</param>
    /// <param name="player">Requested identity.</param>
    /// <param name="generation">Requested generation.</param>
    /// <param name="epoch">Current authority fence.</param>
    /// <param name="result">Authoritative result.</param>
    /// <returns>Reliable control packet.</returns>
    public static byte[] EncodeReservation(ulong session, ulong player, ulong generation, ulong epoch, ReservationResult result) => Pack(5, JsonSerializer.SerializeToUtf8Bytes(new { Session = session, Player = player, Generation = generation, Epoch = epoch, Result = result }));

    /// <summary>Validates a response against the exact outstanding reservation request.</summary>
    /// <param name="data">Reliable host response.</param>
    /// <param name="session">Expected session.</param>
    /// <param name="player">Expected player.</param>
    /// <param name="generation">Expected connection generation.</param>
    /// <param name="epoch">Expected authority fence.</param>
    /// <returns>Validated reservation result.</returns>
    public static ReservationResult DecodeReservation(ReadOnlySpan<byte> data, ulong session, ulong player, ulong generation, ulong epoch)
    {
        using var document = Parse(data, 5);
        try
        {
            var root = document.RootElement;
            var result = (ReservationResult)root.GetProperty("Result").GetInt32();
            if (root.GetProperty("Session").GetUInt64() != session || root.GetProperty("Player").GetUInt64() != player || root.GetProperty("Generation").GetUInt64() != generation || root.GetProperty("Epoch").GetUInt64() != epoch || !Enum.IsDefined(result))
            {
                throw new ArgumentException("Mismatched reservation response.");
            }

            return result;
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new ArgumentException("Invalid reservation response.", exception);
        }
    }

    /// <summary>Encodes one complete host state and the recipient's assigned identity.</summary>
    /// <param name="state">Validated authoritative state.</param>
    /// <param name="player">Recipient identity.</param>
    /// <param name="activated">Whether the recipient owns committed participation rather than a pending bootstrap.</param>
    /// <returns>Reliable packet.</returns>
    public static byte[] EncodeState(LobbySnapshot state, ulong player, bool activated = true)
    {
        // Binary names keep up to 256 historical participants within the existing transport/checkpoint bounds.
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(state.Session);
        writer.Write(state.Revision);
        writer.Write(state.Match);
        writer.Write((byte)state.Phase);
        writer.Write((byte)state.Map);
        writer.Write(state.CurrentHostId);
        writer.Write(state.AuthorityEpoch);
        writer.Write(player);
        writer.Write(activated);
        writer.Write((byte)state.Players.Count);
        foreach (var participant in state.Players)
        {
            writer.Write(participant.Id);
            writer.Write(participant.Name);
            writer.Write(participant.Ready);
            writer.Write(participant.Connected);
            writer.Write(participant.Generation);
            writer.Write(participant.RetainedHost);
        }

        writer.Write((ushort)state.Departed.Count);
        foreach (var participant in state.Departed)
        {
            writer.Write(participant.Id);
            writer.Write(participant.Name);
        }

        return Pack(0, stream.ToArray());
    }

    /// <summary>Encodes a sender-scoped intent with phase generation to reject stale commands.</summary>
    /// <param name="command">Requested action.</param>
    /// <param name="state">Last accepted state, absent for admission.</param>
    /// <param name="ready">Requested readiness.</param>
    /// <param name="name">Admission display name.</param>
    /// <returns>Reliable packet.</returns>
    /// <param name="player">Previous assigned identity for resume only.</param>
    /// <param name="generation">Previous connection generation for resume only.</param>
    /// <param name="gameVersion">Runtime version; defaults to this build.</param>
    public static byte[] EncodeCommand(LobbyCommand command, LobbySnapshot? state, bool ready = false, string name = "", ulong player = 0, ulong generation = 0, string? gameVersion = null) => Pack(1, JsonSerializer.SerializeToUtf8Bytes(new { Command = command, Session = state?.Session ?? 0, Match = state?.Match ?? 0, Phase = state?.Phase ?? SessionPhase.Lobby, Ready = ready, Name = PlayerName.Sanitize(name), Player = player, Generation = generation, AuthorityEpoch = state?.AuthorityEpoch ?? 1, GameVersion = gameVersion ?? Sessions.GameVersion.Current.ToString() }));

    /// <summary>Encodes only the previous assignment; the host independently resolves authenticated identity.</summary>
    /// <param name="session">Expected logical session.</param>
    /// <param name="player">Previous player assignment.</param>
    /// <param name="generation">Last acknowledged connection generation.</param>
    /// <param name="authorityEpoch">Expected authority fence from current routing metadata.</param>
    /// <param name="gameVersion">Runtime version; defaults to this build.</param>
    /// <returns>Reliable resume intent.</returns>
    /// <param name="command">Resume or a read/release operation using the same authenticated assignment.</param>
    public static byte[] EncodeResume(ulong session, ulong player, ulong generation, ulong authorityEpoch = 1, string? gameVersion = null, LobbyCommand command = LobbyCommand.Resume) => Pack(1, JsonSerializer.SerializeToUtf8Bytes(new { Command = command, Session = session, Match = session, Phase = SessionPhase.Lobby, Ready = false, Name = "Player", Player = player, Generation = generation, AuthorityEpoch = authorityEpoch, GameVersion = gameVersion ?? Sessions.GameVersion.Current.ToString() }));

    /// <summary>Validates and decodes a host publication.</summary>
    /// <param name="data">Bounded reliable payload.</param>
    /// <returns>Detached state and recipient identity.</returns>
    public static (LobbySnapshot State, ulong Player, bool Activated) DecodeState(ReadOnlySpan<byte> data)
    {
        if (data.Length is < 5 or > MaximumBytes || !IsLobby(data) || data[2] != Version || data[3] != 0)
        {
            throw new ArgumentException("Invalid lobby state envelope.");
        }

        try
        {
            using var stream = new MemoryStream(data[4..].ToArray(), false);
            using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
            ulong session = reader.ReadUInt64();
            ulong revision = reader.ReadUInt64();
            ulong match = reader.ReadUInt64();
            var phase = (SessionPhase)reader.ReadByte();
            var map = (MatchMap)reader.ReadByte();
            ulong host = reader.ReadUInt64();
            ulong epoch = reader.ReadUInt64();
            ulong id = reader.ReadUInt64();
            bool activated = ReadBoolean(reader);
            int count = reader.ReadByte();
            if (count is < 1 or > 8)
            {
                throw new ArgumentException("Invalid roster count.");
            }

            var players = Enumerable.Range(0, count).Select(_ => new SessionPlayer(reader.ReadUInt64(), reader.ReadString(), ReadBoolean(reader), ReadBoolean(reader), reader.ReadUInt64(), ReadBoolean(reader))).ToArray();
            int departed = reader.ReadUInt16();
            if (departed + count > Matches.MatchState.MaximumPlayers)
            {
                throw new ArgumentException("Invalid history count.");
            }

            var history = Enumerable.Range(0, departed).Select(_ => new MatchParticipant(reader.ReadUInt64(), reader.ReadString())).ToArray();
            var state = new LobbySnapshot(session, revision, match, phase, players, host, epoch, history, map);
            if (!state.Players.Any(player => player.Id == id) || stream.Position != stream.Length)
            {
                throw new ArgumentException("Recipient is absent from the lobby.");
            }

            return (state, id, activated);
        }
        catch (Exception exception) when (exception is IOException or FormatException or DecoderFallbackException)
        {
            throw new ArgumentException("Malformed lobby state.", exception);
        }
    }

    /// <summary>Validates and decodes a client intent.</summary>
    /// <param name="data">Bounded reliable payload.</param>
    /// <returns>Sender-scoped action and phase guard.</returns>
    public static (LobbyCommand Command, ulong Session, ulong Match, SessionPhase Phase, bool Ready, string Name, ulong Player, ulong Generation, string GameVersion, ulong AuthorityEpoch) DecodeCommand(ReadOnlySpan<byte> data)
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

            return (command, root.GetProperty("Session").GetUInt64(), root.GetProperty("Match").GetUInt64(), (SessionPhase)root.GetProperty("Phase").GetInt32(), root.GetProperty("Ready").GetBoolean(), PlayerName.Sanitize(root.GetProperty("Name").GetString()), root.GetProperty("Player").GetUInt64(), root.GetProperty("Generation").GetUInt64(), root.TryGetProperty("GameVersion", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString()! : string.Empty, root.GetProperty("AuthorityEpoch").GetUInt64());
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
        result[2] = Version;
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
        if (data.Length is < 5 or > MaximumBytes || !IsLobby(data) || data[2] != Version || data[3] != kind)
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

    private static bool ReadBoolean(BinaryReader reader) => reader.ReadByte() switch
    {
        0 => false,
        1 => true,
        _ => throw new ArgumentException("Invalid boolean."),
    };
}
