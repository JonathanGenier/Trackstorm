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
            StartupStage[] expected = [StartupStage.Preloader, StartupStage.Failed, StartupStage.Preloader, StartupStage.Splash, StartupStage.Failed, StartupStage.FrontendLoading, StartupStage.Failed, StartupStage.FrontendLoading, StartupStage.MainMenu];
            Check(_stages.SequenceEqual(expected), "Startup transition order is explicit and deterministic");
            VerifyMenu(session);
        }
    }

    private async void VerifyMenu(Networking.DevelopmentSession session)
    {
        try
        {
            var menu = session.MainMenu;
            // Synthetic input must not depend on which desktop window the operator is viewing.
            GetParent().GetNode<Input.PlayerInput>("PlayerInput").Adapter.Enabled = true;
            Check(!menu.Settled && menu.Targets.All(button => button.Disabled), "Drop blocks every target immediately at Loader reveal");
            Check(menu.Targets.Select(button => button.Text).SequenceEqual(new[] { "Play", "Garage", "Settings", "Quit" }), "Exactly four approved entries");
            menu.Targets[0].EmitSignal(BaseButton.SignalName.Pressed);
            Tap(Key.Enter);
            Joy(JoyButton.A, true);
            await Frames(12);
            Check(session.Stage == Networking.ApplicationStage.MainMenu, "Mouse signal, keyboard and controller cannot activate during drop");
            await Capture("drop");
            for (int frame = 0; frame < 180 && !menu.Interactive; frame++) await Frames(1);
            Check(menu.Interactive, "Interaction enables after settle");
            Check(session.Stage == Networking.ApplicationStage.MainMenu, "Held accept cannot leak across settle");
            Joy(JoyButton.A, false);
            await Frames(2);
            await Capture("settled");
            Rect2 stableTarget = menu.Targets[0].GetGlobalRect();
            Tap(Key.Down);
            Check(menu.SelectedId == "Settings", "Keyboard skips Garage");
            Joy(JoyButton.DpadDown, true);
            Joy(JoyButton.DpadDown, false);
            Check(menu.SelectedId == "Quit", "Controller takes over keyboard focus");
            for (int count = 0; count < 18; count++) Tap(Key.Down);
            Check(menu.SelectedId == "Quit", "Repeated directional navigation wraps and skips Garage");
            Click(menu.Targets[1]);
            Check(menu.SelectedId == "Quit" && session.Stage == Networking.ApplicationStage.MainMenu, "Garage neither selects nor activates");
            Click(menu.Targets[2]);
            var settings = GetParent().GetNode<Settings.PlayerSettingsController>("PlayerSettings").GetNode<Settings.SettingsPanel>("SettingsPanel");
            Check(settings.CurrentPage == Settings.MenuPage.Settings, "Mouse after controller opens existing Settings");
            await Frames(2);
            Tap(Key.Escape);
            await Frames(3);
            Check(settings.CurrentPage == Settings.MenuPage.Closed && menu.SelectedId == "Settings", "Settings returns with prior main-menu selection");
            await Frames(90);
            Check(menu.Targets[0].GetGlobalRect() == stableTarget, "Idle artwork never moves input targets");
            Check(menu.SelectedId == "Settings", "Stationary pointer and ambient motion preserve selection");
            await Capture("settings-selected");
            Click(menu.Targets[0]);
            Check(session.Stage == Networking.ApplicationStage.LobbyBrowser, "Play opens existing Lobby Browser");
            Check(_startup.BackgroundInstanceId == _loaderBackground && _startup.MusicInstanceId == _loaderMusic && _startup.MediaPlaying, "Entrance, Settings, and browser retain active MenuShell players");
            session.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Text == "Back to Main Menu").EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(3);
            Check(session.Stage == Networking.ApplicationStage.MainMenu && menu.Interactive, "Browser return retains settled menu");
            if (DisplayServer.GetName() != "headless")
            {
                Vector2I original = GetWindow().Size;
                foreach (Vector2I size in new[] { new Vector2I(640, 360), new Vector2I(1280, 720), new Vector2I(1600, 900) })
                {
                    GetWindow().Size = size;
                    await Frames(4);
                    Rect2 viewport = GetViewport().GetVisibleRect();
                    Check(menu.Targets.All(button => viewport.Encloses(button.GetGlobalRect())), $"Targets fit {size}");
                    await Capture($"layout-{size.X}x{size.Y}");
                }
                GetWindow().Size = original;
            }
            GD.Print("Startup integration passed: phase-aware recovery, drop input gating, mixed keyboard/controller/mouse navigation, disabled Garage, repeated navigation, stable targets, Settings/browser return, and persistent MenuShell media.");
            Finish();
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }

    private static void Tap(Key key)
    {
        foreach (bool pressed in new[] { true, false })
        {
            using var input = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed };
            Godot.Input.ParseInputEvent(input);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private static void Joy(JoyButton button, bool pressed)
    {
        using var input = new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = pressed };
        Godot.Input.ParseInputEvent(input);
        Godot.Input.FlushBufferedEvents();
    }

    private static void Click(Button button)
    {
        Vector2 position = button.GetGlobalRect().GetCenter();
        using var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position, Relative = new Vector2(8, 8) };
        Godot.Input.ParseInputEvent(motion);
        Godot.Input.FlushBufferedEvents();
        foreach (bool pressed in new[] { true, false })
        {
            using var input = new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = pressed };
            Godot.Input.ParseInputEvent(input);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private async Task Frames(int count)
    {
        for (int frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://.godot/main-menu-checks");
        System.IO.Directory.CreateDirectory(directory);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(directory, name + ".png"));
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
            throw new InvalidOperationException(message);
        }
    }

    private void Fail(string message)
    {
        GD.PushError("Startup integration failed: " + message);
        GetTree().Quit(1);
    }
}
