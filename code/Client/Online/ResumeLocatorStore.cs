using System.Text.Json;

namespace Trackstorm.Client.Online;

/// <summary>Best-effort ephemeral routing persistence containing no password, ticket or platform credential.</summary>
internal sealed class ResumeLocatorStore(string path)
{
    /// <summary>Loads only a bounded, unexpired locator for the current authenticated user.</summary>
    /// <param name="identity">Current authenticated identity.</param>
    /// <param name="now">Injectable UTC time.</param>
    /// <returns>Validated routing hint, otherwise absent.</returns>
    internal ResumeLocator? Load(string identity, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            if (new FileInfo(path).Length <= 4096)
            {
                var locator = JsonSerializer.Deserialize<ResumeLocator>(File.ReadAllText(path));
                if (locator is not null && locator.Identity == identity && locator.Lobby?.Length is > 0 and <= 256 && locator.Host?.Length == 32 &&
                    locator.Session is > 0 and < ulong.MaxValue && locator.Player > 0 && locator.Generation is > 0 and < ulong.MaxValue && locator.Expires > now && locator.Expires <= now.AddMinutes(3))
                {
                    return locator;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Persistence is optional; failure never grants or changes gameplay identity.
        }

        Clear();
        return null;
    }

    /// <summary>Atomically replaces the short-lived routing hint; failure leaves normal in-process resume available.</summary>
    /// <param name="locator">Current acknowledged connection boundary.</param>
    internal void Save(ResumeLocator locator)
    {
        try
        {
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(locator));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No long-term credentials or additional resume secrets are persisted.
        }
    }

    /// <summary>Clears hints on explicit leave, terminal rejection, expiry or account switch.</summary>
    internal void Clear()
    {
        try
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The local deadline and host authorization still make stale files inert.
        }
    }
}
