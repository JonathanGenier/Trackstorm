using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Native practice driving, orbit, wall recovery and respawn using the production composition.</summary>
public sealed partial class CameraObstructionDrive : Node
{
    private readonly PlayerInput _input = new();
    private readonly Label _label = new() { Position = new Vector2(24, 24) };
    private readonly List<object> _frames = new();
    private VehicleArena _arena = null!;
    private VehicleChaseCamera _camera = null!;
    private string _output = string.Empty;
    private float _time;
    private float _minimum = float.MaxValue;
    private float _maximumZ;
    private bool _reset;
    private ulong _life;

    /// <inheritdoc/>
    public override void _Ready()
    {
        _output = OS.GetCmdlineUserArgs().Single(value => value.StartsWith("--obstruction-output=", StringComparison.Ordinal))[21..];
        AddChild(_input);
        _input.GameplayAvailable = () => true;
        _arena = new VehicleArena { LegacyTestLayout = true, CameraInput = _input.Adapter };
        AddChild(_arena);
        _camera = _arena.GetNode<VehicleChaseCamera>("ChaseCamera");
        _input.FrameCaptured += Advance;
        var canvas = new CanvasLayer();
        AddChild(canvas);
        canvas.AddChild(_label);
        _label.AddThemeFontSizeOverride("font_size", 24);
        ProcessPriority = 100;
    }

    private void Advance(InputFrame frame)
    {
        ushort reverse = _time is > 0.5f and < 5 ? ushort.MaxValue : (ushort)0;
        ushort forward = _time is >= 5 and < 10 ? ushort.MaxValue : (ushort)0;
        _arena.Advance(new InputFrame(frame.Tick, 0, forward, reverse, 0, 0, 0));
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        try
        {
            _time += (float)delta;
            // Synthetic controls must remain active when the recorder window is not focused.
            _input.Adapter.Enabled = true;
            _input._Process(0);
            Vector3 position = _arena.Player.GetGlobalTransformInterpolated().Origin;
            float distance = _camera.GlobalPosition.DistanceTo(position + Vector3.Up * 0.5f);
            if (_time > 0.5f && _time < 10)
            {
                _minimum = Math.Min(_minimum, distance);
                _maximumZ = Math.Max(_maximumZ, position.Z);
                using var sphere = new SphereShape3D { Radius = 0.20f };
                using var query = new PhysicsShapeQueryParameters3D { Shape = sphere, Transform = new Transform3D(Basis.Identity, _camera.GlobalPosition), CollisionMask = 1, Margin = 0, Exclude = new Godot.Collections.Array<Rid> { _arena.Player.GetRid() } };
                Require(_camera.GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0, "Native driving camera stays outside solid geometry");
            }
            if (_time is > 6 and < 7)
            {
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
                Send(new InputEventMouseMotion { ScreenRelative = new Vector2((float)delta * 500, 0) });
            }
            else Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            float yawOffset = Mathf.AngleDifference(_arena.Player.GetGlobalTransformInterpolated().Basis.GetEuler().Y, _camera.GlobalBasis.GetEuler().Y);
            if (_time is > 6.85f and < 7) Require(Math.Abs(yawOffset) > 1, "Native driving must actually exercise free-look");
            if (_time is > 9 and < 10) Require(Math.Abs(yawOffset) < 0.01f, "Native driving camera recenters after release");
            if (!_reset && _time >= 10)
            {
                Require(_minimum < 10 && _maximumZ > 30, "Real reverse input approaches the wall and contracts the camera");
                Require(distance > 15, "Driving away clears the obstruction");
                _life = _arena.Player.Snapshot.LifeId;
                _arena.ResetVehicles();
                _reset = true;
            }
            _label.Text = $"TS-132 | Native reverse toward wall / drive away / orbit / reset\n{_time:0.00}s | distance {distance:0.00} m | speed {_arena.Player.LinearVelocity.Length():0.0} m/s";
            _frames.Add(new { time = _time, distance, position.X, position.Y, position.Z, life = _arena.Player.Snapshot.LifeId });
            if (_time >= 12)
            {
                Require(_arena.Player.Snapshot.LifeId > _life && distance > 15, "Native life reset restores the normal chase");
                File.WriteAllText(_output + ".json", System.Text.Json.JsonSerializer.Serialize(_frames));
                GD.Print($"Camera obstruction drive passed: native reverse/forward input, wall contraction {_minimum:F3}m, reached Z={_maximumZ:F3}, orbit/recenter, real life reset, final distance={distance:F3}m.");
                SetProcess(false);
                _input.FrameCaptured -= Advance;
                Finish();
            }
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private async void Finish()
    {
        _arena.QueueFree();
        _input.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Task.Delay(100);
        GetTree().Quit();
    }

    private static void Send(InputEvent value)
    {
        using (value)
        {
            Godot.Input.ParseInputEvent(value);
            Godot.Input.FlushBufferedEvents();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
