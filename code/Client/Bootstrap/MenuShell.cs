using Godot;

namespace Trackstorm.Client.Bootstrap;

/// <summary>Owns the persistent frontend background and startup status presented above it.</summary>
internal sealed partial class MenuShell : CanvasLayer
{
    private readonly VideoStreamPlayer _background = new()
    {
        Name = "FrontendVideo",
        Expand = true,
        Loop = true,
        Volume = 0,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    private readonly Control _root = new();
    private readonly Label _status = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1, ShowPercentage = false };
    private readonly Button _retry = new() { Text = "Retry initialization", Visible = false };
    private readonly Button _quit = new() { Text = "Quit", Visible = false };
    private readonly AudioStreamPlayer _music = new() { Name = "FrontendMusic", Bus = "Music" };
    private PanelContainer _loader = null!;
    private bool _active = true;

    /// <summary>Raised when the player requests another initialization attempt.</summary>
    internal event Action? RetryRequested;

    /// <summary>Raised when the player quits from a failed startup.</summary>
    internal event Action? QuitRequested;

    /// <summary>Stable identity used to verify background continuity.</summary>
    internal ulong BackgroundInstanceId => _background.GetInstanceId();

    /// <summary>Stable identity used to verify independent music continuity.</summary>
    internal ulong MusicInstanceId => _music.GetInstanceId();

    /// <summary>Whether both independent frontend media players are active.</summary>
    internal bool MediaPlaying => _background.IsPlaying() && _music.Playing;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Layer = -5;
        AddChild(_background);
        _background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_music);
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _loader = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -250,
            OffsetRight = 250,
            OffsetTop = -115,
            OffsetBottom = 115,
        };
        _loader.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.055f, 0.09f, 0.94f),
            BorderColor = new Color("b32b38"),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 28,
            ContentMarginRight = 28,
            ContentMarginTop = 24,
            ContentMarginBottom = 24,
        });
        _root.AddChild(_loader);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 14);
        _loader.AddChild(content);
        var title = new Label { Text = "PREPARING TRACKSTORM", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 26);
        content.AddChild(title);
        content.AddChild(_status);
        content.AddChild(_progress);
        content.AddChild(_retry);
        content.AddChild(_quit);
        _retry.Pressed += () => RetryRequested?.Invoke();
        _quit.Pressed += () => QuitRequested?.Invoke();
    }

    /// <summary>Displays bounded application initialization progress.</summary>
    /// <param name="item">Human-readable current work.</param>
    /// <param name="progress">Normalized completion.</param>
    internal void ShowProgress(string item, double progress)
    {
        _loader.Show();
        _loader.Modulate = Colors.White;
        _status.Text = item;
        _progress.Value = Math.Clamp(progress, 0, 1);
        _progress.Show();
        _retry.Hide();
        _quit.Hide();
    }

    /// <summary>Displays a recoverable initialization failure.</summary>
    /// <param name="message">Safe player-facing failure detail.</param>
    internal void ShowFailure(string message)
    {
        _status.Text = "Startup failed\n" + message;
        _progress.Hide();
        _retry.Show();
        _quit.Show();
        _retry.GrabFocus();
    }

    /// <summary>Starts the independent looping video and Music-bus stream owned by this shell.</summary>
    /// <param name="video">Silent Godot-compatible frontend video.</param>
    /// <param name="music">Authoritative frontend music.</param>
    internal void StartMedia(VideoStream video, AudioStream music)
    {
        if (_background.Stream is not null || _music.Stream is not null)
        {
            return;
        }

        _background.Stream = (VideoStream)video.Duplicate();
        AudioStream musicPlayback = (AudioStream)music.Duplicate();
        if (musicPlayback is AudioStreamMP3 mp3)
        {
            mp3.Loop = true;
        }

        _music.Stream = musicPlayback;
        if (_active)
        {
            _background.Play();
            _music.Play();
        }
    }

    /// <summary>Suspends frontend presentation and music while gameplay owns the viewport.</summary>
    /// <param name="active">Whether the application is currently in its frontend.</param>
    internal void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;
        Visible = active;
        if (!active)
        {
            _background.Stop();
            _music.Stop();
        }
        else
        {
            if (_background.Stream is not null)
            {
                _background.Play();
            }

            if (_music.Stream is not null)
            {
                _music.Play();
            }
        }
    }

    /// <summary>Fades loader chrome away without replacing this shell.</summary>
    internal void ShowMainMenu()
    {
        var tween = CreateTween();
        tween.TweenProperty(_loader, "modulate:a", 0, 0.35).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenCallback(Callable.From(_loader.Hide));
    }
}
