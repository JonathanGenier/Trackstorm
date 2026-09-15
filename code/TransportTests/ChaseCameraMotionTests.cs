using System.Numerics;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Deterministic positional camera response without native nodes or input devices.</summary>
[TestFixture]
internal sealed class ChaseCameraMotionTests
{
    /// <summary>Acceleration extends the chase distance; braking smoothly moves it forward.</summary>
    [Test]
    public void AccelerationAndBrakingHaveOppositeWeight()
    {
        var motion = new ChaseCameraMotion();
        motion.ObserveVelocity(new Vector3(0, 0, -10), 1);
        Step(motion);
        Assert.That(motion.Offset.Y, Is.InRange(0.001f, 0.1f));
        for (int i = 0; i < 120; i++)
        {
            Step(motion);
        }

        Assert.That(motion.Offset.Y, Is.EqualTo(0.45f).Within(0.001));
        float before = motion.Offset.Y;
        motion.ObserveVelocity(Vector3.Zero, 1);
        Step(motion);
        Assert.That(motion.Offset.Y, Is.LessThan(before).And.GreaterThan(0));
        for (int i = 0; i < 120; i++)
        {
            Step(motion);
        }

        Assert.That(motion.Offset.Y, Is.EqualTo(-0.45f).Within(0.001));
        motion.ObserveVelocity(Vector3.Zero, 1);
        for (int i = 0; i < 120; i++)
        {
            Step(motion);
        }

        Assert.That(motion.Offset.Length(), Is.LessThan(0.001));
    }

    /// <summary>Measured left/right acceleration moves the camera toward the outside of the turn.</summary>
    /// <param name="direction">Signed lateral acceleration direction.</param>
    [TestCase(-1)]
    [TestCase(1)]
    public void TurningAndSlipAreBounded(int direction)
    {
        var motion = new ChaseCameraMotion();
        motion.Reset(new Vector3(0, 0, -10));
        motion.ObserveVelocity(new Vector3(direction * 10, 0, -10), 1);
        for (int i = 0; i < 120; i++)
        {
            Step(motion);
        }

        Assert.That(motion.Offset.X * direction, Is.LessThan(-0.3f));
        motion.ObserveVelocity(new Vector3(direction * 1000, 0, -1000), 0.001f);
        for (int i = 0; i < 120; i++)
        {
            Step(motion);
            Assert.That(Math.Abs(motion.Offset.X), Is.LessThanOrEqualTo(0.4f));
            Assert.That(Math.Abs(motion.Offset.Y), Is.LessThanOrEqualTo(0.7f));
        }

        motion.ObserveVelocity(new Vector3(direction * 5, 0, 0), 1);
        motion.ObserveVelocity(new Vector3(direction * 5, 0, 0), 1);
        for (int i = 0; i < 120; i++)
        {
            Step(motion);
        }

        Assert.That(motion.Offset.X, Is.EqualTo(-direction * 0.075f).Within(0.001));
    }

    /// <summary>Physics samples produce equivalent motion at different render rates and headings.</summary>
    [Test]
    public void RenderRatesAndRotatedHeadingAgree()
    {
        var results = new List<Vector2>();
        foreach (int fps in new[] { 30, 60, 144 })
        {
            var motion = new ChaseCameraMotion();
            motion.ObserveVelocity(new Vector3(-10, 0, 0), 1);
            for (int i = 0; i < fps; i++)
            {
                Step(motion, 1f / fps, MathF.PI / 2);
            }

            results.Add(motion.Offset);
            Assert.That(motion.Offset.Y, Is.GreaterThan(0.4f));
            Assert.That(Math.Abs(motion.Offset.X), Is.LessThan(0.001));
        }

        Assert.That(Vector2.Distance(results[0], results[2]), Is.LessThan(0.00001));
    }

    /// <summary>Impacts coalesce, brushes remain neutral and feedback decays without shifting inertia.</summary>
    [Test]
    public void ShakeIsBoundedAndDecays()
    {
        var motion = new ChaseCameraMotion();
        for (int i = 0; i < 120; i++)
        {
            motion.Collision(3, 3, 0.55f);
            Step(motion);
        }

        Assert.That(motion.Shake, Is.Zero);
        motion.Collision(6, 3, 0.55f);
        float light = motion.Shake;
        for (int i = 0; i < 30; i++)
        {
            Step(motion);
        }

        motion.Collision(23, 3, 0.55f);
        Assert.That(motion.Shake, Is.GreaterThan(light));
        for (int i = 0; i < 100; i++)
        {
            motion.Impulse(0.65f);
        }

        Assert.That(motion.Shake, Is.EqualTo(0.65f));
        for (int i = 0; i < 600; i++)
        {
            Step(motion);
        }

        Assert.That(motion.ShakeOffset, Is.Zero);
        Assert.That(motion.Offset, Is.EqualTo(Vector2.Zero));
    }

    /// <summary>A new life starts with no artificial acceleration, even at a nonzero initial velocity.</summary>
    [Test]
    public void ResetClearsInertiaAndFeedback()
    {
        var motion = new ChaseCameraMotion();
        motion.Impulse(1);
        motion.ObserveVelocity(new Vector3(0, 0, -20), 1);
        Step(motion);
        motion.Reset(new Vector3(0, 0, -20));
        motion.ObserveVelocity(new Vector3(0, 0, -20), 1f / 60);
        Step(motion);
        Assert.That(motion.Offset, Is.EqualTo(Vector2.Zero));
        Assert.That(motion.ShakeOffset, Is.Zero);
    }

    private static void Step(ChaseCameraMotion motion, float delta = 1f / 60, float heading = 0) =>
        motion.Advance(delta, heading, 0.045f, 0.025f, 0.015f, 0.7f, 0.4f, 8, 7);
}
