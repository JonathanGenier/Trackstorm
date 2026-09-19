using Godot;
using Trackstorm.Client.Development;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Verification;

/// <summary>Native binding, focus and responsive layout regressions for the composed DevTools shell.</summary>
public sealed partial class MenuIntegrationChecks
{
    private async Task VerifyDevToolsNavigation()
    {
        var numbers = Descendants(_devTools.Configs).OfType<LineEdit>().Where(editor => editor.GetParent() is GridContainer).ToArray();
        LineEdit mass = numbers.Single(editor => editor.Name == "vehicle_mass");
        LineEdit acceleration = numbers.Single(editor => editor.Name == "vehicle_acceleration");
        mass.GrabFocus();
        Joy(JoyButton.DpadDown);
        Check(GetViewport().GuiGetFocusOwner() == acceleration, "controller Down leaves numeric editor exactly once");
        Joy(JoyButton.DpadUp);
        Check(GetViewport().GuiGetFocusOwner() == mass, "controller Up returns to preceding editor");
        Joy(JoyButton.DpadRight);
        Check(GetViewport().GuiGetFocusOwner() == acceleration, "controller Right leaves numeric editor");
        Joy(JoyButton.DpadLeft);
        Check(GetViewport().GuiGetFocusOwner() == mass, "controller Left leaves numeric editor");
        var spin = Descendants(_devTools.Configs).OfType<SpinBox>().First();
        spin.GetLineEdit().GrabFocus();
        Joy(JoyButton.DpadDown);
        Check(GetViewport().GuiGetFocusOwner() != spin.GetLineEdit(), "controller can leave SpinBox's internal numeric editor");

        var before = _session.DeveloperConfiguration;
        using (var typed = new InputEventKey { Keycode = Key.Key9, PhysicalKeycode = Key.Key9, Unicode = '9', Pressed = true })
        {
            mass.GrabFocus();
            mass.SelectAll();
            Godot.Input.ParseInputEvent(typed);
            Godot.Input.FlushBufferedEvents();
        }

        KeyEvent(Key.Key9, false);
        Check(mass.Text == "9" && _session.DeveloperConfiguration == before, "native numeric typing stages text without applying configuration");
        Tap(Key.Enter);
        Check(_session.DeveloperConfiguration == before, "logical Accept in numeric editor does not Apply");
        Buttons(_devTools).Single(button => button.Text == "Cancel").GrabFocus();
        Joy(JoyButton.A);
        Check(mass.Text != "9" && _session.DeveloperConfiguration == before, "controller Accept activates Discard without mutating configuration");

        (InputAction Action, Key Key, JoyButton Button)[] remaps =
        [
            (InputAction.MenuUp, Key.I, JoyButton.LeftShoulder),
            (InputAction.MenuDown, Key.K, JoyButton.RightShoulder),
            (InputAction.MenuLeft, Key.J, JoyButton.DpadUp),
            (InputAction.MenuRight, Key.L, JoyButton.DpadDown),
            (InputAction.MenuAccept, Key.U, JoyButton.X),
            (InputAction.MenuCancel, Key.O, JoyButton.Y),
        ];
        foreach (var remap in remaps)
        {
            using var key = new InputEventKey { PhysicalKeycode = remap.Key };
            using var button = new InputEventJoypadButton { Device = 0, ButtonIndex = remap.Button };
            _player.Adapter.Bindings.Replace(remap.Action, key, button);
        }

        foreach (var remap in remaps.Take(4))
        {
            mass.GrabFocus();
            Tap(remap.Key);
            Check(GetViewport().GuiGetFocusOwner() != mass, "remapped keyboard navigation leaves editor: " + remap.Action);
            mass.GrabFocus();
            Joy(remap.Button);
            Check(GetViewport().GuiGetFocusOwner() != mass, "remapped controller navigation leaves editor: " + remap.Action);
        }

        Button stats = Buttons(_devTools).Single(button => button.Text == "Stats");
        mass.GrabFocus();
        Tap(Key.Down);
        Check(GetViewport().GuiGetFocusOwner() == mass, "unbound native Down cannot bypass remapped numeric navigation");
        stats.GrabFocus();
        Tap(Key.Enter);
        Check(_devTools.SelectedTab == DevToolsTab.Configs, "native Enter cannot bypass remapped Accept");
        Tap(Key.U);
        Check(_devTools.SelectedTab == DevToolsTab.Stats, "remapped keyboard Accept selects tab");
        Buttons(_devTools).Single(button => button.Text == "Configs").GrabFocus();
        Joy(JoyButton.X);
        Check(_devTools.SelectedTab == DevToolsTab.Configs, "remapped controller Accept selects tab");

        using (var axis = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightY, AxisValue = 1 })
        {
            _player.Adapter.Bindings.Replace(InputAction.MenuDown, axis);
            mass.GrabFocus();
            Godot.Input.ParseInputEvent(axis);
            Godot.Input.FlushBufferedEvents();
            Check(GetViewport().GuiGetFocusOwner() == acceleration, "remapped analog menu navigation leaves editor");
            _devTools._Process(0.41);
            Check(GetViewport().GuiGetFocusOwner() == numbers.Single(editor => editor.Name == "vehicle_braking"), "held logical direction repeats after menu delay");
            using var released = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightY, AxisValue = 0 };
            Godot.Input.ParseInputEvent(released);
            Godot.Input.FlushBufferedEvents();
        }

        Tap(Key.O);
        Check(!_devTools.IsOpen && _menu.CurrentPage == MenuPage.Settings && _player.Adapter.GameplaySuppressed, "remapped Cancel closes only DevTools and preserves underlying suppression");
        Control? restored = GetViewport().GuiGetFocusOwner();
        Check(restored is not null && _menu.IsAncestorOf(restored), "Cancel restores underlying menu focus");
        Tap(Key.F1);
        using (var cancel = new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.Y, Pressed = true })
        {
            Godot.Input.ParseInputEvent(cancel);
            Godot.Input.FlushBufferedEvents();
            await Frames(5);
            Check(!_devTools.IsOpen && _menu.CurrentPage == MenuPage.Settings && GetViewport().GuiGetFocusOwner() == restored, "held controller Cancel cannot leak into underlying Settings");
            cancel.Pressed = false;
            Godot.Input.ParseInputEvent(cancel);
            Godot.Input.FlushBufferedEvents();
        }

        _player.Adapter.Bindings.RestoreDefaults();
        Tap(Key.F1);
        await Frames(20);
        spin.Value = spin.Value == spin.MaxValue ? spin.Value - 1 : spin.Value + 1;
        await Frames(2);
        var feedback = Descendants(_devTools.Configs.Footer).OfType<Label>().Single(label => label.Name == "ConfigFeedback");
        Check(_devTools.Configs.HasUnappliedChanges, "responsive footer fixture stages a configuration change");
        Check(feedback.Text == "Unsaved changes", "staged responsive footer fixture updates feedback text");
        ulong tick = _bootstrap.CurrentSimulationTick;
        foreach (Vector2I size in new[] { new Vector2I(640, 360), new Vector2I(1280, 720), new Vector2I(2560, 1080) })
        {
            GetWindow().Size = size;
            await Frames(5);
            Check(_devTools.Configs.Size.X <= 641, "Configs content width is bounded at " + size);
            float column = mass.GetGlobalRect().Position.X;
            foreach (GridContainer rows in Descendants(_devTools.Configs).OfType<GridContainer>())
            {
                var label = rows.GetChild<Label>(0);
                Control editor = rows.GetChild<Control>(1);
                Check(editor.GetGlobalRect().Position.X - label.GetGlobalRect().End.X <= 17, "label and value columns retain compact spacing");
                Check(Math.Abs(editor.GetGlobalRect().Position.X - column) < 1, "gameplay and network rows share the value column");
            }

            foreach (LineEdit number in numbers)
            {
                Check(number.GetParent() is GridContainer { Columns: 2 }, "configuration editor uses two-column layout");
                Check(Math.Abs(number.GetGlobalRect().Position.X - column) < 1, "numeric editors share one aligned value column");
                Check(number.GetGlobalRect().End.X <= GetViewport().GetVisibleRect().End.X, "value column fits viewport");
            }

            var scroll = Descendants(_devTools.Configs).OfType<ScrollContainer>().Single();
            numbers[^1].GrabFocus();
            await Frames(3);
            Check(scroll.ScrollVertical > 0, "focus follows last numeric editor through vertical scrolling");
            Rect2 viewport = GetViewport().GetVisibleRect();
            var reset = Buttons(_devTools).Single(button => button.Text == "Reset to Defaults");
            var apply = Buttons(_devTools).Single(button => button.Text == "Apply Settings");
            var cancel = Buttons(_devTools).Single(button => button.Text == "Cancel");
            var close = Buttons(_devTools).Single(button => button.Text == "Close");
            foreach (var button in new[] { reset, apply, cancel, close })
            {
                Check(button.IsVisibleInTree() && viewport.Encloses(button.GetGlobalRect()), "footer button stays inside viewport after scrolling: " + button.Text);
                Check(button.GetGlobalRect().Position.Y >= scroll.GetGlobalRect().End.Y, "footer sits below scrolling settings");
            }

            Check(reset.GetGlobalRect().Position.X < apply.GetGlobalRect().Position.X && apply.GetGlobalRect().End.X <= cancel.GetGlobalRect().Position.X && cancel.GetGlobalRect().End.X <= close.GetGlobalRect().Position.X, "footer action order is Reset, Apply, Cancel, Close");
            Check(feedback.IsVisibleInTree() && feedback.Text == "Unsaved changes", "footer shows persistent Unsaved changes feedback");
            Check(feedback.GetGlobalRect().End.Y <= apply.GetGlobalRect().Position.Y + 1, "feedback remains above the persistent action row");
            Check(feedback.GetGlobalRect().Position.X >= apply.GetGlobalRect().Position.X - 1 && feedback.GetGlobalRect().Position.X < close.GetGlobalRect().End.X, "feedback remains aligned with the Apply, Cancel and Close group");
            var force = Buttons(_devTools).Single(button => button.Name == "ForceStart");
            Check(!Descendants(_devTools.Configs).OfType<Button>().Any(button => button.Name == "ForceStart"), "Force Start has no duplicate inside Configs");
            Check(viewport.Encloses(force.GetGlobalRect()) && force.GetGlobalRect().Position.X > viewport.Size.X / 2 && force.GetGlobalRect().End.Y <= scroll.GetGlobalRect().Position.Y, "Force Start remains at the shell top-right outside Configs scrolling");
            foreach (var button in new[] { force, reset, apply, cancel, close })
            {
                Check(button.Icon is not null, "DevTools action has an icon: " + button.Text);
            }

            var resetStyle = (StyleBoxFlat)reset.GetThemeStylebox("normal");
            var applyStyle = (StyleBoxFlat)apply.GetThemeStylebox("normal");
            var cancelStyle = (StyleBoxFlat)cancel.GetThemeStylebox("normal");
            var closeStyle = (StyleBoxFlat)close.GetThemeStylebox("normal");
            Check(resetStyle.BgColor == new Color("1b2027") && resetStyle.BorderColor == new Color("e24a4a") && resetStyle.BorderWidthTop > 0, "Reset uses a dark fill with red border");
            Check(applyStyle.BgColor == new Color("238636"), "Apply Settings uses a green fill");
            Check(cancelStyle.BgColor == new Color("b4232c"), "Cancel uses a red fill");
            Check(closeStyle.BgColor == new Color("1f6feb"), "Close uses a blue fill");
            mass.GrabFocus();
            await Frames(3);
            await Capture($"configs-{size.X}x{size.Y}");
        }

        Buttons(_devTools).Single(button => button.IsVisibleInTree() && button.Text == "Cancel").EmitSignal(BaseButton.SignalName.Pressed);
        Check(_bootstrap.CurrentSimulationTick > tick && !_bootstrap.GetTree().Paused, "simulation continues while navigating DevTools");
        GetWindow().Size = new Vector2I(1280, 720);
        Tap(Key.F2);
        Check(_devTools.SelectedTab == DevToolsTab.Stats && !Descendants(_devTools.Stats).OfType<LineEdit>().Any(), "F2 selects read-only Stats after remapped navigation");
        TabBar statsTabs = Descendants(_devTools.Stats).OfType<TabContainer>().Single().GetTabBar();
        statsTabs.GrabFocus();
        Joy(JoyButton.DpadRight);
        Check(statsTabs.CurrentTab == 1, "logical Right selects existing Stats player page");
        Joy(JoyButton.DpadLeft);
        Check(statsTabs.CurrentTab == 0, "logical Left returns to existing Stats session page");
        Tap(Key.F3);
        Check(_devTools.SelectedTab == DevToolsTab.Logs && !Descendants(_devTools.Logs).OfType<LineEdit>().Any(), "F3 selects read-only Logs after remapped navigation");
        Tap(Key.F1);
        Check(_devTools.SelectedTab == DevToolsTab.Configs && Descendants(_bootstrap).OfType<DevToolsShell>().Count() == 1, "F1 returns to the same single shell");
    }
}
