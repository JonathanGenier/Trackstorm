using Godot;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Verification;

public sealed partial class ItemIntegrationChecks
{
    private InputFrame DiscardInput(bool controller, bool switchSlot)
    {
        _input.Adapter.Enabled = true;
        _input.Adapter.Observe();
        if (switchSlot)
        {
            using var toggle = new InputEventKey { PhysicalKeycode = Key.E, Pressed = true };
            Godot.Input.ParseInputEvent(toggle); Godot.Input.FlushBufferedEvents(); _input.Adapter.Observe();
            using var toggleRelease = new InputEventKey { PhysicalKeycode = Key.E, Pressed = false };
            Godot.Input.ParseInputEvent(toggleRelease); Godot.Input.FlushBufferedEvents(); _input.Adapter.Observe();
        }
        using InputEvent discard = controller
            ? new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.DpadLeft, Pressed = true }
            : new InputEventKey { PhysicalKeycode = Key.X, Pressed = true };
        Godot.Input.ParseInputEvent(discard); Godot.Input.FlushBufferedEvents(); _input.Adapter.Observe();
        using var release = (InputEvent)discard.Duplicate();
        if (release is InputEventKey key) { key.Pressed = false; }
        if (release is InputEventJoypadButton button) { button.Pressed = false; }
        Godot.Input.ParseInputEvent(release); Godot.Input.FlushBufferedEvents(); _input.Adapter.Observe();
        var frame = _input.Adapter.Capture(0);
        Require((frame.Pressed & InputButtons.DiscardItem) != 0, "Physical discard tap reaches logical frame.");
        Require((_input.Adapter.Capture(0).Pressed & InputButtons.DiscardItem) == 0, "Discard edge is captured once.");
        return frame;
    }
}
