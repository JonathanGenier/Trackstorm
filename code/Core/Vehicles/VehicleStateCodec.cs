using System.Buffers.Binary;
using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Explicit version-five movement snapshot encoding, independent of engine and CLR layouts.</summary>
public static class VehicleStateCodec
{
    /// <summary>Version, tick, thirteen physics floats, flags, surface, handling floats and a two-byte oil timer.</summary>
    public const int SerializedSize = 119;

    /// <summary>Encodes a validated snapshot with little-endian IEEE floats and integers.</summary>
    /// <param name="state">Movement snapshot.</param>
    /// <returns>Exactly one version-five snapshot.</returns>
    public static byte[] Encode(VehicleState state)
    {
        state.Validate();
        byte[] bytes = new byte[SerializedSize];
        bytes[0] = 5;
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
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(62), state.SteeringAngle);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(66), state.Handbrake);
        bytes[70] = (byte)state.CurrentSurface;
        float[] handling = [state.FrontSlip, state.RearSlip, state.LongitudinalAcceleration, state.LateralAcceleration, state.LandingIntensity, state.Wheels.Compression.X, state.Wheels.Compression.Y, state.Wheels.Compression.Z, state.Wheels.Compression.W];
        for (int index = 0; index < handling.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(71 + (index * 4)), handling[index]);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(107), checked((ushort)state.OilTicks));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(109), checked((ushort)state.Nitro.RemainingTicks));
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(111), state.Nitro.AccelerationMultiplier);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(115), state.Nitro.SpeedMultiplier);
        return bytes;
    }

    /// <summary>Rejects unsupported versions, malformed values, reserved flags and wrong lengths.</summary>
    /// <param name="bytes">One serialized snapshot.</param>
    /// <returns>Validated movement state.</returns>
    public static VehicleState Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SerializedSize || bytes[0] != 5 || (bytes[61] & ~3) != 0)
        {
            throw new ArgumentException("Expected one version-five movement snapshot.", nameof(bytes));
        }

        float[] values = new float[13];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(9 + (index * 4))..]);
        }

        var physics = new VehiclePhysicsState(new Vector3(values[0], values[1], values[2]), new Quaternion(values[3], values[4], values[5], values[6]), new Vector3(values[7], values[8], values[9]), new Vector3(values[10], values[11], values[12]));
        return new VehicleState(BinaryPrimitives.ReadUInt64LittleEndian(bytes[1..]), physics, (bytes[61] & 1) != 0, (bytes[61] & 2) != 0, BinaryPrimitives.ReadSingleLittleEndian(bytes[62..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[66..]), (SurfaceType)bytes[70], BinaryPrimitives.ReadSingleLittleEndian(bytes[71..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[75..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[79..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[83..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[87..]), new WheelSupport(new Vector4(BinaryPrimitives.ReadSingleLittleEndian(bytes[91..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[95..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[99..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[103..]))), BinaryPrimitives.ReadUInt16LittleEndian(bytes[107..]), new NitroState(BinaryPrimitives.ReadUInt16LittleEndian(bytes[109..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[111..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[115..])));
    }
}
