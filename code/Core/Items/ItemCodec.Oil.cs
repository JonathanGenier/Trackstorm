
namespace Trackstorm.Core.Items;

public static partial class ItemCodec
{
    private static void Pour(BinaryWriter writer, OilPour? pour)
    {
        writer.Write(pour is not null);
        if (pour is null) { return; }
        writer.Write(pour.Life); writer.Write(pour.StartTick);
        writer.Write(pour.DurationTicks); writer.Write(pour.IntervalTicks);
        writer.Write(pour.Speed); writer.Write(pour.Gravity); writer.Write(pour.Radius);
        writer.Write(pour.Emitted); writer.Write(pour.Deposited); writer.Write(pour.Cancelled);
        Vector(writer, pour.PreviousVelocity); Vector(writer, pour.HeadOffset); Vector(writer, pour.HeadVelocity);
        writer.Write(pour.Stiffness); writer.Write(pour.Damping); writer.Write(pour.Reaction); writer.Write(pour.Kickback);
        writer.Write((byte)pour.Drops.Length);
        foreach (var drop in pour.Drops.Drops) { Vector(writer, drop.Position); Vector(writer, drop.Velocity); writer.Write(drop.RemainingTicks); }
    }

    private static OilPour? Pour(BinaryReader reader)
    {
        if (!reader.ReadBoolean()) { return null; }
        var pour = new OilPour
        {
            Life = reader.ReadUInt64(), StartTick = reader.ReadUInt64(), DurationTicks = reader.ReadInt32(), IntervalTicks = reader.ReadSingle(),
            Speed = reader.ReadSingle(), Gravity = reader.ReadSingle(), Radius = reader.ReadSingle(),
            Emitted = reader.ReadInt32(), Deposited = reader.ReadBoolean(), Cancelled = reader.ReadBoolean(),
            PreviousVelocity = Vector(reader), HeadOffset = Vector(reader), HeadVelocity = Vector(reader),
            Stiffness = reader.ReadSingle(), Damping = reader.ReadSingle(), Reaction = reader.ReadSingle(), Kickback = reader.ReadSingle()
        };
        var drops = new OilDrop[Count(reader, 48)];
        for (int i = 0; i < drops.Length; i++) { drops[i] = new(Vector(reader), Vector(reader), reader.ReadInt32()); }
        pour = pour with { Drops = new OilFlight(drops) };
        pour.Validate();
        return pour;
    }
}
