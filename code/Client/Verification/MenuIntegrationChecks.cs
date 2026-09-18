using Godot;
using Trackstorm.Client.Bootstrap;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises the composed production menu with a real solo UDP arena and native logical input.</summary>
public sealed partial class MenuIntegrationChecks : Node
{
    private int _assertions;
    private string _directory = string.Empty;
    private SimulationBootstrap _bootstrap = null!;
    private SettingsPanel _menu = null!;
    private DevelopmentSession _session = null!;
    private PlayerInput _player = null!;
    private PlayerSettingsController _settings = null!;
    private DevelopmentSession? _remote;
    private string _endpoint = string.Empty;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta) => _remote?.Advance(new InputFrame(0, 0, 30000, 0, 0, 0, 0));

    /// <summary>Runs isolated integration checks and exits through the production Quit action.</summary>
    public async void Run()
    {
        try
        {
            _directory = OS.GetCmdlineUserArgs().Single(argument => argument.StartsWith("--menu-output=", StringComparison.Ordinal))[14..];
            using (var reservation = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            {
                _endpoint = $"127.0.0.1:{((System.Net.IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            }

            _bootstrap = new SimulationBootstrap { OnlineEnabled = false, SettingsPath = System.IO.Path.Combine(_directory, "settings.json") };
            _bootstrap.AddChild(new PlayerInput { Name = "PlayerInput" });
            AddChild(_bootstrap);
            _player = _bootstrap.GetNode<PlayerInput>("PlayerInput");
            _settings = _bootstrap.GetNode<PlayerSettingsController>("PlayerSettings");
            _menu = _settings.GetNode<SettingsPanel>("SettingsPanel");
            _session = _bootstrap.GetNode<DevelopmentSession>("DevelopmentSession");
            await Frames(3);
            CheckCursor(false, "Main Menu pointer");
            Press("Settings");
            CheckCursor(false, "Main Menu Settings pointer");
            Press("Back");
            await EnterArena();
            CheckCursor(true, "gameplay captures mouse");
            VerifyFocus(true);
            VerifyMouseItem();
            Check(_session.Arena!.Driver.Match?.Phase == MatchPhase.Waiting, "solo match is Waiting");
            Tap(Key.Escape);
            await Frames(2);
            Check(_menu.CurrentPage == MenuPage.Game, "ESC opens in solo Waiting arena");
            Check(!_menu.GetTree().Paused, "menu does not pause tree");
            Check(_player.Adapter.GameplaySuppressed, "local gameplay suppressed");
            CheckCursor(false, "ESC releases mouse immediately");
            VerifyFocus(false);
            Check(!Buttons(_session).Any(button => button.IsVisibleInTree() && (button.Text.Contains("Leave", StringComparison.OrdinalIgnoreCase) || button.Text.Contains("Return", StringComparison.OrdinalIgnoreCase))), "legacy session buttons absent in arena");
            Check(Buttons(_menu).Count(button => button.Text == "Settings" && button.IsVisibleInTree()) == 1, "only Game Menu Settings entry visible");
            ulong tick = _bootstrap.CurrentSimulationTick;
            KeyEvent(Key.W, true);
            await Frames(20);
            Check(_player.LatestFrame.Accelerate == 0 && _player.LatestFrame.Steering == 0, "native driving key suppressed");
            Check(_bootstrap.CurrentSimulationTick > tick, "authoritative network simulation advances behind menu");
            KeyEvent(Key.W, false);
            await Capture("game");
            Tap(Key.Escape);
            Check(_menu.CurrentPage == MenuPage.Closed && !_player.Adapter.GameplaySuppressed, "second ESC closes and restores local input");
            CheckCursor(true, "ESC restores capture immediately");
            Tap(Key.Escape);
            Tap(Key.Enter);
            Check(_menu.CurrentPage == MenuPage.Closed, "focused Back to Game closes");
            Tap(Key.Escape);
            await Frames(2);
            Click("Back to Game");
            Check(_menu.CurrentPage == MenuPage.Closed, "native pointer Back to Game closes");
            CheckCursor(true, "pointer return restores capture immediately");
            Check((_player.Adapter.Capture(0).Pressed & InputButtons.UseItem) == 0, "menu click cannot use an item on return");
            VerifyMouseItem();

            // Start/A/D-pad/B use the existing gamepad bindings, including shared gameplay buttons.
            Joy(JoyButton.Start);
            Joy(JoyButton.DpadDown);
            Joy(JoyButton.A);
            Check(_menu.CurrentPage == MenuPage.Settings, "gamepad opens Settings through logical navigation");
            CheckCursor(false, "controller navigation preserves pointer availability");
            await Frames(2);
            Click("Audio");
            Check(_menu.CurrentPage == MenuPage.Audio, "mouse pointer selects category after controller navigation");
            CheckCursor(false, "Settings category pointer");
            _player._Notification((int)NotificationApplicationFocusOut);
            _menu.Close();
            CheckCursor(false, "closing menu while unfocused cannot recapture");
            _player._Notification((int)NotificationApplicationFocusIn);
            CheckCursor(true, "focus regain uses changed gameplay context");
            Tap(Key.Escape);
            Press("Settings");
            Press("Audio");
            Tap(Key.Escape);
            await Capture("settings");
            foreach (MenuPage page in Enum.GetValues<MenuPage>().Where(page => page >= MenuPage.Audio))
            {
                Press(page == MenuPage.DeveloperOptions ? "Developer Options" : page.ToString());
                Check(_menu.CurrentPage == page, "category opens: " + page);
                CheckCursor(false, "category releases capture: " + page);
                await Frames(2);
                if (page == MenuPage.DeveloperOptions)
                {
                    var content = Descendants(_menu).OfType<VBoxContainer>().Single(node => node.Name == "DeveloperOptions");
                    Check(content.GetChildCount() == 1, "Developer Options owns one unified developer panel");
                    await Frames(20);
                    Check(Buttons(_menu).Any(button => button.IsVisibleInTree() && button.Text == "Apply Settings"), "Developer Options exposes host tuning");
                }

                await Capture(page.ToString());
                Joy(JoyButton.B);
                Check(_menu.CurrentPage == MenuPage.Settings, "category Back returns to Settings: " + page);
            }

            Press("Audio");
            var sliders = Descendants(_menu).OfType<HSlider>().ToArray();
            sliders[0].Value = 64;
            sliders[1].Value = 37;
            sliders[2].Value = 81;
            Tap(Key.Right);
            Check(_settings.Current.MasterVolume == 0.65 && _settings.Current.MusicVolume == 0.37 && _settings.Current.SfxVolume == 0.81, "sliders and logical Right update existing audio model");
            Check(Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music")) - (20 * Math.Log10(0.37))) < 0.01, "existing audio bus receives gain");
            Tap(Key.Escape);
            Press("Gameplay");
            Tap(Key.Right);
            Check(_settings.Current.SpeedUnit == SpeedUnit.MilesPerHour, "logical option navigation updates speed preference");
            Tap(Key.Escape);
            Press("Interface");
            Tap(Key.Enter);
            Tap(Key.Down);
            Tap(Key.Enter);
            Check(_settings.Current.ShowFps && _settings.Current.ShowPing, "independent visibility settings update existing model");
            Tap(Key.Escape);
            Press("Controls");
            Tap(Key.Enter);
            Check(_settings.Current.InvertSteering, "existing invert steering exposed");
            Button binding = Buttons(_menu).Single(button => button.Name == "Binding_MenuDown");
            binding.EmitSignal(BaseButton.SignalName.Pressed);
            Tap(Key.J);
            Check(_settings.Current.Bindings[InputAction.MenuDown].Contains("key:74"), "native binding capture reuses persistence");
            Tap(Key.Escape);
            Control? before = GetViewport().GuiGetFocusOwner();
            Tap(Key.J);
            Check(GetViewport().GuiGetFocusOwner() != before, "remapped logical menu navigation works");
            Press("Controls");
            binding.EmitSignal(BaseButton.SignalName.Pressed);
            Tap(Key.Escape);
            Check(_menu.CurrentPage == MenuPage.Controls, "ESC cancels capture without navigating");
            Press("Restore default bindings");
            Tap(Key.Escape);
            Tap(Key.Escape);
            Check(_menu.CurrentPage == MenuPage.Game, "Settings Back returns to Game Menu");
            Press("Back to Game");
            CheckCursor(true, "Settings return restores capture immediately");
            VerifyMouseItem();
            Tap(Key.F1);
            CheckCursor(false, "F1 Developer Options releases capture");
            Tap(Key.F1);
            CheckCursor(true, "F1 close restores capture");
            Tap(Key.F2);
            CheckCursor(false, "Statistic Panel releases capture");
            Tap(Key.F2);
            Tap(Key.F3);
            CheckCursor(false, "Event Log releases capture");
            Tap(Key.F3);
            CheckCursor(true, "diagnostic close restores capture");
            Check(new PlayerSettingsStore(_bootstrap.SettingsPath!).Load().SpeedUnit == SpeedUnit.MilesPerHour, "close flushes existing settings file");
            Joy(JoyButton.B);
            Check(_menu.CurrentPage == MenuPage.Closed, "shared handbrake button does not open the menu");

            foreach (Vector2I size in new[] { new Vector2I(640, 360), new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(2560, 1080) })
            {
                GetWindow().Size = size;
                Tap(Key.Escape);
                await Frames(3);
                Rect2 bounds = _menu.MenuBounds;
                Check(bounds.Position.X >= 0 && bounds.Position.Y >= 0 && bounds.End.X <= GetViewport().GetVisibleRect().Size.X + 1 && bounds.End.Y <= GetViewport().GetVisibleRect().Size.Y + 1, "resolution-safe menu bounds " + size);
                await Capture($"menu-{size.X}x{size.Y}");
                Tap(Key.Escape);
            }

            Tap(Key.Escape);
            Press("Leave to Main Menu");
            await Until(() => _session.LeaveComplete && _session.Arena is null, "leave cleanup completes");
            Check(_menu.CurrentPage == MenuPage.Closed, "leave closes overlay");
            await Frames(2);
            CheckCursor(false, "leaving arena restores Main Menu pointer");
            await EnterArena();
            Check(_session.Arena!.Driver.Match?.Phase == MatchPhase.Waiting, "reenter has fresh Waiting state");
            _session.Leave();
            await Frames(3);
            await VerifyRemoteProgress();
            Tap(Key.Escape);
            Press("Quit");
            Check(_session.LeaveComplete && _session.Arena is null, "Quit invokes production session cleanup before exit");
            GD.Print($"Menu integration passed: {_assertions} assertions; solo Waiting, navigation, settings, leave and production Quit.");
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<Button> Buttons(Node node) => Descendants(node).OfType<Button>();

    private static void KeyEvent(Key key, bool pressed)
    {
        using var input = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed };
        Godot.Input.ParseInputEvent(input);
        Godot.Input.FlushBufferedEvents();
    }

    private static void Tap(Key key)
    {
        KeyEvent(key, true);
        KeyEvent(key, false);
    }

    private static void Joy(JoyButton button)
    {
        foreach (bool pressed in new[] { true, false })
        {
            using var input = new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = pressed };
            Godot.Input.ParseInputEvent(input);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private static void MouseButtonEvent(bool pressed, Vector2 position)
    {
        using var input = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = position, GlobalPosition = position };
        Godot.Input.ParseInputEvent(input);
        Godot.Input.FlushBufferedEvents();
    }

    private async Task EnterArena()
    {
        _session.Open(true, _endpoint, "Menu check");
        await Until(() => _session.Lobby?.State is not null, "host starts");
        CheckCursor(false, "lobby pointer");
        Check(_session.Lobby!.Request(LobbyCommand.Ready, true), "solo host ready");
        Check(_session.Lobby.Request(LobbyCommand.Start), "solo arena entry uses existing lobby lifecycle");
        await Until(() => _session.Arena?.Driver.Match is not null, "arena initialized");
        await Frames(2);
        CheckCursor(true, "arena entry restores capture");
    }

    private void CheckCursor(bool captured, string message)
    {
        // The dummy display server cannot verify OS mouse modes. Run -Visual for these assertions.
        if (DisplayServer.GetName() != "headless")
        {
            Check(Godot.Input.MouseMode == (captured ? Godot.Input.MouseModeEnum.Captured : Godot.Input.MouseModeEnum.Visible), message);
        }
    }

    private void VerifyFocus(bool gameplay)
    {
        _player._Notification((int)NotificationApplicationFocusOut);
        CheckCursor(false, "focus loss releases capture");
        Check(!_player.Adapter.Enabled, "focus loss suppresses local input");
        _player._Process(0);
        CheckCursor(false, "background processing cannot recapture");
        _player._Notification((int)NotificationApplicationFocusIn);
        CheckCursor(gameplay, "focus regain restores current context");
    }

    private void VerifyMouseItem()
    {
        MouseButtonEvent(false, Vector2.Zero);
        _player.Adapter.Observe();
        MouseButtonEvent(true, Vector2.Zero);
        Check((_player.Adapter.Capture(0).Pressed & InputButtons.UseItem) != 0, "captured LMB still produces logical item press");
        MouseButtonEvent(false, Vector2.Zero);
        _player.Adapter.Capture(0);
    }

    private void Click(string text)
    {
        Button button = Buttons(_menu).Single(button => button.IsVisibleInTree() && button.Text == text);
        Vector2 position = button.GetGlobalRect().GetCenter();
        using var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position, Relative = new Vector2(12, 8) };
        Godot.Input.ParseInputEvent(motion);
        Godot.Input.FlushBufferedEvents();
        MouseButtonEvent(true, position);
        MouseButtonEvent(false, position);
    }

    private async Task VerifyRemoteProgress()
    {
        _session.Open(true, _endpoint, "Menu host");
        var world = new SubViewport { Size = new Vector2I(640, 360), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(world);
        _remote = new DevelopmentSession();
        world.AddChild(_remote);
        _remote.Open(false, _endpoint, "Remote driver");
        await Until(() => _session.Lobby?.State?.Players.Count == 2 && _remote.Lobby?.State?.Players.Count == 2, "two real UDP peers admitted");
        _session.Lobby!.Request(LobbyCommand.Ready, true);
        _remote.Lobby!.Request(LobbyCommand.Ready, true);
        await Until(() => _session.Lobby.State!.CanStart, "two peers ready");
        _session.Lobby.Request(LobbyCommand.Start);
        await Until(() => _remote.Arena?.Driver.Match?.Phase == MatchPhase.Active, "two-player match becomes Active");
        Tap(Key.Escape);
        Check(_menu.CurrentPage == MenuPage.Game, "same menu opens in Active phase");
        ulong remoteTick = _remote.Arena!.Driver.Latest!.Tick;
        var position = _remote.Arena.LocalState!.ObservedPhysics.Position;
        await Frames(45);
        Check(_remote.Arena.Driver.Latest!.Tick > remoteTick, "remote snapshots advance while host menu is open");
        Check(_remote.Arena.LocalState!.ObservedPhysics.Position != position, "remote vehicle continues driving while local controls are suppressed");
        Check(_player.Adapter.GameplaySuppressed, "suppression remains local to host");
        Tap(Key.Escape);
        ulong departingPlayer = _remote.Lobby!.LocalPlayerId;
        _remote.Leave();
        await Until(
            () => _remote.LeaveComplete && _session.Lobby.State!.Players.Count == 2 &&
            !_session.Lobby.State.Players.Single(player => player.Id == departingPlayer).Connected,
            "client graceful leave completes while the arena retains its player reservation");
        _remote = null;
        world.QueueFree();
        await Frames(2);
    }

    private async Task Until(Func<bool> condition, string message)
    {
        for (int frame = 0; frame < 600 && !condition(); frame++)
        {
            await Frames(1);
        }

        Check(condition(), message);
    }

    private async Task Frames(int count)
    {
        for (int frame = 0; frame < count; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void Press(string text) => Buttons(_menu).Single(button => button.IsVisibleInTree() && button.Text == text).EmitSignal(BaseButton.SignalName.Pressed);

    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng(System.IO.Path.Combine(_directory, name + ".png")) == Error.Ok, "screenshot " + name);
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        _assertions++;
    }
}
