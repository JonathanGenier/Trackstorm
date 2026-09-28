using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class BoostCameraMotionTests
{
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void BoostIsBoundedSmoothAndReturnsToRoadSpeedFeedback(int fps)
    {
        var motion = new BoostCameraMotion();
        for (int i = 0; i < fps * 2; i++) { motion.Advance(1f / fps, false, 60); }
        float ordinary = motion.FovExpansion;
        motion.Advance(1f / fps, true, 60);
        Assert.That(motion.FovExpansion, Is.InRange(ordinary, ordinary + 3));
        for (int i = 0; i < fps * 2; i++) { motion.Advance(1f / fps, true, 60); }
        Assert.That(motion.FovExpansion, Is.EqualTo(8).Within(.01));
        Assert.That(motion.PullBack, Is.InRange(.54f, .55f));
        for (int i = 0; i < fps * 3; i++) { motion.Advance(1f / fps, false, 60); }
        Assert.That(motion.FovExpansion, Is.EqualTo(ordinary).Within(.01));
        Assert.That(motion.StreakStrength, Is.EqualTo(.04f).Within(.001));
    }

    [Test]
    public void RepeatedUseCannotStackAndResetClearsAllFeedback()
    {
        var motion = new BoostCameraMotion();
        for (int i = 0; i < 600; i++)
        {
            motion.Advance(1f / 60, i % 12 < 6, 200);
            Assert.That(motion.FovExpansion, Is.InRange(0, 8));
            Assert.That(motion.PullBack, Is.InRange(0, .55f));
        }
        motion.Reset();
        Assert.That(motion.FovExpansion + motion.PullBack + motion.StreakStrength, Is.Zero);
        for (int i = 0; i < 120; i++) { motion.Advance(1f / 60, true, 0); }
        Assert.That(motion.FovExpansion, Is.InRange(3.14f, 3.16f));
        Assert.That(motion.StreakStrength, Is.Zero, "Stationary Boost must not invent travelling scenery.");
    }
}
