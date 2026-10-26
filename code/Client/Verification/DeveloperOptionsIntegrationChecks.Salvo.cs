using Godot;
using Trackstorm.Client.Development;
using Trackstorm.Client.Items;
using Trackstorm.Core.Development;

namespace Trackstorm.Client.Verification;

public sealed partial class DeveloperOptionsIntegrationChecks
{
    private async Task CheckSalvoOptions()
    {
        var panel = _devTools.Configs;
        var search = Descendants(panel).OfType<LineEdit>().Single(editor => editor.Name == "ConfigSearch");
        search.Text = "Salvo"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        foreach (var option in GameplayOptions.All.Where(option => option.Key.StartsWith("items.salvo_", StringComparison.Ordinal)))
        {
            Check(Descendants(panel).OfType<LineEdit>().Any(editor => editor.Name == option.Key.Replace('.', '_') && editor.IsVisibleInTree()),
                "Salvo search exposes gameplay control " + option.Key);
        }
        var section = Descendants(panel).OfType<ConfigsAccordion>().Single(s =>
            s.GetChildren().OfType<Button>().Single().Text.Contains("Salvo · Local explosion VFX", StringComparison.Ordinal));
        var editors = Descendants(section.Body).OfType<SpinBox>().ToArray();
        Check(editors.Length == 6 && editors.All(e => e.IsVisibleInTree()), "Six independent Salvo explosion preview controls are visible");
        var authority = _host.DeveloperConfiguration;
        double[] values = [8, 12, .24, 25, .5, 2];
        for (int i = 0; i < values.Length; i++) { editors[i].Value = values[i]; }
        var expected = new SalvoExplosionSettings { FireworkScale = 8, SmokeScale = 12, BurstSpacing = .24f,
            SmokeLifetime = 25, SmokeOpacity = .5f, Brightness = 2 };
        Check(SalvoExplosionSettings.Current == expected, "Every native Salvo editor changes its corresponding VFX setting");
        Check(_host.DeveloperConfiguration == authority && !panel.HasUnappliedChanges, "Local VFX leaves gameplay radius, flight and authoritative draft unchanged");
        var effect = new SalvoExplosion { Seed = 123 }; AddChild(effect); effect.SetProcess(false); effect.Advance(.1f);
        Check(effect.Settings == expected, "Actual explosion captures all six preview settings");
        var smoke = Descendants(effect).OfType<MultiMeshInstance3D>().Single(v => v.Name == "ExplosionSmoke");
        var material = (ShaderMaterial)smoke.Multimesh.Mesh.SurfaceGetMaterial(0);
        Check(material.GetShaderParameter("visual_scale").AsSingle() == 12 && material.GetShaderParameter("lifetime").AsSingle() == 25 &&
            material.GetShaderParameter("opacity").AsSingle() == .5f, "Smoke renderer receives size, lifetime and opacity");
        search.Text = "Salvo local explosion"; search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        await Frames(3); await Capture("salvo-explosion-controls");
        Descendants(section.Body).OfType<Button>().Single(b => b.Text == "Reset to Defaults").EmitSignal(BaseButton.SignalName.Pressed);
        Check(SalvoExplosionSettings.Current == new SalvoExplosionSettings() && effect.Settings == expected,
            "Reset restores large default VFX for future impacts without restarting existing smoke");
        effect.Advance(.4f); Check(effect.BurstsStarted == 3, "Configured burst spacing drives the real explosion");
        effect.Advance(24.4f); Check(!effect.IsQueuedForDeletion(), "Configured smoke persists until its captured lifetime");
        effect.Advance(.2f); Check(effect.IsQueuedForDeletion(), "Configured smoke expires once at its captured lifetime");
        Set("items.salvo_speed", 120); panel.Apply();
        await Until(() => _host.DeveloperConfiguration.Items.SalvoSpeed == 120, "Existing launch-speed control applies through authority");
        Check(_host.DeveloperConfiguration.Items.SalvoBlastRadius == .9144f && SalvoExplosionSettings.Current.SmokeScale == 10,
            "Launch speed, three-foot damage radius and visual sizes remain independent");
    }
}
