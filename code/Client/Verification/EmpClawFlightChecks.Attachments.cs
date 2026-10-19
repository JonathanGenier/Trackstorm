using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class EmpClawFlightChecks
{
    private Core.Input.InputFrame _victimInput;
    private N.Quaternion _victimOrientation = N.Quaternion.Identity;

    private EmpClawAttachments Attachments(int peer) => _arenas[peer].FindChildren("*", "Node", true, false).OfType<EmpClawAttachments>().Single();

    private async Task<EmpClawAttachment> AttachNative(N.Quaternion orientation)
    {
        _victimOrientation = orientation;
        _positions[1] = new(0, 105, 0);
        _positions[2] = new(0, 106.3f, -12);
        _positions[3] = new(40, 105, 30);
        ulong id = await Fire(0, 2);
        await Until(() => Host.Items.EmpAttachments.Any(c => c.Id == id), "Native sweep creates an attachment at its actual car contact.");
        await Until(() => _arenas.All(a => a.Driver.ItemState!.EmpAttachments.Any(c => c.Id == id)), "Attachment reaches all three impaired UDP peers.");
        return Host.Items.EmpAttachments.Single(c => c.Id == id);
    }

    private async Task NativeDriving()
    {
        var claw = await AttachNative(N.Quaternion.Identity);
        _positions.Remove(2);
        await Frames(150);
        var body = _arenas[0].Bodies[2];
        var start = body.GlobalPosition;
        _victimInput = new(0, 6000, ushort.MaxValue, 0, 0, 0, 0);
        await Frames(180);
        Check(body.GlobalPosition.DistanceTo(start) > 2, "EMP victim physically accelerates and turns under reduced engine power.");
        Check(Host.World.GetVehicle(2).Movement.EnginePowerScale == .2f, "Suppression survives real driving and native contacts.");
        Check(Host.Items.Grant(Host.World, 2, HeldItem.Nitro), "Victim obtains normal Nitro.");
        await Frames(60);
        var slot = Host.Items.Slots.Single(s => s.Vehicle == 2).Active;
        _victimInput = new(0, 0, ushort.MaxValue, 0, Core.Input.InputButtons.UseItem, Core.Input.InputButtons.UseItem, 0);
        Check(Host.Items.RequestUse(Host.World, 2, slot.Life, slot.Token), "Victim requests real Nitro.");
        await Until(() => Host.World.GetVehicle(2).Movement.Nitro.Active, "Real Nitro activates while EMP remains.");
        Check(Host.World.GetVehicle(2).Movement.EnginePowerScale == .2f, "Nitro shares the same capped engine power fraction.");
        await Frames(60);
        await Capture("native-driving-nitro", body.GlobalPosition + new Vector3(7, 4, 7), body.GlobalPosition);
        _victimInput = default;
        // Seed one airborne angular-velocity fixture, then let production movement and
        // native collision queries integrate the tumble without scripted poses.
        var boundary = Host.World.State;
        Host.World.Restore(new(boundary.Tick, boundary.LastInput, boundary.Vehicles.Select(v =>
        {
            if (v.VehicleId != 2) { return v; }
            var old = v.Movement.Physics;
            var pose = new Core.Vehicles.VehiclePhysicsState(old.Position + N.Vector3.UnitY * 2,
                old.Orientation, old.LinearVelocity + N.Vector3.UnitY * 5, new(3, 0, 3));
            return new Core.Vehicles.VehicleSnapshot(v.VehicleId, v.LifeId,
                new(boundary.Tick, pose, false, false, 0, 0, empUntilTick: v.Movement.EmpUntilTick), v.Damage, pose);
        }), boundary.Match));
        await Frames(60);
        Check(Host.Items.EmpAttachments.Any(c => c.Id == claw.Id), "Physical collision/tumble retains the same attachment identity.");
        await Capture("native-tumble", body.GlobalPosition + new Vector3(7, 4, 7), body.GlobalPosition);
        Check(Host.Items.RemoveEmpAttachment(claw.Id), "Remove the final claw after native driving.");
        await Frames(120);
        Check(Host.World.GetVehicle(2).Movement.EnginePowerScale == 1, "Native driving restores full power after the final claw ends.");
        Check(Enumerable.Range(0, 3).All(p => Attachments(p).Views.Count == 0 && Attachments(p).ReleasedCount == 0), "Repeated-use cleanup includes native driving resources.");
        Record(new { scenario = "native-driving-nitro-tumble", displacement = body.GlobalPosition.DistanceTo(start), hp = Host.World.GetVehicle(2).Damage.CurrentHP });
    }

    private async Task AttachmentLifecycle()
    {
        var faces = new (string Name, N.Quaternion Pose)[]
        {
            ("rear", N.Quaternion.Identity), ("front", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, MathF.PI)),
            ("right", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, MathF.PI / 2)),
            ("left", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -MathF.PI / 2)),
            ("roof", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, MathF.PI / 2)),
            ("underside", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, -MathF.PI / 2))
        };
        foreach (var face in faces)
        {
            var claw = await AttachNative(face.Pose);
            await Frames(30);
            Check(Host.World.GetVehicle(2).Movement.EnginePowerScale == .2f, "Native hit applies one 80% impairment.");
            Check(claw.ExpiresAtTick - claw.AttachedAtTick == 1800, "Exact thirty-second authoritative attachment budget.");
            Check(Attachments(0).Views.Single().Inflation > 2.5f, "Attached nose inflates.");
            await Capture("attachment-" + face.Name, new(7, 111, -7), VehicleBody.ToGodot(_positions[2]));
            Record(new { scenario = "native-attachment", face = face.Name, claw.Id, claw.LocalPosition, claw.LocalNormal });
            Check(Host.Items.RemoveEmpAttachment(claw.Id), "Specific host removal accepted.");
            await Until(() => _arenas.All(a => a.Driver.ItemState!.EmpAttachments.Count == 0), "Removal synchronized.");
            // Reliable delivery/retransmission and the cosmetic tail run on different clocks.
            // Bound cleanup after delivery instead of assuming it arrived in the first 0.5 seconds.
            await Until(() => Enumerable.Range(0, 3).All(p => Attachments(p).Views.Count == 0 && Attachments(p).ReleasedCount == 0),
                "All attachment and release resources reclaimed.", 360);
        }

        var first = await AttachNative(N.Quaternion.Identity);
        var second = await AttachNative(N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, .9f));
        var third = await AttachNative(N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, MathF.PI / 2));
        Check(Host.Items.EmpAttachments.Count == 3, "Three simultaneous independent claws.");
        Check(first.ExpiresAtTick < second.ExpiresAtTick && second.ExpiresAtTick < third.ExpiresAtTick, "New hits never refresh earlier deadlines.");
        await Frames(30);
        await Capture("three-attached", new(7, 111, -7), VehicleBody.ToGodot(_positions[2]));
        var stable = Host.Items.EmpAttachments.ToArray();
        float maximumError = 0;
        for (int frame = 0; frame < 180; frame++)
        {
            _victimOrientation = N.Quaternion.CreateFromYawPitchRoll(frame * .025f, frame * .03f, frame * .04f);
            _positions[2] = new(MathF.Sin(frame * .04f) * 4, 107 + MathF.Sin(frame * .03f), -12 + MathF.Cos(frame * .04f) * 3);
            await Frames(1);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var view in Attachments(0).Views)
            {
                var nearest = stable.Min(c => view.Attachment.GlobalPosition.DistanceTo(
                    _arenas[0].Bodies[2].VisualTransform * VehicleBody.ToGodot(c.LocalPosition)));
                maximumError = Math.Max(maximumError, nearest);
            }
        }
        Check(maximumError < .02f, $"Moving/rolling rendered contact anchors remain stable: {maximumError} m.");
        Record(new { scenario = "rotating-moving-anchors", maximumError, attachments = 3 });

        // Exercise the production admission checkpoint and host recovery serializers at a nonzero age.
        var fresh = Host.PrepareJoin(99, Host.Items.Revision + 1);
        Check(fresh is not null, "Fresh active admission captures current attachments.");
        var decoded = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(fresh!));
        Check(decoded.Items.EmpAttachments.SequenceEqual(stable), "Late join checkpoint preserves every original anchor/deadline.");
        var authority = new ItemAuthority(Host.Items.Configuration);
        authority.Restore(decoded.Items, Host.Items.Revision, Host.Items.TokenHighWater);
        Check(authority.EmpAttachments.SequenceEqual(stable), "Recovered authority preserves independent remaining lifetimes.");
        foreach (int peer in Enumerable.Range(0, 3))
        {
            Attachments(peer).Restore();
            Attachments(peer).Observe(_arenas[peer].Driver.ItemState!);
            Check(Attachments(peer).Views.Count == 3 && Attachments(peer).Views.All(v => v.Inflation > 2.5f), "Reconstruction seeds current visuals without duplicate instances or entry replay.");
        }

        _victimOrientation = N.Quaternion.Identity;
        foreach (var claw in stable)
        {
            await Until(() => Host.World.State.Tick >= claw.ExpiresAtTick - 1, "Independent natural deadline approached.", 2200);
            Check(Host.Items.EmpAttachments.Any(c => c.Id == claw.Id), "Claw still active immediately before its deadline.");
            await Frames(1);
            Check(Host.Items.EmpAttachments.All(c => c.Id != claw.Id), "Only the expiring identity ends at its exact tick.");
            Check(Host.World.GetVehicle(2).Movement.EnginePowerScale == (claw == third ? 1 : .2f), "Power returns only at the final natural expiry.");
            await Frames(8);
            await Capture("expiry-" + claw.Id, new(7, 111, -7), VehicleBody.ToGodot(_positions[2]));
        }
        await Frames(120);
        Check(Enumerable.Range(0, 3).All(p => Attachments(p).Views.Count == 0 && Attachments(p).ReleasedCount == 0), "Natural deflation/fizzle/release leaves no instances on any peer.");
        Record(new { scenario = "independent-natural-expiry", deadlines = stable.Select(c => c.ExpiresAtTick).ToArray(), fullPower = Host.World.GetVehicle(2).Movement.EnginePowerScale });
        await NativeDriving();
        GD.Print($"EMP attachment integration passed: {_checks} checks; native contacts, three impaired peers, independent expiry, rotating anchors and reconstruction.");
    }
}
