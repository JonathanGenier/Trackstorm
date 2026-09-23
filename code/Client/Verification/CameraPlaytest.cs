using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Synthetic native controls exercised through a running arena's displayed-pose camera.</summary>
internal static class CameraPlaytest
{
    internal static async Task Run(Node owner, VehicleChaseCamera camera, PlayerInput input, Func<Transform3D> pose, string output, Action? respawn = null)
    {
        input.GameplayAvailable = () => true;
        input.Adapter.Enabled = true;
        input.Adapter.GameplaySuppressed = false;
        input.Adapter.DiagnosticSuppressed = false;
        input._Process(0);
        camera.InputSource = input.Adapter;
        camera.ResetFollow();
        async Task Wait(float seconds)
        {
            double elapsed = 0;
            int frames = 0;
            // ProcessFrame resumes before node processing; one slow frame must not skip the camera update.
            do
            {
                await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
                elapsed += owner.GetProcessDeltaTime();
                frames++;
            } while (elapsed < seconds || frames < 2);
        }

        void Send(InputEvent value)
        {
            using (value)
            {
                Godot.Input.ParseInputEvent(value);
                Godot.Input.FlushBufferedEvents();
            }
        }

        float Offset()
        {
            Vector3 vehicle = -pose().Basis.Z;
            Vector3 view = -camera.GlobalBasis.Z;
            return Mathf.AngleDifference(MathF.Atan2(-vehicle.X, -vehicle.Z), MathF.Atan2(-view.X, -view.Z));
        }

        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Live camera: " + message);
        }

        async Task Capture(string name)
        {
            if (DisplayServer.GetName() != "headless")
            {
                await owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                owner.GetViewport().GetTexture().GetImage().SavePng(output + ".camera-" + name + ".png");
                // Absorb synchronous image encoding before the next timed control transition.
                await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
                await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }

        await Wait(0.1f);
        await Capture("chase");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Send(new InputEventMouseMotion { ScreenRelative = new Vector2(400, -60) });
        await Wait(0.5f);
        Require(Math.Abs(Offset() + 1.2f) < 0.03f, "RMB orbit follows the displayed pose.");
        await Capture("mouse-orbit");
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        await Wait(0.15f);
        Require(Math.Abs(Offset()) is > 0.1f and < 1.1f, "Release returns smoothly, without snapping.");
        await Capture("returning");
        await Wait(2);
        Require(Math.Abs(Offset()) < 0.01f, "Mouse release returns behind the vehicle.");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0.8f });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightY, AxisValue = -0.3f });
        await Wait(0.6f);
        Require(Math.Abs(Offset()) > 0.5f, "Controller orbits on the live path.");
        await Capture("stick-orbit");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0.07f });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightY, AxisValue = -0.09f });
        await Wait(2);
        Require(Math.Abs(Offset()) < 0.01f, "Neutral stick with noise recenters.");
        await Capture("recentered");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightY, AxisValue = 0 });
        if (respawn is not null)
        {
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            Send(new InputEventMouseMotion { ScreenRelative = new Vector2(-400, 40) });
            await Wait(0.2f);
            Require(Math.Abs(Offset()) > 1, "Reset begins with an active orbit.");
            respawn();
            await Wait(0.2f);
            Require(Math.Abs(Offset()) < 0.01f, "New life resets while RMB remains held.");
            await Capture("respawn");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        }

        GD.Print("Live camera playtest passed: mouse orbit/smooth return, two-axis controller orbit/noisy-neutral return, displayed vehicle pose" + (respawn is null ? "." : ", native life reset."));
    }
}
