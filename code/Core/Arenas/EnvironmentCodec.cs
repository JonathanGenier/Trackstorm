using System.Numerics;

namespace Trackstorm.Core.Arenas;

/// <summary>Compact bounded complete publication, reused inside the existing recovery envelopes.</summary>
public static class EnvironmentCodec
{
    public static bool IsEnvironment(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 'T' && bytes[1] == 'D';

    public static byte[] Encode(EnvironmentSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'T'); writer.Write((byte)'D'); writer.Write((byte)2);
        writer.Write(snapshot.Session); writer.Write(snapshot.Tick);
        writer.Write((ushort)snapshot.Rocks.Count); writer.Write((ushort)snapshot.Plants.Count);
        foreach (var rock in snapshot.Rocks)
        {
            writer.Write(rock.Stage);
            if (rock.Stage == 0) { continue; }
            writer.Write(rock.Damage); writer.Write(rock.ImpactReadyTick);
            writer.Write(rock.Offset.X); writer.Write(rock.Offset.Z);
            writer.Write(rock.Velocity.X); writer.Write(rock.Velocity.Z);
        }
        for (int i = 0; i < snapshot.Plants.Count; i += 8)
        {
            byte bits = 0;
            for (int j = 0; j < 8 && i + j < snapshot.Plants.Count; j++) { if (snapshot.Plants[i + j]) { bits |= (byte)(1 << j); } }
            writer.Write(bits);
        }
        return stream.ToArray();
    }

    public static EnvironmentSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 23 or > 30231 || !IsEnvironment(bytes) || bytes[2] != 2) { throw new ArgumentException("Invalid environment packet."); }
        using var reader = new BinaryReader(new MemoryStream(bytes.ToArray()));
        reader.ReadBytes(3);
        ulong session = reader.ReadUInt64(), tick = reader.ReadUInt64();
        int count = reader.ReadUInt16(), plants = reader.ReadUInt16();
        if (count > EnvironmentLayout.MaximumPieces || plants > 4096 || bytes.Length < 23 + count + (plants + 7) / 8) { throw new ArgumentException("Invalid environment length."); }
        var rocks = new EnvironmentRockState[count];
        for (int i = 0; i < count; i++)
        {
            if (reader.BaseStream.Position >= bytes.Length) { throw new ArgumentException("Truncated rock state."); }
            byte stage = reader.ReadByte();
            if (stage == 0) { continue; }
            if (reader.BaseStream.Position + 28 > bytes.Length) { throw new ArgumentException("Truncated rock state."); }
            float damage = reader.ReadSingle(); ulong ready = reader.ReadUInt64();
            rocks[i] = new(stage, damage, ready, new Vector3(reader.ReadSingle(), 0, reader.ReadSingle()), new Vector3(reader.ReadSingle(), 0, reader.ReadSingle()));
        }
        var cleared = new bool[plants];
        if (reader.BaseStream.Position + (plants + 7) / 8 != bytes.Length) { throw new ArgumentException("Invalid environment trailing length."); }
        for (int i = 0; i < plants; i += 8)
        {
            byte bits = reader.ReadByte();
            for (int j = 0; j < 8; j++)
            {
                if (i + j < plants) { cleared[i + j] = (bits & (1 << j)) != 0; }
                else if ((bits & (1 << j)) != 0) { throw new ArgumentException("Reserved vegetation bits."); }
            }
        }
        return new(session, tick, rocks, cleared);
    }
}
