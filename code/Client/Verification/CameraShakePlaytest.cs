using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Settings;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Short native impact sequence with a zero-shake reference camera and rendered motion evidence.</summary>
public sealed partial class CameraShakePlaytest : Node
{
    private VehicleArena _arena = null!;
    private VehicleChaseCamera _camera = null!;
    private VehicleChaseCamera _reference = null!;
    private PlayerInput _input = null!;
    private PlayerSettingsController _settings = null!;
    private readonly Label _label = new() { Position = new Vector2(24, 24) };
    private readonly List<object> _results = new();
    private string _output = string.Empty;
    private int _phase = -1;
    private float _seconds;
    private float _peakPixels;
    private float _peakMetres;
    private bool _hit;
    private bool _disabled;
    private bool _enabled;
    private bool _secondHit;
    private bool _minorContact;
    private static readonly string[] Names = ["Collision 0%", "Collision 50%", "Collision 100%", "Damage 0%", "Damage 50%", "Damage 100%", "Minor contact 100%", "Disable during damage / re-enable", "Repeated damage, same life"];
    private static readonly double[] Intensities = [0, 0.5, 1, 0, 0.5, 1, 1, 1, 1];

    /// <inheritdoc/>
    public override void _Ready()
    {
        _output = OS.GetCmdlineUserArgs().Single(value => value.StartsWith("--shake-output=", StringComparison.Ordinal))[15..];
        _input = new PlayerInput();
        AddChild(_input);
        _settings = new PlayerSettingsController();
        _settings.Initialize(_input.Adapter, _output + ".settings.json");
        AddChild(_settings);
        _arena = new VehicleArena { LegacyTestLayout = true, CameraSettings = _settings };
        AddChild(_arena);
        _camera = _arena.GetNode<VehicleChaseCamera>("ChaseCamera");
        _reference = new VehicleChaseCamera { MaximumShakeMetres = 0, Fov = _camera.Fov };
        AddChild(_reference);
        _camera.MakeCurrent();
        var layer = new CanvasLayer();
        AddChild(layer);
        layer.AddChild(_label);
        _label.AddThemeFontSizeOverride("font_size", 26);
        // Sample after both production arena presentation and the actual camera have advanced.
        ProcessPriority = 100;
        _input.FrameCaptured += Advance;
        NextPhase();
    }

