using System.Text.Json;
using Trackstorm.Core.Input;

namespace Trackstorm.Core.Settings;

/// <summary>Versioned, tolerant preference serialization without filesystem access.</summary>
public static class PlayerSettingsJson
{
    /// <summary>Writes explicit stable field and enum names, independent of CLR enum numbering.</summary>
    /// <param name="settings">Validated snapshot.</param>
    /// <returns>Version-one JSON.</returns>
    public static string Serialize(PlayerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(new
        {
            version = 1,
            masterVolume = settings.MasterVolume,
            musicVolume = settings.MusicVolume,
            sfxVolume = settings.SfxVolume,
            cameraShakeIntensity = settings.CameraShakeIntensity,
            tireEffects = settings.TireEffects.Values,
            fullscreen = settings.Fullscreen,
            windowWidth = settings.WindowWidth,
            windowHeight = settings.WindowHeight,
            speedUnit = settings.SpeedUnit == SpeedUnit.MilesPerHour ? "mph" : "km/h",
            showFps = settings.ShowFps,
            showPing = settings.ShowPing,
            invertSteering = settings.InvertSteering,
            deadZone = settings.DeadZone,
            bindingDefaultsVersion = settings.BindingDefaultsVersion,
            bindings = settings.Bindings.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
        });
    }

    /// <summary>Defaults malformed documents and invalid fields independently; ignores unknown actions and fields.</summary>
    /// <param name="json">Saved JSON, or null for an absent file.</param>
    /// <returns>A safe immutable snapshot.</returns>
    public static PlayerSettings Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 262144)
        {
            return new PlayerSettings();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Number(root, "version", 1) != 1)
            {
                return new PlayerSettings();
            }

            var settings = new PlayerSettings
            {
                MasterVolume = Number(root, "masterVolume", 1),
                MusicVolume = Number(root, "musicVolume", 1),
                SfxVolume = Number(root, "sfxVolume", 1),
                CameraShakeIntensity = Number(root, "cameraShakeIntensity", 1),
                Fullscreen = Boolean(root, "fullscreen"),
                WindowWidth = Integer(root, "windowWidth", 1280),
                WindowHeight = Integer(root, "windowHeight", 720),
                SpeedUnit = root.TryGetProperty("speedUnit", out JsonElement unit) && unit.ValueKind == JsonValueKind.String && unit.GetString() == "mph"
                    ? SpeedUnit.MilesPerHour : SpeedUnit.KilometresPerHour,
                ShowFps = Boolean(root, "showFps"),
                ShowPing = Boolean(root, "showPing"),
                InvertSteering = Boolean(root, "invertSteering"),
                DeadZone = Number(root, "deadZone", 0.15),
                BindingDefaultsVersion = Math.Max(0, Integer(root, "bindingDefaultsVersion", 0)),
            };
            if (root.TryGetProperty("bindings", out JsonElement bindings) && bindings.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in bindings.EnumerateObject())
                {
                    if (!Enum.TryParse(entry.Name, out InputAction action) || !Enum.IsDefined(action) || action.ToString() != entry.Name ||
                        entry.Value.ValueKind != JsonValueKind.Array || entry.Value.GetArrayLength() > 32)
                    {
                        continue;
                    }

                    JsonElement[] values = entry.Value.EnumerateArray().ToArray();
                    if (values.Any(value => value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 128))
                    {
                        continue;
                    }

                    settings = settings.WithBindings(action, values.Select(value => value.GetString()!));
                }
            }

            if (root.TryGetProperty("tireEffects", out JsonElement tire) && tire.ValueKind == JsonValueKind.Object)
            {
                var effects = TireEffectSettings.Defaults;
                foreach (var option in TireEffectSettings.Options)
                {
                    double value = Number(tire, option.Key, option.Default);
                    if (effects.TryApply(new Dictionary<string, double> { [option.Key] = value }, out var accepted, out _)) { effects = accepted; }
                }
                settings = settings with { TireEffects = effects };
            }
            return settings;
        }
        catch (JsonException)
        {
            return new PlayerSettings();
        }
    }

    private static double Number(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result) && double.IsFinite(result) ? result : fallback;

    private static int Integer(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : fallback;

    private static bool Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
