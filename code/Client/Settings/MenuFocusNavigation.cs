using Godot;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Settings;

/// <summary>Shared logical focus and control activation for Settings and DevTools.</summary>
internal static class MenuFocusNavigation
{
    /// <summary>Routes one action across the visible controls, including text editors.</summary>
    /// <param name="action">Action resolved by the existing player bindings.</param>
    /// <param name="controls">Visible, enabled controls in navigation order.</param>
    /// <param name="focused">Current viewport focus owner.</param>
    internal static void Navigate(InputAction action, Control[] controls, Control? focused)
    {
        if (controls.Length == 0)
        {
            return;
        }

        int index = Array.IndexOf(controls, focused);
        if (index < 0)
        {
            controls[0].GrabFocus();
            return;
        }

        int direction = action is InputAction.MenuUp or InputAction.MenuLeft ? -1 : 1;
        if (action is InputAction.MenuUp or InputAction.MenuDown)
        {
            controls[(index + direction + controls.Length) % controls.Length].GrabFocus();
        }
        else if (action is InputAction.MenuLeft or InputAction.MenuRight)
        {
            if (focused is HSlider slider)
            {
                slider.Value += direction * slider.Step;
            }
            else if (focused is OptionButton option && option.ItemCount > 0)
            {
                option.Selected = (option.Selected + direction + option.ItemCount) % option.ItemCount;
                option.EmitSignal(OptionButton.SignalName.ItemSelected, (long)option.Selected);
            }
            else if (focused is TabBar tabs && tabs.TabCount > 0)
            {
                tabs.CurrentTab = (tabs.CurrentTab + direction + tabs.TabCount) % tabs.TabCount;
            }
            else
            {
                controls[(index + direction + controls.Length) % controls.Length].GrabFocus();
            }
        }
        else if (action == InputAction.MenuAccept && focused is BaseButton button)
        {
            if (button is OptionButton option)
            {
                if (option.ItemCount > 0)
                {
                    option.Selected = (option.Selected + 1) % option.ItemCount;
                    option.EmitSignal(OptionButton.SignalName.ItemSelected, (long)option.Selected);
                }
            }
            else
            {
                if (button.ToggleMode)
                {
                    button.ButtonPressed = !button.ButtonPressed;
                }

                button.EmitSignal(BaseButton.SignalName.Pressed);
            }
        }
    }

}
