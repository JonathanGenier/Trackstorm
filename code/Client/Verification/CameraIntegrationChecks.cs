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
            ulong inputTick = 0;
            foreach (int direction in new[] { -1, 1 })
            {
                using var axis = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = direction };
                using var key = new InputEventKey { PhysicalKeycode = direction < 0 ? Key.A : Key.D, Pressed = true };
                Godot.Input.ParseInputEvent(axis);
                Godot.Input.ParseInputEvent(key);
                Godot.Input.FlushBufferedEvents();
                int initialSteering = input.Adapter.Capture(++inputTick).Steering;
                Require(initialSteering * direction > 0 && Math.Abs(initialSteering) < 32767, "Progressive steering reaches vehicle input");
                Require(Godot.Input.GetJoyAxis(0, JoyAxis.RightX) == direction, "Right stick remains available");
                for (int i = 0; i < 120; i++)
                {
                    using var mouse = new InputEventMouseMotion { ScreenRelative = new Vector2(direction * 50, 0) };
                    Godot.Input.ParseInputEvent(mouse);
                    Godot.Input.FlushBufferedEvents();
                    input.Adapter.Capture(++inputTick);
                    Follow();
                }

                Require(input.Adapter.Capture(++inputTick).Steering == direction * 32767, "Held steering reaches full vehicle input");
                Require(camera.GlobalTransform.IsEqualApprox(neutral), "Mouse, right stick and steering alone do not move or rotate camera");
                using var release = new InputEventKey { PhysicalKeycode = key.PhysicalKeycode, Pressed = false };
                using var releasedAxis = new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 };
                Godot.Input.ParseInputEvent(release);
                Godot.Input.ParseInputEvent(releasedAxis);
                Godot.Input.FlushBufferedEvents();
                for (int i = 0; i < 120; i++)
                {
                    input.Adapter.Capture(++inputTick);
                    Follow();
                }

                Require(input.Adapter.Capture(++inputTick).Steering == 0, "Released steering returns to neutral");
            }

            foreach (int fps in new[] { 30, 60, 144 })
            {
                foreach (float yaw in new[] { -1f, 1f, 3.13f, -3.13f, 0f })
                {
                    var pose = new Transform3D(Basis.FromEuler(new Vector3(0, yaw, 0)), Vector3.Zero);
                    camera.Follow(pose, state, 1f / fps);
                    RequireHeading(camera, yaw);
                    Basis immediateAim = camera.GlobalBasis;
                    camera.Follow(pose, state, 1f / fps);
                    Require(camera.GlobalBasis.IsEqualApprox(immediateAim), "No rotational catch-up on the following frame");
                }
            }

            camera.Follow(new Transform3D(Basis.FromEuler(new Vector3(0, 1, 0)), Vector3.Zero), state, 1f / 60);
            foreach (Basis unstable in new[] { Basis.FromEuler(new Vector3(Mathf.Pi / 2, -1, 0)), Basis.FromEuler(new Vector3(0, -1, Mathf.Pi)), new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), new Basis(new Vector3(float.NaN, 0, 0), Vector3.Up, Vector3.Back) })
            {
                camera.Follow(new Transform3D(unstable, Vector3.Zero), state, 1f / 60);
                RequireHeading(camera, 1);
                Require(camera.GlobalTransform.IsFinite(), "Unstable orientation retains finite camera pose");
            }

            Follow();
            RequireHeading(camera, 0);
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
            float lateralBeforeTurn = camera.Motion.Offset.X;
            var turnedPose = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.Pi / 2, 0)), Vector3.Zero);
            camera.Follow(turnedPose, state, 1f / 60);
            RequireHeading(camera, Mathf.Pi / 2);
            Require(camera.Motion.Offset.X > 0 && camera.Motion.Offset.X < lateralBeforeTurn, "Lateral inertia damps independently of immediate heading change");
            Require(Math.Abs(camera.Motion.Offset.X) <= camera.MaximumLateralInertia, "Lateral inertia remains bounded during heading change");
            Require(Math.Abs(camera.GlobalPosition.Dot(turnedPose.Basis.X) - camera.Motion.Offset.X) < 0.00001f, "Camera retains lateral positional weight in the displayed vehicle frame");
            camera.Motion.ObserveVelocity(System.Numerics.Vector3.Zero, 1);
            camera.Motion.ObserveVelocity(System.Numerics.Vector3.Zero, 1);
            for (int i = 0; i < 240; i++)
            {
                Follow();
            }

            Require(camera.GlobalPosition.DistanceTo(neutral.Origin) < 0.001f && camera.GlobalBasis.Z.DistanceTo(neutral.Basis.Z) < 0.00001f, "Camera settles within one millimetre and 0.001 degrees of normal chase pose");
            var physics = state.Movement.Physics;
            var contact = new VehicleContact(new System.Numerics.Vector3(-23, 0, 0), System.Numerics.Vector3.UnitX, 0, 0);
            camera.ObserveCollision(new VehicleObservation(physics, System.Numerics.Vector3.UnitY, new[] { contact }), 900);
            Follow();
            Require(camera.Motion.Shake > 0 && camera.GlobalBasis.IsEqualApprox(fixedAim), "Collision feedback preserves camera orientation");
            for (int i = 0; i < 240; i++)
            {
                Follow();
            }

            Require(camera.Motion.Shake == 0, "Collision feedback decays to neutral");
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

            VerifyFreeLook(camera, input, state);
            VerifyShakeSettings(camera, input, state);
            camera.QueueFree();
            input.QueueFree();
            GD.Print("Camera integration passed: RMB orbit/hold/release, controller/dead-zone return, suppression, identity/life/reseed resets, 30/60/144 FPS, unchanged gameplay input, heading/inertia/feedback regressions.");
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

    private void VerifyShakeSettings(VehicleChaseCamera camera, PlayerInput input, VehicleSnapshot state)
    {
        var settings = new Settings.PlayerSettingsController();
        string path = ProjectSettings.GlobalizePath($"res://.godot/camera-checks/{Guid.NewGuid():N}.settings.json");
        settings.Initialize(input.Adapter, path);
        AddChild(settings);
        camera.SettingsSource = settings;
        camera.InputSource = null;
        var other = new VehicleChaseCamera();
        AddChild(other);
        other.Follow(Transform3D.Identity, state, 1f / 60);
        float fullOffset = 0;
        foreach (double intensity in new[] { 1d, 0.5d, 0.25d, 0d })
        {
            settings.UpdateSettings(settings.Current with { CameraShakeIntensity = intensity });
            camera.ResetFollow();
            camera.Follow(Transform3D.Identity, state, 1f / 60);
            Transform3D baseline = camera.GlobalTransform;
            var contact = new VehicleContact(new System.Numerics.Vector3(-23, 0, 0), System.Numerics.Vector3.UnitX, 0, 0);
            var observation = new VehicleObservation(state.ObservedPhysics, System.Numerics.Vector3.UnitY, new[] { contact });
            camera.ObserveCollision(observation, 900);
            camera.Follow(Transform3D.Identity, state, 1f / 60);
            float offset = camera.GlobalPosition.Y - baseline.Origin.Y;
            if (intensity == 1) fullOffset = offset;
            Require(fullOffset > 0 && Math.Abs(offset - fullOffset * intensity) < 0.00001, "Local intensity scales only the final shake displacement");
            Require(camera.GlobalBasis.IsEqualApprox(baseline.Basis) && camera.GlobalPosition.X == baseline.Origin.X && camera.GlobalPosition.Z == baseline.Origin.Z, "Intensity preserves normal heading and chase position");
            float envelope = camera.Motion.Shake;
            camera.ObserveCollision(observation, 900);
            Require(camera.Motion.Shake == envelope, "Repeated contacts within cooldown cannot stack shake");
            Require(camera.Motion.Shake <= 1 && Math.Abs(offset) <= camera.MaximumShakeMetres, "Feedback is safely bounded");
            settings.UpdateSettings(settings.Current with { CameraShakeIntensity = 0 });
            camera.Follow(Transform3D.Identity, state, 1f / 60);
            Require(camera.Motion.Shake == 0 && camera.GlobalTransform.IsEqualApprox(baseline), "Zero immediately clears an active impact without moving the baseline");
            settings.UpdateSettings(settings.Current with { CameraShakeIntensity = 1 });
            camera.Follow(Transform3D.Identity, state, 1f / 60);
            Require(camera.Motion.Shake == 0, "Re-enabling does not resurrect disabled feedback");
        }

        camera.ResetFollow();
        camera.Follow(Transform3D.Identity, state, 1f / 60);
        var minor = new VehicleContact(new System.Numerics.Vector3(-3, 0, 0), System.Numerics.Vector3.UnitX, 0, 0);
        camera.ObserveCollision(new VehicleObservation(state.ObservedPhysics, System.Numerics.Vector3.UnitY, new[] { minor }), 900);
        Require(camera.Motion.Shake == 0, "Minor contacts at the threshold remain silent");
        other.Motion.Impulse(0.5f);
        settings.UpdateSettings(settings.Current with { CameraShakeIntensity = 0 });
        camera.Follow(Transform3D.Identity, state, 1f / 60);
        Require(other.Motion.Shake == 0.5f, "Local settings do not affect another camera");
        camera.SettingsSource = null;
        other.QueueFree();
        settings.QueueFree();
        GD.Print("Camera shake settings passed: 0/25/50/100%, active disable/re-enable, minor/repeated contacts, bounds, local isolation and unchanged chase transform.");
    }

    private static void VerifyFreeLook(VehicleChaseCamera camera, PlayerInput input, VehicleSnapshot state)
    {
        camera.InputSource = input.Adapter;
        input.GameplayAvailable = () => true;
        input._Process(0);
        input.Adapter.Enabled = true;
        input.Adapter.CameraAvailable = true;
        void Send(InputEvent value)
        {
            using (value)
            {
                Godot.Input.ParseInputEvent(value);
                Godot.Input.FlushBufferedEvents();
            }
        }

        foreach (int fps in new[] { 30, 60, 144 })
        {
            camera.ResetFollow();
            camera.Follow(Transform3D.Identity, state, 1f / fps);
            Transform3D baseline = camera.GlobalTransform;
            Send(new InputEventMouseMotion { ScreenRelative = new Vector2(100, 0) });
            camera.Follow(Transform3D.Identity, state, 1f / fps);
            RequireHeading(camera, 0);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            for (int i = 0; i < fps; i++)
            {
                Send(new InputEventMouseMotion { ScreenRelative = new Vector2(300f / fps, -30f / fps) });
                camera.Follow(Transform3D.Identity, state, 1f / fps);
            }

            RequireHeading(camera, -0.9f);
            Require(camera.GlobalPosition.DistanceTo(baseline.Origin) > 5, "Mouse orbits the vehicle, not only the viewing direction.");
            Basis held = camera.GlobalBasis;
            camera.Follow(Transform3D.Identity, state, 1f / fps);
            Require(camera.GlobalBasis.IsEqualApprox(held), "Held RMB retains the angle at rest.");
            var frame = input.Adapter.Capture(1000);
            Require(frame.Steering == 0 && frame.Accelerate == 0 && frame.Brake == 0 && frame.Held == 0, "Camera input never reaches gameplay frame.");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            camera.Follow(Transform3D.Identity, state, 1f / fps);
            RequireHeading(camera, -0.9f * MathF.Exp(-6f / fps));
            for (int i = 0; i < fps * 3; i++) camera.Follow(Transform3D.Identity, state, 1f / fps);
            RequireHeading(camera, 0);

            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 1 });
            for (int i = 0; i < fps; i++) camera.Follow(Transform3D.Identity, state, 1f / fps);
            RequireHeading(camera, -2.2f);
            input.Adapter.DeadZone = 0;
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0.1f });
            for (int i = 0; i < fps * 3; i++) camera.Follow(Transform3D.Identity, state, 1f / fps);
            RequireHeading(camera, 0);
            Require(input.Adapter.CameraIntent == Vector2.Zero, "Analog noise is neutral even with driving dead zone disabled.");
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });

            foreach (int boundary in new[] { 0, 1, 2 })
            {
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
                Send(new InputEventMouseMotion { ScreenRelative = new Vector2(200, 50) });
                camera.Follow(Transform3D.Identity, state, 1f / fps);
                Send(new InputEventMouseMotion { ScreenRelative = new Vector2(200, 50) });
                var next = new VehicleSnapshot(state.VehicleId + (boundary == 0 ? 1ul : 0), state.LifeId + (boundary == 1 ? 1ul : 0), state.Movement, state.Damage, state.ObservedPhysics);
                if (boundary == 2) camera.ResetFollow();
                camera.Follow(Transform3D.Identity, next, 1f / fps);
                RequireHeading(camera, 0);
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
                camera.Follow(Transform3D.Identity, state, 1f / fps);
            }

            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            Send(new InputEventMouseMotion { ScreenRelative = new Vector2(200, 0) });
            input.Adapter.GameplaySuppressed = true;
            camera.Follow(Transform3D.Identity, state, 1f / fps);
            RequireHeading(camera, 0);
            input.Adapter.GameplaySuppressed = false;
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            Send(new InputEventMouseMotion { ScreenRelative = new Vector2(100, 0) });
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            camera.Follow(Transform3D.Identity, state, 1f / fps);
            RequireHeading(camera, -0.3f);
        }
    }

    private static void RequireHeading(VehicleChaseCamera camera, float yaw)
    {
        Vector3 facing = -camera.GlobalBasis.Z;
        float actualYaw = MathF.Atan2(-facing.X, -facing.Z);
        Require(Math.Abs(Mathf.AngleDifference(actualYaw, yaw)) < 0.00001f, "Camera yaw matches displayed heading within 0.001 degrees after one update");
    }
}
