using Godot;
using Trackstorm.Client.Bootstrap;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises production startup order, recovery, and persistent MenuShell ownership.</summary>
internal sealed partial class StartupIntegrationChecks : Node
{
    private readonly List<StartupStage> _stages = [StartupStage.Preloader];
    private readonly HashSet<StartupFailurePhase> _recovered = [];
    private StartupController _startup = null!;
    private ulong _loaderBackground;
    private ulong _loaderMusic;
    private ulong _splashVideo;
    private double _seconds;

    /// <inheritdoc/>
    public override void _Ready() => _startup.StageChanged += OnStageChanged;

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _seconds += delta;
        if (_seconds > 45)
        {
            Fail("startup verification timed out");
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree() => _startup.StageChanged -= OnStageChanged;

    /// <summary>Supplies the production startup owner before this check enters the tree.</summary>
    /// <param name="startup">Startup owner under verification.</param>
    internal void Initialize(StartupController startup) => _startup = startup;

    private void OnStageChanged(StartupStage stage)
    {
        _stages.Add(stage);
        if (stage == StartupStage.Splash)
        {
            Check(_startup.GetNodeOrNull<MenuShell>("MenuShell") is null, "Splash completes before MenuShell exists");
            Check(_startup.SplashPlaying, "Splash video with embedded audio begins in the Splash state");
            Check(!_startup.MediaPlaying, "Loader video and music do not play during Splash");
            _splashVideo = _startup.SplashVideoInstanceId;
            Check(_splashVideo != 0, "Splash owns a dedicated video player");
        }
        else if (stage == StartupStage.FrontendLoading)
        {
            ulong current = _startup.BackgroundInstanceId;
            Check(_startup.SplashVideoInstanceId == 0, "Completed Splash video does not continue into Loader");
            Check(current != _splashVideo, "Loader uses a distinct video player after Splash completes");
            Check(current != 0, "MenuShell background begins with Loader");
            Check(_startup.MediaPlaying, "Frontend video and independent music begin with Loader");
            if (_loaderBackground == 0)
            {
                _loaderBackground = current;
                _loaderMusic = _startup.MusicInstanceId;
                Check(_loaderMusic != 0, "Frontend music player begins with Loader");
            }
            else
            {
                Check(current == _loaderBackground, "Retry preserves MenuShell background");
                Check(_startup.MusicInstanceId == _loaderMusic, "Retry preserves frontend music player");
            }
        }
        else if (stage == StartupStage.Failed)
        {
            StartupFailurePhase phase = _startup.FailurePhase ?? throw new InvalidOperationException("Failed startup has no owning phase.");
            Check(GetParent().GetNodeOrNull<Networking.DevelopmentSession>("DevelopmentSession") is null, "Failure cannot enter Main Menu");
            if (phase == StartupFailurePhase.FrontendDependencies)
            {
                Check(!_startup.MediaPlaying, "Dependency failure presents recovery without pretending media loaded");
            }
            else if (phase == StartupFailurePhase.FrontendSetup)
            {
                Check(!_startup.MediaPlaying, "Frontend setup failure clears partial media before retry");
            }
            else
            {
                Check(_startup.MediaPlaying, "Application failure preserves valid MenuShell media");
                Check(GetTree().AutoAcceptQuit, "Application rollback restores safe native window close behavior");
            }

            Check(_recovered.Add(phase), $"{phase} fails only once before successful retry");
            _startup.RetryForVerification();
        }
        else if (stage == StartupStage.MainMenu)
        {
            Check(_recovered.SetEquals(Enum.GetValues<StartupFailurePhase>()), "Every startup failure phase recovered through its own retry path");
            Check(_startup.BackgroundInstanceId == _loaderBackground, "Main Menu retains exact Loader background instance");
            Check(_startup.MusicInstanceId == _loaderMusic, "Main Menu retains exact Loader music instance");
            Check(_startup.MediaPlaying, "Main Menu retains continuously playing frontend media");
            Check(!GetTree().AutoAcceptQuit, "Successful composition restores coordinated application close ownership");
            Check(GetParent().GetNodeOrNull<Networking.DevelopmentSession>("DevelopmentSession") is not null, "Main Menu exists only after initialization");
            var session = GetParent().GetNode<Networking.DevelopmentSession>("DevelopmentSession");
            Check(session.Stage == Networking.ApplicationStage.MainMenu, "Startup enters Main Menu before browsing");
            session.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Text == "Browse online lobbies").EmitSignal(BaseButton.SignalName.Pressed);
            Check(session.Stage == Networking.ApplicationStage.LobbyBrowser, "Main Menu enters Lobby Browser");
            Check(_startup.BackgroundInstanceId == _loaderBackground && _startup.MusicInstanceId == _loaderMusic && _startup.MediaPlaying, "Browser retains the active MenuShell media");
            session.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Text == "Back to Main Menu").EmitSignal(BaseButton.SignalName.Pressed);
            Check(session.Stage == Networking.ApplicationStage.MainMenu, "Browser returns to Main Menu");
            StartupStage[] expected = [StartupStage.Preloader, StartupStage.Failed, StartupStage.Preloader, StartupStage.Splash, StartupStage.Failed, StartupStage.FrontendLoading, StartupStage.Failed, StartupStage.FrontendLoading, StartupStage.MainMenu];
            Check(_stages.SequenceEqual(expected), "Startup transition order is explicit and deterministic");
            GD.Print("Startup integration passed: phase-aware dependency, frontend setup, and application recovery with persistent MenuShell media.");
            Finish();
        }
    }

    private async void Finish()
    {
        for (int frame = 0; frame < 30; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        _startup.GetNode<MenuShell>("MenuShell").ResetMedia();
        for (int frame = 0; frame < 4; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        GetTree().Quit();
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            Fail(message);
        }
    }

    private void Fail(string message)
    {
        GD.PushError("Startup integration failed: " + message);
        GetTree().Quit(1);
    }
}
