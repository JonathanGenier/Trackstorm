using System.Text.Json;

namespace Trackstorm.Client.Online;

/// <summary>Public service address bundled with the game; no player configuration is required.</summary>
internal static class LeaseEndpointConfiguration
{
    /// <summary>Resolves the bundled URL with an optional explicit developer override.</summary>
    /// <returns>The validated HTTPS base address.</returns>
    internal static Uri Resolve()
    {
        using var stream = typeof(LeaseEndpointConfiguration).Assembly.GetManifestResourceStream("Trackstorm.Client.AuthorityLeaseEndpoint.json")
            ?? throw new InvalidOperationException("This build is missing online coordination configuration.");
        using var document = JsonDocument.Parse(stream);
        return Resolve(System.Environment.GetEnvironmentVariable("TRACKSTORM_LEASE_URL"), document.RootElement.GetProperty("url").GetString());
    }

    /// <summary>Validates configuration without environment or engine dependencies.</summary>
    /// <param name="overrideUrl">Explicit developer/test override, or null for the bundled value.</param>
    /// <param name="bundledUrl">Public endpoint prepared by the release owner.</param>
    /// <returns>Canonical service base address.</returns>
    internal static Uri Resolve(string? overrideUrl, string? bundledUrl)
    {
        string? value = overrideUrl ?? bundledUrl;
        if (overrideUrl is null && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Online coordination is not configured in this build. Use a configured Trackstorm build.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || value != value.Trim())
        {
            throw new InvalidOperationException(overrideUrl is null ? "The bundled online coordination address is invalid." : "TRACKSTORM_LEASE_URL must be a trusted HTTPS service address without credentials, query or fragment.");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
