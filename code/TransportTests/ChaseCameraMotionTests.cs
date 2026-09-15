using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Pure Client camera controls tested without Godot or native devices.</summary>
[TestFixture]
internal sealed class ChaseCameraMotionTests
{
    /// <summary>Steering and manual contributions stay bounded even under sustained extreme inputs.</summary>
    /// <param name="sign">Direction of the requested look.</param>
    [TestCase(-1)]
    [TestCase(1)]
    public void CombinedLookRespectsLimits(int sign)
    {
        var motion = new ChaseCameraMotion();
        for (int i = 0; i < 600; i++)
        {
            Step(motion, sign * 4, sign * 100, sign * 65);
            Assert.That(Math.Abs(motion.LookAngle), Is.LessThanOrEqualTo(20));
            Assert.That(Math.Abs(motion.SteeringAngle), Is.LessThanOrEqualTo(10));
        }

        Assert.That(motion.LookAngle, Is.EqualTo(sign * 20).Within(0.001));
    }

    /// <summary>Release converges smoothly to steering, then to neutral without overshooting.</summary>
    [Test]
    public void ReleaseRecentersToSteering()
    {
        var motion = new ChaseCameraMotion();
        for (int i = 0; i < 180; i++)
        {
            Step(motion, 1, 1);
        }

        float before = motion.LookAngle;
        Step(motion, 1);
        Assert.That(Math.Abs(motion.LookAngle - before), Is.LessThan(0.1));
        for (int i = 0; i < 600; i++)
        {
            Step(motion, 1);
        }

        Assert.That(motion.LookAngle, Is.EqualTo(10).Within(0.001));
        for (int i = 0; i < 600; i++)
        {
            Step(motion);
        }

        Assert.That(motion.LookAngle, Is.EqualTo(0).Within(0.001));
    }

    /// <summary>Repeated brushes stay neutral; meaningful impacts scale, coalesce and decay without bias.</summary>
    [Test]
    public void ShakeRejectsBrushesAndDecays()
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
        Assert.That(light, Is.GreaterThan(0));
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
        Assert.That(motion.LookAngle, Is.Zero);
    }

    /// <summary>Time-based controls behave consistently at low and high render rates.</summary>
    [Test]
    public void RenderRatesConvergeAndResetClearsMemory()
    {
        var samples = new List<float>();
        foreach (int fps in new[] { 30, 60, 144 })
        {
            var motion = new ChaseCameraMotion();
            for (int i = 0; i < fps * 2; i++)
            {
                Step(motion, -1, 0, 65, 1f / fps);
            }

            for (int i = 0; i < fps * 2; i++)
            {
                Step(motion, -1, 0, 0, 1f / fps);
            }

            samples.Add(motion.LookAngle);
            motion.Impulse(1);
            motion.Reset();
            Assert.That(motion.LookAngle, Is.Zero);
            Assert.That(motion.SteeringAngle, Is.Zero);
            Assert.That(motion.ShakeOffset, Is.Zero);
        }

        Assert.That(samples.Max() - samples.Min(), Is.LessThan(0.02));
    }

    /// <summary>Runtime tuning changes enforce smaller limits immediately.</summary>
    [Test]
    public void SmallerConfiguredLimitAppliesImmediately()
    {
        var motion = new ChaseCameraMotion();
        for (int i = 0; i < 300; i++)
        {
            Step(motion, 1, 100);
        }

        motion.Advance(1f / 60, 1, 100, 100, 3, 5, 7, 0.25f, 4, 7);
        Assert.That(motion.LookAngle, Is.LessThanOrEqualTo(5));
        Assert.That(motion.SteeringAngle, Is.LessThanOrEqualTo(3));
    }

    private static void Step(ChaseCameraMotion motion, float steering = 0, float mouse = 0, float stick = 0, float delta = 1f / 60) =>
        motion.Advance(delta, steering, mouse, stick, 10, 20, 7, 0.25f, 4, 7);
}
