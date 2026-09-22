using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Frontend;

/// <summary>Modular suspended frontend with fixed input targets and one authoritative selection.</summary>
internal sealed partial class HangingMainMenu : Control
{
    private static readonly InputAction[] Actions = [InputAction.MenuUp, InputAction.MenuDown, InputAction.MenuLeft, InputAction.MenuRight, InputAction.MenuAccept];
    private readonly Control _layout = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly Control _art = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly List<Button> _targets = [];
    private readonly List<HangingMenuPlate> _plates = [];
    private readonly Dictionary<InputAction, bool> _held = [];
    private MainMenuEntry[] _entries = [];
    private Texture2D _chain = null!;
    private float _elapsed = 2;
    private float _clock;
    private float _height;
    private int _selected;
    private bool _wasActive;
    private bool _wasInteractive;
    private double _repeat;
    private InputAction? _repeatAction;

    internal PlayerInputAdapter? NavigationInput { get; set; }
    internal Func<bool> Active { get; set; } = () => true;
    internal Func<bool> Blocked { get; set; } = () => false;
    internal bool Settled => _elapsed >= 1.12f;
    internal string SelectedId => _entries.Length == 0 ? string.Empty : _entries[_selected].Id;
    internal IReadOnlyList<Button> Targets => _targets;
    internal bool Interactive => IsVisibleInTree() && Settled && !Blocked();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_layout);
        _layout.AddChild(_art);
        _chain = new AtlasTexture { Atlas = GD.Load<Texture2D>("res://assets/frontend/main-menu/Chain.png"), Region = new Rect2(277, 92, 170, 258) };
        Resized += Layout;
    }

    /// <summary>Rebuilds content, spacing and connections without new artwork.</summary>
    internal void SetEntries(IEnumerable<MainMenuEntry> entries)
    {
        foreach (Node child in _art.GetChildren()) { _art.RemoveChild(child); child.QueueFree(); }
        foreach (Button target in _targets) { _layout.RemoveChild(target); target.QueueFree(); }
        _targets.Clear();
        _plates.Clear();
        _entries = entries.ToArray();
        _height = 350 + _entries.Length * 185;
        _selected = Math.Max(0, Array.FindIndex(_entries, entry => entry.Available));
        // Flags are separate fabric-only textures, behind their rigid header attachments.
        Fabric("FlagRed.png", new Vector2(4, 274), new Vector2(156, 322), true, 0);
        Fabric("FlagCheckered.png", new Vector2(80, 263), new Vector2(125, 390), false, 2);
        Fabric("FlagRed.png", new Vector2(839, 274), new Vector2(156, 322), false, 3);
        var header = Picture("Header.png", new Vector2(0, 70), new Vector2(1000, 333));
        _art.AddChild(header);
        for (int index = 0; index < _entries.Length; index++)
        {
            int captured = index;
            MainMenuEntry entry = _entries[index];
            var plate = new HangingMenuPlate { Position = new Vector2(110, 350 + index * 185) };
            plate.Initialize(entry);
            _art.AddChild(plate);
            _plates.Add(plate);
            var target = new Button { Name = entry.Id, Text = entry.Label, Position = new Vector2(195, 362 + index * 185), Size = new Vector2(610, 156), FocusMode = entry.Available ? FocusModeEnum.All : FocusModeEnum.None };
            // Transparent native controls provide stable mouse press/release semantics.
            target.Modulate = new Color(1, 1, 1, 0);
            target.Pressed += () => { if (Interactive && entry.Available) entry.Activate(); };
            target.FocusEntered += () => { if (Interactive && entry.Available) _selected = captured; };
            target.GuiInput += input =>
            {
                if (Interactive && entry.Available && input is InputEventMouseMotion motion && motion.Relative.LengthSquared() > 0)
                { _selected = captured; target.GrabFocus(); }
            };
            _layout.AddChild(target);
            _targets.Add(target);
        }
        Layout();
    }

    internal void BeginEntrance()
    {
        _elapsed = 0;
        _wasInteractive = false;
        UpdateMotion();
        UpdateTargets();
    }

    public override void _Process(double delta)
    {
        Visible = Active();
        _clock += (float)delta;
        if (IsVisibleInTree()) _elapsed += (float)delta;
        bool interactive = Interactive;
        UpdateTargets();
        if (interactive && (!_wasActive || !_wasInteractive) && _targets.Count > 0)
            _targets[_selected].GrabFocus();
        SampleNavigation(interactive && _wasInteractive, delta);
        _wasActive = IsVisibleInTree();
        _wasInteractive = interactive;
        UpdateMotion();
        UpdateTargets();
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree() || Blocked()) return;
        SampleNavigation(Interactive && _wasInteractive, 0);
        if (@event is InputEventMouse) return;
        if (Actions.Any(action => @event.IsAction(PlayerInputBindings.Name(action))) ||
            new[] { "ui_accept", "ui_up", "ui_down", "ui_left", "ui_right", "ui_focus_next", "ui_focus_prev" }.Any(action => @event.IsAction(action)))
            GetViewport().SetInputAsHandled();
    }

    public override void _Draw()
    {
        if (_chain is null) return;
        // Draw in design coordinates. The upper endpoints never inherit the assembly transform.
        DrawSetTransform(_layout.Position, 0, _layout.Scale);
        foreach (float x in new[] { 102f, 898f })
        {
            Vector2 bottom = new Vector2(x, 292) + _art.Position;
            Chain(new Vector2(x, -45), bottom);
        }
        foreach (float x in new[] { 199f, 801f, 295f, 705f })
            Chain(new Vector2(x, 310) + _art.Position, new Vector2(x, _height - 42) + _art.Position);
        DrawSetTransform(Vector2.Zero);
    }

    private static TextureRect Picture(string file, Vector2 position, Vector2 size) => new()
    {
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        Texture = GD.Load<Texture2D>("res://assets/frontend/main-menu/" + file), Position = position, Size = size,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private void Fabric(string file, Vector2 position, Vector2 size, bool flip, float phase)
    {
        var flag = Picture(file, position, size);
        flag.FlipH = flip;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/frontend/main-menu/Fabric.gdshader") };
        material.SetShaderParameter("phase", phase);
        flag.Material = material;
        _art.AddChild(flag);
    }

    private void Chain(Vector2 start, Vector2 end)
    {
        if (end.Y <= start.Y) return;
        Vector2 direction = (end - start).Normalized();
        float length = start.DistanceTo(end);
        // Repeat a fixed-size pair of links; changing length reveals links instead of stretching metal.
        for (float offset = 0; offset < length; offset += 41)
        {
            float height = Math.Min(45, length - offset);
            Vector2 point = start + direction * offset;
            DrawTextureRectRegion(_chain, new Rect2(point.X - 15, point.Y, 30, height), new Rect2(0, 0, _chain.GetWidth(), _chain.GetHeight() * height / 45));
        }
    }

    private void UpdateMotion()
    {
        float y;
        if (_elapsed < 0.52f) y = -(_height + 80) * (1 - MathF.Pow(Math.Clamp(_elapsed / 0.52f, 0, 1), 2));
        else if (_elapsed < 1.12f)
        {
            float t = (_elapsed - 0.52f) / 0.6f;
            y = -7 * MathF.Sin(t * MathF.PI) * (1 - t);
        }
        else y = 0;
        float blend = Mathf.SmoothStep(0, 1, Math.Clamp((_elapsed - 1.12f) / 1.2f, 0, 1));
        _art.Position = new Vector2(MathF.Sin(_clock * 0.65f) * 2.4f * blend, y);
        QueueRedraw();
    }

    private void UpdateTargets()
    {
        for (int i = 0; i < _targets.Count; i++)
        {
            _targets[i].Disabled = !Interactive || !_entries[i].Available;
            _plates[i].Selected = _entries[i].Available && i == _selected;
            _plates[i].Pressed = _targets[i].IsPressed();
        }
    }

    private void SampleNavigation(bool dispatch, double delta)
    {
        if (NavigationInput is not { } input) return;
        foreach (InputAction action in Actions)
        {
            bool held = input.Enabled && input.Bindings.Strength(action, input.DeadZone) > 0.5f;
            bool previous = _held.GetValueOrDefault(action);
            _held[action] = held;
            if (!dispatch) continue;
            if (held && !previous)
            {
                Navigate(action);
                _repeatAction = action == InputAction.MenuAccept ? null : action;
                _repeat = 0.4;
            }
            else if (held && _repeatAction == action)
            {
                _repeat -= delta;
                if (_repeat <= 0) { Navigate(action); _repeat = 0.12; }
            }
            else if (!held && _repeatAction == action) _repeatAction = null;
        }
        if (!dispatch) _repeatAction = null;
    }

    private void Navigate(InputAction action)
    {
        if (!Interactive || _entries.Length == 0) return;
        if (action == InputAction.MenuAccept)
        {
            if (_entries[_selected].Available) _entries[_selected].Activate();
            return;
        }
        int direction = action is InputAction.MenuUp or InputAction.MenuLeft ? -1 : 1;
        for (int attempt = 0; attempt < _entries.Length; attempt++)
        {
            _selected = (_selected + direction + _entries.Length) % _entries.Length;
            if (!_entries[_selected].Available) continue;
            _targets[_selected].GrabFocus();
            break;
        }
    }

    private void Layout()
    {
        // Keep the complete suspension and its stationary targets together, leaving the title visible on the right.
        float scale = 0.75f * Math.Min(Size.X / 1040, Size.Y / Math.Max(1, _height + 20));
        _layout.Scale = Vector2.One * scale;
        _layout.Position = new Vector2(Size.X * 0.04f, 0);
        QueueRedraw();
    }
}
