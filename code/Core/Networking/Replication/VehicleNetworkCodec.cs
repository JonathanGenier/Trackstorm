using System.Numerics;
using System.Text;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Versioned binary gameplay messages, separate from future reliable combat events.</summary>
public static class VehicleNetworkCodec
{
    /// <summary>Reliable session assignment message kind.</summary>
    public const byte Welcome = 1;
    /// <summary>Unreliable redundant input message kind.</summary>
    public const byte Inputs = 2;
    /// <summary>Unreliable full vehicle snapshot message kind.</summary>
    public const byte Snapshot = 3;
    /// <summary>Full authoritative prototype prop publication.</summary>
    public const byte Props = 4;
    private const int MaximumBytes = 16384;

    /// <summary>Checks the protocol header before routing a bounded payload.</summary>
    /// <param name="bytes">Complete transport payload.</param>
    /// <returns>Recognized message kind.</returns>
    public static byte Kind(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 4 or > MaximumBytes || bytes[0] != 0x54 || bytes[1] != 0x53 || bytes[2] != 2 || bytes[3] is < Welcome or > Props)
        {
            throw new ArgumentException("Invalid vehicle network header.");
        }

        return bytes[3];
    }

    /// <summary>Encodes a complete, bounded host prop observation.</summary>
    /// <param name="snapshot">Complete host publication.</param>
    /// <returns>Versioned bounded bytes.</returns>
    public static byte[] EncodeProps(Arenas.ArenaPropSnapshot snapshot) => Write(Props, writer =>
    {
        writer.Write(snapshot.Session);
        writer.Write(snapshot.Tick);
        foreach (VehiclePhysicsState body in snapshot.Bodies)
        {
            WriteVector(writer, body.Position);
            writer.Write(body.Orientation.X);
            writer.Write(body.Orientation.Y);
            writer.Write(body.Orientation.Z);
            writer.Write(body.Orientation.W);
            WriteVector(writer, body.LinearVelocity);
            WriteVector(writer, body.AngularVelocity);
        }
    });

    /// <summary>Rejects malformed, nonfinite, excessive or incomplete prop state.</summary>
    /// <param name="bytes">Complete publication bytes.</param>
    /// <returns>Validated copied prop state.</returns>
    public static Arenas.ArenaPropSnapshot DecodeProps(ReadOnlySpan<byte> bytes) => Read(bytes, Props, reader =>
    {
        ulong session = reader.ReadUInt64();
        ulong tick = reader.ReadUInt64();
        var bodies = new VehiclePhysicsState[3];
        for (int index = 0; index < bodies.Length; index++)
        {
            bodies[index] = new VehiclePhysicsState(ReadVector(reader), new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), ReadVector(reader), ReadVector(reader));
        }

        return new Arenas.ArenaPropSnapshot(session, tick, bodies);
    });

    /// <summary>Encodes a reliable assignment without granting authority to a client-chosen identity.</summary>
    /// <param name="session">Host generation.</param>
    /// <param name="vehicle">Assigned gameplay identity.</param>
    /// <returns>Versioned control payload.</returns>
    public static byte[] EncodeWelcome(ulong session, ulong vehicle) => Write(Welcome, writer =>
    {
        ArgumentOutOfRangeException.ThrowIfZero(session);
        ArgumentOutOfRangeException.ThrowIfZero(vehicle);
        writer.Write(session);
        writer.Write(vehicle);
    });

    /// <summary>Decodes a reliable assignment.</summary>
    /// <param name="bytes">Complete control payload.</param>
    /// <returns>Host generation and assigned vehicle.</returns>
    public static (ulong Session, ulong Vehicle) DecodeWelcome(ReadOnlySpan<byte> bytes) => Read(bytes, Welcome, reader =>
    {
        ulong session = reader.ReadUInt64();
        ulong vehicle = reader.ReadUInt64();
        if (session == 0 || vehicle == 0)
        {
            throw new ArgumentException("Invalid session assignment.");
        }

        return (session, vehicle);
    });

    /// <summary>Encodes at most four redundant commands, with no client-owned vehicle state.</summary>
    /// <param name="session">Negotiated host generation.</param>
    /// <param name="inputs">Ordered recent inputs.</param>
    /// <returns>Unreliable input payload.</returns>
    public static byte[] EncodeInputs(ulong session, IReadOnlyList<SequencedInput> inputs) => Write(Inputs, writer =>
    {
        if (session == 0 || inputs.Count is < 1 or > InputHistory.Redundancy)
        {
            throw new ArgumentException("Invalid input envelope.");
        }

        writer.Write(session);
        writer.Write((byte)inputs.Count);
        foreach (SequencedInput input in inputs)
        {
            writer.Write(input.Sequence);
            byte[] frame = new byte[InputFrame.SerializedSize];
            input.Frame.Write(frame);
            writer.Write(frame);
        }
    });

    /// <summary>Decodes bounded input; host ordering and sender validation remain mandatory.</summary>
    /// <param name="bytes">Complete input payload.</param>
    /// <returns>Session and input window.</returns>
    public static (ulong Session, SequencedInput[] Inputs) DecodeInputs(ReadOnlySpan<byte> bytes) => Read(bytes, Inputs, reader =>
    {
        ulong session = reader.ReadUInt64();
        byte count = reader.ReadByte();
        if (session == 0 || count is < 1 or > InputHistory.Redundancy)
        {
            throw new ArgumentException("Invalid input window size.");
        }

        var inputs = new SequencedInput[count];
        for (int i = 0; i < count; i++)
        {
            inputs[i] = new SequencedInput(reader.ReadUInt32(), InputFrame.Read(reader.ReadBytes(InputFrame.SerializedSize)));
        }

        return (session, inputs);
    });

    /// <summary>Encodes all active vehicles compactly, retaining replay-critical collision and movement memory.</summary>
    /// <param name="snapshot">Authoritative roster.</param>
    /// <returns>Binary snapshot, normally under 1200 bytes for eight undamaged vehicles.</returns>
    public static byte[] EncodeSnapshot(WorldSnapshot snapshot) => Write(Snapshot, writer =>
    {
        writer.Write(snapshot.Session);
        writer.Write(snapshot.Tick);
        writer.Write((byte)snapshot.Vehicles.Count);
        foreach (ReplicatedVehicle vehicle in snapshot.Vehicles)
        {
            VehicleSnapshot state = vehicle.State;
            writer.Write(state.VehicleId);
            writer.Write(state.LifeId);
            writer.Write(vehicle.AcknowledgedInput);
            writer.Write(VehicleStateCodec.Encode(state.Movement));
            WriteVector(writer, state.ObservedPhysics.LinearVelocity);
            WriteVector(writer, state.ObservedPhysics.AngularVelocity);
            if (state.Effects.Count > 64)
            {
                throw new ArgumentException("Excessive effects.");
            }

            writer.Write((byte)state.Effects.Count);
            foreach (var effect in state.Effects)
            {
                writer.Write(effect.Effect.Damage);
                WriteVector(writer, effect.Effect.Impulse);
                WriteVector(writer, effect.Effect.Offset);
                writer.Write(effect.Attribution.Source);
                writer.Write(effect.Attribution.InstigatorId);
                writer.Write(effect.Attribution.Context);
            }

            writer.Write(state.Damage.MaxHP);
            writer.Write(state.Damage.CurrentHP);
            writer.Write(state.Damage.LastDamage is not null);
            if (state.Damage.LastDamage is DamageEvent damage)
            {
                writer.Write(damage.Sequence);
                writer.Write(damage.Tick);
                writer.Write(damage.Amount);
                writer.Write(damage.Attribution.Source);
                writer.Write(damage.Attribution.InstigatorId);
                writer.Write(damage.Attribution.Context);
                writer.Write(damage.DestroyedTransition);
            }

            writer.Write(state.Damage.LastCollisionTick.HasValue);
            if (state.Damage.LastCollisionTick is ulong collision)
            {
                writer.Write(collision);
            }
        }
    });

    /// <summary>Validates the complete roster before any live state is changed.</summary>
    /// <param name="bytes">Complete snapshot payload.</param>
    /// <returns>Detached authoritative roster.</returns>
    public static WorldSnapshot DecodeSnapshot(ReadOnlySpan<byte> bytes) => Read(bytes, Snapshot, reader =>
    {
        ulong session = reader.ReadUInt64();
        ulong tick = reader.ReadUInt64();
        byte count = reader.ReadByte();
        if (count is < 1 or > 8)
        {
            throw new ArgumentException("Invalid snapshot vehicle count.");
        }

        var vehicles = new ReplicatedVehicle[count];
        for (int i = 0; i < count; i++)
        {
            ulong id = reader.ReadUInt64();
            ulong life = reader.ReadUInt64();
            uint ack = reader.ReadUInt32();
            VehicleState movement = VehicleStateCodec.Decode(reader.ReadBytes(VehicleStateCodec.SerializedSize));
            var observed = new VehiclePhysicsState(movement.Physics.Position, movement.Physics.Orientation, ReadVector(reader), ReadVector(reader));
            byte effectCount = reader.ReadByte();
            if (effectCount > 64)
            {
                throw new ArgumentException("Excessive effects.");
            }

            var effects = new VehicleEffectRequest[effectCount];
            for (int effectIndex = 0; effectIndex < effects.Length; effectIndex++)
            {
                effects[effectIndex] = new VehicleEffectRequest(new DamageEffect(reader.ReadSingle(), ReadVector(reader), ReadVector(reader)), new DamageContext(reader.ReadString(), reader.ReadUInt64(), reader.ReadString()));
            }

            float maxHP = reader.ReadSingle();
            float hp = reader.ReadSingle();
            DamageEvent? damage = ReadFlag(reader) ? new DamageEvent(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadSingle(), new DamageContext(reader.ReadString(), reader.ReadUInt64(), reader.ReadString()), ReadFlag(reader)) : null;
            ulong? collision = ReadFlag(reader) ? reader.ReadUInt64() : null;
            var state = new VehicleSnapshot(id, life, movement, new VehicleDamageState(maxHP, hp, damage, collision), observed, effects);
            // Reject snapshots that cannot be restored under the negotiated fixed tuning.
            new VehicleMovement(new(), observed).Restore(movement);
            if (maxHP != new DamageConfiguration().MaxHP)
            {
                throw new ArgumentException("Unsupported network health tuning.");
            }

            vehicles[i] = new ReplicatedVehicle(state, ack);
        }

        return new WorldSnapshot(session, tick, vehicles);
    });

    private static byte[] Write(byte kind, Action<BinaryWriter> encode)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        writer.Write(new byte[] { 0x54, 0x53, 2, kind });
        encode(writer);
        if (stream.Length > MaximumBytes)
        {
            throw new ArgumentException("Vehicle network payload is too large.");
        }

        return stream.ToArray();
    }

    private static T Read<T>(ReadOnlySpan<byte> bytes, byte kind, Func<BinaryReader, T> decode)
    {
        if (Kind(bytes) != kind)
        {
            throw new ArgumentException("Unexpected vehicle message kind.");
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), false);
            using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
            stream.Position = 4;
            T result = decode(reader);
            if (stream.Position != stream.Length)
            {
                throw new ArgumentException("Trailing vehicle message data.");
            }

            return result;
        }
        catch (Exception exception) when (exception is IOException or FormatException or DecoderFallbackException)
        {
            throw new ArgumentException("Malformed vehicle network payload.", nameof(bytes), exception);
        }
    }

    private static bool ReadFlag(BinaryReader reader) => reader.ReadByte() switch { 0 => false, 1 => true, _ => throw new ArgumentException("Invalid flag.") };
    private static Vector3 ReadVector(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }
}
