using Godot;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Settings;

/// <summary>Logical focus routing and page lifecycle for the existing settings owner.</summary>
internal sealed partial class SettingsPanel
{
    private static readonly InputAction[] NavigationActions =
    [
        InputAction.Pause, InputAction.MenuCancel, InputAction.MenuUp, InputAction.MenuDown,
        InputAction.MenuLeft, InputAction.MenuRight, InputAction.MenuAccept,
    ];

    /// <summary>Closes the overlay and flushes the existing preference owner.</summary>
    internal void Close()
    {
        _navigation.Close();
        ShowPage();
    }

    private static string Title(MenuPage page) => page switch
    {
        MenuPage.Game => "Game Menu",
        MenuPage.DeveloperOptions => "Developer Options",
        _ => page.ToString(),
    };

    private static void AddButton(VBoxContainer parent, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, parent.Name == "Game" ? 60 : 0) };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private VBoxContainer Page(MenuPage page)
    {
        var column = new VBoxContainer { Name = page.ToString(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false };
        column.AddThemeConstantOverride("separation", page == MenuPage.Settings ? 6 : 10);
        _scroll.AddChild(column);
        _pages.Add(page, column);
        return column;
    }

    private void Select(MenuPage page)
    {
        _navigation.Select(page);
        ShowPage();
    }

    private void Back()
    {
        if (ExitStatus() is not null)
        {
            return;
        }

        if (_preview is not null)
        {
            RevertDisplay();
            return;
        }

        _capture = null;
        _navigation.Back();
        ShowPage();
    }

    private void ShowPage()
    {
        bool open = CurrentPage != MenuPage.Closed;
        _panel.Visible = _shade.Visible = open;
        _openSettings.Visible = !open && !ArenaAvailable();
        foreach ((MenuPage page, VBoxContainer column) in _pages)
        {
            column.Visible = page == CurrentPage;
        }

        _title.Text = Title(CurrentPage).ToUpperInvariant();
        _panel.Size = new Vector2(640, CurrentPage == MenuPage.Game ? 510 : 680);
        _scroll.Size = new Vector2(544, CurrentPage == MenuPage.Game ? 288 : 378);
        _status.Position = new Vector2(48, CurrentPage == MenuPage.Game ? 460 : 620);
        _panel.QueueRedraw();
        Layout();
        _back.Visible = CurrentPage != MenuPage.Game;
        _status.Visible = CurrentPage is not MenuPage.Game and not MenuPage.DeveloperOptions;
        _scroll.ScrollVertical = 0;
        _input.GameplaySuppressed = open;
        _input.Observe();
        _repeatAction = null;
        if (open)
        {
            Focusable().FirstOrDefault()?.GrabFocus();
        }
        else
        {
            _capture = null;
            RevertDisplay();
            _settings.Flush();
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        }
    }

    private IEnumerable<Control> Focusable()
    {
        if (_confirmation.Visible)
        {
            return new Control[] { _keep, _revert };
        }

        return CurrentPage == MenuPage.Closed ? Array.Empty<Control>() : Descendants(_pages[CurrentPage])
            .OfType<Control>().Where(control => control.IsVisibleInTree() && control.FocusMode == Control.FocusModeEnum.All && control is not BaseButton { Disabled: true })
            .Concat(_back.Visible ? new Control[] { _back } : Array.Empty<Control>());
    }

    private void SampleNavigation(bool dispatch, bool gamepad = false)
    {
        if (!gamepad && CurrentPage == MenuPage.DeveloperOptions && GetViewport().GuiGetFocusOwner() is LineEdit)
        {
            dispatch = false;
            _repeatAction = null;
        }

        bool routed = false;
        foreach (InputAction action in NavigationActions)
        {
            bool held = _input.Enabled && _input.Bindings.Strength(action, _input.DeadZone) > 0.5f;
            bool previous = _menuHeld.GetValueOrDefault(action);
            _menuHeld[action] = held;
            if (!held && _repeatAction == action)
            {
                _repeatAction = null;
            }

            if (held && !previous && dispatch && !routed)
            {
                routed = true;
                Navigate(action);
                if (action is InputAction.MenuUp or InputAction.MenuDown or InputAction.MenuLeft or InputAction.MenuRight)
                {
                    _repeatAction = action;
                    _repeatDelay = 0.4;
                }
            }
        }
    }

    private void Navigate(InputAction action)
    {
        if (CurrentPage == MenuPage.Closed)
        {
            if (ArenaAvailable() && action == InputAction.Pause)
            {
                _navigation.Open(true);
                ShowPage();
            }

            return;
        }

        if (action is InputAction.MenuCancel or InputAction.Pause)
        {
            Back();
            return;
        }

        Control[] controls = Focusable().ToArray();
        if (controls.Length == 0)
        {
            return;
        }

        Control? focused = GetViewport().GuiGetFocusOwner();
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
            else
            {
                controls[(index + direction + controls.Length) % controls.Length].GrabFocus();
            }
        }
        else if (action == InputAction.MenuAccept && focused is BaseButton button)
        {
            if (button is OptionButton option)
            {
                option.Selected = (option.Selected + 1) % option.ItemCount;
                option.EmitSignal(OptionButton.SignalName.ItemSelected, (long)option.Selected);
            }
            else if (button.ToggleMode)
            {
                button.ButtonPressed = !button.ButtonPressed;
            }
            else
            {
                button.EmitSignal(BaseButton.SignalName.Pressed);
            }
        }
    }

    private void Layout()
    {
        float scale = Math.Min(1, Math.Min(_root.Size.X / 680, _root.Size.Y / (_panel.Size.Y + 40)));
        _panel.Scale = Vector2.One * scale;
        _panel.Position = (_root.Size - (_panel.Size * scale)) / 2;
    }

}
