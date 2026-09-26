using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Bounded, replaceable presentation samples. Reliable item state owns membership and outcomes.</summary>
public static class ProjectileMotionCodec
{
    /// <summary>Header and at most sixteen fixed-size projectile samples.</summary>
    public const int MaximumBytes = 28 + ItemAuthority.MaximumProjectiles * 40;

    /// <summary>Recognizes the motion protocol; full validation remains mandatory.</summary>
    public static bool IsMotion(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 'T' && bytes[1] == 'J';

    /// <summary>Encodes current host motion against a reliable membership boundary.</summary>
    public static byte[] Encode(ItemPublication baseline, ulong tick, IReadOnlyList<MissileState> missiles)
    {
        using var stream = new MemoryStream(MaximumBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'T'); writer.Write((byte)'J'); writer.Write((byte)1);
        writer.Write(baseline.World.Session); writer.Write(baseline.Revision); writer.Write(tick);
        writer.Write((byte)missiles.Count);
        foreach (var missile in missiles)
        {
            writer.Write(missile.Id);
            WriteVector(writer, missile.Position); WriteVector(writer, missile.Velocity);
            writer.Write(missile.RemainingTicks);
            writer.Write(missile.Arc?.ElapsedTicks ?? -1);
        }
        byte[] bytes = stream.ToArray();
        // Validate production sender data through the same bounded contract as the receiver.
        ValidateHeader(bytes, baseline, out _);
        return bytes;
    }

    /// <summary>Reconstructs presentation only; never changes inventory, HP, prediction, or a checkpoint baseline.</summary>
    public static (ulong Tick, IReadOnlyList<MissileState> Missiles) Decode(ReadOnlySpan<byte> bytes, ItemPublication baseline)
    {
        ValidateHeader(bytes, baseline, out ulong tick);
        using var stream = new MemoryStream(bytes.ToArray(), false);
        using var reader = new BinaryReader(stream);
        stream.Position = 28;
        var missiles = new MissileState[baseline.Missiles.Count];
        ulong elapsed = tick - baseline.World.Tick;
        for (int i = 0; i < missiles.Length; i++)
        {
            var original = baseline.Missiles[i];
            ulong id = reader.ReadUInt64();
            Vector3 position = ReadVector(reader), velocity = ReadVector(reader);
            int remaining = reader.ReadInt32(), arcTick = reader.ReadInt32();
            if (id != original.Id || elapsed >= (ulong)original.RemainingTicks || remaining != original.RemainingTicks - (int)elapsed ||
                !VehiclePhysicsState.IsFinite(position) || !VehiclePhysicsState.IsFinite(velocity) ||
                velocity.Length() <= 0 || velocity.Length() > (original.Arc is null ? 301 : 1000) ||
                (original.Arc is null ? arcTick != -1 : arcTick != original.Arc.ElapsedTicks + (int)elapsed))
            { throw new ArgumentException("Invalid projectile motion sample."); }
            var arc = original.Arc is { } flight ? flight with { ElapsedTicks = arcTick } : null;
            if (arc is not null && Vector3.Distance(position, arc.At(arcTick)) > 0.01f)
            { throw new ArgumentException("Invalid projectile arc position."); }
            missiles[i] = original with { Position = position, Velocity = velocity, RemainingTicks = remaining, Arc = arc };
        }
        return (tick, Array.AsReadOnly(missiles));
    }

    private static void ValidateHeader(ReadOnlySpan<byte> bytes, ItemPublication baseline, out ulong tick)
    {
        tick = 0;
        if (bytes.Length < 28 || bytes.Length > MaximumBytes || !IsMotion(bytes) || bytes[2] != 1 ||
            bytes[27] != baseline.Missiles.Count || bytes.Length != 28 + bytes[27] * 40)
        { throw new ArgumentException("Invalid projectile motion header."); }
        ulong session = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes[3..]);
        ulong revision = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes[11..]);
        tick = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes[19..]);
        if (session != baseline.World.Session || revision != baseline.Revision || tick <= baseline.World.Tick)
        { throw new ArgumentException("Projectile motion requires its exact reliable baseline."); }
    }

    private static void WriteVector(BinaryWriter writer, Vector3 vector) { writer.Write(vector.X); writer.Write(vector.Y); writer.Write(vector.Z); }
    private static Vector3 ReadVector(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
