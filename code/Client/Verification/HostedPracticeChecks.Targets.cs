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
        Require(pose.Position.X == marker.Position.X && pose.Position.Z == marker.Position.Z, "parked exactly over authored pond marker");
        Require(parked.Movement.Grounded && parked.Movement.CurrentSurface == SurfaceType.Dirt, "parked car supported by dirt");
        GD.Print($"PARKED_SUPPORT surface={parked.Movement.CurrentSurface} wheels={parked.Movement.Wheels.Compression}");
        ulong claims = host.Spawns!.Balances.Single(b => b.Player == id).Total;
        Require(host.Items.Slots.Single(s => s.Vehicle == id).Full && claims == 2, "normal cooldown fills two slots then stops");
        await Frames(90);
        Require(host.Spawns.Balances.Single(b => b.Player == id).Total == claims, "full inventory causes no duplicate claims");
        Require(host.TryConfigure(0, new Dictionary<string, double> { ["spawns.cooldown_ticks"] = 30 }, out _), "short test cooldown accepted");
        for (int cycle = 0; cycle < 12; cycle++)
        {
            var slot = host.Items.Slots.Single(s => s.Vehicle == id);
            Require(host.Items.Discard(host.World, id, slot.Life, slot.Active.Token, slot.SelectionRevision), "ordinary discard frees one slot");
            await Until(() => host.Spawns.Balances.Single(b => b.Player == id).Total == claims + 1, "stationary respawn pickup");
            claims++;
            await Until(() => remote.Arena!.Driver.ItemState!.Balances.SingleOrDefault(b => b.Player == id)?.Total == claims, "replicated single claim");
            Require(host.World.GetVehicle(id).Movement.Physics == pose, "pickup cannot move target");
        }
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
        GD.Print($"PARKED_TARGET claims={claims} repeated=12 fixedPose=true rays=4 collisionHP={host.World.GetVehicle(id).Damage.CurrentHP:F2}");
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
