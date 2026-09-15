using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Native camera/input verification, independent of physical controller availability.</summary>
public sealed partial class CameraIntegrationChecks : Node3D
{
    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <summary>Exercises the production camera node and exits nonzero on a failed invariant.</summary>
    public void Run()
    {
        try
        {
            var input = new PlayerInput();
            AddChild(input);
            input.SetPhysicsProcess(false);
            var camera = new VehicleChaseCamera();
            AddChild(camera);
            var simulation = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60));
            simulation.AddVehicle(1, new VehicleConfiguration(), new DamageConfiguration(), new VehiclePhysicsState(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero));
            VehicleSnapshot state = simulation.GetVehicle(1);
            void Follow(float steering = 0) => camera.Follow(Transform3D.Identity, state, steering, 1f / 60);
            Follow();
            foreach (int direction in new[] { -1, 1 })
            {
                for (int i = 0; i < 180; i++)
                {
                    using var mouse = new InputEventMouseMotion { ScreenRelative = new Vector2(direction * 50, 0) };
                    camera._UnhandledInput(mouse);
                    Follow(direction);
                }

                Require(Math.Abs(camera.Motion.LookAngle - (direction * 20)) < 0.01f, "Mouse and steering clamp");
                float actual = Mathf.RadToDeg(MathF.Atan2(-camera.Position.X, camera.Position.Z));
                Require(Math.Abs(actual - (direction * 20)) < 0.1f, "Actual camera orbit follows combined angle");
                for (int i = 0; i < 600; i++)
                {
                    Follow();
                }

                Require(Math.Abs(camera.Motion.LookAngle) < 0.001f, "Smooth recenter reaches neutral");
            }

            using var axis = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 1 };
            Godot.Input.ParseInputEvent(axis);
            Godot.Input.FlushBufferedEvents();
            Require(input.Adapter.CameraLookStrength() == 1, "Assigned right stick sampled");
            for (int i = 0; i < 180; i++)
            {
                Follow(1);
            }

            Require(camera.Motion.LookAngle > 19.9f, "Controller drives camera");
            input.Adapter.GameplaySuppressed = true;
            Require(input.Adapter.CameraLookStrength() == 0, "Settings suppress look");
            for (int i = 0; i < 600; i++)
            {
                using var mouse = new InputEventMouseMotion { ScreenRelative = new Vector2(100, 0) };
                camera._UnhandledInput(mouse);
                Follow();
            }

            Require(Math.Abs(camera.Motion.LookAngle) < 0.001f, "Settings reject mouse and stick input");
            input.Adapter.GameplaySuppressed = false;
            input.Adapter.Enabled = false;
            Require(input.Adapter.CameraLookStrength() == 0, "Focus suppresses look");
            using var releasedAxis = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 };
            Godot.Input.ParseInputEvent(releasedAxis);
            Godot.Input.FlushBufferedEvents();
            input.Adapter.Enabled = true;
            var physics = state.Movement.Physics;
            var damageInput = new Core.Input.InputFrame(1, 0, 0, 0, 0, 0, 0);
            simulation.Step(damageInput, new[] { new VehicleStepRequest(1, damageInput, new VehicleObservation(physics, System.Numerics.Vector3.UnitY), new[] { new VehicleEffectRequest(new DamageEffect(20, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero), new DamageContext("missile", 2, "camera-check")) }) });
            state = simulation.GetVehicle(1);
            Follow();
            float damagedShake = camera.Motion.Shake;
            Require(damagedShake > 0, "Accepted damage triggers feedback");
            Follow();
            Require(camera.Motion.Shake < damagedShake, "Repeated snapshot does not replay damage");
            var resetInput = new Core.Input.InputFrame(2, 0, 0, 0, 0, 0, 0);
            simulation.Step(resetInput, new[] { new VehicleStepRequest(1, resetInput, new VehicleObservation(physics, System.Numerics.Vector3.UnitY), reset: physics) });
            state = simulation.GetVehicle(1);
            Follow();
            Require(camera.Motion.Shake == 0 && camera.Motion.LookAngle == 0, "New life clears camera memory");
            foreach (float pitch in new[] { 0f, 1.55f, 3.14f, -1.55f, 0f })
            {
                for (int i = 0; i < 120; i++)
                {
                    var pose = new Transform3D(Basis.FromEuler(new Vector3(pitch, i * 0.015f, pitch)), new Vector3(i * 0.03f, MathF.Sin(i * 0.03f) * 4, 0));
                    camera.Follow(pose, state, i % 20 < 10 ? 1 : -1, 1f / 60);
                    Require(camera.GlobalPosition.IsFinite() && camera.GlobalBasis.IsFinite(), "Finite airborne/rotating camera");
                    Require(camera.GlobalBasis.Y.Y > 0.5f, "Readable level horizon");
                }
            }

            camera.QueueFree();
            input.QueueFree();
            GD.Print("Camera integration passed: native mouse/right-stick, combined actual angle, recenter, settings/focus gates, airborne/spinning horizon.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