    private void Advance(InputFrame frame) => _arena.Advance(new InputFrame(frame.Tick, 0, 0, 0, 0, 0, 0));

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        try
        {
            _seconds += (float)delta;
            _reference.Follow(_arena.Player.GetGlobalTransformInterpolated(), _arena.Player.Snapshot, (float)delta);
            Vector3 target = _arena.Player.GetGlobalTransformInterpolated().Origin;
            float pixels = _camera.UnprojectPosition(target).DistanceTo(_reference.UnprojectPosition(target));
            pixels *= 720 / GetViewport().GetVisibleRect().Size.Y;
            float metres = _camera.GlobalPosition.DistanceTo(_reference.GlobalPosition);
            if (_phase == 6)
            {
                _minorContact |= _arena.Player.GetCollidingBodies().OfType<Node3D>().Any(body => body.GlobalPosition.Z < -39);
            }
            // The first native step applies the queued life reset; both cameras then share a baseline.
            if (_seconds > 0.1f)
            {
                _peakPixels = Math.Max(_peakPixels, pixels);
                _peakMetres = Math.Max(_peakMetres, metres);
                Require(_camera.GlobalBasis.IsEqualApprox(_reference.GlobalBasis), "Shake cannot rotate the horizon or retune aim");
                Require(metres <= 0.6501f, "Actual rendered displacement stays within the hard bound");
            }

            if (_phase is 3 or 4 or 5 or 7 or 8 && !_hit && _seconds >= 0.5f)
            {
                Hit();
                _hit = true;
            }

            if (_phase == 7 && !_disabled && _seconds >= 0.6f)
            {
                _settings.UpdateSettings(_settings.Current with { CameraShakeIntensity = 0 });
                _disabled = true;
            }
            if (_phase == 7 && !_enabled && _seconds >= 0.9f)
            {
                _settings.UpdateSettings(_settings.Current with { CameraShakeIntensity = 1 });
                _enabled = true;
            }
            if (_phase == 7 && _seconds > 0.7f)
            {
                Require(pixels < 0.01f, "Disabling clears visible motion; re-enable cannot replay the old damage");
            }
            if (_phase == 8 && !_secondHit && _seconds >= 1.1f)
            {
                Hit();
                _secondHit = true;
            }

            _label.Text = $"TS-109  |  {Names[_phase]}\nShake only: {pixels:0.0} px  |  HP {_arena.Player.DamageState.CurrentHP:0}\n{_seconds:0.00}s  — same chase, heading and physics";
            if (_seconds >= 2)
            {
                float hp = _arena.Player.DamageState.CurrentHP;
                Require(_phase != 6 || (_minorContact && _peakPixels < 0.01f && hp == 100), "Minor native wall contact actually occurs and remains silent");
                Require(pixels < 0.1f, "Feedback settles back to the unshaken chase view");
                Require(Intensities[_phase] != 0 || _peakPixels < 0.01f, "Zero produces no screen-space shake");
                Require(_phase is 0 or 1 or 2 ? hp < 100 : true, "Native collision actually occurred");
                Require(_phase is 3 or 4 or 5 or 7 ? hp == 80 : true, "The real damage event removes 20 HP");
                Require(_phase != 8 || hp == 60, "Two distinct same-life damage events remain effective");
                Require(_phase is 2 or 5 ? _peakPixels >= 8 : true, "Meaningful full-intensity feedback is visibly sized at 720p");
                Require(_phase is 1 or 4 ? _peakPixels >= 4 : true, "Half intensity remains perceptible at 720p");
                _results.Add(new { phase = Names[_phase], peakPixels = _peakPixels, peakMetres = _peakMetres, hp });
                GD.Print($"Shake playtest: {Names[_phase]}, peak={_peakPixels:F3}px / {_peakMetres:F4}m, HP={hp:F3}");
                NextPhase();
            }
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private void Hit() => _arena.Player.ApplyEffect(new DamageEffect(20, Numerics.Vector3.Zero, Numerics.Vector3.Zero), new DamageContext("missile", 99, "shake-playtest"));

    private void NextPhase()
    {
        if (++_phase == Names.Length)
        {
            SetProcess(false);
            _input.FrameCaptured -= Advance;
            File.WriteAllText(_output + ".json", System.Text.Json.JsonSerializer.Serialize(_results));
            GD.Print("Camera shake playtest passed: real impacts/damage, projected pixels, zero/half/full, minor contacts, disable/re-enable and repeated damage.");
            Finish();
            return;
        }

        _seconds = _peakPixels = _peakMetres = 0;
        _hit = _disabled = _enabled = _secondHit = _minorContact = false;
        _settings.UpdateSettings(_settings.Current with { CameraShakeIntensity = Intensities[_phase] });
        bool collision = _phase < 3;
        var position = new Numerics.Vector3(collision || _phase == 6 ? 28 : -20, VehicleDimensions.RideHeight, collision ? -28 : _phase == 6 ? -37 : 20);
        var velocity = new Numerics.Vector3(0, 0, collision ? -23 : _phase == 6 ? -2 : 0);
        _arena.Player.ResetBody(new VehiclePhysicsState(position, Numerics.Quaternion.Identity, velocity, Numerics.Vector3.Zero));
        _camera.ResetFollow();
        _reference.ResetFollow();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private async void Finish()
    {
        _arena.QueueFree();
        _reference.QueueFree();
        _settings.QueueFree();
        _input.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Task.Delay(100);
        GetTree().Quit();
    }
}
