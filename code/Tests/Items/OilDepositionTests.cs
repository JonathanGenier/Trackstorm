using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class OilDepositionTests
{
    private static OilPatch Activation() => new(1, 2, new(0, 10, 0), Vector3.UnitY, .65f)
    { Pour = new() { Life = 1, StartTick = 1 }, ExpiresAtTick = 1801 };

    [Test]
    public void AirbornePourIsOneHazardWithFortyRearwardGravityParcelsAndNoPhantomCoverage()
    {
        var patch = Activation();
        var pose = new VehiclePhysicsState(new(0, 10, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        for (ulong tick = 1; tick <= 120; tick++)
        {
            patch = OilDeposition.Advance(patch, tick, pose, null);
            patch.Validate();
            Assert.That(patch.Pour!.Deposited, Is.False);
            Assert.That(patch.Pour.Drops.Drops.All(d => d.Velocity.Z > 0 && d.Velocity.Y < 0), Is.True);
            Assert.That(patch.Id, Is.EqualTo(1));
        }
        Assert.That(patch.Pour!.Emitted, Is.EqualTo(40));
        Assert.That(patch.ExpiresAtTick, Is.EqualTo(1801));
        for (ulong tick = 121; tick <= 360; tick++) { patch = OilDeposition.Advance(patch, tick, pose, null); }
        Assert.That(patch.Pour!.Emitted, Is.EqualTo(40));
        Assert.That(patch.Pour.Drops.Length, Is.Zero);
    }

    [TestCase(0f)]
    [TestCase(.4f)]
    [TestCase(1.5707963f)]
    public void ImpactGeometryAndOrganicTireCoverageUseTheActualSurface(float slope)
    {
        var normal = Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, slope));
        var patch = Activation();
        var pose = new VehiclePhysicsState(new(0, 10, 0), Quaternion.Identity, new(2, 0, -12), Vector3.Zero);
        patch = OilDeposition.Advance(patch, 1, pose, (from, to, r) => new(Vector3.Lerp(from, to, .5f), normal, r));
        Assert.That(patch.Pour!.Deposited, Is.True);
        Assert.That(patch.Normal, Is.EqualTo(normal));
        Assert.That(patch.Position.Y, Is.GreaterThan(12), "Never invent a patch under the airborne car.");
        var segment = new OilSegment(patch.Position, patch.Position, normal, patch.Radius);
        Assert.That(segment.Contains(patch.Position), Is.True);
        Assert.That(segment.Contains(patch.Position + normal * .09f), Is.False);
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(normal, Math.Abs(normal.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX));
        float edge = segment.EdgeRadius(0);
        Assert.That(segment.Contains(patch.Position + tangent * (edge - .001f)), Is.True);
        Assert.That(segment.Contains(patch.Position + tangent * (edge + .001f)), Is.False);
    }

    [Test]
    public void UnsupportedImpactsRetireParcelsAndTurningSpillsRemainBounded()
    {
        var patch = Activation();
        for (ulong tick = 1; tick <= 600; tick++)
        {
            var pose = new VehiclePhysicsState(new(tick * .1f, 10, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, tick * .01f), new(12, 0, -20), Vector3.Zero);
            patch = OilDeposition.Advance(patch, tick, pose, (from, to, r) => new(to, Vector3.UnitY, tick % 2 == 0 ? r : 0));
            patch.Validate();
            Assert.That(patch.AdditionalSegments.Segments.Count, Is.LessThanOrEqualTo(63));
            Assert.That(patch.Pour!.Drops.Length, Is.LessThanOrEqualTo(48));
        }
        Assert.That(patch.AdditionalSegments.Segments.Count, Is.GreaterThan(10));
        Assert.That(patch.Pour!.Drops.Length, Is.Zero);
        Assert.That(patch.ExpiresAtTick, Is.EqualTo(1801));
    }

    [Test]
    public void FortyEightDelayedImpactsReserveGeometryForEveryParcel()
    {
        var patch = Activation() with { Pour = new() { Life = 1, StartTick = 1, DurationTicks = 96, IntervalTicks = 2, Gravity = 1 } };
        var pose = new VehiclePhysicsState(new(0, 10, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        for (ulong tick = 1; tick <= 400; tick++)
        {
            patch = OilDeposition.Advance(patch, tick, pose, (from, to, radius) => tick > 96 ? new(to, Vector3.UnitY, radius) : null);
        }
        Assert.That(patch.Pour!.Emitted, Is.EqualTo(48));
        Assert.That(patch.AdditionalSegments.Segments.Count, Is.EqualTo(63), "One primary, 47 remaining impact pieces and sixteen satellites.");
        Assert.That(patch.AdditionalSegments.Segments.Count(s => s.Radius > .2f), Is.EqualTo(47), "Every remaining parcel deposits even when all hit after emission finishes.");
        Assert.That(patch.Pour.Drops.Length, Is.Zero);
    }

    [Test]
    public void SpringRemainsBoundedUnderBrakingCorneringAndLandingDiscontinuities()
    {
        Vector3 offset = Vector3.Zero, velocity = Vector3.Zero;
        for (int i = 0; i < 6000; i++)
        {
            (offset, velocity) = OilPour.Spring(offset, velocity, new Vector3(i % 2 == 0 ? 10000 : -10000, i % 60 == 0 ? 9000 : 0, 3000), 100, 1, .5f);
            Assert.That(offset.Length(), Is.LessThanOrEqualTo(.45001f));
            Assert.That(velocity.Length(), Is.LessThanOrEqualTo(5.0001f));
        }
    }
}
