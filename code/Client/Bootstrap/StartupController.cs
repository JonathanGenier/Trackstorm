using Godot;

namespace Trackstorm.Client.Bootstrap;

/// <summary>Coordinates minimal boot presentation, application resource loading, and recoverable completion.</summary>
internal sealed partial class StartupController : Node
{
    private const string FrontendVideoPath = "res://assets/frontend/Menu no music.ogv";
    private const string FrontendMusicPath = "res://assets/audio/project/music/Welcome to the Carnage Circus main menu.mp3";

    private static readonly string[] FrontendResources =
    [
        FrontendVideoPath,
        FrontendMusicPath,
    ];

    private static readonly string[] ApplicationResources =
    [
        "res://assets/hud/Health.png",
        "res://assets/hud/Item.png",
        "res://assets/hud/Speed.png",
        "res://assets/hud/Timer.png",
        "res://assets/hud/Wrench.svg",
        "res://assets/hud/Missile.svg",
        "res://assets/hud/Component.gdshader",
        "res://assets/items/materials/DamageFlash.gdshader",
        "res://assets/audio/project/music/Welcome to the Carnage Circus Arena.mp3",
        "res://assets/audio/project/music/Welcome to the Carnage Circus Arena 2.mp3",
        "res://assets/audio/project/music/Welcome to the Carnage Circus Arena 3.mp3",
        "res://assets/audio/freesound/arena/ambience.wav",
        "res://assets/audio/freesound/combat/death.wav",
        "res://assets/audio/freesound/combat/destruction.wav",
        "res://assets/audio/freesound/combat/end.wav",
        "res://assets/audio/freesound/combat/explosion.wav",
        "res://assets/audio/freesound/combat/fire.wav",
        "res://assets/audio/freesound/vehicle/high.wav",
        "res://assets/audio/freesound/vehicle/idle.wav",
        "res://assets/audio/freesound/vehicle/low.wav",
        "res://assets/audio/freesound/vehicle/skid.wav",
        "res://assets/audio/kenney/impact/impactMetal_heavy_000.ogg",
        "res://assets/audio/kenney/impact/impactMetal_light_000.ogg",
        "res://assets/audio/kenney/impact/impactMetal_medium_000.ogg",
        "res://assets/audio/kenney/impact/impactMetal_medium_001.ogg",
        "res://assets/audio/kenney/interface/confirmation_001.ogg",
        "res://assets/audio/kenney/interface/confirmation_002.ogg",
        "res://assets/audio/kenney/interface/confirmation_003.ogg",
        "res://assets/audio/kenney/interface/confirmation_004.ogg",
        "res://assets/audio/kenney/interface/tick_001.ogg",
        "res://assets/audio/kenney/scifi/engineCircular_000.ogg",
        "res://assets/audio/kenney/scifi/forceField_000.ogg",
        "res://assets/audio/kenney/scifi/forceField_001.ogg",
        "res://assets/audio/kenney/scifi/forceField_002.ogg",
    ];

    private readonly StartupFlow _flow = new();
    private readonly List<Resource> _resources = new();
    private SplashScreen? _splash;
    private MenuShell? _shell;
    private AudioStream? _frontendMusic;
    private VideoStream? _frontendVideo;
    private string? _preloadingPath;
    private int _preloadIndex;
    private string? _loadingPath;
    private int _resourceIndex;
    private bool _preloaderPresented;

    /// <summary>Raised after every validated startup transition.</summary>
    internal event Action<StartupStage>? StageChanged;

    /// <summary>Application composition invoked only after required resources load.</summary>
    internal Func<bool> InitializeApplication { get; set; } = () => true;

    /// <summary>Initializes saved audio routing before frontend music begins.</summary>
    internal Func<bool> PrepareFrontend { get; set; } = () => true;

    /// <summary>Main-menu reveal invoked only after composition succeeds.</summary>
    internal Action PresentMainMenu { get; set; } = () => { };

    /// <summary>Rolls back any partially composed application after required initialization fails.</summary>
    internal Action AbortApplication { get; set; } = () => { };

    /// <summary>Current validated startup stage.</summary>
    internal StartupStage Stage => _flow.Stage;

    /// <summary>Identity of the current persistent frontend background.</summary>
    internal ulong BackgroundInstanceId => _shell?.BackgroundInstanceId ?? 0;

    /// <summary>Identity of the current independent frontend music player.</summary>
    internal ulong MusicInstanceId => _shell?.MusicInstanceId ?? 0;

    /// <summary>Whether the persistent frontend video and music are both playing.</summary>
    internal bool MediaPlaying => _shell?.MediaPlaying == true;

    /// <summary>Configurable splash duration used by production and runtime verification.</summary>
    internal double SplashDuration { get; set; } = 1.4;

