using System.Numerics;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class CameraAimAttractionTests
{
    [Test]
    public void AcquisitionPaddingHelpsSmallDistantBodiesWithoutWideningIntermediateOrCloseMargins()
    {
        float cone = MathF.PI / 36;
        Assert.That(CameraAimAttraction.AcquisitionMargin(.1f, cone), Is.EqualTo(cone * .5f));
        Assert.That(CameraAimAttraction.AcquisitionMargin(.05f, cone), Is.EqualTo(cone * .5f));
        Assert.That(CameraAimAttraction.AcquisitionMargin(.01f, cone), Is.InRange(cone * .8f, cone));
        Assert.That(CameraAimAttraction.AcquisitionMargin(0, cone), Is.EqualTo(cone));
        Assert.That(CameraAimAttraction.AcquisitionMargin(.01f, 0), Is.Zero);
    }

    [Test]
    public void TrackingCrossesTheWorldHeadingSeamWithoutTurningTheLongWay()
    {
        var result = CameraAimAttraction.Motion(new(MathF.PI - .01f, 0), new(-MathF.PI + .01f, .01f), 1f / 30);
        Assert.That(result.X, Is.EqualTo(.02f).Within(.00001f));
        Assert.That(result.Y, Is.EqualTo(.01f).Within(.00001f));
        Assert.That(CameraAimAttraction.Motion(Vector2.Zero, new(2, 1), 1f / 60).Length(), Is.EqualTo(MathF.PI / 60).Within(.00001f));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void PullConvergesWithoutInputAndIsBoundedAtEveryFrameRate(int fps)
    {
        Vector2 error = new(.05f, -.03f);
        for (int i = 0; i < fps; i++)
        {
            var correction = CameraAimAttraction.Pull(error, 18, 1f / fps);
            Assert.That(correction.Length(), Is.LessThanOrEqualTo(18 * MathF.PI / 180 / fps + .00001f));
            Assert.That(Vector2.Dot(correction, error), Is.GreaterThanOrEqualTo(0));
            error -= correction;
        }
        Assert.That(error.Length(), Is.LessThan(.0001f));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void FineCorrectionsRetainAndAccumulatedSweepsReleaseIndependentlyOfFrameRate(int fps)
    {
        float dt = 1f / fps, cone = MathF.PI / 36;
        Vector2 fine = CameraAimAttraction.Gesture(Vector2.Zero, new(1.5f, 0), Vector2.Zero, dt);
        Assert.That(CameraAimAttraction.Breakaway(new(-.027f, 0), fine, new(.0045f, 0), cone), Is.False,
            "A small correction remains fine input even outside a distant projected body");
        Vector2 gesture = Vector2.Zero;
        for (int i = 0; i < Math.Ceiling(.1f * fps); i++)
        { gesture = CameraAimAttraction.Gesture(gesture, new(300 * dt, 0), Vector2.Zero, dt); }
        Assert.That(CameraAimAttraction.Breakaway(new(-.01f, 0), gesture, new(.001f, 0), cone), Is.True,
            "Physical sweep releases promptly even at low view sensitivity");
        Assert.That(CameraAimAttraction.Breakaway(new(.02f, 0), gesture, new(.001f, 0), cone), Is.False,
            "Input toward an acquired car is not outward departure");
        Assert.That(CameraAimAttraction.Breakaway(new(-.06f, 0), fine, new(.001f, 0), cone), Is.True,
            "Slow accumulated outward placement eventually releases");
        foreach (float stick in new[] { .3f, 1f })
        {
            gesture = Vector2.Zero;
            for (int i = 0; i < Math.Ceiling(.1f * fps); i++)
            { gesture = CameraAimAttraction.Gesture(gesture, Vector2.Zero, new(stick, 0), dt); }
            Assert.That(CameraAimAttraction.Breakaway(new(-.01f, 0), gesture, new(.001f, 0), cone), Is.EqualTo(stick == 1));
        }
    }
}
