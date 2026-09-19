using Godot;

namespace Trackstorm.Client.Bootstrap;

/// <summary>Coordinates minimal boot presentation, application resource loading, and recoverable completion.</summary>
internal sealed partial class StartupController : Node
{
    private const string SplashVideoPath = "res://assets/frontend/Splashscreen.ogv";
    private const string FrontendVideoPath = "res://assets/frontend/Menu no music.ogv";
    private const string FrontendMusicPath = "res://assets/audio/project/music/Welcome to the Carnage Circus main menu.mp3";

    private static readonly string[] FrontendResources =
    [
        SplashVideoPath,
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
    private VideoStream? _splashVideo;
    private string? _preloadingPath;
    private int _preloadIndex;
    private string? _loadingPath;
    private int _resourceIndex;
    private bool _preloaderPresented;
    private bool _dependencyFailureShell;

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

    /// <summary>Phase that owns the current recoverable failure.</summary>
    internal StartupFailurePhase? FailurePhase => _flow.FailurePhase;

    /// <summary>Whether the dedicated one-shot Splash video is currently playing.</summary>
    internal bool SplashPlaying => _splash?.Playing == true;

    /// <summary>Identity of the dedicated Splash video player.</summary>
    internal ulong SplashVideoInstanceId => _splash?.VideoInstanceId ?? 0;

    /// <summary>Whether the next system initialization should fail for runtime verification.</summary>
    internal bool FailNextInitialization { get; set; }

    /// <summary>Whether runtime verification should fail the next frontend dependency attempt.</summary>
    internal bool FailNextFrontendDependency { get; set; }

    /// <summary>Whether runtime verification should fail the next MenuShell setup attempt.</summary>
    internal bool FailNextFrontendSetup { get; set; }

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

            try
            {
                if (FailNextFrontendDependency)
                {
                    FailNextFrontendDependency = false;
                    throw new InvalidOperationException("Frontend dependency loading was intentionally failed for verification.");
                }

                LoadNextFrontendResource();
            }
            catch (Exception exception)
            {
                Fail(StartupFailurePhase.FrontendDependencies, exception.Message);
            }

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
        EnsureShell();
        _flow.ShowFrontendLoader();
        StartFrontendSetup();
    }

    private void StartFrontendSetup()
    {
        try
        {
            if (FailNextFrontendSetup)
            {
                FailNextFrontendSetup = false;
                throw new InvalidOperationException("MenuShell setup was intentionally failed for verification.");
            }

            if (!PrepareFrontend())
            {
                throw new InvalidOperationException("Required frontend audio settings did not initialize.");
            }

            MenuShell shell = _shell ?? throw new InvalidOperationException("MenuShell was not created before frontend setup.");
            shell.StartMedia(
                _frontendVideo ?? throw new InvalidOperationException("Frontend video was not preloaded."),
                _frontendMusic ?? throw new InvalidOperationException("Frontend music was not preloaded."));
            StageChanged?.Invoke(_flow.Stage);
            shell.ShowProgress("Loading shared application resources…", 0);
        }
        catch (Exception exception)
        {
            _shell!.ResetMedia();
            Fail(StartupFailurePhase.FrontendSetup, exception.Message);
        }
    }

    private void LoadNextFrontendResource()
    {
        if (_preloadIndex >= FrontendResources.Length)
        {
            RemoveDependencyFailureShell();
            _splash = new SplashScreen { Name = "SplashScreen" };
            _splash.Initialize(_splashVideo ?? throw new InvalidOperationException("Splash video was not preloaded."));
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
        if (path == SplashVideoPath)
        {
            _splashVideo = resource as VideoStream ?? throw new InvalidOperationException("Splash video has an unsupported imported type.");
        }
        else if (path == FrontendVideoPath)
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
                Fail(StartupFailurePhase.ApplicationInitialization, exception.Message);
            }

            return;
        }

        try
        {
            _shell!.ShowProgress("Starting application systems…", 1);
            if (!InitializeApplication())
            {
                throw new InvalidOperationException("A required application system did not initialize.");
            }

            if (FailNextInitialization)
            {
                FailNextInitialization = false;
                throw new InvalidOperationException("Required application initialization was intentionally failed for verification.");
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
            Fail(StartupFailurePhase.ApplicationInitialization, exception.Message);
        }
    }

    private void EnsureShell()
    {
        if (_shell is not null)
        {
            return;
        }

        _shell = new MenuShell { Name = "MenuShell" };
        _shell.RetryRequested += Retry;
        _shell.QuitRequested += () => GetTree().Quit(1);
        AddChild(_shell);
    }

    private void Fail(StartupFailurePhase phase, string message)
    {
        if (phase == StartupFailurePhase.FrontendDependencies)
        {
            if (_splash is not null)
            {
                if (_splash.GetParent() == this)
                {
                    RemoveChild(_splash);
                }

                _splash.Free();
                _splash = null;
            }

            EnsureShell();
            _dependencyFailureShell = true;
        }

        _flow.Fail(phase);
        _shell!.ShowFailure(message);
        StageChanged?.Invoke(_flow.Stage);
    }

    private void Retry()
    {
        StartupFailurePhase phase = _flow.Retry();
        if (phase == StartupFailurePhase.FrontendDependencies)
        {
            _preloadIndex = 0;
            _preloadingPath = null;
            _splashVideo = null;
            _frontendVideo = null;
            _frontendMusic = null;
            _shell!.ShowProgress("Retrying startup presentation dependencies…", 0);
            StageChanged?.Invoke(_flow.Stage);
            return;
        }

        if (phase == StartupFailurePhase.FrontendSetup)
        {
            _shell!.ShowProgress("Retrying frontend presentation…", 0);
            StartFrontendSetup();
            return;
        }

        _resourceIndex = 0;
        _loadingPath = null;
        _resources.Clear();
        _shell!.ShowProgress("Retrying shared application resources…", 0);
        StageChanged?.Invoke(_flow.Stage);
    }

    private void RemoveDependencyFailureShell()
    {
        if (!_dependencyFailureShell || _shell is null)
        {
            return;
        }

        RemoveChild(_shell);
        _shell.Free();
        _shell = null;
        _dependencyFailureShell = false;
    }
}
