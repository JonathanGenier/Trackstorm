using Godot;
using Trackstorm.Core.Development;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Development;

internal sealed partial class DeveloperOptionsPanel
{
    private FileDialog? _importDialog;
    private object? _importOwner;
    private ulong _importEpoch;

    private object? ImportOwner() => (object?)Session()?.Arena?.Driver ?? Session()?.Lobby;

    private void ImportConfigs()
    {
        if (_importOwner is not null || _exportText is not null || AwaitingConfirmation || Session()?.CanConfigureDeveloperOptions != true) return;
        _importOwner = ImportOwner();
        _importEpoch = Session()!.Lobby!.State!.AuthorityEpoch;
        if (_importDialog is null)
        {
            _importDialog = new FileDialog
            {
                Title = "Import Configs", FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, Transient = true, Exclusive = true,
                Filters = ["*.txt ; Trackstorm config exports"],
            };
            AddChild(_importDialog);
            _importDialog.FileSelected += LoadImport;
            _importDialog.Canceled += () => { _importOwner = null; _status.Text = "Import cancelled."; };
        }
        _importDialog.CurrentFile = "configs.txt";
        _importDialog.PopupCenteredRatio();
    }

    private void LoadImport(string path)
    {
        if (_importOwner is null) return;
        try
        {
            if (!ReferenceEquals(_importOwner, ImportOwner()) || Session()?.Lobby?.State?.AuthorityEpoch != _importEpoch ||
                Session()?.CanConfigureDeveloperOptions != true || AwaitingConfirmation)
            { _status.Text = "Session changed. Reopen Import Configs to try again."; return; }
            // Bounded read even if the file grows or changes while the picker is open.
            using var file = System.IO.File.OpenRead(path);
            byte[] bytes = new byte[ConfigurationChangesImport.MaximumLength + 1];
            int count = 0;
            while (count < bytes.Length)
            {
                int read = file.Read(bytes, count, bytes.Length - count);
                if (read == 0) break;
                count += read;
            }
            if (count > ConfigurationChangesImport.MaximumLength) { _status.Text = "Config file exceeds 64 KiB."; return; }
            string text = new System.Text.UTF8Encoding(false, true).GetString(bytes, 0, count);
            if (!ConfigurationChangesImport.TryParse(text, Session()!.DeveloperConfiguration, out var edits, out string version, out string error))
            { _status.Text = error; return; }
            _draft.Import(edits);
            RenderValues();
            UpdateFeedback();
            _status.Text = version == GameVersion.Current.ToString()
                ? "Listed settings imported into draft. Apply Settings to share them."
                : $"Imported listed settings from v{version}. Review, then Apply Settings.";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { _status.Text = "Could not read config export. Choose a readable UTF-8 text file."; }
        finally { _importOwner = null; }
    }
}
