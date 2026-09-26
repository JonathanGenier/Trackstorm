using System.Numerics;
using System.Collections.Immutable;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Items;

/// <summary>Bounded version-thirteen reliable item protocol. Requests carry no claimed player or outcome.</summary>
public static class ItemCodec
{
    /// <summary>Accommodates the maximum lifetime-derived Oil set and its pass counts and overlap latches.</summary>
    public const int MaximumBytes = 64 * 1024 * 1024;

    private static int WideCount(BinaryReader reader, int maximum)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > maximum || count > reader.BaseStream.Length - reader.BaseStream.Position) { throw new ArgumentException("Invalid item count."); }
        return count;
    }
    /// <summary>Recognizes only this protocol's magic; complete decode remains mandatory.</summary>
    /// <param name="bytes">Transport payload.</param>
    /// <returns>Whether this is an item envelope.</returns>
    public static bool IsItem(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 0x54 && bytes[1] == 0x49;

    /// <summary>Identifies selection intent; decoding still validates the whole message.</summary>
    public static bool IsSwitch(ReadOnlySpan<byte> bytes) => IsItem(bytes) && bytes.Length >= 4 && bytes[3] == 3;

    /// <summary>Encodes an ordered life-scoped selection command without a claimed player.</summary>
    public static byte[] EncodeSwitch(ulong session, ulong life, ulong revision) => Write(3, writer =>
    {
        if (session == 0 || life == 0 || revision == 0) { throw new ArgumentException("Invalid switch command."); }
        writer.Write(session);
        writer.Write(life);
        writer.Write(revision);
    });

    /// <summary>Decodes a complete selection command, rejecting stale layouts and malformed data.</summary>
    public static (ulong Session, ulong Life, ulong Revision) DecodeSwitch(ReadOnlySpan<byte> bytes) => Read(bytes, 3, reader =>
    {
        var value = (Session: reader.ReadUInt64(), Life: reader.ReadUInt64(), Revision: reader.ReadUInt64());
        if (value.Session == 0 || value.Life == 0 || value.Revision == 0) { throw new ArgumentException("Invalid switch command."); }
        return value;
    });

    /// <summary>Encodes an exact ownership capability in the match generation.</summary>
    /// <param name="session">Arena generation.</param>
    /// <param name="life">Vehicle life.</param>
    /// <param name="token">Granted item token.</param>
    /// <returns>Reliable use bytes.</returns>
    /// <param name="inputSequence">Optional originating input sequence, binding remote sustained activation to its captured frame.</param>
    public static byte[] EncodeUse(ulong session, ulong life, ulong token, uint? inputSequence = null) => Write(1, writer =>
    {
        if (session == 0 || life == 0 || token == 0)
        {
            throw new ArgumentException("Invalid use capability.");
        }

        writer.Write(session);
        writer.Write(life);
        writer.Write(token);
        writer.Write(inputSequence.HasValue);
        writer.Write(inputSequence.GetValueOrDefault());
    });

    /// <summary>Decodes use intent without trusting client identity.</summary>
    /// <param name="bytes">Complete request.</param>
    /// <returns>Generation, life and token.</returns>
    public static (ulong Session, ulong Life, ulong Token, uint? InputSequence) DecodeUse(ReadOnlySpan<byte> bytes) => Read(bytes, 1, reader =>
    {
        ulong session = reader.ReadUInt64();
        ulong life = reader.ReadUInt64();
        ulong token = reader.ReadUInt64();
        bool sequenced = reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new ArgumentException("Invalid input sequence flag.") };
        uint sequence = reader.ReadUInt32();
        if (!sequenced && sequence != 0) { throw new ArgumentException("Invalid unsequenced use."); }
        var value = (Session: session, Life: life, Token: token, InputSequence: sequenced ? (uint?)sequence : null);
        if (value.Session == 0 || value.Life == 0 || value.Token == 0)
        {
            throw new ArgumentException("Invalid use capability.");
        }

        return value;
    });

    /// <summary>Encodes complete reliable item and HP outcomes.</summary>
    /// <param name="state">Validated host publication.</param>
    /// <param name="previous">Prior ordered publication; unchanged Oil may reference its exact revision. Omit for checkpoints.</param>
    /// <returns>Bounded bytes.</returns>
    public static byte[] EncodeState(ItemPublication state, ItemPublication? previous = null) => Write(2, writer =>
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
            writer.Write(slot.SecondToken);
            writer.Write((byte)slot.SecondItem);
            writer.Write(slot.ActiveSlot);
            writer.Write(slot.SelectionRevision);
            writer.Write(slot.NitroCharge);
            writer.Write(slot.SecondNitroCharge);
            writer.Write(slot.EngagedToken);
            writer.Write(slot.SalvoShots);
            writer.Write(slot.SecondSalvoShots);
            writer.Write(slot.SalvoReadyTick);
            writer.Write(slot.SecondSalvoReadyTick);
            Ammo(writer, slot.Ammo);
            Ammo(writer, slot.SecondAmmo);
        }

        writer.Write((byte)state.Missiles.Count);
        foreach (var missile in state.Missiles)
        {
            writer.Write(missile.Id);
            writer.Write(missile.Owner);
            Vector(writer, missile.Position);
            Vector(writer, missile.Velocity);
            writer.Write(missile.RemainingTicks);
            writer.Write(missile.Arc is not null);
            if (missile.Arc is { } arc)
            {
                Vector(writer, arc.Origin);
                Vector(writer, arc.Target);
                writer.Write(arc.Height);
                writer.Write(arc.DurationTicks);
                writer.Write(arc.ElapsedTicks);
                writer.Write(arc.Life);
            }
        }

        writer.Write((byte)state.Mines.Count);
        foreach (var mine in state.Mines)
        {
            writer.Write(mine.Id);
            writer.Write(mine.Owner);
            Vector(writer, mine.Position);
            Vector(writer, mine.Velocity);
            Vector(writer, mine.Normal);
            writer.Write(mine.SeatingTicks);
        }
        bool reuseOil = previous is not null && previous.World.Session == state.World.Session && previous.Revision < state.Revision && previous.Patches.SequenceEqual(state.Patches) && previous.OilContacts.SequenceEqual(state.OilContacts);
        writer.Write(reuseOil ? previous!.Revision : 0);
        if (!reuseOil)
        {
            writer.Write(state.Patches.Count);
            foreach (var patch in state.Patches)
            {
                writer.Write(patch.Id);
                writer.Write(patch.Owner);
                Vector(writer, patch.Position);
                Vector(writer, patch.Normal);
                writer.Write(patch.Radius);
                writer.Write(patch.ExpiresAtTick);
                writer.Write((byte)patch.PassLimit);
                writer.Write((byte)patch.PassesUsed);
            }
            writer.Write(state.OilContacts.Count);
            foreach (var contact in state.OilContacts)
            {
                writer.Write(contact.Patch);
                writer.Write(contact.Vehicle);
                writer.Write(contact.Life);
            }
        }

        writer.Write((byte)state.Balances.Count);
        foreach (var balance in state.Balances)
        {
            writer.Write(balance.Player);
            writer.Write(balance.Total);
            writer.Write((byte)balance.SelectedItem);
            writer.Write((byte)ItemRegistry.Categories.Count);
            foreach (var category in ItemRegistry.Categories)
            {
                writer.Write((byte)category.Identity);
                writer.Write(balance.Credits[category.Identity]);
                writer.Write(balance.Counts[category.Identity]);
            }
        }
        writer.Write((byte)state.Events.Count);
        foreach (var outcome in state.Events)
        {
            writer.Write(outcome.Token);
            writer.Write(outcome.Owner);
            writer.Write((byte)outcome.Item);
            Vector(writer, outcome.Position);
            writer.Write(outcome.Impact);
            Vector(writer, outcome.Origin);
            writer.Write(outcome.Tracer);
        }
    });

    /// <summary>Rejects malformed counts, flags, versions, values and trailing data before state is accepted.</summary>
    /// <param name="bytes">Complete host publication.</param>
    /// <param name="previous">Prior ordered publication, required only for a referenced Oil baseline.</param>
    /// <returns>Validated detached state.</returns>
    public static ItemPublication DecodeState(ReadOnlySpan<byte> bytes, ItemPublication? previous = null) => Read(bytes, 2, reader =>
    {
        ulong revision = reader.ReadUInt64();
        int length = reader.ReadInt32();
        if (length is < 4 or > 16384)
        {
            throw new ArgumentException("Invalid nested world length.");
        }

        WorldSnapshot world = VehicleNetworkCodec.DecodeSnapshot(reader.ReadBytes(length));
        var spawns = new ItemSpawnState[Count(reader, Arenas.ArenaConfiguration.MaximumItemSpawns)];
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
            slots[i] = new(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), (HeldItem)reader.ReadByte())
            { SecondToken = reader.ReadUInt64(), SecondItem = (HeldItem)reader.ReadByte(), ActiveSlot = reader.ReadByte(), SelectionRevision = reader.ReadUInt64(), NitroCharge = reader.ReadDouble(), SecondNitroCharge = reader.ReadDouble(), EngagedToken = reader.ReadUInt64(),
                SalvoShots = reader.ReadInt32(), SecondSalvoShots = reader.ReadInt32(), SalvoReadyTick = reader.ReadUInt64(), SecondSalvoReadyTick = reader.ReadUInt64(), Ammo = Ammo(reader), SecondAmmo = Ammo(reader) };
        }

        var missiles = new MissileState[Count(reader, ItemAuthority.MaximumProjectiles)];
        for (int i = 0; i < missiles.Length; i++)
        {
            missiles[i] = new(reader.ReadUInt64(), reader.ReadUInt64(), Vector(reader), Vector(reader), reader.ReadInt32());
            bool arcing = reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new ArgumentException("Invalid arc flag.") };
            if (arcing) { missiles[i] = missiles[i] with { Arc = new(Vector(reader), Vector(reader), reader.ReadSingle(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadUInt64()) }; }
        }

        var mines = new ProxyMineState[Count(reader, ItemAuthority.MaximumMines)];
        for (int i = 0; i < mines.Length; i++)
        {
            mines[i] = new(reader.ReadUInt64(), reader.ReadUInt64(), Vector(reader), Vector(reader), Vector(reader), reader.ReadInt32());
        }
        ulong oilBaseline = reader.ReadUInt64();
        OilPatch[] patches;
        OilContact[] contacts;
        if (oilBaseline != 0)
        {
            if (previous is null || previous.Revision != oilBaseline || oilBaseline >= revision || previous.World.Session != world.Session) { throw new ArgumentException("Missing Oil publication baseline."); }
            patches = previous.Patches.ToArray();
            contacts = previous.OilContacts.ToArray();
        }
        else
        {
            patches = new OilPatch[WideCount(reader, ItemAuthority.MaximumPatches)];
            for (int i = 0; i < patches.Length; i++)
            {
                patches[i] = new(reader.ReadUInt64(), reader.ReadUInt64(), Vector(reader), Vector(reader), reader.ReadSingle()) { ExpiresAtTick = reader.ReadUInt64(), PassLimit = reader.ReadByte(), PassesUsed = reader.ReadByte() };
            }
            int contactCount = WideCount(reader, ItemAuthority.MaximumPatches * 6);
            contacts = new OilContact[contactCount];
            for (int i = 0; i < contacts.Length; i++)
            {
                contacts[i] = new(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64());
            }
        }

        var balances = new PlayerItemBalance[Count(reader, 8)];
        for (int i = 0; i < balances.Length; i++)
        {
            ulong player = reader.ReadUInt64();
            ulong total = reader.ReadUInt64();
            var selected = (HeldItem)reader.ReadByte();
            if (reader.ReadByte() != ItemRegistry.Categories.Count) { throw new ArgumentException("Invalid category roster."); }
            var credits = ImmutableDictionary.CreateBuilder<ItemCategory, decimal>();
            var counts = ImmutableDictionary.CreateBuilder<ItemCategory, ulong>();
            foreach (var category in ItemRegistry.Categories)
            {
                if (reader.ReadByte() != (byte)category.Identity) { throw new ArgumentException("Invalid category identity."); }
                credits.Add(category.Identity, reader.ReadDecimal());
                counts.Add(category.Identity, reader.ReadUInt64());
            }
            balances[i] = new() { Player = player, Total = total, SelectedItem = selected, Credits = credits.ToImmutable(), Counts = counts.ToImmutable() };
        }
        var events = new ItemEvent[Count(reader, ItemAuthority.MaximumProjectiles * 2 + ItemAuthority.MaximumMines + 16)];
        for (int i = 0; i < events.Length; i++)
        {
            ulong token = reader.ReadUInt64();
            ulong owner = reader.ReadUInt64();
            var item = (HeldItem)reader.ReadByte();
            Vector3 position = Vector(reader);
            bool impact = reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new ArgumentException("Invalid impact flag.") };
            events[i] = new(token, owner, item, position, impact) { Origin = Vector(reader), Tracer = reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new ArgumentException("Invalid tracer flag.") } };
        }

        return new ItemPublication(revision, world, slots, missiles, events, spawns, patches, contacts, balances, mines);
    });

    private static byte[] Write(byte kind, Action<BinaryWriter> encode)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { 0x54, 0x49, 13, kind });
        encode(writer);
        if (stream.Length > MaximumBytes)
        {
            throw new ArgumentException("Item payload too large.");
        }

        return stream.ToArray();
    }

    private static T Read<T>(ReadOnlySpan<byte> bytes, byte kind, Func<BinaryReader, T> decode)
    {
        if (bytes.Length is < 4 or > MaximumBytes || !IsItem(bytes) || bytes[2] != 13 || bytes[3] != kind)
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

    private static void Ammo(BinaryWriter writer, MachineGunAmmo? ammo)
    {
        writer.Write(ammo is not null);
        if (ammo is null) { return; }
        writer.Write(ammo.Remaining);
        writer.Write(ammo.Capacity);
        writer.Write(ammo.Phase);
    }

    private static MachineGunAmmo? Ammo(BinaryReader reader) => reader.ReadByte() switch
    {
        0 => null,
        1 => new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadDouble()),
        _ => throw new ArgumentException("Invalid ammunition flag."),
    };

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
