using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class HostedPracticeChecks
{
    private async Task CheckMissilePracticePair(DevelopmentSession session, DevelopmentSession remote, ulong[] cars)
    {
        var arena = session.Arena!;
        var host = arena.Driver.Host!;
        var input = new PlayerInput(); AddChild(input); input.SetPhysicsProcess(false); input.SetProcessInput(false);
        input.GameplayAvailable = () => true;
        arena.CameraInput = input.Adapter;
        var camera = arena.GetNode<VehicleChaseCamera>("ChaseCamera");
        var view = (SubViewport)session.GetParent();
        view.Size = new(1280, 800); view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        string output = ProjectSettings.GlobalizePath("res://.godot/ts263/lobby");
        System.IO.Directory.CreateDirectory(output);
        void Send(InputEvent value)
        {
            input.Adapter.Enabled = true;
            Godot.Input.ParseInputEvent(value); Godot.Input.FlushBufferedEvents(); input._Input(value); value.Dispose();
        }
        async Task Look(ulong target)
        {
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            for (int attempt = 0; attempt < 10 && arena.AssistedCar != target; attempt++)
            {
                Vector3 direction = (arena.Bodies[target].VisualTransform * new Vector3(0, .3f, 0) - camera.GlobalPosition).Normalized();
                float yaw = MathF.Atan2(-direction.X, -direction.Z), pitch = MathF.Asin(direction.Y);
                Send(new InputEventMouseMotion { ScreenRelative = new(-Mathf.AngleDifference(camera.Rotation.Y, yaw) / .003f,
                    -(pitch - camera.Rotation.X) / .003f) });
                await Frames(15);
                GD.Print($"LOBBY_AIM target={target} selected={arena.AssistedCar} {arena.AimTargetDiagnostics(target)}");
            }
            Require(arena.AssistedCar == target, "shared Missile acquisition/switch to lobby car " + target);
            await Frames(25);
            Require(arena.AssistedCar == target && arena.TargetIndicator.Target == target && arena.TargetIndicator.RingVisible && arena.TargetIndicator.ChevronVisible,
                "retained ground ring and overhead chevron for lobby car " + target);
            if (DisplayServer.GetName() != "headless")
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = view.GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(output, "lobby-car-" + target + ".png"));
            }
        }
        for (int pass = 0; pass < 2; pass++)
        {
            host.Items.RemovePlayer(arena.Driver.LocalVehicleId);
            await Frames(100);
            // Place only the human shooter beside the real route-driven pair. Their
            // ordinary lobby identities, input, physics and route remain untouched.
            var state = host.World.State;
            var leader = host.World.GetVehicle(cars[0]).ObservedPhysics;
            var right = N.Vector3.Transform(N.Vector3.UnitX, leader.Orientation);
            var forward = N.Vector3.Transform(-N.Vector3.UnitZ, leader.Orientation);
            N.Vector3 placement = leader.Position - right * 20 - forward * 5;
            using var groundQuery = PhysicsRayQueryParameters3D.Create(VehicleBody.ToGodot(placement) + Vector3.Up * 100,
                VehicleBody.ToGodot(placement) - Vector3.Up * 100, 1);
            using var groundHit = arena.GetWorld3D().DirectSpaceState.IntersectRay(groundQuery);
            Require(groundHit.Count > 0, "native support exists beneath lobby shooter fixture");
            placement.Y = groundHit["position"].AsVector3().Y + 1.2f;
            GD.Print($"LOBBY_SHOOTER placement={placement} support={groundHit["position"]}");
            var pose = new VehiclePhysicsState(placement,
                leader.Orientation, N.Vector3.Zero, N.Vector3.Zero);
            host.World.Restore(new(state.Tick, state.LastInput, state.Vehicles.Select(v => v.VehicleId != arena.Driver.LocalVehicleId ? v :
                new VehicleSnapshot(v.VehicleId, v.LifeId, new(state.Tick, pose, false, false, 0, 0), v.Damage, pose)), state.Match));
            arena.Bodies[arena.Driver.LocalVehicleId].Apply(pose);
            await Until(() => host.World.GetVehicle(arena.Driver.LocalVehicleId).Movement.Grounded, "lobby shooter settles on native support");
            Require(arena.Driver.GiveDeveloperItem(HeldItem.Missile), "lobby shooter acquires Missile");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); camera.ResetFollow();
            await Until(() => arena.Driver.LocalItem?.Active.Item == HeldItem.Missile && arena.Driver.MissileReady?.Invoke(arena.Driver.LocalItem) == true, "lobby Missile deployment");
            foreach (ulong target in new[] { cars[pass], cars[1 - pass], cars[pass] }) { await Look(target); }
            ulong token = arena.Driver.LocalItem!.Active.Token;
            var aim = host.Items.Aims.Single(a => a.Vehicle == arena.Driver.LocalVehicleId);
            var toward = N.Vector3.Normalize(VehicleBody.ToCore(arena.Bodies[cars[pass]].VisualTransform * new Vector3(0, .3f, 0)) - aim.Origin);
            Require(N.Vector3.Dot(aim.Direction, toward) > .995f, "locked initial aim follows selected moving lobby car");
            Require(arena.Driver.RequestItemUse(), "fire while locked on lobby car " + cars[pass]);
            await Until(() => host.Items.Slots.Single(s => s.Vehicle == arena.Driver.LocalVehicleId).Active.Item == HeldItem.None, "one-shot authority consumed");
            await Until(() => remote.Arena!.Driver.ItemState?.Slots.Any(s => s.Vehicle == arena.Driver.LocalVehicleId && s.Active.Token == token && s.Active.Item == HeldItem.None) == true,
                "remote confirms fired lobby Missile capability");
            Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); await Frames(10);
            Require(arena.AssistedCar == 0 && !arena.TargetIndicator.ChevronVisible, "lobby release clears lock presentation");
            GD.Print($"LOBBY_MISSILE target={cars[pass]} token={token} direction={aim.Direction} speed={host.World.GetVehicle(cars[pass]).Speed:F3}");
        }
        arena.CameraInput = null;
        input.QueueFree();
    }
}
