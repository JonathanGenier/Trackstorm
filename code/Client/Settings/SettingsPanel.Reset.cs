namespace Trackstorm.Client.Settings;

internal sealed partial class SettingsPanel
{
    private void ConfirmUserReset()
    {
        _resetPending = true;
        _confirmText.Text = "Reset all user settings and input bindings to their defaults? Progress, accounts and developer tuning will be preserved.";
        _keep.Text = "Reset";
        _revert.Text = "Cancel";
        _scroll.Hide();
        _confirmation.Show();
        _revert.GrabFocus();
    }

    private void EndUserReset()
    {
        _resetPending = false;
        _confirmation.Hide();
        _scroll.Show();
        _keep.Text = "Keep";
        _revert.Text = "Revert";
        ShowPage();
    }

    private void ResetUserSettings()
    {
        _settings.ResetUserSettings();
        foreach (var refresh in _refreshPreferences) refresh();
        RefreshDisplaySelection();
        RefreshBindings();
        EndUserReset();
    }
}
