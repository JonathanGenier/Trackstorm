using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class MissileTerrainTests
{
    private static readonly MissileTerrainConfiguration Tuning = new();
    private static MissileState Shot(float y = 1, float pitch = 0) => new(1, 1, new(0, y, 0),
        Vector3.Transform(-Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch * MathF.PI / 180)) * 120, 300);

    private static MissileTerrainSample? Plane(Vector3 from, Vector3 to, float slope = 0, float level = 0)
    {
        float height = level - from.Z * slope;
        return from.Y >= height && to.Y <= height ? new(new(from.X, height, from.Z), Vector3.Normalize(new Vector3(0, 1, slope))) : null;
    }
    private static MissileState Fly(MissileState shot, Func<Vector3, Vector3, MissileTerrainSample?> terrain, MissileTerrainConfiguration? tuning = null)
    {
        var velocity = MissileFlight.Correct(shot, tuning ?? Tuning, terrain);
        Assert.That(velocity.Length(), Is.EqualTo(shot.Velocity.Length()).Within(0.001));
        Assert.That(Math.Abs(Pitch(velocity) - Pitch(shot.Velocity)), Is.LessThanOrEqualTo((tuning ?? Tuning).TurnRate / 60 + 0.001));
        return shot with { Position = shot.Position + velocity / 60, Velocity = velocity, RemainingTicks = shot.RemainingTicks - 1 };
    }
    private static float Pitch(Vector3 velocity) => MathF.Atan2(velocity.Y, new Vector2(velocity.X, velocity.Z).Length()) * 180 / MathF.PI;

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void NearbyGroundConvergesGraduallyWithoutChangingHeading(float height)
    {
        var shot = Shot(height);
        for (int i = 0; i < 180; i++) { shot = Fly(shot, (a, b) => Plane(a, b)); }
        Assert.That(shot.Position.Y, Is.EqualTo(1).Within(0.04));
        Assert.That(shot.Velocity.X, Is.Zero);
    }

    [TestCase(-80)]
    [TestCase(-60)]
    [TestCase(60)]
    [TestCase(80)]
    public void SteepIntentRemainsStraightWithoutFeasibleForwardSupport(float angle)
    {
        var shot = Shot(1, angle);
        Assert.That(MissileFlight.Correct(shot, Tuning, (a, b) => Plane(a, b)), Is.EqualTo(shot.Velocity));
    }

    [Test]
    public void ReturningNearbyGroundReengagesGraduallyAfterMissingSupport()
    {
        var shot = Shot(2);
        var committed = shot.Velocity;
        for (int i = 0; i < 20; i++) { shot = Fly(shot, (_, _) => null); }
        Assert.That(shot.Velocity, Is.EqualTo(committed));
        shot = Fly(shot, (a, b) => Plane(a, b));
        Assert.That(shot.Velocity.Y, Is.LessThan(0));
        var reacquired = shot.Velocity;
        for (int i = 0; i < 10; i++) { shot = Fly(shot, (_, _) => null); }
        Assert.That(shot.Velocity, Is.EqualTo(reacquired));
        for (int i = 0; i < 180; i++) { shot = Fly(shot, (a, b) => Plane(a, b)); }
        Assert.That(shot.Position.Y, Is.EqualTo(1).Within(0.04));
    }

    [Test]
    public void DistantGroundCannotAttractAnAirborneMissile()
    {
        var shot = Shot(20);
        for (int i = 0; i < 120; i++) { shot = Fly(shot, (a, b) => Plane(a, b)); }
        Assert.That(shot.Position.Y, Is.EqualTo(20));
        Assert.That(shot.Velocity, Is.EqualTo(Shot(20).Velocity));
    }

    [TestCase(5)]
    [TestCase(10)]
    [TestCase(-5)]
    [TestCase(-10)]
    public void SuitableInclinesAndDeclinesStayAtCarHitClearance(float angle)
    {
        float slope = MathF.Tan(angle * MathF.PI / 180);
        var shot = Shot(1, angle);
        for (int i = 0; i < 180; i++)
        {
            shot = Fly(shot, (a, b) => Plane(a, b, slope));
            Assert.That(shot.Position.Y + shot.Position.Z * slope, Is.InRange(0.8f, 1.2f));
        }
    }

    [Test]
    public void UpcomingRampIsDetectedBeforeContactWithoutSnapping()
    {
        var shot = Shot();
        float slope = MathF.Tan(10 * MathF.PI / 180);
        for (int i = 0; i < 100; i++)
        {
            shot = Fly(shot, (a, b) => a.Z > -35 ? Plane(a, b) : Plane(a, b, slope, -35 * slope));
            float ground = Math.Max(0, (-shot.Position.Z - 35) * slope);
            Assert.That(shot.Position.Y - ground, Is.InRange(0.3f, 3.0f), $"tick={i}, position={shot.Position}");
        }
    }

    [Test]
    public void CliffNeverChasesLowerPlatformAndCanReacquireTwice()
    {
        var shot = Shot();
        for (int i = 0; i < 200; i++)
        {
            var previous = shot;
            shot = Fly(shot, (a, b) => Plane(a, b, level: a.Z is < -30 and > -100 or < -190 and > -260 ? -8 : 0));
            Assert.That(shot.Position.Y, Is.EqualTo(1).Within(0.001));
            Assert.That(shot.Velocity, Is.EqualTo(previous.Velocity));
        }
    }

    [Test]
    public void SmoothUnevenGroundAndCrossBankNeverAddYaw()
    {
        var shot = Shot();
        for (int i = 0; i < 240; i++)
        {
            shot = Fly(shot, (a, b) =>
            {
                float height = 0.35f * MathF.Sin(-a.Z / 35);
                var normal = Vector3.Normalize(new Vector3(0.2f, 1, 0.01f * MathF.Cos(-a.Z / 35)));
                return a.Y >= height && b.Y <= height ? new(new(a.X, height, a.Z), normal) : null;
            });
            Assert.That(shot.Position.Y - 0.35f * MathF.Sin(-shot.Position.Z / 35), Is.InRange(0.6f, 1.4f));
            Assert.That(shot.Velocity.X, Is.Zero);
        }
    }

    [Test]
    public void DownhillBeyondAFlatTabletopCannotPullFlightThroughTheDeck()
    {
        var shot = Shot(1.17f, 0.3f);
        for (int i = 0; i < 35; i++)
        {
            shot = Fly(shot, (a, b) =>
            {
                float d = -a.Z;
                float height = d < 35 ? 0 : d < 50 ? -(d - 35) * 0.423f : -6.345f;
                var normal = Vector3.Normalize(new Vector3(0, 1, d is > 35 and < 50 ? -0.423f : 0));
                return a.Y >= height && b.Y <= height ? new(new(a.X, height, a.Z), normal) : null;
            });
            if (-shot.Position.Z <= 35) { Assert.That(shot.Position.Y, Is.GreaterThan(0.8f), "Descend only when current support actually descends"); }
        }
    }

    [Test]
    public void AShortBankIsDetectedEvenWhenTheLongProbeIsBeyondItsEdge()
    {
        var shot = Shot(1.3f, 20);
        Vector3 original = shot.Velocity;
        shot = Fly(shot, (a, b) =>
        {
            float d = -a.Z;
            if (d > 18) { return null; }
            return Plane(a, b, MathF.Tan(35 * MathF.PI / 180));
        });
        Assert.That(Pitch(shot.Velocity), Is.GreaterThan(Pitch(original)));
        Assert.That(shot.Velocity.X, Is.Zero);
    }

    [Test]
    public void RisingSupportContinuesToGuideWhenAllForwardSamplesMiss()
    {
        var shot = Shot(1, 20);
        var velocity = MissileFlight.Correct(shot, Tuning, (a, b) => a.Z == 0 ? Plane(a, b, 0.7f) : null);
        Assert.That(Pitch(velocity), Is.GreaterThan(20));
        Assert.That(Pitch(velocity), Is.LessThanOrEqualTo(20 + Tuning.TurnRate / 60 + 0.001f));
        Assert.That(MissileFlight.Correct(shot, Tuning, (_, _) => null), Is.EqualTo(shot.Velocity));
    }

    [Test]
    public void WallsInvalidNormalsAndMissingSamplesLeaveVelocityCommitted()
    {
        var shot = Shot();
        foreach (var normal in new[] { Vector3.UnitX, Vector3.Zero, new Vector3(float.NaN), Vector3.Normalize(new Vector3(0, 1, 3)) })
        { Assert.That(MissileFlight.Correct(shot, Tuning, (a, _) => new(new(a.X, 0, a.Z), normal)), Is.EqualTo(shot.Velocity)); }
        Assert.That(MissileFlight.Correct(shot, Tuning, (_, _) => null), Is.EqualTo(shot.Velocity));
    }

    [Test]
    public void QueriesAreBoundedAndDisabledCorrectionDoesNotQuery()
    {
        int queries = 0;
        var shot = Shot(0.5f);
        var velocity = MissileFlight.Correct(shot, Tuning, (a, b) =>
        {
            queries++;
            Assert.That(Vector3.Distance(a, b), Is.LessThanOrEqualTo(2 * Tuning.DetectionRange));
            return Plane(a, b);
        });
        Assert.That(queries, Is.LessThanOrEqualTo(4));
        Assert.That(velocity.Y, Is.GreaterThan(0));
        Assert.That(MissileFlight.Correct(shot, Tuning with { TurnRate = 0 }, (_, _) => throw new Exception()), Is.EqualTo(shot.Velocity));
    }

    [Test]
    public void RecoveryUsesOnlySerializedPositionVelocityAndRemainingLifetime()
    {
        var host = new HostVehicleSession(99);
        var shot = Shot(1.7f);
        for (int i = 0; i < 30; i++) { shot = Fly(shot, (a, b) => Plane(a, b)); }
        var publication = new ItemPublication(1, host.Snapshot(), [], [shot], []);
        var restored = ItemCodec.DecodeState(ItemCodec.EncodeState(publication)).Missiles.Single();
        for (int i = 0; i < 40; i++)
        {
            shot = Fly(shot, (a, b) => Plane(a, b)); restored = Fly(restored, (a, b) => Plane(a, b));
            Assert.That(restored, Is.EqualTo(shot));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CleanupNeverInvokesDamageForMisses(bool worldBound)
    {
        var host = new HostVehicleSession(99, new ItemConfiguration { MissileLifetimeTicks = 300, MissileLifetimeSeconds = 0.05f, MissileWorldLimit = 256 });
        host.Items.Grant(host.World, 1, HeldItem.Missile);
        MissileTestPreparation.Wait(host);
        host.UseItem(0, 99, 1, host.Items.Slots.Single().Token);
        int queries = 0;
        VehicleObservation Observe(VehicleSnapshot state) => new(new VehiclePhysicsState(new Vector3(worldBound ? 300 : 0, 50, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY);
        for (int i = 0; i < 3; i++)
        {
            host.Step(default, Observe, (_, _) => { queries++; return null; });
            Assert.That(host.Items.Events.Any(e => e.Impact), Is.False);
        }
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(queries, Is.EqualTo(worldBound ? 0 : 3));
    }
}
