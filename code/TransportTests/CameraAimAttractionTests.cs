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

    [Test]
    public void SmallErrorsRetainButDeliberateAwayInputReleasesBothDevices()
    {
        Vector2 error = new(.02f, 0);
        Assert.That(CameraAimAttraction.Breakaway(error, new(-1, 0), new(-.3f, 0), 1f / 60), Is.False);
        Assert.That(CameraAimAttraction.Breakaway(error, new(-8, 0), Vector2.Zero, 1f / 60), Is.True);
        Assert.That(CameraAimAttraction.Breakaway(error, Vector2.Zero, new(-.8f, 0), 1f / 60), Is.True);
        Assert.That(CameraAimAttraction.Breakaway(error, new(4, 0), new(.8f, 0), 1f / 60), Is.False);
        Assert.That(CameraAimAttraction.Breakaway(error, new(8, 0), Vector2.Zero, 1f / 60), Is.True, "A deliberate sweep beyond centre must not get trapped");
        Assert.That(CameraAimAttraction.Pull(error, 0, .1f), Is.EqualTo(Vector2.Zero));
    }
}
