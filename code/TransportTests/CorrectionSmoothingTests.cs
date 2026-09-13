using System.Numerics;
using Trackstorm.Client.Networking;

namespace Trackstorm.Transport.Tests;

/// <summary>Deterministic presentation correction checks without Godot or native sockets.</summary>
[TestFixture]
internal sealed class CorrectionSmoothingTests
{
    /// <summary>Zero/tiny errors remain stable and large errors snap explicitly.</summary>
    /// <param name="distance">Correction magnitude.</param>
    /// <param name="offset">Expected initial visible offset.</param>
    /// <param name="snaps">Expected hard-snap count.</param>
    [TestCase(0f, 0f, 0)]
    [TestCase(0.005f, 0f, 0)]
    [TestCase(0.01f, 0f, 0)]
    [TestCase(0.5f, 0.5f, 0)]
    [TestCase(3f, 0f, 1)]
    [TestCase(20f, 0f, 1)]
    public void CorrectionThresholds(float distance, float offset, int snaps)
    {
        var smoothing = new CorrectionSmoothing();
        smoothing.Correct(new Vector3(distance, 0, 0), Vector3.Zero, Quaternion.Identity, Quaternion.Identity);
        Assert.That(smoothing.Offset.X, Is.EqualTo(offset));
        Assert.That(smoothing.HardSnaps, Is.EqualTo(snaps));
    }

    /// <summary>Small position and orientation corrections converge consistently across render rates.</summary>
    [Test]
    public void CorrectionDecayIsIndependentOfRenderRate()
    {
        var slow = new CorrectionSmoothing();
        var fast = new CorrectionSmoothing();
        foreach (var smoothing in new[] { slow, fast })
        {
            smoothing.Correct(Vector3.UnitX, Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f), Quaternion.Identity);
        }

        for (int i = 0; i < 30; i++)
        {
            slow.Advance(1f / 30);
        }

        for (int i = 0; i < 144; i++)
        {
            fast.Advance(1f / 144);
        }

        Assert.That(Vector3.Distance(slow.Offset, fast.Offset), Is.LessThan(0.00001));
        Assert.That(slow.Offset.Length(), Is.LessThan(0.00001));
        Assert.That(Math.Abs(Quaternion.Dot(slow.Rotation, Quaternion.Identity)), Is.GreaterThan(0.99999));
        Assert.Throws<ArgumentOutOfRangeException>(() => slow.Advance(-1));
    }
}
