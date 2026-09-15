using System.Numerics;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Items;

/// <summary>Bounded version-two reliable item protocol. Requests carry no claimed player or outcome.</summary>
public static class ItemCodec
{
    /// <summary>Recognizes only this protocol's magic; complete decode remains mandatory.</summary>
    /// <param name="bytes">Transport payload.</param>
    /// <returns>Whether this is an item envelope.</returns>
    public static bool IsItem(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 0x54 && bytes[1] == 0x49;

    /// <summary>Encodes an exact ownership capability in the match generation.</summary>
    /// <param name="session">Arena generation.</param>
    /// <param name="life">Vehicle life.</param>
    /// <param name="token">Granted item token.</param>
    /// <returns>Reliable use bytes.</returns>
    public static byte[] EncodeUse(ulong session, ulong life, ulong token) => Write(1, writer =>
    {
        if (session == 0 || life == 0 || token == 0)
        {
            throw new ArgumentException("Invalid use capability.");
        }

        writer.Write(session);
        writer.Write(life);
        writer.Write(token);
    });

    /// <summary>Decodes use intent without trusting client identity.</summary>
    /// <param name="bytes">Complete request.</param>
    /// <returns>Generation, life and token.</returns>
    public static (ulong Session, ulong Life, ulong Token) DecodeUse(ReadOnlySpan<byte> bytes) => Read(bytes, 1, reader =>
    {
        var value = (Session: reader.ReadUInt64(), Life: reader.ReadUInt64(), Token: reader.ReadUInt64());
        if (value.Session == 0 || value.Life == 0 || value.Token == 0)
        {
            throw new ArgumentException("Invalid use capability.");
        }

        return value;
    });

    /// <summary>Encodes complete reliable item and HP outcomes.</summary>
    /// <param name="state">Validated host publication.</param>
    /// <returns>Bounded bytes.</returns>
    public static byte[] EncodeState(ItemPublication state) => Write(2, writer =>
    {
        writer.Write(state.Revision);
        byte[] world = VehicleNetworkCodec.EncodeSnapshot(state.World);
        writer.Write(world.Length);
        writer.Write(world);
        writer.Write((byte)state.Spawns.Count);
        foreach (var spawn in state.Spawns)
        {
            byte[] id = System.Text.Encoding.UTF8.GetBytes(spawn.Id);
            writer.Write((byte)id.Length);
            writer.Write(id);
            writer.Write(spawn.Available);
            writer.Write(spawn.NextActivationTick);
            writer.Write(spawn.ClaimedBy);
            writer.Write(spawn.Token);
            writer.Write((byte)spawn.Item);
        }

        writer.Write((byte)state.Slots.Count);
        foreach (var slot in state.Slots)
        {
            writer.Write(slot.Vehicle);
            writer.Write(slot.Life);
            writer.Write(slot.Token);
            writer.Write((byte)slot.Item);
        }

        writer.Write((byte)state.Missiles.Count);
        foreach (var missile in state.Missiles)
        {
            writer.Write(missile.Id);
            writer.Write(missile.Owner);
            Vector(writer, missile.Position);
            Vector(writer, missile.Velocity);
            writer.Write(missile.RemainingTicks);
        }

        writer.Write((byte)state.Events.Count);
        foreach (var outcome in state.Events)
        {
            writer.Write(outcome.Token);
            writer.Write(outcome.Owner);
            writer.Write((byte)outcome.Item);
            Vector(writer, outcome.Position);
            writer.Write(outcome.Impact);
        }
    });

    /// <summary>Rejects malformed counts, flags, versions, values and trailing data before state is accepted.</summary>
    /// <param name="bytes">Complete host publication.</param>
    /// <returns>Validated detached state.</returns>
    public static ItemPublication DecodeState(ReadOnlySpan<byte> bytes) => Read(bytes, 2, reader =>
    {
        ulong revision = reader.ReadUInt64();
        int length = reader.ReadInt32();
        if (length is < 4 or > 16384)
        {
            throw new ArgumentException("Invalid nested world length.");
        }

        WorldSnapshot world = VehicleNetworkCodec.DecodeSnapshot(reader.ReadBytes(length));
        var spawns = new ItemSpawnState[Count(reader, 8)];
        for (int i = 0; i < spawns.Length; i++)
        {
            int idLength = Count(reader, 128);
            string id = new System.Text.UTF8Encoding(false, true).GetString(reader.ReadBytes(idLength));
            bool available = reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new ArgumentException("Invalid availability flag.") };
            spawns[i] = new(id, available, reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), (HeldItem)reader.ReadByte());
        }

        var slots = new ItemSlot[Count(reader, 8)];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = new(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), (HeldItem)reader.ReadByte());
        }

        var missiles = new MissileState[Count(reader, ItemAuthority.MaximumProjectiles)];
        for (int i = 0; i < missiles.Length; i++)
        {
            missiles[i] = new(reader.ReadUInt64(), reader.ReadUInt64(), Vector(reader), Vector(reader), reader.ReadInt32());
        }

        var events = new ItemEvent[Count(reader, ItemAuthority.MaximumProjectiles + 8)];
        for (int i = 0; i < events.Length; i++)
        {
            ulong token = reader.ReadUInt64();
            ulong owner = reader.ReadUInt64();
            var item = (HeldItem)reader.ReadByte();
            Vector3 position = Vector(reader);
            bool impact = reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new ArgumentException("Invalid impact flag.") };
            events[i] = new(token, owner, item, position, impact);
        }

        return new ItemPublication(revision, world, slots, missiles, events, spawns);
    });

    private static byte[] Write(byte kind, Action<BinaryWriter> encode)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { 0x54, 0x49, 2, kind });
        encode(writer);
        if (stream.Length > 32768)
        {
            throw new ArgumentException("Item payload too large.");
        }

        return stream.ToArray();
    }

    private static T Read<T>(ReadOnlySpan<byte> bytes, byte kind, Func<BinaryReader, T> decode)
    {
        if (bytes.Length is < 4 or > 32768 || !IsItem(bytes) || bytes[2] != 2 || bytes[3] != kind)
        {
            throw new ArgumentException("Invalid item header.");
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), false);
            using var reader = new BinaryReader(stream);
            stream.Position = 4;
            T result = decode(reader);
            if (stream.Position != stream.Length)
            {
                throw new ArgumentException("Trailing item data.");
            }

            return result;
        }
        catch (IOException exception)
        {
            throw new ArgumentException("Truncated item data.", exception);
        }
    }

    private static int Count(BinaryReader reader, int maximum)
    {
        int count = reader.ReadByte();
        return count <= maximum ? count : throw new ArgumentException("Excessive item count.");
    }

    private static Vector3 Vector(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static void Vector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }
}
