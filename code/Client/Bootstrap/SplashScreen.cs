using Godot;

namespace Trackstorm.Client.Bootstrap;

/// <summary>Dedicated one-shot video presentation with no application initialization responsibility.</summary>
internal sealed partial class SplashScreen : CanvasLayer
{
    private readonly VideoStreamPlayer _video = new()
    {
        Name = "SplashVideo",
        Expand = true,
        Loop = false,
        AudioTrack = 0,
        Bus = "Master",
        Volume = 1,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    private bool _completed;

    /// <summary>Raised once after this presentation has fully elapsed.</summary>
    internal event Action? Completed;

    /// <summary>Whether the splash video, including its embedded audio, is currently playing.</summary>
    internal bool Playing => _video.IsPlaying();

    /// <summary>Stable identity used by native startup verification.</summary>
    internal ulong VideoInstanceId => _video.GetInstanceId();

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = 20;
        if (_video.Stream is null)
        {
            throw new InvalidOperationException("Splash video must be initialized before entering the scene tree.");
        }

        AddChild(_video);
        _video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _video.Finished += Complete;
        _video.Play();
    }

    /// <summary>Assigns the preloaded Godot-compatible splash stream.</summary>
    /// <param name="stream">One-shot video stream with embedded audio.</param>
    internal void Initialize(VideoStream stream) => _video.Stream = (VideoStream)stream.Duplicate();

    private void Complete()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        Completed?.Invoke();
    }
}
