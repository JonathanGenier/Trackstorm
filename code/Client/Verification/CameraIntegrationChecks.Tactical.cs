using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

public sealed partial class CameraIntegrationChecks
{
    private void VerifyTacticalFraming(VehicleChaseCamera camera, PlayerInput input, VehicleSnapshot state)
    {
        var settings = new Settings.PlayerSettingsController();
        settings.Initialize(input.Adapter, ProjectSettings.GlobalizePath($"res://.godot/camera-checks/{Guid.NewGuid():N}.tactical.json"));
        AddChild(settings); camera.SettingsSource = settings; camera.InputSource = input.Adapter;
        void Send(InputEvent value) { using (value) { Godot.Input.ParseInputEvent(value); Godot.Input.FlushBufferedEvents(); } }
        foreach (int fps in new[] { 30, 60, 144 })
        foreach (bool controller in new[] { false, true })
        foreach (double angle in new[] { 0d, 5, 55 })
        {
            settings.UpdateSettings(new() { CameraDownAngle = angle, CameraFov = angle == 55 ? 90 : 70,
                CameraHeight = angle == 55 ? 3 : .6, CameraDistance = angle == 55 ? 1.5 : 1.2 });
            camera.ResetFollow(); camera.WeaponAiming = true;
            camera.Follow(Transform3D.Identity, state, 1f / fps);
            Transform3D driving = camera.GlobalTransform;
            Require(Math.Abs(camera.Rotation.X + Mathf.DegToRad((float)angle)) < .0001, "Configured downward angle applies at spawn");
            Send(controller ? new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 1 }
                : new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            float yawTravel = 0;
            float lastYaw = 0;
            for (int frame = 0; frame < fps * 7; frame++)
            {
                if (!controller) Send(new InputEventMouseMotion { ScreenRelative = new(2.2f / .003f / fps, 0) });
                Vector3 previousPosition = camera.GlobalPosition;
                camera.Follow(Transform3D.Identity, state, 1f / fps);
                Vector3 facing = -camera.GlobalBasis.Z;
                float yaw = MathF.Atan2(-facing.X, -facing.Z);
                float step = Mathf.AngleDifference(lastYaw, yaw);
                Require(step < 0 && Math.Abs(step) <= (controller ? 2.76f : 2.21f) / fps, "Orbit moves every rendered frame through the wrap without a spike");
                Require(camera.GlobalPosition.DistanceTo(previousPosition) < 50f / fps, "Tactical entry retains bounded lens motion");
                Require(Math.Abs(camera.Rotation.X + Mathf.DegToRad((float)angle)) < .0001, "Horizontal armed input cannot introduce vertical pitch");
                yawTravel += Math.Abs(step); lastYaw = yaw;
                Vector2 center = camera.GetViewport().GetVisibleRect().GetCenter();
                Require(camera.ProjectRayNormal(center).Dot(-camera.GlobalBasis.Z) > .99999f, "Transition keeps final projection aligned with center aim ray");
            }
            if (controller) Require(Math.Abs(yawTravel - 2.75f * (7 - 1f / 60)) < .002f,
                "New default controller gain delivers 25 percent faster full-stick orbit at every render rate");
            Require(yawTravel > Mathf.Tau * 2, "Both devices complete two continuous full orbits");
            Require(Math.Abs(camera.GlobalPosition.Y - driving.Origin.Y) < .001,
                "Horizontal armed orbit preserves configured lens height");
            Send(controller ? new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 }
                : new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            for (int frame = 0; frame < fps * 3; frame++) camera.Follow(Transform3D.Identity, state, 1f / fps);
            Require(camera.GlobalPosition.DistanceTo(driving.Origin) < .001 && camera.GlobalBasis.IsEqualApprox(driving.Basis),
                "Exit restores exact configured driving view after mouse or unengaged controller orbit");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            Send(new InputEventMouseMotion { ScreenRelative = new(0, -3000) });
            for (int frame = 0; frame < fps; frame++) camera.Follow(Transform3D.Identity, state, 1f / fps);
            Require((-camera.GlobalBasis.Z).Dot(Vector3.Up) > .99999f, "Entry blend cannot pull held sky aim off the existing vertical limit");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            for (int frame = 0; frame < fps * 3; frame++) camera.Follow(Transform3D.Identity, state, 1f / fps);
            for (int frame = 0; frame < fps * 2; frame++)
            {
                Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = frame % 10 < 5 });
                camera.Follow(Transform3D.Identity, state, 1f / fps);
                Require(camera.GlobalTransform.IsFinite(), "Repeated toggles stay bounded");
            }
            input.Adapter.GameplaySuppressed = true;
            camera.ResetFollow(); camera.Follow(Transform3D.Identity, state, 1f / fps);
            Require(camera.GlobalTransform.IsEqualApprox(driving), "Suppression and reseed discard transition history");
            input.Adapter.GameplaySuppressed = false;
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            camera.WeaponAiming = true;
            camera.AimAttraction = (_, _, _, _) => (Vector2.Zero, true);
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            for (int frame = 0; frame < fps; frame++)
            {
                camera.Follow(Transform3D.Identity, state, 1f / fps);
                Require(Math.Abs(camera.Rotation.X + Mathf.DegToRad((float)angle)) < .0001,
                    "Tactical framing cannot rotate away from an existing acquired bearing");
            }
            Require(camera.GlobalPosition.DistanceTo(driving.Origin) < .001, "Engagement preserves configured lens position");
            camera.AimAttraction = null; camera.WeaponAiming = true;
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        }
        camera.SettingsSource = null; camera.InputSource = null; camera.ResetFollow(); settings.QueueFree();
        GD.Print("Tactical camera passed: 18 rate/device/angle cases, double 360 orbits, continuous entry/exit, centered projection, rapid toggles and reseed.");
    }
}
