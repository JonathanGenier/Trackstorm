using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class AerialCameraMotionTests
{
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void BriefHopsRemainCloseAndSustainedFlightBlendsAndRecovers(int fps)
    {
        var motion = new AerialCameraMotion();
        for (int i = 0; i < fps; i++) motion.Advance(1f / fps, false, .1f, 1);
        Assert.That(motion.Pullback, Is.Zero);
        float previous = 0;
        for (int i = 0; i < fps * 2; i++)
        {
            motion.Advance(1f / fps, false, 1, 1);
            Assert.That(motion.Pullback, Is.InRange(previous, previous + .5f));
            previous = motion.Pullback;
        }
        Assert.That(motion.Pullback, Is.EqualTo(3.6f).Within(.002));
        motion.Advance(1f / fps, true, 0, 1);
        Assert.That(motion.Pullback, Is.GreaterThan(3));
        for (int i = 0; i < fps * 2; i++) motion.Advance(1f / fps, true, 0, 1);
        Assert.That(motion.Pullback, Is.LessThan(.001));
        motion.Reset();
        Assert.That(motion.Amount, Is.Zero);
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(1.5f)]
    public void StrengthChangesAreSmoothAndBounded(float strength)
    {
        var motion = new AerialCameraMotion();
        for (int i = 0; i < 240; i++) motion.Advance(1f / 60, false, 1, strength);
        Assert.That(motion.Pullback, Is.EqualTo(3.6f * strength).Within(.001));
        float previous = motion.Pullback;
        motion.Advance(1f / 60, false, 1, 0);
        Assert.That(motion.Pullback, Is.InRange(previous * .8f, previous));
        motion.Reset();
        Assert.That(motion.Pullback, Is.Zero);
    }
}
