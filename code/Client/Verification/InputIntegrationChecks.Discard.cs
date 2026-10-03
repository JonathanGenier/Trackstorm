using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Core.Input;
using Trackstorm.Core.Settings;

namespace Trackstorm.Client.Verification;

public sealed partial class InputIntegrationChecks
{
    private void VerifyDiscard()
    {
        var adapter = _player.Adapter;
        adapter.Bindings.RestoreDefaults();
        Check(InputMap.ActionHasEvent(PlayerInputBindings.Name(InputAction.DiscardItem), new InputEventKey { PhysicalKeycode = Key.X }), "X defaults to discard");
        Check(InputMap.ActionHasEvent(PlayerInputBindings.Name(InputAction.DiscardItem), new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.DpadLeft }), "D-pad Left defaults to discard");
        foreach (InputEvent binding in adapter.Bindings.CopyBindings(InputAction.DiscardItem))
        {
            using (binding)
            {
                foreach (int gate in new[] { 0, 1, 2 })
                {
                    SetPressed(binding, true); Send(binding);
                    adapter.Enabled = gate != 0;
                    adapter.GameplaySuppressed = gate == 1;
                    adapter.DiagnosticSuppressed = gate == 2;
                    Check((adapter.Capture(1).Pressed & InputButtons.DiscardItem) == 0, "Focus/UI/diagnostics suppress pending discard");
                    adapter.Enabled = true;
                    adapter.GameplaySuppressed = adapter.DiagnosticSuppressed = false;
                    Check((adapter.Capture(2).Pressed & InputButtons.DiscardItem) == 0, "Closing suppression requires discard release");
                    SetPressed(binding, false); Send(binding); adapter.Capture(3);
                    SetPressed(binding, true); Send(binding);
                    SetPressed(binding, false); Send(binding);
                    var tap = adapter.Capture(4);
                    Check((tap.Pressed & tap.Released & InputButtons.DiscardItem) != 0, "Short keyboard/controller discard tap survives");
                    var bytes = new byte[InputFrame.SerializedSize]; tap.Write(bytes);
                    Check(InputFrame.Read(bytes) == tap, "Discard bit serializes through the logical frame");
                    Check((adapter.Capture(5).Pressed & InputButtons.DiscardItem) == 0, "Discard tap never repeats");
                }
            }
        }
        using var remap = new InputEventKey { PhysicalKeycode = Key.Z };
        adapter.Bindings.Replace(InputAction.DiscardItem, remap);
        var saved = InputBindingPreferences.Capture(adapter, new PlayerSettings());
        adapter.Bindings.RestoreDefaults();
        InputBindingPreferences.Apply(adapter, PlayerSettingsJson.Deserialize(PlayerSettingsJson.Serialize(saved)));
        Check(InputBindingPreferences.Capture(adapter, saved).Bindings[InputAction.DiscardItem].SequenceEqual(new[] { "key:90" }), "Custom discard binding persists");
        adapter.Bindings.Replace(InputAction.DiscardItem);
        saved = InputBindingPreferences.Capture(adapter, saved);
        adapter.Bindings.RestoreDefaults(); InputBindingPreferences.Apply(adapter, saved);
        Check(adapter.Bindings.CopyBindings(InputAction.DiscardItem).Length == 0, "Explicit discard unbind persists");
        adapter.Bindings.RestoreDefaults(); InputBindingPreferences.Apply(adapter, new PlayerSettings());
        Check(adapter.Bindings.CopyBindings(InputAction.DiscardItem).Length == 2, "Old saves without a discard override inherit defaults");
    }
}
