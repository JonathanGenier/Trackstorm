using System.Numerics;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class MagneticAimCursorTests
{
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
