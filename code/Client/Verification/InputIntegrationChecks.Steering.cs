using Godot;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class InputIntegrationChecks
{
    private void VerifySteeringPrecision()
    {
        VerifyPedalPrecision();
        VerifyBrakeEdges();
        foreach (Key key in new[] { Key.A, Key.D })
        {
            _player.Adapter.Enabled = false;
            _player.Adapter.Capture(0);
            _player.Adapter.Enabled = true;
            Send(new InputEventKey { PhysicalKeycode = key, Pressed = true });
            var pose = new VehiclePhysicsState(N.Vector3.Zero, N.Quaternion.Identity, new(0, 0, -20), N.Vector3.Zero);
            var movement = new VehicleMovement(new(), pose);
            float previous = 0;
            for (ulong tick = 1; tick <= 240; tick++)
            {
                var frame = _player.Adapter.Capture(tick);
                var state = movement.Step(frame, pose, N.Vector3.UnitY, surface: SurfaceType.Asphalt);
                float angle = Math.Abs(state.SteeringAngle);
                Check(angle >= previous && angle - previous <= movement.Configuration.SteeringResponse / 60 + 0.00001f, "native digital wheel buildup is monotonic and bounded");
                if (tick == 6)
                {
                    GD.Print($"Keyboard {key}, 100 ms: target={frame.Steering / 32767f:F4}, wheel={angle * 180 / MathF.PI:F3} degrees");
                    Check(angle is > 0.001f and < 0.035f, "100 ms keyboard tap produces a usable angle below two degrees");
                }
                previous = angle;
            }
            Check(Math.Abs(previous - movement.Configuration.SteeringLimit(20)) < 0.001f, "holding a digital key reaches the speed-appropriate steering limit");
            Send(new InputEventKey { PhysicalKeycode = key, Pressed = false });
            for (ulong tick = 241; tick <= 420; tick++)
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
        Check(Math.Abs(_player.Adapter.Capture(0).Steering - 2896) <= 1, "analog half stick gives fine curved intent and takes ownership over the released keyboard tail");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0 });
        foreach (float stick in new[] { 0.16f, 0.25f, 0.5f, 0.75f, 1f, 0f, -0.16f, -0.25f, -0.5f, -0.75f, -1f, 1f, -1f, 0f })
        {
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = stick });
            float normalized = InputAxis.Normalize(stick, _player.Adapter.DeadZone);
            float expected = MathF.CopySign(MathF.Pow(Math.Abs(normalized), 3.5f), normalized);
            Check(Math.Abs(_player.Adapter.Capture(0).Steering / 32767f - expected) < 0.00004f,
                $"native stick {stick} preserves continuous curved steering {expected}, including center and reversals");
        }
        // Sweep actual native axis samples through binding/dead-zone/curve/recording.
        // A three-degree jump would be visible here; wire resolution is about 0.0016 degree.
        foreach (float sign in new[] { -1f, 1f })
        {
            int previous = 0;
            var distinct = new HashSet<int>();
            for (int sample = 150; sample <= 550; sample++)
            {
                Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = sign * sample / 1000f });
                int intent = Math.Abs((int)_player.Adapter.Capture(0).Steering);
                Check(intent >= previous && (intent - previous) * 0.9f * 180 / (32767 * MathF.PI) < 0.04f,
                    "small native stick increments produce continuous subdegree wheel targets");
                distinct.Add(intent);
                if (sample == 400) { Check(intent * 0.9f * 180 / (32767 * MathF.PI) < 1, "40 percent raw stick retains subdegree precision"); }
                previous = intent;
            }
            Check(distinct.Count > 300, "near-center native sweep retains hundreds of distinct analog targets");
        }
        _player.Adapter.SteeringSensitivity = 2;
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0.575f });
        Check(Math.Abs(_player.Adapter.Capture(0).Steering - 5792) <= 1, "ground sensitivity scales curved analog intent independently of keyboard");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 1 });
        Check(_player.Adapter.Capture(0).Steering == short.MaxValue, "sensitivity clamps full stick to full authority");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0 });
        _player.Adapter.Enabled = false; _player.Adapter.Capture(0); _player.Adapter.Enabled = true;
        _player.Adapter.SteeringSensitivity = 1;
    }
    private void VerifyPedalPrecision()
    {
        foreach (bool brake in new[] { false, true })
        {
            _player.Adapter.Enabled = false; _player.Adapter.Capture(0); _player.Adapter.Enabled = true;
            Key key = brake ? Key.S : Key.W;
            Send(new InputEventKey { PhysicalKeycode = key, Pressed = true });
            int previous = 0;
            for (ulong tick = 1; tick <= 30; tick++)
            {
                var frame = _player.Adapter.Capture(tick);
                int value = brake ? frame.Brake : frame.Accelerate;
                Check(value >= previous, "digital pedal rises monotonically");
                if (tick == 1) { Check(value > 0 && value < 10000, "first digital pedal sample is intermediate"); }
                if (tick == 6) { Check(brake ? value is > 19000 and < 20500 : value is > 15000 and < 18000, "brake commits faster than progressive throttle"); }
                previous = value;
            }
            Check(previous == 65535, "held pedal reaches full authority");
            Send(new InputEventKey { PhysicalKeycode = key, Pressed = false });
            var released = _player.Adapter.Capture(31);
            Check((brake ? released.Brake : released.Accelerate) is > 0 and < 65535, "digital pedal release decays instead of snapping");
            var axis = brake ? JoyAxis.TriggerLeft : JoyAxis.TriggerRight;
            Send(new InputEventJoypadMotion { Device = 0, Axis = axis, AxisValue = 0.575f });
            var analog = _player.Adapter.Capture(32);
            Check(Math.Abs((brake ? analog.Brake : analog.Accelerate) - 32768) <= 1, "analog pedal bypasses digital ramp and owns a released tail");
            Send(new InputEventJoypadMotion { Device = 0, Axis = axis, AxisValue = 0 });
            var neutral = _player.Adapter.Capture(33);
            Check((brake ? neutral.Brake : neutral.Accelerate) == 0, "released digital tail cannot reappear after analog release");
        }
    }

    private void VerifyBrakeEdges()
    {
        foreach (bool analog in new[] { false, true })
        {
            _player.Adapter.Enabled = false; _player.Adapter.Capture(0); _player.Adapter.Enabled = true;
            void Pedal(bool down)
            {
                if (analog) { Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.TriggerLeft, AxisValue = down ? 0.575f : 0 }); }
                else { Send(new InputEventKey { PhysicalKeycode = Key.S, Pressed = down }); }
            }
            Pedal(true);
            var first = _player.Adapter.Capture(1);
            Check((first.Pressed & InputButtons.Brake) != 0, "keyboard/partial analog brake records raw press independently of pedal ramp");
            for (ulong tick = 2; tick <= 10; tick++) { _player.Adapter.Capture(tick); }
            Pedal(false);
            Pedal(true);
            var repeated = _player.Adapter.Capture(11);
            Check((repeated.Pressed & repeated.Released & repeated.Held & InputButtons.Brake) != 0, "quick release/repress survives within one capture for deliberate reverse");
            Pedal(false);
            _player.Adapter.Capture(12);
        }
        _player.Adapter.Enabled = false; _player.Adapter.Capture(0); _player.Adapter.Enabled = true;
    }

}
