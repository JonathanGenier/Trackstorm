using System.Text;
using Trackstorm.Core.Development;

namespace Trackstorm.Client.Development;

/// <summary>Host-local atomic storage, separate from user preferences and never applied by joining clients.</summary>
internal sealed class DeveloperSettingsStore
{
    private readonly string _path;
    private DeveloperSettingsFile _file;

    /// <summary>Loads host-local overrides independently of user preferences.</summary>
    /// <param name="path">Host-local persistence file path.</param>
    internal DeveloperSettingsStore(string path)
    {
        _path = path;
        var defaults = GameplayConfiguration.HostedDefaults;
        _file = DeveloperSettingsFile.Read(string.Empty, defaults);
        try
        {
            if (File.Exists(path))
            {
                _file = DeveloperSettingsFile.Read(new FileInfo(path).Length <= 65536 ? File.ReadAllText(path) : new string(' ', 65537), defaults);
            }

            Status = !_file.CanSave ? "File schema unsupported; persistence disabled."
                : _file.RejectedRecords > 0 ? $"Loaded valid tuning; ignored {_file.RejectedRecords} invalid records." : "Host tuning loaded.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = "Developer settings could not be read; using canonical defaults.";
        }

        Current = _file.Configuration;
    }

    /// <summary>Most recent accepted host-local tuning.</summary>
    internal GameplayConfiguration Current { get; private set; }
    /// <summary>Safe persistence status without file contents or credentials.</summary>
    internal string Status { get; private set; } = string.Empty;

    /// <summary>Called only by host composition; the joined client's file never enters its driver.</summary>
    /// <returns>Most recent accepted host tuning.</returns>
    internal GameplayConfiguration LoadForHost() => Current;

    /// <summary>Persists a successfully accepted host transaction with atomic replacement.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    internal bool Save(GameplayConfiguration configuration)
    {
        Current = configuration;
        try
        {
            string text = _file.Write(configuration);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, _path, true);
            Status = "Host tuning saved.";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = "Applied for this session; saving failed. Press Apply Settings to retry.";
            return false;
        }
    }
}
