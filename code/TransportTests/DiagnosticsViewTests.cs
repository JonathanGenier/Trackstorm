using Trackstorm.Client.Networking;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Settings;

namespace Trackstorm.Transport.Tests;

/// <summary>Deterministic presentation boundaries without Godot or native SDK loading.</summary>
[TestFixture]
internal sealed class DiagnosticsViewTests
{
    /// <summary>Startup and corrupt timing remain safe; the average uses total time, not mean instantaneous FPS.</summary>
    [Test]
    public void AveragesFrameTimeAndHoldsCompletedWindow()
    {
        var sampler = new FrameRateSampler();
        Assert.That(sampler.FramesPerSecond, Is.Null);
        foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            sampler.Add(invalid);
        }

        Assert.That(sampler.FramesPerSecond, Is.Null);
        for (int i = 0; i < 20; i++)
        {
            sampler.Add(0.01);
            sampler.Add(0.015);
        }

        Assert.That(sampler.FramesPerSecond, Is.EqualTo(80).Within(0.00001));
        sampler.Add(0.001);
        Assert.That(sampler.FramesPerSecond, Is.EqualTo(80).Within(0.00001));
        sampler.Add(0.999);
        Assert.That(sampler.FramesPerSecond, Is.EqualTo(2));
        sampler.Add(double.MaxValue);
        Assert.That(double.IsFinite(sampler.FramesPerSecond!.Value), Is.True);
    }

    /// <summary>Steady rendering converges to the expected rate.</summary>
    /// <param name="rate">Representative rendering rate.</param>
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void SteadyFrames(int rate)
    {
        var sampler = new FrameRateSampler();
        for (int i = 0; i < rate * 2; i++)
        {
            sampler.Add(1.0 / rate);
        }

        Assert.That(sampler.FramesPerSecond, Is.EqualTo(rate).Within(0.00001));
    }

    /// <summary>All lifecycle states override stray samples; valid zero and high latency stay numeric.</summary>
    /// <param name="state">Current connection lifecycle.</param>
    /// <param name="ping">Optional latency.</param>
    /// <param name="expected">Exact compact label.</param>
    [TestCase(ConnectionDiagnosticState.Connected, 0, "Ping  0 ms")]
    [TestCase(ConnectionDiagnosticState.Connected, 45, "Ping  45 ms")]
    [TestCase(ConnectionDiagnosticState.Connected, int.MaxValue, "Ping  2147483647 ms")]
    [TestCase(ConnectionDiagnosticState.Connected, null, "Ping  —")]
    [TestCase(ConnectionDiagnosticState.Connected, -1, "Ping  —")]
    [TestCase(ConnectionDiagnosticState.Connecting, 45, "Ping  connecting")]
    [TestCase(ConnectionDiagnosticState.Reconnecting, 45, "Ping  reconnecting")]
    [TestCase(ConnectionDiagnosticState.Disconnected, 45, "Ping  disconnected")]
    [TestCase(ConnectionDiagnosticState.Unavailable, 45, "Ping  —")]
    public void FormatsLifecycle(ConnectionDiagnosticState state, int? ping, string expected)
    {
        var view = DiagnosticsView.Create(new(), null, new(state, new(ping, null, null)));
        Assert.That(view.Ping, Is.EqualTo(expected));
        Assert.That(view.Fps, Is.EqualTo("FPS  —"));
    }

    /// <summary>Visibility is independent and formatting cannot leak nonfinite input.</summary>
    [Test]
    public void IndependentVisibilityAndFiniteOutput()
    {
        foreach (bool fps in new[] { false, true })
        {
            foreach (bool ping in new[] { false, true })
            {
                foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 60 })
                {
                    var view = DiagnosticsView.Create(new PlayerSettings { ShowFps = fps, ShowPing = ping }, value, default);
                    Assert.That(view.FpsVisible, Is.EqualTo(fps));
                    Assert.That(view.PingVisible, Is.EqualTo(ping));
                    Assert.That(view.Fps, Is.EqualTo(value == 60 ? "FPS  60" : "FPS  —"));
                }
            }
        }
    }
}
