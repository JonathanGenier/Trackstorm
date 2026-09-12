using Trackstorm.Core.Input;

namespace Trackstorm.Core.Tests.Input;

/// <summary>Protects value equality, stable wire bytes and deterministic logical edge sequences.</summary>
[TestFixture]
internal sealed class InputFrameTests
{
    /// <summary>Wire bytes have a pinned version, little-endian order and no padding.</summary>
    [Test]
    public void Serialization_MatchesKnownBytesAndValueEquality()
    {
        var frame = new InputFrame(0x0807060504030201, -32767, 65535, 32768, InputButtons.Leaderboard, InputButtons.UseItem, InputButtons.Drift);
        byte[] bytes = new byte[InputFrame.SerializedSize];
        frame.Write(bytes);
        byte[] expected = [1, 1, 2, 3, 4, 5, 6, 7, 8, 1, 128, 255, 255, 0, 128, 4, 0, 2, 0, 1, 0];
        Assert.That(bytes, Is.EqualTo(expected));
        Assert.That(InputFrame.Read(bytes), Is.EqualTo(frame));
        Assert.That(InputFrame.Read(bytes).GetHashCode(), Is.EqualTo(frame.GetHashCode()));
        Assert.That(new InputFrame(2, 0, 0, 0, 0, 0, 0), Is.Not.EqualTo(new InputFrame(1, 0, 0, 0, 0, 0, 0)));
    }

    /// <summary>Neutral default and all legal integer endpoints round-trip.</summary>
    [Test]
    public void Serialization_RoundTripsBoundaryFrames()
    {
        InputFrame[] frames = [default, new(ulong.MaxValue, 32767, 65535, 65535, (InputButtons)1023, (InputButtons)1023, (InputButtons)1023)];
        foreach (InputFrame frame in frames)
        {
            byte[] bytes = new byte[InputFrame.SerializedSize];
            frame.Write(bytes);
            Assert.That(InputFrame.Read(bytes), Is.EqualTo(frame));
        }
    }

    /// <summary>Unknown versions, sizes, reserved bits and the asymmetric short endpoint are rejected.</summary>
    [Test]
    public void Serialization_RejectsMalformedFrames()
    {
        Assert.Throws<ArgumentException>(() => InputFrame.Read([]));
        Assert.Throws<ArgumentException>(() => InputFrame.Read(new byte[22]));
        Assert.Throws<ArgumentException>(() => default(InputFrame).Write(new byte[20]));
        byte[] bytes = new byte[InputFrame.SerializedSize];
        Assert.Throws<ArgumentException>(() => InputFrame.Read(bytes));
        bytes[0] = 1;
        bytes[10] = 128;
        Assert.Throws<ArgumentOutOfRangeException>(() => InputFrame.Read(bytes));
        bytes[10] = 0;
        foreach (int offset in new[] { 16, 18, 20 })
        {
            bytes[offset] = 128;
            Assert.Throws<ArgumentOutOfRangeException>(() => InputFrame.Read(bytes));
            bytes[offset] = 0;
        }
    }

    /// <summary>Press/hold/release of leaderboard leaves independent vehicle input intact.</summary>
    [Test]
    public void Capture_LeaderboardDoesNotChangeVehicleControls()
    {
        var capture = new InputFrameCapture();
        capture.Observe(InputButtons.Drift | InputButtons.Leaderboard);
        InputFrame press = capture.Capture(1, 123, 456, 789);
        InputFrame hold = capture.Capture(2, 123, 456, 789);
        capture.Observe(InputButtons.Drift);
        InputFrame release = capture.Capture(3, 123, 456, 789);
        Assert.Multiple(() =>
        {
            Assert.That(press.Pressed, Is.EqualTo(InputButtons.Drift | InputButtons.Leaderboard));
            Assert.That(hold.Pressed, Is.EqualTo(InputButtons.None));
            Assert.That(hold.Held, Is.EqualTo(press.Held));
            Assert.That(release.Released, Is.EqualTo(InputButtons.Leaderboard));
            Assert.That(release.Held, Is.EqualTo(InputButtons.Drift));
            Assert.That(new[] { press, hold, release }.Select(frame => (frame.Steering, frame.Accelerate, frame.Brake)), Is.All.EqualTo((123, 456, 789)));
        });
    }

    /// <summary>Between-tick taps survive once, even when no button is held at capture.</summary>
    [Test]
    public void Capture_PreservesShortTapAndConsumesEdgesOnlyOnce()
    {
        var capture = new InputFrameCapture();
        capture.Observe(InputButtons.UseItem);
        capture.Observe(InputButtons.None);
        InputFrame tap = capture.Capture(1, 0, 0, 0);
        InputFrame next = capture.Capture(2, 0, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(tap.Held, Is.EqualTo(InputButtons.None));
            Assert.That(tap.Pressed, Is.EqualTo(InputButtons.UseItem));
            Assert.That(tap.Released, Is.EqualTo(InputButtons.UseItem));
            Assert.That(next.Pressed | next.Released, Is.EqualTo(InputButtons.None));
        });
    }

    /// <summary>Replay derives the same logical held/edge sequence from encoded frames without capture or devices.</summary>
    [Test]
    public void Replay_ProducesIdenticalPureInputStateSequence()
    {
        var capture = new InputFrameCapture();
        var frames = new List<InputFrame>();
        InputButtons[] states = [InputButtons.None, InputButtons.Leaderboard, InputButtons.Leaderboard, InputButtons.None, InputButtons.UseItem, InputButtons.None];
        for (int i = 0; i < states.Length; i++)
        {
            capture.Observe(states[i]);
            frames.Add(capture.Capture((ulong)i, (short)(i * -100), (ushort)(i * 1000), 0));
        }

        byte[][] recording = frames.Select(frame =>
        {
            byte[] bytes = new byte[InputFrame.SerializedSize];
            frame.Write(bytes);
            return bytes;
        }).ToArray();
        InputFrame[] firstReplay = recording.Select(bytes => InputFrame.Read(bytes)).ToArray();
        InputFrame[] secondReplay = recording.Select(bytes => InputFrame.Read(bytes)).ToArray();
        Assert.That(firstReplay, Is.EqualTo(frames));
        Assert.That(secondReplay, Is.EqualTo(firstReplay));
        Assert.That(firstReplay.Select(frame => frame.Held), Is.EqualTo(states));
        Assert.That(firstReplay.Select(frame => frame.Pressed), Is.EqualTo(new[] { InputButtons.None, InputButtons.Leaderboard, InputButtons.None, InputButtons.None, InputButtons.UseItem, InputButtons.None }));
        Assert.That(firstReplay.Select(frame => frame.Released), Is.EqualTo(new[] { InputButtons.None, InputButtons.None, InputButtons.None, InputButtons.Leaderboard, InputButtons.None, InputButtons.UseItem }));
    }
}
