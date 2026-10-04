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
        for (int i = 0; i < fps; i++) motion.Advance(1f / fps, false, .3f, 1);
        Assert.That(motion.Pullback, Is.Zero);
        float previous = 0;
        for (int i = 0; i < fps * 2; i++)
        {
            motion.Advance(1f / fps, false, 2, 1);
            Assert.That(motion.Pullback, Is.InRange(previous, previous + .5f));
            previous = motion.Pullback;
        }
        Assert.That(motion.Pullback, Is.EqualTo(3.6f).Within(.002));
        motion.Advance(1f / fps, true, 0, 1);
        Assert.That(motion.Pullback, Is.GreaterThan(2.9f));
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
        for (int i = 0; i < 240; i++) motion.Advance(1f / 60, false, 2, strength);
        Assert.That(motion.Pullback, Is.EqualTo(3.6f * strength).Within(.001));
        float previous = motion.Pullback;
        motion.Advance(1f / 60, false, 2, 0);
        Assert.That(motion.Pullback, Is.InRange(previous * .8f, previous));
        motion.Reset();
        Assert.That(motion.Pullback, Is.Zero);
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void RepeatedSmallHopsStayCloseAndLandingNeverContinuesExpansion(int fps)
    {
        var motion = new AerialCameraMotion();
        for (int hop = 0; hop < 8; hop++)
        {
            for (int frame = 1; frame <= fps / 2; frame++)
                motion.Advance(1f / fps, false, frame / (float)fps, 1.5f);
            Assert.That(motion.Pullback, Is.LessThan(.15f), "Half-second hops at maximum strength should barely change distance");
            float landed = motion.Pullback;
            for (int frame = 0; frame < fps / 2; frame++)
            {
                motion.Advance(1f / fps, true, 0, 1.5f, recoveringHeading: frame < fps / 10);
                Assert.That(motion.Pullback, Is.LessThanOrEqualTo(landed));
                landed = motion.Pullback;
            }
        }
        for (int frame = 1; frame <= fps * 3; frame++)
            motion.Advance(1f / fps, false, frame / (float)fps, 1);
        Assert.That(motion.Pullback, Is.GreaterThan(3.5f), "Long jumps still gain the full aerial view");
        float expanded = motion.Pullback;
        motion.Advance(1f / fps, true, 0, 1, recoveringHeading: true);
        Assert.That(motion.Pullback, Is.EqualTo(expanded), "Backward landing retains earned room without more expansion");
    }
}
