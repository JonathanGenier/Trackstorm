using System.Buffers.Binary;
using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Portable movement snapshots reject malformed state and preserve every replay field.</summary>
[TestFixture]
internal sealed class VehicleStateCodecTests
{
    /// <summary>Physics, flags, steering, handbrake, tire loads and wheel compression round trip exactly.</summary>
    [Test]
    public void Snapshot_RoundTripsEveryField()
    {
        var physics = new VehiclePhysicsState(new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f), new Vector3(-4, 5, 6), new Vector3(0.1f, 0.2f, 0.3f));
        var state = new VehicleState(9876, physics, true, true, 0.25f, 0.7f, SurfaceType.Mud, 0.3f, 0.8f, -4, 5, 0.4f, new WheelSupport(new Vector4(0.1f, 0.2f, 0.3f, 0.4f)), powerSlip: 0.17f);
        byte[] bytes = VehicleStateCodec.Encode(state);
        Assert.That(bytes.Length, Is.EqualTo(155));
        Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(1)), Is.EqualTo(9876));
        Assert.That(VehicleStateCodec.Decode(bytes), Is.EqualTo(state));
    }

    /// <summary>Transport validation rejects corrupt layouts and values before movement can consume them.</summary>
    [Test]
    public void Snapshot_RejectsInvalidPayloads()
    {
        var physics = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        byte[] bytes = VehicleStateCodec.Encode(new VehicleState(0, physics, false, false, 0, 0));
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes.AsSpan(1)));
        bytes[0] = 2;
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
        bytes[0] = 7;
        bytes[61] = 128;
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
        bytes[61] = 0;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9), float.NaN);
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9), float.PositiveInfinity);
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9), float.NegativeInfinity);
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9), 0);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(66), -1);
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(66), 0);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(62), 5);
        Assert.Throws<ArgumentException>(() => VehicleStateCodec.Decode(bytes));
    }
}
