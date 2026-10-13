using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Approved shortened burst retains the previous dose and comparable supported coverage.</summary>
[TestFixture]
internal sealed class OilBurstCoverageTests
{
    [TestCase(0f)]
    [TestCase(10f)]
    [TestCase(20f)]
    public void OneSecondDoseRetainsComparableStraightRoadCoverage(float speed)
    {
        var patch = new OilPatch(1, 1, Vector3.Zero, Vector3.UnitY, 1.3f) { Pour = new() { Life = 1, StartTick = 1, PreviousVelocity = new(0, 0, -speed) }, ExpiresAtTick = 1801 };
        for (ulong tick = 1; tick <= 300; tick++)
        {
            var pose = new VehiclePhysicsState(new(0, 1.145f, -speed * (tick - 1) / 60), Quaternion.Identity, new(0, 0, -speed), Vector3.Zero);
            patch = OilDeposition.Advance(patch, tick, pose, Ground);
        }
        var current = patch.AdditionalSegments.Segments.Prepend(new OilSegment(patch.Position, patch.Position, patch.Normal, patch.Radius)).ToArray();
        var previous = PreviousTwoSecondCoverage(speed);
        float oldArea = Area(previous, true), newArea = Area(current, false);
        TestContext.Out.WriteLine($"{speed} m/s: previous={oldArea:F2} m2, one-second={newArea:F2} m2, ratio={newArea / oldArea:F3}; forty parcels.");
        Assert.That(patch.Pour!.Emitted, Is.EqualTo(40));
        Assert.That(newArea / oldArea, Is.InRange(.8f, 1.5f), "Shortening the burst must not halve effective coverage or multiply its area.");
    }

    private static OilImpact? Ground(Vector3 from, Vector3 to, float radius) => from.Y >= 0 && to.Y <= 0
        ? new(Vector3.Lerp(from, to, from.Y / (from.Y - to.Y)), Vector3.UnitY, radius) : null;

    // Frozen Round 2 reference: forty parcels/120 ticks, 12 m/s, .22 downward,
    // 1.1 m support, previous organic edge. This is deliberately independent
    // of the revised emission and edge implementation being measured.
    private static OilSegment[] PreviousTwoSecondCoverage(float speed)
    {
        var flight = new List<OilDrop>(); var pieces = new List<OilSegment>(); int emitted = 0;
        for (int tick = 0; tick < 300; tick++)
        {
            if (tick < 120 && tick % 3 == 0)
            {
                flight.Add(new(new(0, 4.020f, 2.070f - speed * tick / 60), new Vector3(0, 0, -speed) + Vector3.Normalize(new(0, -.22f, 1)) * 12, 180)); emitted++;
            }
            for (int i = flight.Count - 1; i >= 0; i--)
            {
                var drop = flight[i]; Vector3 velocity = drop.Velocity - Vector3.UnitY * (9.81f / 60);
                Vector3 end = drop.Position + (drop.Velocity + velocity) / 120;
                var hit = Ground(drop.Position, end, 1.1f);
                if (hit is not null)
                {
                    float radius = 1.1f * (.82f + .16f * MathF.Sin(emitted * 2.399f + drop.RemainingTicks));
                    Vector3 previous = pieces.Count > 0 ? pieces[^1].End : hit.Position;
                    pieces.Add(new(Vector3.Distance(previous, hit.Position) < radius * 2 ? previous : hit.Position, hit.Position, Vector3.UnitY, radius));
                    flight.RemoveAt(i);
                }
                else { flight[i] = new(end, velocity, drop.RemainingTicks - 1); }
            }
        }
        return pieces.ToArray();
    }

    private static float Area(OilSegment[] pieces, bool previous)
    {
        float lowZ = pieces.Min(p => Math.Min(p.Start.Z, p.End.Z) - p.Radius), highZ = pieces.Max(p => Math.Max(p.Start.Z, p.End.Z) + p.Radius);
        int hits = 0;
        for (float x = -3; x <= 3; x += .1f)
        {
            for (float z = lowZ; z <= highZ; z += .1f)
            {
                bool Contains(OilSegment piece)
                {
                    Vector3 point = new(x, 0, z);
                    if (!previous) { return piece.Contains(point); }
                    Vector3 along = piece.End - piece.Start;
                    float t = along.LengthSquared() > 0 ? Math.Clamp(Vector3.Dot(point - piece.Start, along) / along.LengthSquared(), 0, 1) : 0;
                    Vector3 radial = point - piece.Start - along * t;
                    float angle = MathF.Atan2(-radial.Z, radial.X);
                    float radius = piece.Radius * (.89f + .075f * MathF.Sin(3 * angle + piece.Start.X) + .035f * MathF.Sin(7 * angle + piece.Start.Z));
                    return radial.LengthSquared() <= radius * radius;
                }
                if (pieces.Any(Contains)) { hits++; }
            }
        }
        return hits * .01f;
    }
}
