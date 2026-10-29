using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class HostedPracticeChecks
{
    private static float RouteGap(N.Vector3 behind, N.Vector3 ahead)
    {
        const float length = 428 + MathF.Tau * 91;
        static float Distance(N.Vector3 p)
        {
            if (p.X > 107) { return 214 + MathF.Atan2(p.X - 107, p.Z) * 91; }
            if (p.X < -107) { return 428 + MathF.PI * 91 + MathF.Atan2(-p.X - 107, -p.Z) * 91; }
            return p.Z >= 0 ? p.X + 107 : 214 + MathF.PI * 91 + 107 - p.X;
        }
        return (Distance(ahead) - Distance(behind) + length) % length;
    }

    private async Task CheckParkedTarget(DevelopmentSession session, DevelopmentSession remote, ulong id)
    {
        var arena = session.Arena!;
        var host = arena.Driver.Host!;
        var parked = host.World.GetVehicle(id);
        var pose = parked.Movement.Physics;
        var marker = host.World.Arena.Items.Single(m => m.Id == "item-infield-04-water");
        var home = marker.Position - N.Vector3.Transform(-N.Vector3.UnitZ, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, marker.Yaw)) * 6.096f;
        Require(new N.Vector2(pose.Position.X - home.X, pose.Position.Z - home.Z).Length() < .3f, "parked twenty feet behind unchanged pond marker");
        Require(parked.Movement.Grounded && parked.Movement.CurrentSurface == SurfaceType.Dirt, "parked car supported by dirt");
        GD.Print($"PARKED_SUPPORT surface={parked.Movement.CurrentSurface} wheels={parked.Movement.Wheels.Compression}");
        ulong claims = host.Spawns!.Balances.Single(b => b.Player == id).Total;
        Require(claims == 1, "one normal pickup before parking");
        Require(host.Items.Grant(host.World, id, HeldItem.Wrench), "fill second slot through ordinary grant");
        await Frames(650);
        Require(host.Spawns.Balances.Single(b => b.Player == id).Total == claims && host.World.GetVehicle(id).Movement.Physics == pose,
            "full inventory stays parked away from respawned crate");
        Require(host.Spawns.States.Single(s => s.Id == marker.Id).Available, "crate respawns while car parks behind it");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            ulong life = host.World.GetVehicle(id).LifeId;
            var input = new Core.Input.InputFrame(host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
            host.World.Step(input, host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, input,
                new(v.Movement.Physics, N.Vector3.UnitY), v.VehicleId == id
                    ? [new VehicleEffectRequest(new(10000, N.Vector3.Zero, N.Vector3.Zero), new("test-destruction", 1, "pond"))] : [])).ToArray());
            Require(!host.World.GetVehicle(id).CanInteract, "ordinary lethal damage destroys pond car");
            if (cycle == 0)
            {
                // Another ordinary claimant makes the next pond trip wait for the crate.
                ulong humanId = arena.Driver.LocalVehicleId;
                var humanPose = host.World.GetVehicle(humanId).Movement.Physics;
                host.Items.RemovePlayer(humanId);
                PlaceHuman(new(marker.Position + N.Vector3.UnitY * VehicleDimensions.RideHeight, pose.Orientation, N.Vector3.Zero, N.Vector3.Zero));
                await Until(() => host.Spawns.States.Single(s => s.Id == marker.Id).ClaimedBy == humanId, "competing pond pickup");
                PlaceHuman(humanPose);
                void PlaceHuman(VehiclePhysicsState value)
                {
                    var boundary = host.World.State;
                    host.World.Restore(new(boundary.Tick, boundary.LastInput, boundary.Vehicles.Select(v => v.VehicleId == humanId
                        ? new VehicleSnapshot(v.VehicleId, v.LifeId, new(boundary.Tick, value, false, false, 0, 0), v.Damage, value) : v), boundary.Match));
                    arena.Bodies[humanId].Apply(value);
                }
            }
            await Until(() => host.World.GetVehicle(id).LifeId > life, "pond respawn");
            Require(new N.Vector2(host.World.GetVehicle(id).Movement.Physics.Position.X - home.X,
                host.World.GetVehicle(id).Movement.Physics.Position.Z - home.Z).Length() < .1f, "respawn begins at rearward home");
            await WaitForPondTrip(session, id, requireWait: cycle == 0);
            claims++;
            await Until(() => remote.Arena!.Driver.ItemState!.Balances.SingleOrDefault(b => b.Player == id)?.Total == claims, "replicated single claim");
            Require(host.Spawns.Balances.Single(b => b.Player == id).Total == claims, "exactly one pond pickup per life");
        }
        parked = host.World.GetVehicle(id); pose = parked.Movement.Physics;
        // Native weapon rays must see the ordinary car collider from all sides.
        foreach (var direction in new[] { Vector3.Left, Vector3.Right, Vector3.Forward, Vector3.Back })
        {
            Vector3 center = arena.Bodies[id].GlobalPosition + Vector3.Up * .2f;
            using var ray = PhysicsRayQueryParameters3D.Create(center + direction * 8, center, 2);
            using var hit = arena.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            Require(hit.Count > 0 && hit["collider"].AsGodotObject() == arena.Bodies[id], "parked car targetable from " + direction);
        }
        // Controlled initial velocity, followed entirely by native collision and authority.
        var world = host.World.State;
        ulong human = arena.Driver.LocalVehicleId;
        var ram = new VehiclePhysicsState(pose.Position + N.Vector3.UnitX * 10, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, MathF.PI / 2), -N.Vector3.UnitX * 12, N.Vector3.Zero);
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != human ? v :
            new VehicleSnapshot(v.VehicleId, v.LifeId, new(world.Tick, ram, false, false, 0, 0), v.Damage, ram)), world.Match));
        arena.Bodies[human].Apply(ram);
        await Frames(150);
        Require(host.World.GetVehicle(id).Movement.Physics == pose, "native ram cannot displace parked target");
        Require(host.World.GetVehicle(id).Damage.CurrentHP < parked.Damage.CurrentHP, "native collision still damages target");
        Require(N.Vector3.Distance(host.World.GetVehicle(human).Movement.Physics.Position, pose.Position) > 2.5f, "ramming car cannot pass through stationary target");
        await Until(() => host.Spawns.States.Single(s => s.Id == marker.Id).Available, "crate reappears clear of inspection car");
        Require(host.World.GetVehicle(id).Movement.Physics == pose && host.Spawns.Balances.Single(b => b.Player == id).Total == claims,
            "available crate never restarts a completed trip");
        GD.Print($"PARKED_TARGET claims={claims} repeatedLives=3 fixedPose=true rays=4 collisionHP={host.World.GetVehicle(id).Damage.CurrentHP:F2}");
    }

    private async Task WaitForPondTrip(DevelopmentSession session, ulong id, bool requireWait = false)
    {
        var host = session.Arena!.Driver.Host!;
        var marker = host.World.Arena.Items.Single(m => m.Id == "item-infield-04-water");
        var forward = N.Vector3.Transform(-N.Vector3.UnitZ, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, marker.Yaw));
        var home = marker.Position - forward * 6.096f;
        float farthest = 0; bool reversed = false; int waiting = 0;
        PondPracticePhase? previous = null;
        VehiclePhysicsState? waitingPose = null;
        for (int frame = 0; frame < 2400; frame++)
        {
            var car = host.World.GetVehicle(id);
            var trip = host.CaptureAuthority().PondPractice!;
            var phase = trip.Life == car.LifeId ? trip.Phase : PondPracticePhase.Approach;
            farthest = Math.Max(farthest, N.Vector3.Dot(car.Movement.Physics.Position - home, forward));
            reversed |= N.Vector3.Dot(car.Movement.Physics.LinearVelocity, forward) < -.3f;
            if (phase == PondPracticePhase.Waiting)
            {
                waiting++;
                waitingPose ??= car.Movement.Physics;
                Require(car.Movement.Physics == waitingPose.Value, "waiting at unavailable crate has no drift or oscillation");
            }
            if (phase != previous || frame % 120 == 0)
                GD.Print($"POND_TRIP life={car.LifeId} phase={phase} position={car.Movement.Physics.Position} velocity={car.Movement.Physics.LinearVelocity} heading={N.Vector3.Transform(-N.Vector3.UnitZ, car.Movement.Physics.Orientation)} wheel={car.Movement.SteeringAngle} HP={car.Damage.CurrentHP}");
            previous = phase;
            if (phase == PondPracticePhase.Parked) { break; }
            await Frames(1);
        }
        var parked = host.World.GetVehicle(id);
        Require(host.CaptureAuthority().PondPractice!.Phase == PondPracticePhase.Parked && farthest > 5.7f && reversed,
            $"pond car drives to original crate then reverses home: distance={farthest} reverse={reversed}");
        Require(!requireWait || waiting > 30, $"unavailable crate waits without loops: {waiting} frames");
        Require(parked.Damage.CurrentHP == parked.Damage.MaxHP, "pond trip causes no collision damage");
        Require(new N.Vector2(parked.Movement.Physics.Position.X - home.X, parked.Movement.Physics.Position.Z - home.Z).Length() < .3f,
            "every pond trip returns to the rearward starting position");
        Require(host.Items.Slots.Any(s => s.Vehicle == id && (s.Item != HeldItem.None || s.SecondItem != HeldItem.None)), "pond inspection retains acquired item");
        var pose = parked.Movement.Physics;
        await Frames(90);
        Require(host.World.GetVehicle(id).Movement.Physics == pose, "parked inspection pose stays exact");
        GD.Print($"POND_TRIP_COMPLETE life={parked.LifeId} travelForward={farthest:F3}m wait={waiting} parked={pose.Position}");
    }

    private async Task CaptureTargets(DevelopmentSession session, ulong[] cars)
    {
        var arena = session.Arena!;
        var view = (SubViewport)session.GetParent();
        view.Size = new(1280, 800); view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        string output = ProjectSettings.GlobalizePath("res://.godot/ts-303");
        System.IO.Directory.CreateDirectory(output);
        var camera = new Camera3D { Far = 1500 }; arena.AddChild(camera); camera.MakeCurrent();
        foreach (ulong id in cars)
        {
            Vector3 target = arena.Bodies[id].GlobalPosition;
            Vector3 inward = new Vector3(-target.X, 0, -target.Z).Normalized();
            camera.GlobalPosition = target + inward * 14 + Vector3.Up * 8; camera.LookAt(target);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = view.GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(output, $"car-{id}.png"));
        }
        camera.QueueFree();
    }
}
