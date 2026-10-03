using System.Numerics;

namespace Trackstorm.Core.Items;

public static partial class ItemCodec
{
    /// <summary>Recognizes replaceable item aim messages; exact decode remains mandatory.</summary>
    public static bool IsAim(ReadOnlySpan<byte> bytes) => IsItem(bytes) && bytes.Length >= 4 && bytes[3] is 4 or 5;
    /// <summary>Encodes only scope, order and desired direction, never a player ID or accepted firing result.</summary>
    public static byte[] EncodeAim(ulong session, ulong life, ulong token, ulong selection, ulong sequence, Vector3 direction) => Write(4, writer =>
    {
        ValidateAimRequest(session, life, token, sequence, direction);
        writer.Write(session); writer.Write(life); writer.Write(token); writer.Write(selection); writer.Write(sequence); Vector(writer, direction);
    });

    /// <summary>Decodes one strictly bounded desired aim request.</summary>
    public static (ulong Session, ulong Life, ulong Token, ulong Selection, ulong Sequence, Vector3 Direction) DecodeAim(ReadOnlySpan<byte> bytes) => bytes.Length != 56 ? throw new ArgumentException("Invalid aim request length.") : Read(bytes, 4, reader =>
    {
        var value = (Session: reader.ReadUInt64(), Life: reader.ReadUInt64(), Token: reader.ReadUInt64(), Selection: reader.ReadUInt64(), Sequence: reader.ReadUInt64(), Direction: Vector(reader));
        ValidateAimRequest(value.Session, value.Life, value.Token, value.Sequence, value.Direction);
        return value;
    });

    /// <summary>Publishes accepted transient solutions without advancing reliable item outcome revisions.</summary>
    public static byte[] EncodeAims(ulong session, ulong tick, ulong configuration, IReadOnlyList<WeaponAimSolution> aims) => Write(5, writer =>
    {
        if (session == 0 || aims.Count > 8 || aims.Select(aim => aim.Vehicle).Distinct().Count() != aims.Count) { throw new ArgumentException("Invalid aim publication."); }
        writer.Write(session); writer.Write(tick); writer.Write(configuration); writer.Write((byte)aims.Count);
        foreach (var aim in aims)
        {
            ValidateAimSolution(aim, tick);
            writer.Write(aim.Vehicle); writer.Write(aim.Life); writer.Write(aim.Token); writer.Write(aim.Tick);
            writer.Write(aim.Yaw); writer.Write(aim.Pitch); Vector(writer, aim.Origin); Vector(writer, aim.Direction);
            writer.Write(aim.Clear); writer.Write(aim.Ready);
        }
    });

    /// <summary>Decodes at most eight bounded accepted solutions, with no inventory or hit side effects.</summary>
    public static (ulong Session, ulong Tick, ulong Configuration, IReadOnlyList<WeaponAimSolution> Aims) DecodeAims(ReadOnlySpan<byte> bytes) => bytes.Length is < 29 or > 557 ? throw new ArgumentException("Invalid aim publication length.") : Read(bytes, 5, reader =>
    {
        ulong session = reader.ReadUInt64(), tick = reader.ReadUInt64(), configuration = reader.ReadUInt64();
        if (session == 0) { throw new ArgumentException("Invalid aim session."); }
        var aims = new WeaponAimSolution[Count(reader, 8)];
        for (int i = 0; i < aims.Length; i++)
        {
            aims[i] = new(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadSingle(), reader.ReadSingle(), Vector(reader), Vector(reader), Flag(reader)) { Ready = Flag(reader) };
            ValidateAimSolution(aims[i], tick);
        }
        if (aims.Select(aim => aim.Vehicle).Distinct().Count() != aims.Length) { throw new ArgumentException("Duplicate aim owner."); }
        return (session, tick, configuration, aims);
    });

    private static bool Flag(BinaryReader reader) => reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new ArgumentException("Invalid aim flag.") };
    private static void ValidateAimRequest(ulong session, ulong life, ulong token, ulong sequence, Vector3 direction)
    {
        if (session == 0 || life == 0 || token == 0 || sequence == 0 || !WeaponAim.IsDirection(direction)) { throw new ArgumentException("Invalid aim request."); }
    }
    private static void ValidateAimSolution(WeaponAimSolution aim, ulong tick)
    {
        if (aim.Vehicle == 0 || aim.Life == 0 || aim.Token == 0 || aim.Tick > tick || tick - aim.Tick > 15 ||
            !float.IsFinite(aim.Yaw) || Math.Abs(aim.Yaw) > MathF.PI + .0001f || !float.IsFinite(aim.Pitch) || aim.Pitch is < -1.05f or > 1.40f ||
            !Vehicles.VehiclePhysicsState.IsFinite(aim.Origin) || aim.Origin.LengthSquared() > 1e12f || !WeaponAim.IsDirection(aim.Direction) ||
            (aim.Ready && !aim.Clear))
        { throw new ArgumentException("Invalid accepted aim solution."); }
    }
}
