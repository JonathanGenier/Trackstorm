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
            void Follow() => camera.Follow(Transform3D.Identity, state, 1f / 60);
            Follow();
            Transform3D neutral = camera.GlobalTransform;
            foreach (int direction in new[] { -1, 1 })
            {
                using var axis = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = direction };
                using var key = new InputEventKey { PhysicalKeycode = direction < 0 ? Key.A : Key.D, Pressed = true };
                Godot.Input.ParseInputEvent(axis);
                Godot.Input.ParseInputEvent(key);
                Godot.Input.FlushBufferedEvents();
                Require(input.Adapter.Capture(1).Steering == direction * 32767, "Steering still reaches vehicle input");
                Require(Godot.Input.GetJoyAxis(0, JoyAxis.RightX) == direction, "Right stick remains available");
                for (int i = 0; i < 120; i++)
                {
                    using var mouse = new InputEventMouseMotion { ScreenRelative = new Vector2(direction * 50, 0) };
                    Godot.Input.ParseInputEvent(mouse);
                    Godot.Input.FlushBufferedEvents();
                    Follow();
                }

                Require(camera.GlobalTransform.IsEqualApprox(neutral), "Mouse, right stick and steering alone do not move or rotate camera");
                using var release = new InputEventKey { PhysicalKeycode = key.PhysicalKeycode, Pressed = false };
                using var releasedAxis = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 };
                Godot.Input.ParseInputEvent(release);
                Godot.Input.ParseInputEvent(releasedAxis);
                Godot.Input.FlushBufferedEvents();
            }

            foreach (float yaw in new[] { -1f, 1f, 3.13f, -3.13f, 0f })
            {
                var pose = new Transform3D(Basis.FromEuler(new Vector3(0, yaw, 0)), Vector3.Zero);
                for (int i = 0; i < 240; i++)
                {
                    camera.Follow(pose, state, 1f / 60);
                }

                Vector3 facing = -camera.GlobalBasis.Z;
                float actualYaw = MathF.Atan2(-facing.X, -facing.Z);
                Require(Math.Abs(Mathf.AngleDifference(actualYaw, yaw)) < 0.001f, "Actual heading determines camera yaw");
            }

            for (int i = 0; i < 240; i++)
            {
                Follow();
            }

            Basis fixedAim = camera.GlobalBasis;
            camera.Motion.ObserveVelocity(new System.Numerics.Vector3(0, 0, -10), 1);
            for (int i = 0; i < 120; i++)
            {
                Follow();
            }

            Require(camera.Position.Z > camera.FollowDistance + 0.4f, "Acceleration extends distance smoothly");
            Require(camera.GlobalBasis.IsEqualApprox(fixedAim), "Acceleration does not change aim");
            camera.Motion.ObserveVelocity(System.Numerics.Vector3.Zero, 1);
            for (int i = 0; i < 120; i++)
            {
                Follow();
            }

            Require(camera.Position.Z < camera.FollowDistance - 0.4f, "Braking moves camera forward");
            camera.Motion.ObserveVelocity(new System.Numerics.Vector3(-10, 0, 0), 1);
            for (int i = 0; i < 120; i++)
            {
                Follow();
            }

            Require(camera.Position.X > 0.3f && camera.GlobalBasis.IsEqualApprox(fixedAim), "Left turn creates outside weight without yaw");
            camera.Motion.ObserveVelocity(System.Numerics.Vector3.Zero, 1);
            camera.Motion.ObserveVelocity(System.Numerics.Vector3.Zero, 1);
            for (int i = 0; i < 240; i++)
            {
                Follow();
            }

            Require(camera.GlobalPosition.DistanceTo(neutral.Origin) < 0.001f && camera.GlobalBasis.Z.DistanceTo(neutral.Basis.Z) < 0.00001f, "Camera settles within one millimetre and 0.001 degrees of normal chase pose");
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
            Require(camera.Motion.Shake == 0 && camera.Motion.Offset == System.Numerics.Vector2.Zero, "New life clears camera memory");
            var movingPhysics = new VehiclePhysicsState(physics.Position, physics.Orientation, new System.Numerics.Vector3(0, 0, -10), System.Numerics.Vector3.Zero);
            var moving = new VehicleSnapshot(state.VehicleId, state.LifeId, new VehicleState(3, movingPhysics, true, false, 0, 0), state.Damage, movingPhysics);
            camera.Follow(Transform3D.Identity, moving, 1f / 60);
            float firstOffset = camera.Motion.Offset.Y;
            Require(firstOffset > 0 && firstOffset < 0.1f, "Observed snapshot acceleration produces smooth inertia");
            var cruising = new VehicleSnapshot(state.VehicleId, state.LifeId, new VehicleState(4, movingPhysics, true, false, 0, 0), state.Damage, movingPhysics);
            camera.Follow(Transform3D.Identity, cruising, 1f / 60);
            Require(camera.Motion.Offset.Y < firstOffset, "Constant measured speed begins settling");
            state = cruising;
            foreach (float pitch in new[] { 0f, 1.55f, 3.14f, -1.55f, 0f })
            {
                for (int i = 0; i < 120; i++)
                {
                    var pose = new Transform3D(Basis.FromEuler(new Vector3(pitch, i * 0.015f, pitch)), new Vector3(i * 0.03f, MathF.Sin(i * 0.03f) * 4, 0));
                    camera.Follow(pose, state, 1f / 60);
                    Require(camera.GlobalPosition.IsFinite() && camera.GlobalBasis.IsFinite(), "Finite airborne/rotating camera");
                    Require(camera.GlobalBasis.Y.Y > 0.5f, "Readable level horizon");
                }
            }

            camera.QueueFree();
            input.QueueFree();
            GD.Print("Camera integration passed: ignored mouse/right-stick/steering, actual heading alignment, acceleration/braking/lateral inertia, settling, damage/life reset, airborne horizon.");
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
