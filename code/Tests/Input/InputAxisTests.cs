using Trackstorm.Core.Input;

namespace Trackstorm.Core.Tests.Input;

/// <summary>Checks normalization boundaries and the deterministic quantization contract.</summary>
[TestFixture]
internal sealed class InputAxisTests
{
    /// <summary>Dead-zone boundaries are neutral; endpoints saturate; the remaining range is rescaled.</summary>
    /// <param name="value">Raw sample.</param>
    /// <param name="expected">Expected normalized sample.</param>
    [TestCase(0f, 0f)]
    [TestCase(0.25f, 0f)]
    [TestCase(-0.25f, 0f)]
    [TestCase(0.625f, 0.5f)]
    [TestCase(-0.625f, -0.5f)]
    [TestCase(1f, 1f)]
    [TestCase(-1f, -1f)]
    [TestCase(2f, 1f)]
    [TestCase(-2f, -1f)]
    [TestCase(float.NaN, 0f)]
    [TestCase(float.PositiveInfinity, 0f)]
    [TestCase(float.NegativeInfinity, 0f)]
    public void Normalize_ConditionsSignedRange(float value, float expected)
    {
        Assert.That(InputAxis.Normalize(value, 0.25f), Is.EqualTo(expected));
    }

    /// <summary>Inversion negates positive/negative samples and preserves neutral.</summary>
    /// <param name="value">Signed sample.</param>
    [TestCase(0.5f)]
    [TestCase(-0.5f)]
    [TestCase(0f)]
    public void Normalize_InversionIsSymmetric(float value)
    {
        Assert.That(InputAxis.Normalize(value, inverted: true), Is.EqualTo(-value));
    }

    /// <summary>Invalid configuration fails rather than silently altering controls.</summary>
    /// <param name="deadZone">Invalid dead zone.</param>
    [TestCase(-0.1f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Normalize_RejectsInvalidDeadZone(float deadZone)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InputAxis.Normalize(0, deadZone));
    }

    /// <summary>The first float outside each dead-zone edge produces signed nonzero input.</summary>
    [Test]
    public void Normalize_ImmediatelyOutsideDeadZoneIsActive()
    {
        Assert.That(InputAxis.Normalize(MathF.BitIncrement(0.25f), 0.25f), Is.GreaterThan(0));
        Assert.That(InputAxis.Normalize(MathF.BitDecrement(-0.25f), 0.25f), Is.LessThan(0));
    }

    /// <summary>Integer range, midpoint and clamping behavior are explicit.</summary>
    [Test]
    public void Quantization_HasCanonicalEndpointsAndRounding()
    {
        Assert.Multiple(() =>
        {
            Assert.That(InputAxis.QuantizeSteering(-2), Is.EqualTo(-32767));
            Assert.That(InputAxis.QuantizeSteering(2), Is.EqualTo(32767));
            Assert.That(InputAxis.QuantizeSteering(0), Is.Zero);
            Assert.That(InputAxis.QuantizeSteering(0.5f), Is.EqualTo(16384));
            Assert.That(InputAxis.QuantizeSteering(-0.5f), Is.EqualTo(-16384));
            Assert.That(InputAxis.QuantizePedal(-1), Is.Zero);
            Assert.That(InputAxis.QuantizePedal(0), Is.Zero);
            Assert.That(InputAxis.QuantizePedal(2), Is.EqualTo(65535));
            Assert.That(InputAxis.QuantizePedal(0.5f), Is.EqualTo(32768));
            Assert.That(InputAxis.QuantizePedal(float.NaN), Is.Zero);
        });
    }
}
