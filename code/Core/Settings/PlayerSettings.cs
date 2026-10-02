using System.Collections.ObjectModel;
using Trackstorm.Core.Input;

namespace Trackstorm.Core.Settings;

/// <summary>Validated, immutable local preference data. Contains no device, filesystem, or engine APIs.</summary>
public sealed record PlayerSettings
{
    /// <summary>Local cosmetic mark preferences, excluded from synchronized gameplay.</summary>
    public TireEffectSettings TireEffects { get; init; } = TireEffectSettings.Defaults;
    private double _masterVolume = 1;
    private double _musicVolume = 1;
    private double _sfxVolume = 1;
    private double _cameraShakeIntensity = 1;
    private SpeedUnit _speedUnit;
    private int _windowWidth = 1280;
    private int _windowHeight = 720;
    private double _deadZone = 0.15;
    private double _steeringSensitivity = 1;
    private double _aerialSensitivity = 1;
    private double _keyboardSteeringSensitivity = 1;
    private double _keyboardAerialSensitivity = 1;
    private IReadOnlyDictionary<InputAction, IReadOnlyList<string>> _bindings =
        new ReadOnlyDictionary<InputAction, IReadOnlyList<string>>(new Dictionary<InputAction, IReadOnlyList<string>>());

    /// <summary>Master gain in [0,1]; non-finite input restores full gain.</summary>
    public double MasterVolume { get => _masterVolume; init => _masterVolume = Volume(value); }

    /// <summary>Music gain in [0,1].</summary>
    public double MusicVolume { get => _musicVolume; init => _musicVolume = Volume(value); }

    /// <summary>Sound-effects gain in [0,1].</summary>
    public double SfxVolume { get => _sfxVolume; init => _sfxVolume = Volume(value); }

    /// <summary>Local collision/damage camera feedback scale in [0,1]; zero disables shake.</summary>
    public double CameraShakeIntensity { get => _cameraShakeIntensity; init => _cameraShakeIntensity = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1; }

    /// <summary>Whether to request fullscreen; defaults to a recoverable window.</summary>
    public bool Fullscreen { get; init; }

    /// <summary>Requested window width; Client additionally checks the current screen.</summary>
    public int WindowWidth { get => _windowWidth; init => _windowWidth = value is >= 640 and <= 7680 ? value : 1280; }

    /// <summary>Requested window height; Client additionally checks the current screen.</summary>
    public int WindowHeight { get => _windowHeight; init => _windowHeight = value is >= 360 and <= 4320 ? value : 720; }

    /// <summary>Speed display units, independent of authoritative state.</summary>
    public SpeedUnit SpeedUnit { get => _speedUnit; init => _speedUnit = Enum.IsDefined(value) ? value : SpeedUnit.KilometresPerHour; }

    /// <summary>Independent frames-per-second counter visibility.</summary>
    public bool ShowFps { get; init; }

    /// <summary>Independent network-latency visibility.</summary>
    public bool ShowPing { get; init; }

    /// <summary>Signed steering inversion consumed by the existing input adapter.</summary>
    public bool InvertSteering { get; init; }

    /// <summary>Analog neutral range; invalid values restore the input system's default.</summary>
    public double DeadZone { get => _deadZone; init => _deadZone = double.IsFinite(value) && value >= 0 && (float)value < 1 ? value : 0.15; }

    /// <summary>Ground controller gain after precision shaping, bounded to 0.1–3.</summary>
    public double SteeringSensitivity { get => _steeringSensitivity; init => _steeringSensitivity = Sensitivity(value); }

    /// <summary>Independent aerial controller gain, bounded to 0.1–3.</summary>
    public double AerialSensitivity { get => _aerialSensitivity; init => _aerialSensitivity = Sensitivity(value); }

    /// <summary>Opaque Client-owned binding tokens per logical action. Missing means defaults; empty means unbound.</summary>
    public IReadOnlyDictionary<InputAction, IReadOnlyList<string>> Bindings => _bindings;

    /// <summary>Client binding-default revision; zero identifies saves predating default migration tracking.</summary>
    public int BindingDefaultsVersion { get; init; } = 2;

    /// <summary>Copies an override without exposing mutable collections or interpreting native binding tokens.</summary>
    /// <param name="action">Existing logical action.</param>
    /// <param name="tokens">Serialized bindings supplied by Client.</param>
    /// <returns>A new preference snapshot.</returns>
    public PlayerSettings WithBindings(InputAction action, IEnumerable<string> tokens)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        ArgumentNullException.ThrowIfNull(tokens);
        string[] copy = tokens.ToArray();
        if (copy.Length > 32 || copy.Any(token => string.IsNullOrWhiteSpace(token) || token.Length > 128))
        {
            throw new ArgumentException("Bindings require at most 32 nonempty tokens of at most 128 characters.", nameof(tokens));
        }

        var bindings = new Dictionary<InputAction, IReadOnlyList<string>>(_bindings)
        {
            [action] = Array.AsReadOnly(copy),
        };
        return this with { _bindings = new ReadOnlyDictionary<InputAction, IReadOnlyList<string>>(bindings) };
    }

    /// <summary>Keyboard/mouse steering ramp-rate multiplier in [0.1,3].</summary>
    public double KeyboardSteeringSensitivity { get => _keyboardSteeringSensitivity; init => _keyboardSteeringSensitivity = Sensitivity(value); }

    /// <summary>Keyboard/mouse aerial rate gain in [0.1,1]; one retains full authority.</summary>
    public double KeyboardAerialSensitivity { get => _keyboardAerialSensitivity; init => _keyboardAerialSensitivity = double.IsFinite(value) ? Math.Clamp(value, 0.1, 1) : 1; }

    private static double Sensitivity(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.1, 3) : 1;

    private static double Volume(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
}
