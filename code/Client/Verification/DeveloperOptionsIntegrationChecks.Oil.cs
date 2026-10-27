using Godot;
using Trackstorm.Core.Development;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private async Task FinishOilOptions(SubViewport viewport)
    {
        var options = GameplayOptions.All.Where(option => option.Group == "Oil").ToArray();
        Check(options.Length == 18, "all eighteen Oil gameplay, physics, animation and visual controls exercised");
        var search = Descendants(_devTools.Configs).OfType<LineEdit>().Single(editor => editor.Name == "ConfigSearch");
        search.Text = "Oil"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        await Frames(3);
        foreach (var option in options)
        {
            Check(Descendants(_devTools.Configs).OfType<LineEdit>().Any(editor =>
                editor.Name == option.Key.Replace('.', '_') && editor.IsVisibleInTree()), "Oil control visible: " + option.Key);
        }
        Check(!Descendants(_devTools.Configs).OfType<LineEdit>().Any(editor => editor.Name == "items_oil_enemy_contacts"), "no obsolete contact quota control");
        await Capture("oil-configs");
        Press("Reset to Defaults");
        var reset = _host.Arena!.Driver.Configuration;
        Check(options.All(option => option.Read(reset.Configuration) == option.Read(GameplayConfiguration.HostedDefaults)), "Reset restores every current Oil default");
        await Until(() => _client!.Arena!.Driver.Configuration == reset, "Oil defaults reset synchronizes to the remote peer");
        Check(!System.IO.File.Exists(System.IO.Path.Combine(_directory, "settings.json.developer.jsonl")), "session Oil edits do not invent persistent defaults");
        _client!.Leave(); await Until(() => _client.LeaveComplete, "Oil client cleanup");
        _client = null; viewport.QueueFree();
        _host.Leave(); await Until(() => _host.LeaveComplete, "Oil host cleanup");
        _bootstrap.QueueFree(); await Frames(4);
        GD.Print($"Developer Options integration passed: write; Oil-only; {_assertions} assertions, eighteen synchronized controls.");
        GetTree().Quit();
    }
}
