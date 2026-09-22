using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Input;
using Trackstorm.Core.Settings;
using Trackstorm.Core.Simulation;

namespace Trackstorm.Client.Verification;

/// <summary>Explicit isolated settings checks, including separate-process restart and optional rendered UI verification.</summary>
public sealed partial class SettingsIntegrationChecks : Node
{
    private int _assertions;
    private PlayerInput _player = null!;
    private PlayerSettingsController _settings = null!;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <summary>Exercises production preference consumers and exits with a machine-checkable result.</summary>
    public async void Run()
    {
        try
        {
            string[] arguments = OS.GetCmdlineUserArgs();
            string path = arguments.Single(argument => argument.StartsWith("--settings-path=", StringComparison.Ordinal))[16..];
            string phase = arguments.Single(argument => argument.StartsWith("--phase=", StringComparison.Ordinal))[8..];
            _player = new PlayerInput();
            AddChild(_player);
            _player.SetPhysicsProcess(false);
            _settings = new PlayerSettingsController();
            _settings.Initialize(_player.Adapter, path);
            AddChild(_settings);
            if (phase == "write")
            {
                VerifyFileFailures(path + ".failures");
                WritePreferences();
            }
            else if (phase == "read")
            {
                VerifyRestart();
                VerifyConsumers();
                VerifyMalformedBindings();
            }
            else if (phase == "visual")
            {
                await VerifyUi(path);
            }
            else
            {
                throw new ArgumentException("Unknown settings check phase.");
            }

            GD.Print($"Settings integration passed: {phase}, {_assertions} assertions.");
            GetTree().Quit();
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

    private static void Press(Node root, string text) => Descendants(root).OfType<Button>().Single(button => button.Text == text && button.IsVisibleInTree()).EmitSignal(BaseButton.SignalName.Pressed);

    private static void SendKey(Key key, bool pressed)
    {
        using var input = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed };
        Godot.Input.ParseInputEvent(input);
        Godot.Input.FlushBufferedEvents();
    }

    private void VerifyFileFailures(string path)
    {
        var store = new PlayerSettingsStore(path);
        Check(store.Load().Bindings.Count == 0, "missing file defaults");
        File.WriteAllText(path, "{corrupt");
        Check(store.Load().MasterVolume == 1, "corrupt file defaults");
        Check(store.TrySave(new PlayerSettings { MasterVolume = 0.4 }, out _), "initial file save");
        using (var locked = new FileStream(path + ".tmp", FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
        {
            Check(!store.TrySave(new PlayerSettings { MasterVolume = 0.9 }, out string? error) && error is not null, "save failure is reported");
            Check(store.Load().MasterVolume == 0.4, "failed save preserves committed settings");
        }

        Check(store.TrySave(new PlayerSettings { MasterVolume = 0.7 }, out _), "save can retry");
        Check(new PlayerSettingsStore(path).Load().MasterVolume == 0.7, "retried save is durable");
    }

    private void WritePreferences()
    {
        _settings.UpdateSettings(new PlayerSettings
        {
            MasterVolume = 0,
            MusicVolume = 0.25,
            SfxVolume = 0.75,
            CameraShakeIntensity = 0.35,
            Fullscreen = true,
            WindowWidth = 960,
            WindowHeight = 540,
            SpeedUnit = SpeedUnit.MilesPerHour,
            ShowFps = true,
            ShowPing = false,
            InvertSteering = true,
            DeadZone = 0.25,
        });
        using var key = new InputEventKey { PhysicalKeycode = Key.J };
        using var button = new InputEventJoypadButton { Device = 2, ButtonIndex = JoyButton.Y };
        using var axis = new InputEventJoypadMotion { Device = 2, Axis = JoyAxis.RightX, AxisValue = -1 };
        _player.Adapter.Bindings.Replace(InputAction.Accelerate, key, button, axis);
        _player.Adapter.Bindings.Replace(InputAction.Pause, []);
        Check(_settings.Flush(), "all preferences saved for next process");
    }

    private void VerifyRestart()
    {
        PlayerSettings settings = _settings.Current;
        Check(settings.CameraShakeIntensity == 0.35, "camera shake intensity survives process restart");
        Check(settings.MasterVolume == 0 && settings.MusicVolume == 0.25 && settings.SfxVolume == 0.75, "audio gains survive process restart");
        Check(settings.Fullscreen && settings.WindowWidth == 960 && settings.WindowHeight == 540, "display preferences survive process restart");
        Check(settings.SpeedUnit == SpeedUnit.MilesPerHour && settings.ShowFps && !settings.ShowPing, "independent HUD preferences survive process restart");
        Check(_player.Adapter.InvertSteering && _player.Adapter.DeadZone == 0.25f, "input configuration restored");
        Check(settings.Bindings[InputAction.Accelerate].SequenceEqual(new[] { "key:74", "button:2:3", "axis:2:2:-1" }), "all supported binding types and exact device IDs survive");
        Check(settings.Bindings[InputAction.Pause].Count == 0, "explicit unbind survives restart");
        SendKey(Key.J, true);
        Check(_player.Adapter.Capture(1).Accelerate is > 0 and < ushort.MaxValue, "restored physical key drives progressive logical input");
        for (ulong tick = 2; tick <= 31; tick++)
        {
            _player.Adapter.Capture(tick);
        }

        Check(_player.Adapter.Capture(32).Accelerate == ushort.MaxValue, "restored key reaches full throttle");
        SendKey(Key.J, false);
        for (ulong tick = 33; tick <= 62; tick++)
        {
            _player.Adapter.Capture(tick);
        }

        SendKey(Key.W, true);
        Check(_player.Adapter.Capture(63).Accelerate == 0, "replaced default key stays removed");
        SendKey(Key.W, false);
        SendKey(Key.D, true);
        Check(_player.Adapter.Capture(64).Steering is < 0 and > -32767, "restored inversion affects signed steering");
        SendKey(Key.D, false);
    }

    private void VerifyConsumers()
    {
        var hud = new SettingsHud();
        hud.Initialize(_settings);
        AddChild(hud);
        var simulation = new Simulation(new SimulationConfiguration(60));
        SimulationState state = simulation.State;
        hud.SetTelemetry(default);
        Check(!hud.FindChildren("*", "Label", true, false).Cast<Label>().Any(label => label.Text.StartsWith("Speed", StringComparison.Ordinal)), "Obsolete diagnostics speed control is absent");
        foreach (bool fps in new[] { false, true })
        {
            foreach (bool ping in new[] { false, true })
            {
                _settings.UpdateSettings(_settings.Current with { ShowFps = fps, ShowPing = ping });
                Check(hud.FpsVisible == fps && hud.PingVisible == ping, "FPS/Ping visibility changes immediately and independently");
            }
        }

        _settings.UpdateSettings(_settings.Current with { SpeedUnit = SpeedUnit.KilometresPerHour });
        Check(simulation.State.Equals(state), "unit changes leave authoritative state unchanged");
        Check(AudioServer.IsBusMute(AudioServer.GetBusIndex("Master")), "zero master volume mutes");
        Check(Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music")) - (20 * Math.Log10(0.25))) < 0.001, "music gain reaches runtime bus");
        Check(Math.Abs(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("SFX")) - (20 * Math.Log10(0.75))) < 0.001, "SFX gain reaches runtime bus");
        _settings.UpdateSettings(_settings.Current with { MasterVolume = 0.5 });
        Check(!AudioServer.IsBusMute(AudioServer.GetBusIndex("Master")), "raising master volume unmutes");
    }

    private void VerifyMalformedBindings()
    {
        _player.Adapter.Bindings.RestoreDefaults();
        InputBindingPreferences.Apply(_player.Adapter, PlayerSettingsJson.Deserialize("""
            {"bindings":{"RemovedAction":["key:74"],"Accelerate":["key:999999999999"],
            "Brake":["button:-1:0"],"SteerLeft":["axis:0:999:1"],"SteerRight":["key:74","bad-token"]}}
            """));
        foreach (InputAction action in new[] { InputAction.Accelerate, InputAction.Brake, InputAction.SteerLeft, InputAction.SteerRight })
        {
            InputEvent[] bindings = _player.Adapter.Bindings.CopyBindings(action);
            Check(bindings.Length == 2 && bindings[0] is InputEventKey, "malformed override preserves complete default action");
            foreach (InputEvent binding in bindings)
            {
                binding.Dispose();
            }
        }
    }

    private async Task VerifyUi(string path)
    {
        var panel = new SettingsPanel();
        panel.Initialize(_settings, _player.Adapter);
        _settings.AddChild(panel);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Press(panel, "Settings");
        Press(panel, "Audio");
        Check(_player.Adapter.GameplaySuppressed, "opening settings suppresses gameplay");
        SendKey(Key.W, true);
        Check(_player.Adapter.Capture(1).Accelerate == 0, "settings keystrokes cannot accelerate");
        SendKey(Key.W, false);
        foreach (CheckButton toggle in Descendants(panel).OfType<CheckButton>())
        {
            toggle.ButtonPressed = true;
        }

        HSlider[] sliders = Descendants(panel).OfType<HSlider>().ToArray();
        sliders[0].Value = 62;
        sliders[1].Value = 37;
        sliders[2].Value = 81;
        Check(_settings.Current.MasterVolume == 0.62 && _settings.Current.MusicVolume == 0.37 && _settings.Current.SfxVolume == 0.81, "UI volume controls update preferences");
        Check(_settings.Current.ShowFps && _settings.Current.ShowPing && _settings.Current.InvertSteering, "UI toggles update preferences");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using (Image screenshot = GetViewport().GetTexture().GetImage())
        {
            Check(screenshot.SavePng(path + ".png") == Error.Ok, "rendered settings screenshot saved");
        }

        var scroll = Descendants(panel).OfType<ScrollContainer>().Single();
        scroll.ScrollVertical = 600;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using (Image screenshot = GetViewport().GetTexture().GetImage())
        {
            Check(screenshot.SavePng(path + ".controls.png") == Error.Ok, "rendered controls screenshot saved");
        }

        Press(panel, "Back");
        Press(panel, "Gameplay");
        HSlider shake = Descendants(panel).OfType<HSlider>().Single(slider => slider.IsVisibleInTree());
        foreach (double amount in new[] { 0d, 25d, 50d, 100d })
        {
            shake.Value = amount;
            Check(_settings.Current.CameraShakeIntensity == amount / 100, "Gameplay camera shake slider applies immediately");
        }
        shake.Value = 35;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using (Image screenshot = GetViewport().GetTexture().GetImage())
        {
            Check(screenshot.SavePng(path + ".gameplay.png") == Error.Ok, "camera shake settings screenshot saved");
        }
        Press(panel, "Back");
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        Check(new PlayerSettingsStore(path).Load().CameraShakeIntensity == 0.35, "camera shake UI value persists after normal debounce");
        Press(panel, "Controls");
        Button bindingButton = Descendants(panel).OfType<Button>().Single(button => button.Name == "Binding_Accelerate");
        bindingButton.EmitSignal(BaseButton.SignalName.Pressed);
        SendKey(Key.J, true);
        SendKey(Key.J, false);
        Check(_settings.Current.Bindings[InputAction.Accelerate][^1] == "key:74", "UI native key capture persists remap");
        Press(panel, "Restore default bindings");
        Check(_settings.Current.Bindings[InputAction.Accelerate][0] == "key:87", "UI restores default bindings");
        Press(panel, "Back");
        Press(panel, "Video");
        OptionButton mode = Descendants(panel).OfType<OptionButton>().First();
        mode.Selected = 1;
        mode.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        Press(panel, "Preview display change");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen && !_settings.Current.Fullscreen, "fullscreen preview is not persisted prematurely");
        panel._Process(16);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed && !_settings.Current.Fullscreen, "expired display preview reverts");
        OptionButton resolution = Descendants(panel).OfType<OptionButton>().Skip(1).First();
        resolution.Selected = 0;
        Press(panel, "Preview display change");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(DisplayServer.WindowGetSize() == new Vector2I(640, 360), "selected window resolution applies");
        Press(panel, "Keep");

        Check(_settings.Current.WindowWidth == 640 && _settings.Current.WindowHeight == 360, "confirmed resolution updates preferences");
        scroll.ScrollVertical = 0;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using (Image screenshot = GetViewport().GetTexture().GetImage())
        {
            Check(screenshot.SavePng(path + ".small.png") == Error.Ok, "smallest supported window screenshot saved");
        }

        mode.Selected = 1;
        Press(panel, "Preview display change");
        Press(panel, "Keep");
        Check(_settings.Current.Fullscreen, "confirmed display preview commits");
        Press(panel, "Back");
        Press(panel, "Back");
        Check(!_player.Adapter.GameplaySuppressed, "closing settings restores input routing");
        Check(new PlayerSettingsStore(path).Load().Fullscreen, "UI display confirmation persisted");
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
