using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

internal sealed class PitCollisionTests
{
    private static readonly Vector3 Point = new(-1.2f, 0, 1.8f);
    private static VehiclePhysicsState Target(float speed = 15) => new(Vector3.Zero, Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
    private static VehiclePhysicsState Striker(float closing, float speed = 15) => new(new(-2.8f, 0, 3.4f), Quaternion.Identity, new(closing, 0, -speed), Vector3.Zero);

    [TestCase(3000f, 3000f, 2.6f)]
    [TestCase(3000f, 4500f, 3.2f)]
    [TestCase(4500f, 3000f, 2.6f)]
    public void RearQuarterTransfersMomentumAndYawWithAMassDependentStrikerConsequence(float strikerMass, float targetMass, float wheelbase)
    {
        var a = Striker(8); var b = Target();
        var ac = new VehicleConfiguration { Mass = strikerMass };
        var bc = new VehicleConfiguration { Mass = targetMass, Wheelbase = wheelbase };
        var impact = VehicleCollision.ResolvePair(a, ac, b, bc, -Vector3.UnitX, Point);
        var baseline = VehicleCollision.ResolvePair(a, ac with { PitYawResponse = 0 }, b, bc with { PitYawResponse = 0 }, -Vector3.UnitX, Point);
        Assert.That(impact.Second.AngularVelocity.Y, Is.GreaterThan(baseline.Second.AngularVelocity.Y * 2));
        Assert.That(impact.Second.AngularVelocity.Y, Is.InRange(0.3f, bc.PitAngularLimit));
        Assert.That(impact.First.LinearVelocity.X, Is.LessThan(a.LinearVelocity.X - 1));
        Assert.That(Vector3.Distance(impact.First.LinearVelocity * strikerMass + impact.Second.LinearVelocity * targetMass,
            a.LinearVelocity * strikerMass + b.LinearVelocity * targetMass), Is.LessThan(0.01f));
        var closing = impact.First.LinearVelocity + Vector3.Cross(impact.First.AngularVelocity, Point - a.Position) -
            impact.Second.LinearVelocity - Vector3.Cross(impact.Second.AngularVelocity, Point - b.Position);
        Assert.That(Math.Abs(closing.X), Is.LessThan(0.00001f));
        var repeated = impact;
        for (int i = 0; i < 600; i++) { repeated = VehicleCollision.ResolvePair(repeated.First, ac, repeated.Second, bc, -Vector3.UnitX, Point); }
        Assert.That(Vector3.Distance(repeated.Second.AngularVelocity, impact.Second.AngularVelocity), Is.LessThan(0.00001f));
        Assert.That(Vector3.Distance(repeated.First.LinearVelocity, impact.First.LinearVelocity), Is.LessThan(0.00001f));
    }

    [Test]
    public void StrongerAndBetterPlacedHitsDisruptMoreWithoutUnboundedSpin()
    {
        float Yaw(float speed, Vector3 point) => VehicleCollision.ResolvePair(Striker(speed), new(), Target(), new(), -Vector3.UnitX, point).Second.AngularVelocity.Y;
        Assert.That(Yaw(8, Point), Is.GreaterThan(Yaw(3, Point)));
        Assert.That(Yaw(3, Point), Is.GreaterThan(Yaw(0.5f, Point)));
        Assert.That(Yaw(8, Point), Is.GreaterThan(Yaw(8, new(-1.2f, 0, 0.5f))));
        Assert.That(Yaw(60, Point), Is.LessThanOrEqualTo(new VehicleConfiguration().PitAngularLimit));
    }

    [TestCase(0f, 15f, 1.8f)]
    [TestCase(0.5f, 15f, 1.8f)]
    [TestCase(1.5f, 15f, 1.8f)]
    [TestCase(8f, 2f, 1.8f)]
    [TestCase(8f, 15f, 0f)]
    [TestCase(8f, 15f, -1.8f)]
    public void MatchingLightSlowMidSideAndFrontContactsKeepOrdinaryResponse(float closing, float speed, float offset)
    {
        var point = new Vector3(-1.2f, 0, offset);
        var a = Striker(closing, speed); var b = Target(speed); var tuning = new VehicleConfiguration();
        var actual = VehicleCollision.ResolvePair(a, tuning, b, tuning, -Vector3.UnitX, point);
        var ordinary = VehicleCollision.ResolvePair(a, tuning with { PitYawResponse = 0 }, b, tuning with { PitYawResponse = 0 }, -Vector3.UnitX, point);
        Assert.That(actual, Is.EqualTo(ordinary));
    }

    [Test]
    public void HeadOnAndRearFollowingContactsDoNotGainPitYaw()
    {
        var a = new VehiclePhysicsState(new(0, 0, 5), Quaternion.Identity, new(0, 0, -25), Vector3.Zero);
        var b = Target(); var tuning = new VehicleConfiguration();
        var actual = VehicleCollision.ResolvePair(a, tuning, b, tuning, Vector3.UnitZ, new(0.7f, 0, 2.5f));
        var ordinary = VehicleCollision.ResolvePair(a, tuning with { PitYawResponse = 0 }, b, tuning with { PitYawResponse = 0 }, Vector3.UnitZ, new(0.7f, 0, 2.5f));
        Assert.That(actual, Is.EqualTo(ordinary));
    }

    [Test]
    public void PairOrderingAndWorldHeadingDoNotChangeTheOutcome()
    {
        var a = Striker(8); var b = Target(); var tuning = new VehicleConfiguration();
        var actual = VehicleCollision.ResolvePair(a, tuning, b, tuning, -Vector3.UnitX, Point);
        var swapped = VehicleCollision.ResolvePair(b, tuning, a, tuning, Vector3.UnitX, Point);
        Assert.That(swapped, Is.EqualTo((actual.Second, actual.First)));
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.4f);
        Vector3 Rotate(Vector3 v) => Vector3.Transform(v, rotation);
        VehiclePhysicsState Turn(VehiclePhysicsState p) => new(Rotate(p.Position), rotation, Rotate(p.LinearVelocity), Rotate(p.AngularVelocity));
        var turned = VehicleCollision.ResolvePair(Turn(a), tuning, Turn(b), tuning, Rotate(-Vector3.UnitX), Rotate(Point));
        Assert.That(Vector3.Distance(turned.Second.AngularVelocity, Rotate(actual.Second.AngularVelocity)), Is.LessThan(0.00001f));
        Assert.That(Vector3.Distance(turned.First.LinearVelocity, Rotate(actual.First.LinearVelocity)), Is.LessThan(0.00001f));
    }

