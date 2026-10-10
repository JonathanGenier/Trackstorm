using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckAlongsideAcquisition()
    {
        var arena = _arenas[1];
        _arenas[0].Driver.Host!.Items.Grant(_arenas[0].Driver.Host!.World, Shooter, HeldItem.MachineGun);
        arena.AimReleaseObserved = reason => GD.Print("ALONGSIDE_RELEASE " + reason);
        foreach (float x in new[] { 0f, -3.7f })
        {
            Position(new(0, Ground, 35), new(x, Ground, x == 0 ? 25 : 35), new(7, Ground, 25));
            Camera.ResetFollow(); await Frames(20);
            await AimAtCurrent(() => arena.Bodies[1].VisualPosition + Vector3.Up * .9f);
            await Frames(35);
            LogTargetGeometry("alongside regression", 1);
            GD.Print($"ALONGSIDE x={x} target={arena.AssistedCar} {arena.AimTargetDiagnostics(1)}");
            Require(arena.AssistedCar == 1 && arena.AimOverlay.Bounds is { } bounds && bounds.HasPoint(ViewCenter),
                "Aiming onto a close car body is acquisition/fine placement, not centre-relative departure");
        }
        arena.AimReleaseObserved = null;
    }

    private async Task CheckAimApproachAndVisibility(float floor, Settings.PlayerSettingsController settings)
    {
        var arena = _arenas[1];
        var host = _arenas[0].Driver.Host!;
        float cone = host.Items.Configuration.Aim.AssistDegrees;
        Vector3 Center() => arena.Bodies[1].VisualTransform * new Vector3(0, .3f, 0);
        foreach (float distance in new[] { 6f, 35f, 200f })
        {
            Engine.MaxFps = 60;
            settings.UpdateSettings(new());
            host.Items.RemovePlayer(Shooter);
            Position(new(0, floor, distance), new(0, floor, 0), new(100, floor, 0));
            _heldTargets = (new(0, floor, 0), new(100, floor, 0));
            host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
            host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_assist_degrees"] = 0 }, out _);
            Camera.ResetFollow(); await Frames(150);
            await AimAtCurrent(() => Center() + Vector3.Right * Math.Max(5, distance * .14f));
            settings.UpdateSettings(settings.Current with { MouseAimSensitivity = .25 });
            host.TryConfigure(0, new Dictionary<string, double> { ["items.aim_assist_degrees"] = cone }, out _);
            await Frames(30);
            int acquired = -1;
            for (int frame = 0; frame < 90; frame++)
            {
                Send(new InputEventMouseMotion { ScreenRelative = new(-5, 0) });
                await Frames(1);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (arena.AssistedCar == 1) { acquired = frame; break; }
            }
            GD.Print($"CONTINUOUS_APPROACH distance={distance} acquiredFrame={acquired} {arena.AimTargetDiagnostics(1)}");
            Require(acquired >= 0, "Ongoing toward-car mouse movement acquires without first stopping the mouse");
            settings.UpdateSettings(new());
            // Visibility is an independent engagement, not a continuation of the
            // deliberate approach/sweep history measured above.
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(2);
            await AimAtCurrent(Center); await Frames(45);
            Require(arena.AssistedCar == 1, "Fresh visible target established before partial-occlusion measurement");
            // A narrow real obstacle hides the centre ray but leaves the car's sides
            // visible. Repeated camera jolts must not mistake that for full cover.
            var cover = new StaticBody3D { Position = Camera.GlobalPosition.Lerp(Center(), .8f), CollisionLayer = 1 };
            var shape = new BoxShape3D { Size = new(.25f, 4, .25f) };
            cover.AddChild(new CollisionShape3D { Shape = shape }); arena.AddChild(cover);
            var releases = new List<string>();
            arena.AimReleaseObserved = releases.Add;
            for (int frame = 0; frame < 60; frame++)
            {
                if (frame % 15 == 0) { Camera.Motion.Impulse(1); }
                await Frames(1);
            }
            GD.Print($"PARTIAL_VISIBILITY distance={distance} target={arena.AssistedCar} releases={string.Join(';', releases)}");
            Require(arena.AssistedCar == 1 && releases.Count == 0, "Visible body survives centre-ray occlusion and camera jolts");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(2);
            Require(arena.AssistedCar == 0, "RMB release clears even when other body samples remain visible");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true }); await Frames(8);
            Require(arena.AssistedCar == 1, "Visible body permits fresh acquisition with an obscured centre ray");
            shape.Size = new(12, 12, 1); await Frames(4);
            Require(arena.AssistedCar == 0 && releases.Any(reason => reason.Contains("occluded")), "Full cover still clears assistance immediately");
            cover.Free(); arena.AimReleaseObserved = null;
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(4);
        }
        settings.UpdateSettings(new());
    }
}
