using System.Collections.ObjectModel;

namespace Trackstorm.Core.Settings;

/// <summary>Immutable local tire graphics preferences; never included in session configuration or replication.</summary>
public sealed class TireEffectSettings
{
    private readonly IReadOnlyDictionary<string, float> _values;
    /// <summary>Canonical control catalog shared by Configs and tolerant local persistence.</summary>
    public static IReadOnlyList<TireEffectOption> Options { get; } = CreateOptions();
    /// <summary>Default local presentation.</summary>
    public static TireEffectSettings Defaults { get; } = new(Options.ToDictionary(option => option.Key, option => option.Default));
    private TireEffectSettings(Dictionary<string, float> values) => _values = new ReadOnlyDictionary<string, float>(values);
    /// <summary>Accepted overrides for local persistence.</summary>
    public IReadOnlyDictionary<string, float> Values => _values;
    /// <summary>Reads a known catalog key, falling back to its canonical default.</summary>
    public float this[string key] => _values[key];
    /// <summary>Validates one complete staged transaction without partially applying invalid input.</summary>
    public bool TryApply(IReadOnlyDictionary<string, double> edits, out TireEffectSettings result, out string error)
    {
        result = this;
        var values = new Dictionary<string, float>(_values);
        foreach (var (key, value) in edits)
        {
            var option = Options.FirstOrDefault(option => option.Key == key);
            if (option is null || !option.Accepts(value))
            {
                error = option is null ? $"Unknown tire control: {key}" : $"{option.Label}: expected {option.Minimum}–{option.Maximum}{(option.Integral ? " (integer)" : string.Empty)}.";
                return false;
            }
            values[key] = (float)value;
        }
        result = new(values);
        error = string.Empty;
        return true;
    }

    private static IReadOnlyList<TireEffectOption> CreateOptions()
    {
        var options = new List<TireEffectOption>
        {
            new("tire.lifetime", "Tire marks · Local graphics", "Persistence (seconds)", 60, 1, 180),
            new("tire.fade", "Tire marks · Local graphics", "Fade duration (fraction of lifetime)", .3f, .01f, 1),
            new("tire.width", "Tire marks · Local graphics", "Width multiplier", 1, .1f, 3),
            new("tire.intensity", "Tire marks · Local graphics", "Opacity multiplier", 1, 0, 2),
            new("tire.spacing", "Tire marks · Local graphics", "Minimum segment spacing (metres)", .35f, .1f, 2),
            new("tire.speed", "Tire marks · Local graphics", "Minimum speed (m/s)", .8f, 0, 30),
            new("tire.spray_speed", "Tire marks · Local graphics", "Minimum spray speed (m/s)", 2, 0, 30),
            new("tire.slip", "Tire marks · Local graphics", "Hard-surface minimum slip", .35f, 0, 1),
            new("tire.budget", "Tire marks · Local graphics", "Arena segment budget (oldest recycled)", 49152, 256, 65536, true),
            new("tire.distance", "Tire marks · Local graphics", "Visibility distance (metres)", 100, 20, 250),
            new("tire.quality", "Tire marks · Local graphics", "Sampling density (0 disables emission)", 1, 0, 1),
        };
        foreach (string surface in new[] { "asphalt", "concrete", "dirt", "grass", "mud", "deep_mud", "water" })
        {
            bool water = surface == "water";
            string group = "Tire effects · " + surface.Replace('_', ' ');
            options.Add(new($"tire.{surface}.duration", group, water ? "Wake duration (seconds)" : "Disturbance duration multiplier", water ? .9f : 1, water ? .1f : .05f, water ? 3 : 2));
            options.Add(new($"tire.{surface}.width", group, "Width multiplier", 1, .1f, 3));
            options.Add(new($"tire.{surface}.intensity", group, "Intensity multiplier", 1, 0, 2));
            options.Add(new($"tire.{surface}.fade", group, "Fade duration multiplier", 1, .1f, 3));
            options.Add(new($"tire.{surface}.density", group, "Emission density (0 disables surface)", 1, 0, 1));
            options.Add(new($"tire.{surface}.speed", group, "Minimum speed multiplier", 1, 0, 10));
        }
        return options.AsReadOnly();
    }
}
