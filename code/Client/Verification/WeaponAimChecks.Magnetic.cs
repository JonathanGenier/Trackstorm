using Godot;
using Trackstorm.Core.Items;
using Trackstorm.Client.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckMagneticDeparture()
    {
        var arena = _arenas[1];
        var host = _arenas[0].Driver.Host!;
        foreach (float distance in new[] { 35f, 200f })
        foreach (bool controller in new[] { false, true })
        foreach (float sign in new[] { -1f, 1f })
        {
            host.Items.RemovePlayer(Shooter);
            // Isolate distant input from the prototype's intervening world walls.
            float height = Ground + (distance > 100 ? 35 : 0);
            _heldShooter = new(0, height, distance);
            _heldTargets = (new(0, height, 0), new(70, height, 0));
            Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
            host.Items.Grant(host.World, Shooter, HeldItem.Missile);
            Camera.ResetFollow(); await Frames(180);
            await Until(() => arena.Driver.LocalItem is { } item && item.Active.Item == HeldItem.Missile &&
                arena.Driver.MissileReady?.Invoke(item) == true, "Magnetic fixture waits for actual Missile deployment", 1200);
            await AimAtCurrent(() => arena.Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
            Require(arena.AssistedCar == 1, "Magnetic lead fixture acquired");
            Vector2 previous = arena.AimOverlay.Marker!.Value;
            bool departingWhileRetained = false;
            for (int frame = 0; frame < 50; frame++)
            {
                if (controller) Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = sign * .6f });
                else Send(new InputEventMouseMotion { ScreenRelative = new(sign * 1.5f, 0) });
                await Frames(2);
                await ToSignal(GetTree().CreateTimer(0), SceneTreeTimer.SignalName.Timeout);
                RequireShot(arena.AimOverlay.Marker is not null, "Square persists throughout gradual departure");
                Vector2 cursor = arena.AimOverlay.Marker!.Value;
                RequireShot(cursor.DistanceTo(previous) < 35, "Gradual departure has no cursor teleport");
                Vector2 target = Camera.UnprojectPosition(arena.Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
                if (arena.AssistedCar == 1 && cursor.DistanceTo(target) > 2) departingWhileRetained = true;
                previous = cursor;
            }
            Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0 });
            Require(departingWhileRetained && arena.AssistedCar == 0,
                $"Progressive {(controller ? "controller" : "mouse")} departure sign={sign} moves visibly before release");
            await Frames(30);
            await ToSignal(GetTree().CreateTimer(0), SceneTreeTimer.SignalName.Timeout);
            Vector2 marker = arena.AimOverlay.Marker!.Value;
            Vector3 lens = Camera.ProjectRayOrigin(marker);
            Vector3 ray = Camera.ProjectRayNormal(marker);
            using var query = PhysicsRayQueryParameters3D.Create(lens, lens + ray * 300, 1);
            query.HitFromInside = true;
            using var hit = Camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
            var local = arena.Driver.LocalState!;
            N.Vector3 pivot = local.ObservedPhysics.Position + N.Vector3.Transform(WeaponAim.Pivot, local.ObservedPhysics.Orientation);
            N.Vector3 expected = hit.Count == 0 ? VehicleBody.ToCore(ray) : N.Vector3.Normalize(VehicleBody.ToCore(hit["position"].AsVector3()) - pivot);
            Require(N.Vector3.Dot(expected, arena.Driver.DesiredAim!.Value) > .9999f, "Departed visible cursor ray matches submitted Missile intent");
            await Capture($"magnetic-lead-{distance}-{controller}-{sign}");
            // PNG encoding can outlast transient accepted-aim freshness. Resume
            // native simulation before requesting the ordinary ready firing action.
            await Frames(30);
            Require(arena.Driver.RequestItemUse(), "Manually aimed Missile request accepted");
            await Frames(30);
            Require(host.Items.Slots.Single(s => s.Vehicle == Shooter).Active.Item == HeldItem.None, "Authority launches the manually aimed Missile");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        }
        _heldTargets = null;
        _heldShooter = null;
    }
}
