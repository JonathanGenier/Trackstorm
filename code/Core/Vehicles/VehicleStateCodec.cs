using System.Buffers.Binary;
using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Explicit version-one movement snapshot encoding, independent of engine and CLR layouts.</summary>
public static class VehicleStateCodec
{
    /// <summary>Version, tick, thirteen floats, flags and two integer timers.</summary>
    public const int SerializedSize = 70;

    /// <summary>Encodes a validated snapshot with little-endian IEEE floats and integers.</summary>
    /// <param name="state">Movement snapshot.</param>
    /// <returns>Exactly one version-one snapshot.</returns>
    public static byte[] Encode(VehicleState state)
    {
        _ = new VehicleState(state.Tick, state.Physics, state.Grounded, state.Drifting, state.DriftTicks, state.BoostTicks);
        byte[] bytes = new byte[SerializedSize];
        bytes[0] = 1;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(1), state.Tick);
        Vector3 p = state.Physics.Position;
        Quaternion q = state.Physics.Orientation;
        Vector3 v = state.Physics.LinearVelocity;
        Vector3 a = state.Physics.AngularVelocity;
        float[] values = [p.X, p.Y, p.Z, q.X, q.Y, q.Z, q.W, v.X, v.Y, v.Z, a.X, a.Y, a.Z];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9 + (index * 4)), values[index]);
        }

        bytes[61] = (byte)((state.Grounded ? 1 : 0) | (state.Drifting ? 2 : 0));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(62), state.DriftTicks);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(66), state.BoostTicks);
        return bytes;
    }

    /// <summary>Rejects unsupported versions, malformed values, reserved flags and wrong lengths.</summary>
    /// <param name="bytes">One serialized snapshot.</param>
    /// <returns>Validated movement state.</returns>
    public static VehicleState Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SerializedSize || bytes[0] != 1 || (bytes[61] & ~3) != 0)
        {
            throw new ArgumentException("Expected one version-one movement snapshot.", nameof(bytes));
        }

        float[] values = new float[13];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(9 + (index * 4))..]);
        }

        var physics = new VehiclePhysicsState(new Vector3(values[0], values[1], values[2]), new Quaternion(values[3], values[4], values[5], values[6]), new Vector3(values[7], values[8], values[9]), new Vector3(values[10], values[11], values[12]));
        return new VehicleState(BinaryPrimitives.ReadUInt64LittleEndian(bytes[1..]), physics, (bytes[61] & 1) != 0, (bytes[61] & 2) != 0, BinaryPrimitives.ReadInt32LittleEndian(bytes[62..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[66..]));
    }
}
