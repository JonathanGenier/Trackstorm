using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trackstorm.Client.Online;

/// <summary>Validated EOS settings; credentials are never rendered by diagnostics.</summary>
internal sealed class EosConfiguration
{
    /// <summary>Gets the explicit deployment environment; only development is enabled.</summary>
    public string Environment { get; init; } = string.Empty;

    /// <summary>Gets the safe human-readable deployment label.</summary>
    public string DeploymentName { get; init; } = string.Empty;

    /// <summary>Gets the portal product identifier.</summary>
    public string ProductId { get; init; } = string.Empty;

    /// <summary>Gets the portal sandbox identifier.</summary>
    public string SandboxId { get; init; } = string.Empty;

    /// <summary>Gets the portal deployment identifier.</summary>
    public string DeploymentId { get; init; } = string.Empty;

    /// <summary>Gets the untrusted game-client identifier.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Gets the restricted game-client credential; never log it.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>Reads and validates an override file without exposing its contents in errors.</summary>
    /// <param name="path">Override configuration file path.</param>
    /// <returns>The validated configuration.</returns>
    public static EosConfiguration Load(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 16384)
            {
                throw new InvalidOperationException("EOS configuration exceeds 16 KiB.");
            }

            var configuration = JsonSerializer.Deserialize<EosConfiguration>(File.ReadAllText(path), new JsonSerializerOptions
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            }) ?? throw new InvalidOperationException("EOS configuration is empty.");
            configuration.Validate();
            return configuration;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException("Cannot read the EOS configuration override. Supply a readable JSON file; see docs/eos-development.md.");
        }
    }

    /// <summary>Rejects missing, unsafe or unsupported configuration before native calls.</summary>
    public void Validate()
    {
        if (Environment != "development")
        {
            throw new InvalidOperationException("EOS Environment must be development. Production requires a separately reviewed configuration path.");
        }

        if (string.IsNullOrWhiteSpace(DeploymentName) || DeploymentName.Length > 32 || DeploymentName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
        {
            throw new InvalidOperationException("EOS DeploymentName must be a safe 1–32 character label (letters, digits, hyphen, underscore).");
        }

        ValidateValue(ProductId, nameof(ProductId), 64);
        ValidateValue(SandboxId, nameof(SandboxId), 64);
        ValidateValue(DeploymentId, nameof(DeploymentId), 64);
        ValidateValue(ClientId, nameof(ClientId), 64);
        ValidateValue(ClientSecret, nameof(ClientSecret), 64);
    }

    /// <inheritdoc />
    public override string ToString() => "EOS configuration (credentials redacted)";

    private static void ValidateValue(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(c => c < 33 || c > 126) || value.Contains('<') || value.Contains('>'))
        {
            throw new InvalidOperationException($"EOS {name} is missing or malformed. Copy the development value from Product Settings; see docs/eos-development.md.");
        }
    }
}