    [Test]
    public void BatchResolvesOneSidedAndDuplicateContactReportsOnlyOnce()
    {
        var a = Striker(8); var b = Target(); var tuning = new VehicleConfiguration();
        var first = new VehicleContact(a.LinearVelocity - b.LinearVelocity, -Vector3.UnitX, 0, 2, localPosition: Point - a.Position);
        var second = new VehicleContact(b.LinearVelocity - a.LinearVelocity, Vector3.UnitX, 0, 1, localPosition: Point - b.Position);
        var observations = new Dictionary<ulong, VehicleObservation>
        {
            [1] = new(a, Vector3.Zero, [first, first]), [2] = new(b, Vector3.Zero)
        };
        var oneSided = VehicleCollision.ResolveContacts(observations, _ => tuning);
        observations[2] = new(b, Vector3.Zero, [second, second]);
        var both = VehicleCollision.ResolveContacts(observations, _ => tuning);
        var expected = VehicleCollision.ResolvePair(a, tuning, b, tuning, -Vector3.UnitX, Point);
        Assert.That(oneSided[1].Physics, Is.EqualTo(expected.First));
        Assert.That(oneSided[2].Physics, Is.EqualTo(expected.Second));
        Assert.That(both[1].Physics, Is.EqualTo(expected.First));
        Assert.That(both[2].Physics, Is.EqualTo(expected.Second));
        Assert.That(observations[1].Physics, Is.EqualTo(a), "original observations remain untouched");
        Assert.That(both[1].Contacts, Is.EqualTo(observations[1].Contacts), "raw damage evidence is retained");
    }

    [Test]
    public void TuningUsesTheCompleteConfigurationAndRejectsRetiredWireLayout()
    {
        var edits = new Dictionary<string, double> { ["vehicle.pit_closing_speed"] = 2, ["vehicle.pit_yaw_response"] = 0.6,
            ["vehicle.pit_angular_limit"] = 1.1, ["vehicle.air_release_damping"] = 8 };
        Assert.That(GameplayOptions.TryApply(new(), edits, out var changed, out _), Is.True);
        byte[] wire = GameplayConfigurationCodec.Encode(1, new(1, changed));
        Assert.That(GameplayConfigurationCodec.Decode(wire).State.Configuration, Is.EqualTo(changed));
        wire[2] = 42;
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(wire));
        Assert.That(GameplayOptions.TryApply(changed, new Dictionary<string, double> { ["vehicle.pit_closing_speed"] = 0 }, out _, out _), Is.False);
        Assert.Throws<ArgumentException>(() => new VehicleConfiguration { AirReleaseDamping = float.NaN }.Validate());
    }
}