    /// <summary>Whether the next system initialization should fail for runtime verification.</summary>
    internal bool FailNextInitialization { get; set; }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_flow.Stage == StartupStage.Preloader)
        {
            if (!_preloaderPresented)
            {
                _preloaderPresented = true;
                return;
            }

            LoadNextFrontendResource();
            return;
        }

        if (_flow.Stage == StartupStage.FrontendLoading)
        {
            LoadNextResource();
        }
    }

    /// <summary>Invokes the same retry path as the player-facing recovery button.</summary>
    internal void RetryForVerification() => Retry();

    /// <summary>Matches shell presentation to frontend or gameplay ownership.</summary>
    /// <param name="active">Whether frontend presentation currently owns the viewport.</param>
    internal void SetFrontendActive(bool active) => _shell?.SetActive(active);

    private void BeginFrontendLoading()
    {
        _splash?.QueueFree();
        _splash = null;
        _shell = new MenuShell { Name = "MenuShell" };
        _shell.RetryRequested += Retry;
        _shell.QuitRequested += () => GetTree().Quit(1);
        AddChild(_shell);
        _flow.ShowFrontendLoader();
        try
        {
            if (!PrepareFrontend())
            {
                throw new InvalidOperationException("Required frontend audio settings did not initialize.");
            }

            _shell.StartMedia(
                _frontendVideo ?? throw new InvalidOperationException("Frontend video was not preloaded."),
                _frontendMusic ?? throw new InvalidOperationException("Frontend music was not preloaded."));
            StageChanged?.Invoke(_flow.Stage);
            _shell.ShowProgress("Loading shared application resources…", 0);
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private void LoadNextFrontendResource()
    {
        if (_preloadIndex >= FrontendResources.Length)
        {
            _splash = new SplashScreen { Name = "SplashScreen", Duration = SplashDuration };
            _splash.Completed += BeginFrontendLoading;
            AddChild(_splash);
            _flow.ShowSplash();
            StageChanged?.Invoke(_flow.Stage);
            return;
        }

        string path = FrontendResources[_preloadIndex];
        if (_preloadingPath is null)
        {
            Error request = ResourceLoader.LoadThreadedRequest(path, useSubThreads: true);
            if (request != Error.Ok)
            {
                throw new InvalidOperationException($"Frontend resource could not start loading: {path} ({request}).");
            }

            _preloadingPath = path;
            return;
        }

        ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(path);
        if (status == ResourceLoader.ThreadLoadStatus.InProgress)
        {
            return;
        }

        if (status != ResourceLoader.ThreadLoadStatus.Loaded)
        {
            throw new InvalidOperationException($"Frontend resource failed to load: {path} ({status}).");
        }

        Resource resource = ResourceLoader.LoadThreadedGet(path) ?? throw new InvalidOperationException($"Frontend resource could not be loaded: {path}");
        if (path == FrontendVideoPath)
        {
            _frontendVideo = resource as VideoStream ?? throw new InvalidOperationException("Frontend video has an unsupported imported type.");
        }
        else
        {
            _frontendMusic = resource as AudioStream ?? throw new InvalidOperationException("Frontend music has an unsupported imported type.");
        }

        _preloadIndex++;
        _preloadingPath = null;
    }

    private void LoadNextResource()
    {
        string[] paths = ApplicationResources;
        if (_resourceIndex < paths.Length)
        {
            string path = paths[_resourceIndex];
            try
            {
                if (_loadingPath is null)
                {
                    Error request = ResourceLoader.LoadThreadedRequest(path, useSubThreads: true);
                    if (request != Error.Ok)
                    {
                        throw new InvalidOperationException($"Required resource could not start loading: {path} ({request}).");
                    }

                    _loadingPath = path;
                    _shell!.ShowProgress($"Loading {_resourceIndex + 1} of {paths.Length}", (double)_resourceIndex / (paths.Length + 1));
                    return;
                }

                using var itemProgress = new Godot.Collections.Array();
                ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(path, itemProgress);
                if (status == ResourceLoader.ThreadLoadStatus.InProgress)
                {
                    double fraction = itemProgress.Count == 0 ? 0 : itemProgress[0].AsDouble();
                    _shell!.ShowProgress($"Loading {_resourceIndex + 1} of {paths.Length}", (_resourceIndex + fraction) / (paths.Length + 1));
                    return;
                }

                if (status != ResourceLoader.ThreadLoadStatus.Loaded)
                {
                    throw new InvalidOperationException($"Required resource failed to load: {path} ({status}).");
                }

                Resource resource = ResourceLoader.LoadThreadedGet(path) ?? throw new InvalidOperationException($"Required resource could not be loaded: {path}");
                _resources.Add(resource);
                _resourceIndex++;
                _loadingPath = null;
                _shell!.ShowProgress($"Loading {_resourceIndex} of {paths.Length}", (double)_resourceIndex / (paths.Length + 1));
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }

            return;
        }

        try
        {
            _shell!.ShowProgress("Starting application systems…", 1);
            if (FailNextInitialization)
            {
                FailNextInitialization = false;
                throw new InvalidOperationException("Required application initialization was intentionally failed for verification.");
            }

            if (!InitializeApplication())
            {
                throw new InvalidOperationException("A required application system did not initialize.");
            }

            _flow.Complete();
            PresentMainMenu();
            _shell.ShowMainMenu();
            StageChanged?.Invoke(_flow.Stage);
            SetProcess(false);
        }
        catch (Exception exception)
        {
            AbortApplication();
            Fail(exception.Message);
        }
    }

    private void Fail(string message)
    {
        _flow.Fail();
        _shell!.ShowFailure(message);
        StageChanged?.Invoke(_flow.Stage);
    }

    private void Retry()
    {
        _flow.Retry();
        _resourceIndex = 0;
        _loadingPath = null;
        _resources.Clear();
        _shell!.ShowProgress("Retrying shared application resources…", 0);
        StageChanged?.Invoke(_flow.Stage);
    }
}
