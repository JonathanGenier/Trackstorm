using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Settings;

/// <summary>Owns local preference loading, runtime application, change notification, and debounced saving.</summary>
public sealed partial class PlayerSettingsController : Node
{
    private PlayerSettingsStore _store = null!;
    private PlayerInputAdapter _input = null!;
    private double _saveDelay = -1;

    /// <summary>Published after a new snapshot is applied; consumers never access the store.</summary>
    public event Action? Changed;

    /// <summary>Notifies UI of pending, successful, or failed persistence.</summary>
    public event Action? SaveStatusChanged;

    /// <summary>The currently applied preference snapshot.</summary>
    public PlayerSettings Current { get; private set; } = new();

    /// <summary>Player-facing save result.</summary>
    public string SaveStatus { get; private set; } = "Settings save automatically.";

    /// <summary>Applies scalar preferences and queues a save. Bindings remain input-owned and are captured through CaptureInput.</summary>
    /// <param name="settings">Validated preferences to apply.</param>
    public void UpdateSettings(PlayerSettings settings)
    {
        Current = settings;
        _input.InvertSteering = settings.InvertSteering;
        _input.DeadZone = (float)settings.DeadZone;
        ApplyAudio();
        _saveDelay = 0.3;
        SaveStatus = "Saving…";
        Changed?.Invoke();
        SaveStatusChanged?.Invoke();
    }

    /// <summary>Retries or immediately completes a pending save.</summary>
    /// <returns>Whether saving succeeded.</returns>
    public bool Flush()
    {
        Current = InputBindingPreferences.Capture(_input, Current);
        bool saved = _store.TrySave(Current, out string? error);
        SaveStatus = saved ? "Saved on this device." : error!;
        _saveDelay = -1;
        SaveStatusChanged?.Invoke();
        return saved;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_saveDelay >= 0)
        {
            _saveDelay -= delta;
            if (_saveDelay <= 0)
            {
                Flush();
            }
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_store is not null)
        {
            Flush();
        }
    }

    /// <summary>Applies supported window settings. Fullscreen uses the native desktop resolution.</summary>
    /// <param name="settings">Requested display preferences.</param>
    internal static void ApplyDisplay(PlayerSettings settings)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        DisplayServer.WindowSetMode(settings.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        if (!settings.Fullscreen)
        {
            Rect2I usable = DisplayServer.ScreenGetUsableRect();
            var size = new Vector2I(Math.Min(settings.WindowWidth, usable.Size.X), Math.Min(settings.WindowHeight, usable.Size.Y));
            DisplayServer.WindowSetSize(size);
            DisplayServer.WindowSetPosition(usable.Position + ((usable.Size - size) / 2));
        }
    }

    /// <summary>Composes the store and existing input owner before adding this node to the tree.</summary>
    /// <param name="input">Existing input owner.</param>
    /// <param name="path">Local settings file.</param>
    internal void Initialize(PlayerInputAdapter input, string path)
    {
        _input = input;
        _store = new PlayerSettingsStore(path);
        Current = _store.Load();
        InputBindingPreferences.Apply(_input, Current);
        Current = InputBindingPreferences.Capture(_input, Current);
        ApplyAudio();
        ApplyDisplay(Current);
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Captures remaps through the input system's own serialization boundary.</summary>
    internal void CaptureInput() => UpdateSettings(InputBindingPreferences.Capture(_input, Current));

    private static void SetBus(string name, double volume)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }

        AudioServer.SetBusMute(index, volume == 0);
        AudioServer.SetBusVolumeDb(index, volume == 0 ? -80 : (float)(20 * Math.Log10(volume)));
    }

    private void ApplyAudio()
    {
        SetBus("Master", Current.MasterVolume);
        SetBus("Music", Current.MusicVolume);
        SetBus("SFX", Current.SfxVolume);
    }
}
