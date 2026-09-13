using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Arenas;

/// <summary>Bounded prop publication validation, copying and wire integrity.</summary>
internal sealed class ArenaPropSnapshotTests
{
    /// <summary>All three bodies retain their complete poses and velocities.</summary>
    [Test]
    public void RoundTripIsExactAndCopiesInput()
    {
        var body = new VehiclePhysicsState(new Vector3(2, 3, 4), Quaternion.Identity, new Vector3(1, 2, 3), Vector3.UnitY);
        VehiclePhysicsState[] input = { body, body, body };
        var snapshot = new ArenaPropSnapshot(8, 42, input);
        input[0] = default;
        var decoded = VehicleNetworkCodec.DecodeProps(VehicleNetworkCodec.EncodeProps(snapshot));
        Assert.That(decoded.Session, Is.EqualTo(8));
        Assert.That(decoded.Tick, Is.EqualTo(42));
        Assert.That(decoded.Bodies, Is.EqualTo(new[] { body, body, body }));
    }

    /// <summary>Truncation, trailing data, versions and invalid numeric payloads fail.</summary>
    [Test]
    public void MalformedWireFails()
    {
        var body = new VehiclePhysicsState(Vector3.One, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        byte[] bytes = VehicleNetworkCodec.EncodeProps(new ArenaPropSnapshot(1, 1, new[] { body, body, body }));
        for (int length = 0; length < bytes.Length; length++)
        {
            int truncated = length;
            Assert.Throws<ArgumentException>(() => VehicleNetworkCodec.DecodeProps(bytes.AsSpan(0, truncated)));
        }

        Assert.Throws<ArgumentException>(() => VehicleNetworkCodec.DecodeProps(bytes.Concat(new byte[] { 0 }).ToArray()));
        bytes[2] = 255;
        Assert.Throws<ArgumentException>(() => VehicleNetworkCodec.DecodeProps(bytes));
        bytes[2] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(20), float.NaN);
        Assert.Throws<ArgumentException>(() => VehicleNetworkCodec.DecodeProps(bytes));
    }

    /// <summary>Broken native observations and changed prop rosters cannot silently publish.</summary>
    [Test]
    public void InvalidObservationsFail()
    {
        var body = new VehiclePhysicsState(Vector3.One, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        Assert.Throws<ArgumentException>(() => new ArenaPropSnapshot(1, 0, new[] { body }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArenaPropSnapshot(0, 0, new[] { body, body, body }));
        Assert.Throws<ArgumentException>(() => new ArenaPropSnapshot(1, 0, new VehiclePhysicsState[3]));
        var excessive = new VehiclePhysicsState(Vector3.One, Quaternion.Identity, new Vector3(101, 0, 0), Vector3.Zero);
        Assert.Throws<ArgumentException>(() => new ArenaPropSnapshot(1, 0, new[] { body, body, excessive }));
    }
}
