using Trackstorm.Core.Input;

namespace Trackstorm.Core.Tests.Input;

/// <summary>Digital control progression remains bounded and responsive at fixed capture rates.</summary>
[TestFixture]
internal sealed class DrivingInputShapingTests
{
    /// <summary>Equal elapsed fixed time gives the same rise, release and reversal without overshoot.</summary>
    /// <param name="rate">Fixed captures per second.</param>
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(120)]
    public void ProgressionUsesTimeAndDoesNotOvershoot(int rate)
    {
        var tuning = new DrivingInputShaping();
        float input = 0;
        for (int tick = 0; tick < rate / 2; tick++)
        {
            input = DrivingInputShaping.Approach(input, 1, tuning.ThrottleRise, 1f / rate);
        }

        Assert.That(input, Is.EqualTo(1));
        for (int tick = 0; tick < rate / 2; tick++)
        {
            input = DrivingInputShaping.Approach(input, -1, tuning.SteeringReversal, 1f / rate);
        }

        Assert.That(input, Is.EqualTo(-1));
        for (int tick = 0; tick < rate / 2; tick++)
        {
            input = DrivingInputShaping.Approach(input, 0, tuning.SteeringReturn, 1f / rate);
        }

        Assert.That(input, Is.Zero);
    }

    /// <summary>Invalid tuning fails explicitly; first-tick intent is progressive and independently tunable.</summary>
    [Test]
    public void BoundariesAndIndependentRates()
    {
        var tuning = new DrivingInputShaping();
        float throttle = DrivingInputShaping.Approach(0, 1, tuning.ThrottleRise, 1f / 60);
        float brake = DrivingInputShaping.Approach(0, 1, tuning.BrakeRise, 1f / 60);
        Assert.That(throttle, Is.InRange(0.1f, 0.2f));
        Assert.That(brake, Is.GreaterThan(throttle).And.LessThan(0.4f));
        Assert.Throws<ArgumentException>(() => DrivingInputShaping.Approach(0, 1, 0, 1f / 60));
        Assert.Throws<ArgumentException>(() => DrivingInputShaping.Approach(float.NaN, 1, 1, 1f / 60));
        Assert.Throws<ArgumentException>(() => DrivingInputShaping.Approach(0, 1, 1, float.PositiveInfinity));
        Assert.That(DrivingInputShaping.Approach(0, 100, 10, 1), Is.EqualTo(1));
    }

    /// <summary>Digital intent reaches full steering in 50 ms and reverses within 100 ms.</summary>
    [Test]
    public void SteeringRespondsWithinArcadeInputBudget()
    {
        var tuning = new DrivingInputShaping();
        float steering = 0;
        for (int tick = 0; tick < 3; tick++)
        {
            steering = DrivingInputShaping.Approach(steering, 1, tuning.SteeringRise, 1f / 60);
        }

        Assert.That(steering, Is.EqualTo(1));
        for (int tick = 0; tick < 6; tick++)
        {
            steering = DrivingInputShaping.Approach(steering, -1, steering > 0 ? tuning.SteeringReversal : tuning.SteeringRise, 1f / 60);
        }

        Assert.That(steering, Is.EqualTo(-1));
    }
}
