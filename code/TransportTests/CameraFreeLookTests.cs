using System.Numerics;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class CameraFreeLookTests
{
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void OrbitAndReturnAreIndependentOfRenderRate(int fps)
    {
        var look = new CameraFreeLook();
        for (int i = 0; i < fps; i++)
        {
            look.Advance(new Vector2(100f / fps, 0), true, new Vector2(0.5f, 0), 1f / fps, -0.4f);
        }

        Assert.That(look.Yaw, Is.EqualTo(-1.4f).Within(0.00001));
        look.Advance(Vector2.Zero, true, Vector2.Zero, 1, -0.4f);
        Assert.That(look.Yaw, Is.EqualTo(-1.4f).Within(0.00001), "Held RMB keeps the view without motion.");
        for (int i = 0; i < fps; i++)
        {
            look.Advance(Vector2.Zero, false, Vector2.Zero, 1f / fps, -0.4f);
        }

        Assert.That(look.Yaw, Is.EqualTo(-1.4f * MathF.Exp(-6)).Within(0.00001));
    }

    [Test]
    public void OrbitRemainsBoundedAndResetClearsBothAxes()
    {
        var look = new CameraFreeLook();
        look.Advance(new Vector2(100000, -100000), true, Vector2.Zero, 1f / 60, -0.4f);
        Assert.That(look.Yaw, Is.InRange(-MathF.PI, MathF.PI));
        Assert.That(look.Pitch - 0.4f, Is.EqualTo(-MathF.PI / 36).Within(0.00001));
        look.Reset();
        Assert.That(look.Yaw, Is.Zero);
        Assert.That(look.Pitch, Is.Zero);
        look.Advance(new Vector2(100, 0), false, Vector2.Zero, 1f / 60, -0.4f);
        Assert.That(look.Yaw, Is.EqualTo(-0.3f).Within(0.00001), "A drag released between renders is not lost.");
    }
}
