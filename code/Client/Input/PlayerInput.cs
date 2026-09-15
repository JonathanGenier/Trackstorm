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

    /// <inheritdoc/>
    public override void _Ready()
    {
        _adapter = new PlayerInputAdapter(new PlayerInputBindings());
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent @event) => _adapter?.Observe();

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (_adapter is not null && (what == NotificationApplicationFocusOut || what == NotificationApplicationFocusIn))
        {
            _adapter.Enabled = what == NotificationApplicationFocusIn;
            _adapter.Observe();
        }
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        LatestFrame = Adapter.Capture(checked(++_tick));
        FrameCaptured?.Invoke(LatestFrame);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _adapter?.Bindings.Dispose();
        _adapter = null;
    }
}
