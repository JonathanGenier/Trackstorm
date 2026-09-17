using Godot;
using Trackstorm.Core.Input;

namespace Trackstorm.Client.Input;

/// <summary>Owns the single local input adapter and publishes one Core frame per Godot fixed tick.</summary>
public sealed partial class PlayerInput : Node
{
    private PlayerInputAdapter? _adapter;
    private ulong _tick;

    /// <summary>Consumers receive only Core input after capture and before the next fixed tick.</summary>
    public event Action<InputFrame>? FrameCaptured;

    /// <summary>Most recently captured frame; neutral before the first fixed update.</summary>
    public InputFrame LatestFrame { get; private set; }

    /// <summary>Client integration/configuration access, never passed to gameplay.</summary>
    internal PlayerInputAdapter Adapter => _adapter ?? throw new InvalidOperationException("Input is not ready.");

    /// <summary>Composition supplies arena presence; menus and focus reuse the adapter's existing gates.</summary>
    internal Func<bool> GameplayAvailable { get; set; } = () => false;

    /// <inheritdoc/>
    public override void _Ready()
    {
        _adapter = new PlayerInputAdapter(new PlayerInputBindings()) { CaptureInterval = 1f / Engine.PhysicsTicksPerSecond };
        _adapter.Enabled = DisplayServer.GetName() == "headless" || GetWindow().HasFocus();
        _adapter.ControlStateChanged += RefreshMouseMode;
        ProcessMode = ProcessModeEnum.Always;
        RefreshMouseMode();
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event) => _adapter?.Observe();

    /// <inheritdoc/>
    public override void _Process(double delta) => RefreshMouseMode();

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (_adapter is not null && (what == NotificationApplicationFocusOut || what == NotificationApplicationFocusIn))
        {
            _adapter.Enabled = what == NotificationApplicationFocusIn;
            _adapter.Observe();
            RefreshMouseMode();
        }
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        LatestFrame = Adapter.Capture(checked(++_tick));
        FrameCaptured?.Invoke(LatestFrame);
        // Session advancement can enter or leave an arena during this callback.
        RefreshMouseMode();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (_adapter is not null)
        {
            _adapter.ControlStateChanged -= RefreshMouseMode;
            _adapter.Bindings.Dispose();
        }

        _adapter = null;
        Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
    }

    private void RefreshMouseMode()
    {
        var mode = _adapter is { Enabled: true, GameplaySuppressed: false, DiagnosticSuppressed: false } && GameplayAvailable()
            ? Godot.Input.MouseModeEnum.Captured
            : Godot.Input.MouseModeEnum.Visible;
        if (Godot.Input.MouseMode != mode)
        {
            Godot.Input.MouseMode = mode;
        }
    }
}
