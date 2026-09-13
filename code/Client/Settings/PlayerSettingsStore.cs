using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Settings;

/// <summary>Single local file boundary. Writes a sibling temporary file before replacing the committed snapshot.</summary>
internal sealed class PlayerSettingsStore
{
    private readonly string _path;

    /// <summary>Creates a store at an explicit path, allowing isolated verification.</summary>
    /// <param name="path">Local settings file.</param>
    public PlayerSettingsStore(string path)
    {
        _path = path;
    }

    /// <summary>Loads defaults when the file is absent, inaccessible, oversized, or corrupt.</summary>
    /// <returns>A validated snapshot.</returns>
    public PlayerSettings Load()
    {
        try
        {
            return File.Exists(_path) && new FileInfo(_path).Length <= 262144
                ? PlayerSettingsJson.Deserialize(File.ReadAllText(_path)) : new PlayerSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PlayerSettings();
        }
    }

    /// <summary>Commits one snapshot; returns a player-facing error without discarding the previous file.</summary>
    /// <param name="settings">Snapshot to save.</param>
    /// <param name="error">Failure message, or null on success.</param>
    /// <returns>Whether the file was committed.</returns>
    public bool TrySave(PlayerSettings settings, out string? error)
    {
        string temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(PlayerSettingsJson.Serialize(settings));
                writer.Flush();
                stream.Flush(true);
            }

            File.Move(temporary, _path, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "Settings could not be saved. Check free space and folder permissions, then retry.";
            return false;
        }
    }
}
