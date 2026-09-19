using System.Text;
using System.Text.Json;

namespace Trackstorm.Core.Development;

/// <summary>Versioned line-delimited JSON. A damaged record cannot discard unrelated valid overrides.</summary>
public sealed class DeveloperSettingsFile
{
    /// <summary>Current host-local file schema, independent of the gameplay wire protocol.</summary>
    public const int SchemaVersion = 2;
    private readonly Dictionary<string, string> _unknown = new(StringComparer.Ordinal);

    /// <summary>Validated effective values, defaulting missing fields to the caller's canonical configuration.</summary>
    public GameplayConfiguration Configuration { get; private set; } = new();
    /// <summary>Future schemas are preserved without rewriting them.</summary>
    public bool CanSave { get; private set; } = true;
    /// <summary>Number of malformed or rejected records; their contents are never logged.</summary>
    public int RejectedRecords { get; private set; }

    /// <summary>Loads bounded independent records and migrates known stable aliases.</summary>
    /// <returns>Validated current value or recovered host-local file.</returns>
    /// <param name="text">Host-local line-delimited JSON records.</param>
    /// <param name="defaults">Canonical defaults for missing or invalid values.</param>
    public static DeveloperSettingsFile Read(string text, GameplayConfiguration? defaults = null)
    {
        var result = new DeveloperSettingsFile { Configuration = defaults ?? new() };
        if (text.Length > 65536)
        {
            result.RejectedRecords = 1;
            result.CanSave = false;
            return result;
        }

        var pending = new Dictionary<string, double>(StringComparer.Ordinal);
        int loadedSchema = 0;
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var record = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8 });
                if (record.RootElement.TryGetProperty("schema", out var version))
                {
                    if (!version.TryGetInt32(out int schema) || schema is < 0 or > SchemaVersion)
                    {
                        result.CanSave = false;
                    }

                    loadedSchema = Math.Max(loadedSchema, schema);

                    continue;
                }

                string key = record.RootElement.GetProperty("key").GetString() ?? throw new FormatException();
                var value = record.RootElement.GetProperty("value");
                key = key == "vehicle.top_speed" ? "vehicle.forward_speed" : key;
                var option = GameplayOptions.All.SingleOrDefault(option => option.Key == key);
                if (option is null)
                {
                    result._unknown[key] = value.GetRawText();
                }
                else if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number))
                {
                    pending[key] = number;
                }
                else if (option.Boolean && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    pending[key] = value.GetBoolean() ? 1 : 0;
                }
                else
                {
                    result.RejectedRecords++;
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                result.RejectedRecords++;
            }
        }

        if (!result.CanSave)
        {
            return result;
        }

        if (loadedSchema < 2)
        {
            // Old files persisted every field, including unchanged pre-conversion dimensions.
            // Adopt canonical geometry for those defaults; preserve deliberate custom tuning.
            foreach (var (key, oldDefault) in new[] { ("vehicle.wheelbase", 2.3f), ("vehicle.load_height", 0.45f), ("vehicle.suspension_length", 0.8f) })
            {
                if (pending.TryGetValue(key, out double value) && Math.Abs(value - oldDefault) < 0.000001)
                {
                    pending.Remove(key);
                }
            }
        }

        // Apply related valid fields together first, then salvage independent values from a damaged transaction.
        if (GameplayOptions.TryApply(result.Configuration, pending, out var complete, out _))
        {
            result.Configuration = complete;
            return result;
        }

        bool progress;
        do
        {
            progress = false;
            foreach (var pair in pending.ToArray())
            {
                if (GameplayOptions.TryApply(result.Configuration, new Dictionary<string, double> { [pair.Key] = pair.Value }, out var next, out _))
                {
                    result.Configuration = next;
                    pending.Remove(pair.Key);
                    progress = true;
                }
            }
        }
        while (progress && pending.Count > 0);

        result.RejectedRecords += pending.Count;
        return result;
    }

    /// <summary>Writes only tuning and retained unknown records; actions and diagnostics have no file keys.</summary>
    /// <returns>Versioned host-local tuning records.</returns>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    public string Write(GameplayConfiguration configuration)
    {
        if (!CanSave)
        {
            throw new InvalidOperationException("Developer settings use an unsupported schema or exceed safe bounds.");
        }

        configuration.Validate();
        var text = new StringBuilder();
        text.AppendLine(JsonSerializer.Serialize(new { schema = SchemaVersion }));
        foreach (var option in GameplayOptions.All)
        {
            text.AppendLine(JsonSerializer.Serialize(new { key = option.Key, value = option.Read(configuration) }));
        }

        foreach (var pair in _unknown.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"{{\"key\":{JsonSerializer.Serialize(pair.Key)},\"value\":{pair.Value}}}");
        }

        string document = text.ToString();
        if (Encoding.UTF8.GetByteCount(document) > 65536)
        {
            throw new InvalidOperationException("Developer settings exceed the safe file size; the existing file is preserved.");
        }

        return document;
    }
}
