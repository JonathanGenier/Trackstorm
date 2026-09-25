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
        writer.Write((byte)'T'); writer.Write((byte)'D'); writer.Write((byte)1);
        writer.Write(snapshot.Session); writer.Write(snapshot.Tick);
        writer.Write((ushort)snapshot.Rocks.Count); writer.Write((ushort)snapshot.Plants.Count);
        foreach (var rock in snapshot.Rocks)
        {
            writer.Write(rock.Stage); writer.Write(rock.Damage); writer.Write(rock.ImpactReadyTick);
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
        if (bytes.Length is < 23 or > 8000 || !IsEnvironment(bytes) || bytes[2] != 1) { throw new ArgumentException("Invalid environment packet."); }
        using var reader = new BinaryReader(new MemoryStream(bytes.ToArray()));
        reader.ReadBytes(3);
        ulong session = reader.ReadUInt64(), tick = reader.ReadUInt64();
        int count = reader.ReadUInt16(), plants = reader.ReadUInt16();
        if (count > 256 || plants > 4096 || bytes.Length != 23 + count * 29 + (plants + 7) / 8) { throw new ArgumentException("Invalid environment length."); }
        var rocks = new EnvironmentRockState[count];
        for (int i = 0; i < count; i++)
        {
            byte stage = reader.ReadByte(); float damage = reader.ReadSingle(); ulong ready = reader.ReadUInt64();
            rocks[i] = new(stage, damage, ready, new Vector3(reader.ReadSingle(), 0, reader.ReadSingle()), new Vector3(reader.ReadSingle(), 0, reader.ReadSingle()));
        }
        var cleared = new bool[plants];
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
