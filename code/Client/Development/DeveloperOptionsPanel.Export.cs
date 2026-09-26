using Godot;
using Trackstorm.Core.Development;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Development;

internal sealed partial class DeveloperOptionsPanel
{
    private FileDialog? _exportDialog;
    private string? _exportText;

    private void SubmitReset(string? group)
    {
        var edits = GameplayOptions.All.Where(option => group is null || option.Group == group)
            .ToDictionary(option => option.Key, option => option.Read(GameplayConfiguration.HostedDefaults));
        var session = Session();
        string error = "Shared configuration unavailable.";
        if (session?.ConfigureDeveloperOptions(edits, out error) != true)
        {
            _status.Text = error;
            return;
        }
        if (session.Lobby?.ConfigurationPending == true)
        {
            _submittedEdits = edits;
            _status.Text = "Waiting for host reset confirmation…";
            return;
        }
        _draft.Accept(edits, session.DeveloperConfiguration);
        RenderValues();
        _status.Text = "Shared defaults applied.";
        SetFeedback(HasUnappliedChanges ? DeveloperOptionsFeedbackState.Unsaved : DeveloperOptionsFeedbackState.Applied);
    }

    private void ExportChanges()
    {
        if (_exportText is not null || Session() is not { CanConfigureDeveloperOptions: true } session) return;
        // Capture one confirmed boundary when the action is invoked, independently of drafts/dialog duration.
        _exportText = ConfigurationChangesExport.Format(session.DeveloperConfiguration, GameVersion.Current.ToString(), DateTimeOffset.UtcNow);
        if (_exportDialog is null)
        {
            _exportDialog = new FileDialog
            {
                Title = "Export Changes", FileMode = FileDialog.FileModeEnum.SaveFile,
                Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, Transient = true, Exclusive = true,
                Filters = ["*.txt ; Text files"],
            };
            AddChild(_exportDialog);
            _exportDialog.FileSelected += SaveExport;
            _exportDialog.Canceled += () => { _exportText = null; _status.Text = "Export cancelled."; };
        }
        _exportDialog.CurrentFile = "configs.txt";
        _exportDialog.PopupCenteredRatio();
    }

    private void SaveExport(string path)
    {
        if (_exportText is null) return;
        try
        {
            System.IO.File.WriteAllText(path, _exportText, new System.Text.UTF8Encoding(false));
            _status.Text = "Configuration changes exported.";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _status.Text = "Could not save export. Choose a writable destination and try again.";
        }
        finally { _exportText = null; }
    }
}
