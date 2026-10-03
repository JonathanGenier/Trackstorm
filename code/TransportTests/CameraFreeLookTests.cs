using System.Numerics;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class CameraFreeLookTests
{
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void ArmedInputKeepsMouseDirectAndStickPreciseThenReturnsLikeOrdinaryCamera(int fps)
    {
        var mouse = new CameraFreeLook();
        var ordinaryMouse = new CameraFreeLook();
        var fine = new CameraFreeLook();
        var fast = new CameraFreeLook();
        for (int i = 0; i < fps; i++)
        {
            mouse.Advance(new(100f / fps, -100f / fps), true, Vector2.Zero, 1f / fps, -.4f, true);
            ordinaryMouse.Advance(new(100f / fps, -100f / fps), true, Vector2.Zero, 1f / fps, -.4f);
            fine.Advance(Vector2.Zero, false, new(.2f, 0), 1f / fps, -.4f, true);
            fast.Advance(Vector2.Zero, false, Vector2.UnitX, 1f / fps, -.4f, true);
        }
        Assert.That(mouse.Yaw, Is.EqualTo(-.3f).Within(.00001));
        Assert.That(mouse.Pitch, Is.EqualTo(.3f).Within(.00001));
        Assert.That(fine.Yaw, Is.EqualTo(-.088f).Within(.00001));
        Assert.That(fast.Yaw, Is.EqualTo(-2.2f).Within(.00001));
        for (int i = 0; i < fps; i++)
        {
            mouse.Advance(Vector2.Zero, false, Vector2.Zero, 1f / fps, -.4f, true);
            ordinaryMouse.Advance(Vector2.Zero, false, Vector2.Zero, 1f / fps, -.4f);
            Assert.That(mouse.Yaw, Is.EqualTo(ordinaryMouse.Yaw).Within(.00001));
            Assert.That(mouse.Pitch, Is.EqualTo(ordinaryMouse.Pitch).Within(.00001));
        }
        Assert.That(mouse.Yaw, Is.EqualTo(-.3f * MathF.Exp(-6)).Within(.00001));
        Assert.That(mouse.Pitch, Is.EqualTo(.3f * MathF.Exp(-6)).Within(.00001));
    }
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

    [TestCase(false)]
    [TestCase(true)]
    public void OrbitRemainsBoundedAndResetClearsBothAxes(bool weaponAiming)
    {
        var look = new CameraFreeLook();
        look.Advance(new Vector2(100000, -100000), true, Vector2.Zero, 1f / 60, -0.4f, weaponAiming);
        Assert.That(look.Yaw, Is.InRange(-MathF.PI, MathF.PI));
        Assert.That(look.Pitch - 0.4f, Is.Zero.Within(0.00001), "Upward orbit reaches the horizon.");
        look.Advance(new Vector2(0, 100000), true, Vector2.Zero, 1f / 60, -0.4f, weaponAiming);
        Assert.That(look.Pitch - 0.4f, Is.EqualTo(-85 * MathF.PI / 180).Within(0.00001), "Downward orbit stops before the pole.");
        look.Reset();
        Assert.That(look.Yaw, Is.Zero);
        Assert.That(look.Pitch, Is.Zero);
        look.Advance(new Vector2(100, 0), false, Vector2.Zero, 1f / 60, -0.4f, weaponAiming);
        Assert.That(look.Yaw, Is.EqualTo(-0.3f).Within(0.00001), "A drag released between renders is not lost.");
    }
}
