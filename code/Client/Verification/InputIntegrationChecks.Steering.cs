using Godot;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class InputIntegrationChecks
{
    private void VerifySteeringPrecision()
    {
        foreach (Key key in new[] { Key.A, Key.D })
        {
            _player.Adapter.Enabled = false;
            _player.Adapter.Capture(0);
            _player.Adapter.Enabled = true;
            Send(new InputEventKey { PhysicalKeycode = key, Pressed = true });
            var pose = new VehiclePhysicsState(N.Vector3.Zero, N.Quaternion.Identity, new(0, 0, -20), N.Vector3.Zero);
            var movement = new VehicleMovement(new(), pose);
            float previous = 0;
            for (ulong tick = 1; tick <= 180; tick++)
            {
                var frame = _player.Adapter.Capture(tick);
                var state = movement.Step(frame, pose, N.Vector3.UnitY, surface: SurfaceType.Asphalt);
                float angle = Math.Abs(state.SteeringAngle);
                Check(angle >= previous && angle - previous <= movement.Configuration.SteeringResponse / 60 + 0.00001f, "native digital wheel buildup is monotonic and bounded");
                if (tick == 6)
                {
                    GD.Print($"Keyboard {key}, 100 ms: target={frame.Steering / 32767f:F4}, wheel={angle * 180 / MathF.PI:F3} degrees");
                    Check(angle is > 0.005f and < 0.035f, "100 ms keyboard tap produces a usable angle below two degrees");
                }
                previous = angle;
            }
            Check(previous > 0.899f, "holding a digital key still reaches unrestricted full lock");
            Send(new InputEventKey { PhysicalKeycode = key, Pressed = false });
            for (ulong tick = 181; tick <= 330; tick++)
            {
                var state = movement.Step(_player.Adapter.Capture(tick), pose, N.Vector3.UnitY);
                Check(Math.Abs(state.SteeringAngle) <= previous + 0.00001f, "digital release returns progressively without overshoot");
                previous = Math.Abs(state.SteeringAngle);
            }
            Check(previous < 0.001f, "released digital steering settles at center");
        }
        Send(new InputEventKey { PhysicalKeycode = Key.D, Pressed = true });
        for (ulong tick = 1; tick <= 90; tick++) { _player.Adapter.Capture(tick); }
        Send(new InputEventKey { PhysicalKeycode = Key.D, Pressed = false });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0.575f });
        Check(Math.Abs(_player.Adapter.Capture(0).Steering - 16384) <= 1, "analog half intent remains immediate and takes ownership over the released keyboard tail");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0 });
        _player.Adapter.Shaping = DrivingInputShaping.Aerial;
        Send(new InputEventKey { PhysicalKeycode = Key.D, Pressed = true });
        InputFrame air = default;
        for (ulong tick = 1; tick <= 3; tick++) { air = _player.Adapter.Capture(tick); }
        Check(air.Steering == short.MaxValue, "aerial digital authority retains its approved 50 ms buildup");
        Send(new InputEventKey { PhysicalKeycode = Key.D, Pressed = false });
        _player.Adapter.Enabled = false; _player.Adapter.Capture(0); _player.Adapter.Enabled = true;
        _player.Adapter.Shaping = new();
    }
}
