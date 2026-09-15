namespace Trackstorm.Core.Matches;

/// <summary>Reliable, bounded complete totals and score deltas, separate from lossy movement snapshots.</summary>
public static class MatchCodec
{
    /// <summary>Identifies match messages before routing; malformed versions are rejected by decode.</summary>
    /// <param name="bytes">Transport payload.</param>
    /// <returns>Whether the match magic is present.</returns>
    public static bool IsMatch(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 0x54 && bytes[1] == 0x4d;

    /// <summary>Encodes one complete match publication for reliable delivery.</summary>
    /// <param name="session">Nonzero arena generation.</param>
    /// <param name="state">Validated immutable match state.</param>
    /// <returns>Detached bounded bytes.</returns>
    public static byte[] Encode(ulong session, MatchState state)
    {
        ArgumentOutOfRangeException.ThrowIfZero(session);
        ArgumentNullException.ThrowIfNull(state);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { 0x54, 0x4d, 1 });
        writer.Write(session);
        writer.Write(state.Tick);
        writer.Write(state.Revision);
        writer.Write(state.KillTarget);
        writer.Write((byte)state.Phase);
        writer.Write(state.CountdownAtTick ?? 0);
        writer.Write(state.Winner ?? 0);
        writer.Write((ushort)state.Players.Count);
        foreach (PlayerScore score in state.Players)
        {
            writer.Write(score.Player);
            writer.Write(score.Kills);
            writer.Write(score.Deaths);
            writer.Write(score.Wins);
            writer.Write(score.ProcessedLife);
        }

        writer.Write((byte)state.Changes.Count);
        foreach (ScoredDeath change in state.Changes)
        {
            writer.Write(change.Victim);
            writer.Write(change.Life);
            writer.Write(change.Killer);
        }

        return stream.ToArray();
    }

    /// <summary>Rejects truncation, invalid counts, inconsistent winners and trailing data before publication.</summary>
    /// <param name="bytes">Complete reliable payload.</param>
    /// <returns>Session and complete validated state.</returns>
    public static (ulong Session, MatchState State) Decode(ReadOnlySpan<byte> bytes)
    {
        if (!IsMatch(bytes) || bytes.Length is < 51 or > 8192 || bytes[2] != 1)
        {
            throw new ArgumentException("Invalid match header or size.");
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray());
            using var reader = new BinaryReader(stream);
            stream.Position = 3;
            ulong session = reader.ReadUInt64();
            ulong tick = reader.ReadUInt64();
            ulong revision = reader.ReadUInt64();
            int target = reader.ReadInt32();
            var phase = (MatchPhase)reader.ReadByte();
            ulong deadline = reader.ReadUInt64();
            ulong winner = reader.ReadUInt64();
            int count = reader.ReadUInt16();
            if (session == 0 || count > MatchState.MaximumPlayers)
            {
                throw new ArgumentException("Invalid match session or roster count.");
            }

            var players = new PlayerScore[count];
            for (int index = 0; index < count; index++)
            {
                players[index] = new PlayerScore(reader.ReadUInt64(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadUInt64());
            }

            int changes = reader.ReadByte();
            if (changes > 8)
            {
                throw new ArgumentException("Invalid score delta count.");
            }

            var deaths = new ScoredDeath[changes];
            for (int index = 0; index < changes; index++)
            {
                deaths[index] = new ScoredDeath(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64());
            }

            if (stream.Position != stream.Length)
            {
                throw new ArgumentException("Trailing match data.");
            }

            return (session, new MatchState(tick, revision, target, phase, deadline == 0 ? null : deadline, winner == 0 ? null : winner, players, deaths));
        }
        catch (EndOfStreamException exception)
        {
            throw new ArgumentException("Truncated match data.", exception);
        }
    }
}
