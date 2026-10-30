using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class HostedPracticeChecks
{
    private async Task CheckDrivingMachineGun(DevelopmentSession session, ulong target)
    {
        var arena = session.Arena!;
        var host = arena.Driver.Host!;
        ulong owner = arena.Driver.LocalVehicleId;
        var input = new PlayerInput(); AddChild(input); input.SetPhysicsProcess(false); input.SetProcessInput(false);
        input.GameplayAvailable = () => true;
        arena.CameraInput = input.Adapter;
        var camera = arena.GetNode<VehicleChaseCamera>("ChaseCamera");
        var view = (SubViewport)session.GetParent(); view.Size = new(1280, 800); view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        await Until(() => Math.Abs(host.World.GetVehicle(target).ObservedPhysics.Position.X) < 140, "target reaches straight");
        var leader = host.World.GetVehicle(target).ObservedPhysics;
        N.Vector3 forward = N.Vector3.Transform(-N.Vector3.UnitZ, leader.Orientation);
        var pose = new VehiclePhysicsState(leader.Position - forward * 35, leader.Orientation, leader.LinearVelocity, N.Vector3.Zero);
        var world = host.World.State;
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != owner ? v :
            new VehicleSnapshot(owner, v.LifeId, new(world.Tick, pose, true, false, 0, 0), v.Damage, pose)), world.Match));
        arena.Bodies[owner].Apply(pose);
        var follow = new PracticeVehicleInput(true, 35);
        bool firing = false;
        _drivingInput = peer =>
        {
            if (peer != session) return default;
            var command = follow.Capture(host.World.GetVehicle(owner), arena.Driver.Configuration.Configuration.Vehicle,
                host.World.State.Tick + 1, host.World.GetVehicle(target));
            return new(command.Tick, command.Steering, command.Accelerate, command.Brake, firing ? InputButtons.UseItem : 0, 0, 0);
        };
        int shots = 0, hits = 0, banks = 0, visible = 0;
        float oldMotion = 0, newMotion = 0, travelled = 0, oldJitter = 0, newJitter = 0, oldStep = 0, newStep = 0;
        Vector2? lastOld = null, lastNew = null;
        var previous = pose.Position;
        var native = arena.Driver.RaycastWeapon!;
        arena.Driver.RaycastWeapon = (id, from, to) =>
        {
            var result = native(id, from, to);
            if (id == owner && N.Vector3.Distance(from, to) > 200)
            { shots++; if (result?.Vehicle == target) hits++; }
            return result;
        };
        host.Items.RemovePlayer(owner);
        Require(arena.Driver.GiveDeveloperItem(HeldItem.MachineGun), "normal MG grant");
        await Frames(240);
        float initialHp = host.World.GetVehicle(target).Damage.CurrentHP;
        float minimumHp = initialHp;
        for (int frame = 0; frame < 3000; frame++)
        {
            var state = host.World.GetVehicle(owner);
            var targetState = host.World.GetVehicle(target);
            minimumHp = Math.Min(minimumHp, targetState.Damage.CurrentHP);
            var aim = host.Items.Aims.FirstOrDefault(a => a.Vehicle == owner);
            float angle = aim is null ? 1 : N.Vector3.Dot(aim.Direction, N.Vector3.Normalize(targetState.ObservedPhysics.Position + N.Vector3.UnitY * .6f - aim.Origin));
            if (frame % 120 == 0 && angle > .999f && state.Speed > 5)
            { firing = arena.Driver.RequestItemUse(); }
            if (frame % 120 == 12) firing = false;
            await Frames(1);
            if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            state = arena.Driver.LocalState!;
            Require(!input.Adapter.CameraAimActive && arena.AssistedCar == 0, "driving never activates aim assistance");
            travelled += N.Vector3.Distance(previous, state.ObservedPhysics.Position); previous = state.ObservedPhysics.Position;
            if (Math.Abs(state.ObservedPhysics.Position.X) > 107) banks++;
            if (arena.AimOverlay.Marker is Vector2 marker)
            {
                if (view.GetVisibleRect().HasPoint(marker)) visible++;
                var raw = state.ObservedPhysics;
                Vector3 old = VehicleBody.ToGodot(raw.Position + N.Vector3.Transform(WeaponAim.Pivot - N.Vector3.UnitZ * 300, raw.Orientation));
                Vector2 oldMarker = camera.UnprojectPosition(old);
                if (lastNew is Vector2 n && lastOld is Vector2 o && state.Movement.Grounded)
                {
                    float ns = marker.Y - n.Y, os = oldMarker.Y - o.Y;
                    newMotion += Math.Abs(ns); oldMotion += Math.Abs(os);
                    newJitter += Math.Abs(ns - newStep); oldJitter += Math.Abs(os - oldStep);
                    newStep = ns; oldStep = os;
                }
                lastNew = marker; lastOld = oldMarker;
            }
        }
        firing = false; await Frames(30);
        GD.Print($"DRIVING_MG shots={shots} hits={hits} HP={initialHp}->{host.World.GetVehicle(target).Damage.CurrentHP} minimumHP={minimumHp} travel={travelled:F1} bankFrames={banks} visible={visible} oldVerticalTravel={oldMotion:F1}px newVerticalTravel={newMotion:F1}px oldJitter={oldJitter:F1} newJitter={newJitter:F1}");
        Require(shots >= 10 && hits >= shots * .75f && minimumHp < initialHp, "ordinary driving MG reliably damages moving practice car");
        Require(travelled > 300 && banks > 100 && visible > 2900, "native driving covers straights/banks with visible square");
        Require(newJitter < oldJitter * .7f, "road-relative shot and square reduce suspension wobble");
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var capture = view.GetTexture().GetImage(); capture.SavePng(ProjectSettings.GlobalizePath("res://.godot/ts307-r2-driving.png"));
        }
        arena.Driver.RaycastWeapon = native; _drivingInput = null; arena.CameraInput = null; input.QueueFree();
    }
}
