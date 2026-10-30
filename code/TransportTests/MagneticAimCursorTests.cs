using System.Numerics;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class MagneticAimCursorTests
{
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void RearmingRequiresPlayerReturnTravelRatherThanTargetMotionOrMomentaryReversal(int fps)
    {
        float travel = 0;
        float dt = 1f / fps;
        for (int i = 0; i < fps * 5; i++)
            travel = MagneticAimCursor.ReturnTravel(travel, Vector2.Zero, new(-.1f, 0), Vector2.UnitX, dt);
        Assert.That(travel, Is.Zero);
        travel = MagneticAimCursor.ReturnTravel(travel, new(-.001f, 0), new(-.1f, 0), Vector2.UnitX, dt);
        Assert.That(travel, Is.LessThan(.012f));
        for (int i = 0; i < fps; i++)
            travel = MagneticAimCursor.ReturnTravel(travel, new(.1f * dt, 0), new(-.1f, 0), Vector2.UnitX, dt);
        Assert.That(travel, Is.LessThan(.00001f));
        for (int i = 0; i < fps / 2; i++)
            travel = MagneticAimCursor.ReturnTravel(travel, new(-.1f * dt, 0), new(-.1f, 0), Vector2.UnitX, dt);
        Assert.That(travel, Is.GreaterThan(.012f));
    }

    [TestCase(30, false)]
    [TestCase(60, false)]
    [TestCase(144, false)]
    [TestCase(30, true)]
    [TestCase(60, true)]
    [TestCase(144, true)]
    public void AcquiresContinuouslyTracksAndAllowsDirectionalDeparture(int fps, bool controller)
    {
        var cursor = new MagneticAimCursor();
        Vector2 target = new(.04f, .01f);
        cursor.Begin(target);
        float dt = 1f / fps;
        for (int i = 0; i < fps * 2; i++)
        {
            Vector2 before = cursor.Offset;
            cursor.Advance(target, default, controller, controller ? 24 : 18, dt);
            Assert.That(Vector2.Distance(before, cursor.Offset), Is.LessThan(.42f * dt));
        }
        Assert.That(Vector2.Distance(cursor.Offset, target), Is.LessThan(.001f));
        for (int i = 0; i < fps; i++)
        {
            target.X += .03f * dt;
            cursor.Advance(target, new(i % 2 == 0 ? .001f : -.001f, 0), controller, 24, dt);
            Assert.That(cursor.Released, Is.False);
        }
        Assert.That(Vector2.Distance(cursor.Offset, target), Is.LessThan(.002f));
        for (int i = 0; i < fps; i++)
            cursor.Advance(target, new(.15f * dt, 0), controller, 24, dt);
        Assert.That(cursor.Released, Is.True);
        Vector2 released = cursor.Offset;
        cursor.End();
        Assert.That(cursor.Offset, Is.EqualTo(released), "Release must not teleport the visible cursor");
        cursor.Recover(dt);
        Assert.That(cursor.Offset.Length(), Is.LessThan(released.Length()));
    }

    [Test]
    public void ControllerAcquisitionReachesDistantMissesOutsideUnchangedMouseEnvelope()
    {
        float cone = MathF.PI / 36;
        foreach (float radius in new[] { 0f, .01f, .1f })
        {
            float mouse = CameraAimAttraction.AcquisitionMargin(radius, cone);
            Assert.That(CameraAimAttraction.AcquisitionMargin(radius, cone, false), Is.EqualTo(mouse));
            Assert.That(CameraAimAttraction.AcquisitionMargin(radius, cone, true), Is.GreaterThan(mouse));
        }
        Assert.That(CameraAimAttraction.AcquisitionMargin(.01f, cone, true), Is.GreaterThan(MathF.PI / 30));
        Assert.That(CameraAimAttraction.AcquisitionMargin(.01f, 0, true), Is.Zero);
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void ModerateStickCanPassThroughAcquisitionTowardAnotherTarget(int fps)
    {
        var cursor = new MagneticAimCursor();
        cursor.Begin(new(.12f, 0));
        float dt = 1f / fps;
        float maximumStep = 0;
        for (int i = 0; i < fps && !cursor.Released; i++)
        {
            var before = cursor.Offset;
            cursor.Advance(new(.12f, 0), new(.15f * dt, 0), true, 24, dt);
            maximumStep = Math.Max(maximumStep, Vector2.Distance(before, cursor.Offset));
        }
        Assert.That(cursor.Released, Is.True, "Approach must not indefinitely cancel directional switch intent");
        Assert.That(maximumStep, Is.LessThanOrEqualTo(MathF.PI / 5 * dt + .00001f));
        Assert.That(cursor.Retention, Is.Zero);
    }

    [Test]
    public void DisabledPullAndResetNeverManufactureAnAimCorrection()
    {
        var cursor = new MagneticAimCursor();
        cursor.Begin(new(.05f, 0));
        for (int i = 0; i < 120; i++) cursor.Advance(new(.05f + i * .001f, 0), default, false, 0, 1f / 60);
        Assert.That(cursor.Offset, Is.EqualTo(Vector2.Zero));
        cursor.Reset();
        Assert.That(cursor.Retention, Is.Zero);
        Assert.That(cursor.Released, Is.False);
    }
}
