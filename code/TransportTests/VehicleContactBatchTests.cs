using System.Numerics;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

internal sealed class VehicleContactBatchTests
{
    private static readonly VehicleConfiguration Tuning = new();
    private static readonly InputFrame Input = new(2, 0, 0, 0, 0, 0, 0);
    private static VehicleContact Pair(ulong other, Vector3 normal, Vector3 point) => new(Vector3.Zero, normal, 0, other, localPosition: point);
    private static VehicleSnapshot Previous(ulong id, VehiclePhysicsState physics, VehicleEffectRequest[]? effects = null) =>
        new(id, 1, new(1, physics, false, false, 0, 0), new(1000, 1000, null, null), physics, effects);

    [TestCase(false)]
    [TestCase(true)]
    public void MixedBoundaryPreservesTangentialSlowdownAndTorqueWithoutReplayingEffects(bool terrain)
    {
        var incoming = new VehiclePhysicsState(Vector3.Zero, terrain ? Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1.1f) : Quaternion.Identity, new(12, -8, -15), Vector3.Zero);
        var worldContact = new VehicleContact(incoming.LinearVelocity, terrain ? Vector3.UnitY : -Vector3.UnitX, 0, 0,
            terrain: terrain, localPosition: terrain ? new(0, -.2f, -2) : new(1, -1, 2), staticObstacle: !terrain);
        var world = terrain ? TerrainCollision.Resolve(incoming, worldContact, Tuning) :
            EnvironmentCollision.Resolve(incoming, Vector3.UnitY, [worldContact], Tuning);
        Assert.That(world.AngularVelocity.Length(), Is.GreaterThan(.1f));
        Assert.That(Math.Abs(world.LinearVelocity.Z), Is.LessThan(15));
        var partner = new VehiclePhysicsState(new(3, 0, 0), Quaternion.Identity, world.LinearVelocity, world.AngularVelocity);
        var accepted = new VehicleEffectRequest(new DamageEffect(0, new(900, 0, 600), new(0, 1, 0)), new DamageContext("test", 0, "test"));
        var requests = new[]
        {
            new VehicleStepRequest(1, Input, new(world, Vector3.Zero, [worldContact, Pair(2, -Vector3.UnitX, new(1.5f, 0, 0))]), [accepted]),
            new VehicleStepRequest(2, Input, new(partner, Vector3.Zero, [Pair(1, Vector3.UnitX, new(-1.5f, 0, 0))]))
        };
        // Contact-point rotation can close the pair; choose a separating centre velocity
        // so this case tests a world impact with incidental, zero-impulse vehicle touching.
        partner = new(partner.Position, partner.Orientation, partner.LinearVelocity + new Vector3(100, 0, 0), partner.AngularVelocity);
        requests[1] = new(2, Input, new(partner, Vector3.Zero, requests[1].Observation.Contacts));
        var result = VehicleContactBatch.Resolve(requests, id => Previous(id, incoming, [accepted]), _ => Tuning);
        Assert.That(result[0].Observation.Physics, Is.EqualTo(world));
        Assert.That(result[1].Observation.Physics, Is.EqualTo(partner));
        Assert.That(result[0].Observation.Contacts, Is.EqualTo(requests[0].Observation.Contacts));
        Assert.That(result[0].Effects, Is.EqualTo(requests[0].Effects), "new effects stay queued; accepted effects are already in the observed world state");
    }

    [TestCase(0f)]
    [TestCase(.05f)]
    [TestCase(6f)]
    public void SharedPairSolveUsesPostWorldVelocityAndPreservesIndependentWorldComponents(float closing)
    {
        var first = new VehiclePhysicsState(new(-2.8f, 0, 3.4f), Quaternion.Identity, new(closing, 2, -8), new(.2f, 0, .1f));
        var second = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 2, -8), new(.2f, 0, .1f));
        var point = new Vector3(-1.2f, 0, 1.8f);
        var barrier = new VehicleContact(new(0, 2, -15), Vector3.UnitZ, 0, 0, staticObstacle: true);
        var requests = new[]
        {
            new VehicleStepRequest(1, Input, new(first, Vector3.Zero, [barrier, Pair(2, -Vector3.UnitX, point - first.Position)])),
            new VehicleStepRequest(2, Input, new(second, Vector3.Zero, [Pair(1, Vector3.UnitX, point)]))
        };
        var result = VehicleContactBatch.Resolve(requests, id => Previous(id, new(Vector3.Zero, Quaternion.Identity, new(20, -10, -30), Vector3.Zero)), _ => Tuning);
        var expected = VehicleCollision.ResolvePair(first, Tuning, second, Tuning, -Vector3.UnitX, point);
        Assert.That(result[0].Observation.Physics, Is.EqualTo(expected.First));
        Assert.That(result[1].Observation.Physics, Is.EqualTo(expected.Second));
        Assert.That(result[0].Observation.Physics.LinearVelocity.Z, Is.EqualTo(-8), "world tangential slowdown is retained once");
        Assert.That(result[1].Observation.Physics.LinearVelocity.Z, Is.EqualTo(-8));
        Assert.That(result[0].Observation.Physics.LinearVelocity + result[1].Observation.Physics.LinearVelocity,
            Is.EqualTo(first.LinearVelocity + second.LinearVelocity), "pair momentum is exchanged, not manufactured");
        float Energy(VehiclePhysicsState p) => p.LinearVelocity.LengthSquared() + Tuning.Wheelbase * Tuning.Wheelbase / 3 * p.AngularVelocity.LengthSquared();
        Assert.That(Energy(expected.First) + Energy(expected.Second), Is.LessThanOrEqualTo(Energy(first) + Energy(second) + .001f));
        if (closing == 6) { Assert.That(result[1].Observation.Physics.AngularVelocity.Y, Is.GreaterThan(.5f), "qualifying PIT still acts after world contact"); }
    }

    [Test]
    public void MixedContactKeepsTheEntireConnectedNativePairBoundaryIncludingOneSidedReports()
    {
        var observed = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -8), new(0, .5f, 0));
        var world = new VehicleContact(new(8, 0, -15), -Vector3.UnitX, 0, 0, staticObstacle: true);
        var requests = Enumerable.Range(1, 3).Select(id => new VehicleStepRequest((ulong)id, Input,
            new(new(new((id - 1) * 3, 0, 0), Quaternion.Identity, observed.LinearVelocity, observed.AngularVelocity), Vector3.Zero,
                id == 3 ? [world] : new[] { Pair((ulong)id + 1, -Vector3.UnitX, new(1.5f, 0, 0)) }))).ToArray();
        var result = VehicleContactBatch.Resolve(requests, id => Previous(id, new(Vector3.Zero, Quaternion.Identity, new((float)id * 5, 0, -15), Vector3.Zero)), _ => Tuning);
        for (int i = 0; i < 3; i++) { Assert.That(result[i].Observation.Physics, Is.EqualTo(requests[i].Observation.Physics)); }
    }

    [Test]
    public void FullNativeContactReportCannotRewindAnUnreportedWorldImpactOrPartner()
    {
        var requests = Enumerable.Range(1, 3).Select(id => new VehicleStepRequest((ulong)id, Input,
            new(new(new(id * 3, 0, 0), Quaternion.Identity, new(0, 0, -8), new(0, .5f, 0)), Vector3.Zero,
                id == 1 ? [Pair(2, -Vector3.UnitX, new(1.5f, 0, 0))] : Array.Empty<VehicleContact>()))).ToArray();
        var result = VehicleContactBatch.Resolve(requests,
            id => Previous(id, new(Vector3.Zero, Quaternion.Identity, new(10, 0, -15), Vector3.Zero)), _ => Tuning, contactsComplete: false);
        for (int i = 0; i < requests.Length; i++) { Assert.That(result[i].Observation.Physics, Is.EqualTo(requests[i].Observation.Physics)); }
    }

    [Test]
    public void WorldOnlyBoundaryIsUnchangedAndDoesNotDisableAnUnconnectedPitPair()
    {
        var world = new VehiclePhysicsState(new(100, 0, 0), Quaternion.Identity, new(0, 0, -8), new(0, .7f, 0));
        var first = new VehiclePhysicsState(new(-2.8f, 0, 3.4f), Quaternion.Identity, new(6, 0, -15), Vector3.Zero);
        var second = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new(0, 0, -15), Vector3.Zero);
        var point = new Vector3(-1.2f, 0, 1.8f);
        var requests = new[]
        {
            new VehicleStepRequest(1, Input, new(world, Vector3.UnitY, [new(Vector3.Zero, Vector3.UnitY, 0, 0, terrain: true)])),
            new VehicleStepRequest(2, Input, new(new(first.Position, first.Orientation, Vector3.Zero, Vector3.Zero), Vector3.Zero, [Pair(3, -Vector3.UnitX, point - first.Position)])),
            new VehicleStepRequest(3, Input, new(new(second.Position, second.Orientation, Vector3.Zero, Vector3.Zero), Vector3.Zero))
        };
        var result = VehicleContactBatch.Resolve(requests, id => Previous(id, id == 2 ? first : second), _ => Tuning);
        var expected = VehicleCollision.ResolvePair(first, Tuning, second, Tuning, -Vector3.UnitX, point);
        Assert.That(result[0].Observation, Is.SameAs(requests[0].Observation));
        Assert.That(result[1].Observation.Physics, Is.EqualTo(expected.First));
        Assert.That(result[2].Observation.Physics, Is.EqualTo(expected.Second));
        Assert.That(expected.Second.AngularVelocity.Y, Is.GreaterThan(.5f));
    }
}
