namespace Trackstorm.Client.Settings;

/// <summary>Allocation-free half-second frame-time average; invalid timing never enters the window.</summary>
internal sealed class FrameRateSampler
{
    private double _seconds;
    private int _frames;

    /// <summary>Completed window average, absent until half a second has been observed.</summary>
    internal double? FramesPerSecond { get; private set; }

    /// <summary>Accumulates a rendered frame's duration.</summary>
    /// <param name="seconds">Positive finite frame time.</param>
    internal void Add(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            return;
        }

        _seconds += seconds;
        _frames++;
        if (_seconds >= 0.5)
        {
            FramesPerSecond = _frames / _seconds;
            _seconds = 0;
            _frames = 0;
        }
    }
}
