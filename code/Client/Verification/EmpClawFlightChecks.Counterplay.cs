using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class EmpClawFlightChecks
{
    private async Task Counterplay()
    {
        if (OS.GetCmdlineUserArgs().Contains("--emp-combat-only"))
        { await ShieldAndBlast(); GD.Print($"EMP counterplay integration passed: {_checks} checks."); return; }
        for (int cycle = 0; cycle < 3; cycle++)
        {
            _positions[1] = new(15, 108, 15); _positions[2] = new(0, 108, -20);
            _positions[3] = new(30, 108, 30);
            _victimOrientation = cycle == 0 ? N.Quaternion.Identity : N.Quaternion.CreateFromYawPitchRoll(.8f, cycle, 2.7f);
            await Frames(5);
            EmpClawVerificationState.Seed(Host, [2], 3);
            await Frames(70);
            var claws = Host.Items.EmpAttachments.ToArray();
            Check(claws.Length == 3 && Enumerable.Range(0, 3).All(p => Attachments(p).Views.Count == 3), "Three inflated native models converge over impaired UDP.");
            var pose = Host.World.GetVehicle(2).Movement.Physics;
            var claw = claws[1];
            var center = EmpClawGeometry.Nose(claw, pose);
            var view = Attachments(0).Views.OrderBy(v => v.NoseAnchor.GlobalPosition.X).ToArray();
            float error = view.Min(v => v.NoseAnchor.GlobalPosition.DistanceTo(VehicleBody.ToGodot(center)));
            Check(error < .025f, $"Authoritative membrane follows the rendered inflated nose: {error} m.");
            Vector3 metal = VehicleBody.ToGodot(pose.Position + N.Vector3.Transform(claw.LocalPosition, pose.Orientation));
            _obstacles.Add(AddBox(_arenas[0], metal, new(.03f, .03f, .03f)));
            await Frames(5);
            Check(Host.Items.EmpAttachments.Count == 3, "Contact on metal alone does not pop any nose.");
            foreach (var obstacle in _obstacles) { obstacle.QueueFree(); } _obstacles.Clear();
            await Frames(2);
            await Capture($"counterplay-{cycle}-three-noses", VehicleBody.ToGodot(pose.Position) + new Vector3(5, 3, 6), VehicleBody.ToGodot(pose.Position));
            var oldViews = Enumerable.Range(0, 3).Select(p => Attachments(p).Views.ToArray()).ToArray();
            _obstacles.Add(AddBox(_arenas[0], VehicleBody.ToGodot(center), new(.04f, .04f, .04f)));
            await Until(() => Host.Items.EmpAttachments.Count == 2, "Only the contacted membrane pops.");
            Check(Host.Items.EmpAttachments.All(c => c.Id != claw.Id), "Correct claw identity was removed.");
            Check(Host.World.GetVehicle(2).Movement.EnginePowerScale == .2f, "Other independent claws retain capped suppression.");
            Check(Host.Items.EmpAttachments.All(c => claws.Contains(c)), "Surviving anchors and deadlines are unchanged.");
            foreach (var obstacle in _obstacles) { obstacle.QueueFree(); } _obstacles.Clear();
            await Frames(3);
            await Capture($"counterplay-{cycle}-selective-pop", VehicleBody.ToGodot(pose.Position) + new Vector3(5, 3, 6), VehicleBody.ToGodot(pose.Position));
            await ObserveRetirement(oldViews, $"contact-{cycle}", moving: true);
            Check(_arenas.All(a => a.Driver.ItemState!.EmpAttachments.Count == 2), "Reliable pop membership reaches both delayed peers.");
            foreach (var remaining in Host.Items.EmpAttachments)
            {
                var nose = EmpClawGeometry.Nose(remaining, Host.World.GetVehicle(2).Movement.Physics);
                _obstacles.Add(AddBox(_arenas[0], VehicleBody.ToGodot(nose), new(.04f, .04f, .04f)));
            }
            await Until(() => Host.Items.EmpAttachments.Count == 0, "Simultaneous contacts remove the final two claws.");
            Check(Host.World.GetVehicle(2).Movement.EnginePowerScale == 1, "Final contact returns full authoritative power immediately.");
            Check(Host.World.GetVehicle(2).Damage.LastDamage is null, "Balloon contact never damages the victim.");
            await Frames(150);
            Check(Enumerable.Range(0, 3).All(p => Attachments(p).Views.Count == 0 && Attachments(p).ReleasedCount == 0), "Every peer reclaims all released models after repeated pops.");
            foreach (var obstacle in _obstacles) { obstacle.QueueFree(); } _obstacles.Clear();
            Record(new { scenario = "native-independent-nose-pop", cycle, rotated = cycle != 0, membraneError = error, networkDelayMs = 100, lossPercent = 3 });
        }
        await Reset();
        await VehicleAndGroundNoses();
        await AttachedWeaponCounterplay();
        await StoredRoundGunfire();
        await ShieldAndBlast();
        await OwnerAndWeapons();
        GD.Print($"EMP counterplay integration passed: {_checks} checks.");
    }

    private async Task ObserveRetirement(Items.EmpClawVisual[][] before, string name, bool moving = false)
    {
        for (int frame = 0; frame < 60; frame++)
        {
            if (moving) { _positions[2] += new N.Vector3(.025f, 0, 0); }
            await Frames(1);
            for (int peer = 0; peer < 3; peer++)
            foreach (var view in before[peer])
            {
                if (!GodotObject.IsInstanceValid(view) || Attachments(peer).Views.Contains(view)) { continue; }
                Check(!view.BodyVisible, "Retired claw has no visible rigid geometry on the exact removal frame or its cosmetic tail.");
            }
            if (frame is 0 or 5 or 15 or 35)
            {
                Vector3 target = _arenas[0].Bodies[2].GlobalPosition;
                await Capture($"cleanup-{name}-{frame:00}", target + new Vector3(5, 3, 6), target);
            }
        }
        Check(Enumerable.Range(0, 3).All(p => Attachments(p).ReleasedCount == 0), "Only a bounded 0.4-second fragment effect survives removal.");
        // Reconstruction and an older duplicate publication must not leave a one-frame
        // copy or replay a destroyed model after reliable membership has converged.
        for (int peer = 0; peer < 3; peer++)
        {
            var presentation = Attachments(peer);
            var previous = presentation.Views.ToArray();
            var state = _arenas[peer].Driver.ItemState!;
            presentation.Restore();
            Check(previous.All(v => !v.BodyVisible), "Restore hides old rigid models synchronously before deferred freeing.");
            presentation.Observe(state);
            presentation.Observe(state);
            Check(presentation.Views.Count == state.EmpAttachments.Count, "Checkpoint/duplicate membership reconstructs only live identities.");
        }
        Record(new { scenario = "rendered-pop-cleanup", name, moving, sampledFrames = 60, peers = 3 });
    }

    private async Task VehicleAndGroundNoses()
    {
        _positions[1] = new(20, 108, 15); _positions[2] = new(0, 108, -20); _positions[3] = new(30, 108, 30);
        _victimOrientation = N.Quaternion.Identity;
        await Frames(5);
        EmpClawVerificationState.Seed(Host, [2], 3);
        await Frames(60);
        var claw = Host.Items.EmpAttachments[1];
        var nose = EmpClawGeometry.Nose(claw, Host.World.GetVehicle(2).Movement.Physics);
        _positions[3] = nose + N.Vector3.UnitY * .3f;
        await Until(() => Host.Items.EmpAttachments.Count == 0, "Another native vehicle touches the exposed noses and pops them.");
        _positions[3] = new(30, 108, 30);
        await Frames(150);
        Check(_arenas.All(a => a.Driver.ItemState!.EmpAttachments.Count == 0), "Vehicle-contact removal converges on every peer.");
        _positions[2] = new(0, 101.3f, -20);
        _victimOrientation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, MathF.PI);
        await Frames(5);
        float hp = Host.World.GetVehicle(2).Damage.CurrentHP;
        EmpClawVerificationState.Seed(Host, [2], 3);
        await Until(() => Host.Items.EmpAttachments.Count == 0, "Upside-down roof noses pop against real native terrain.");
        Check(Host.World.GetVehicle(2).Damage.CurrentHP == hp, "Grounded membrane pops add no damage.");
        Check(Host.World.GetVehicle(2).Movement.EnginePowerScale == 1, "Rollover contact releases final suppression at the same boundary.");
        var probe = _arenas[0].Driver.ContactEmpNose!;
        N.Vector3 center = new(20, 115, -20);
        _obstacles.Add(AddBox(_arenas[0], VehicleBody.ToGodot(center), new(.01f, 2, 2)));
        await Frames(2);
        Check(probe(2, center - N.Vector3.UnitX * 3, center + N.Vector3.UnitX * 3, EmpClawGeometry.InflatedRadius), "Swept membrane detects a thin wall between disjoint endpoints.");
        Record(new { scenario = "native-vehicle-ground-and-sweep", otherVehicle = true, upsideDownTerrain = true, thinWallSweep = true });
        await Frames(100);
        await Reset();
    }

    private async Task ShieldAndBlast()
    {
        _positions[1] = new(20, 108, 15); _positions[2] = new(0, 108, -20); _positions[3] = new(30, 108, 30);
        _victimOrientation = N.Quaternion.Identity;
        Check(Host.Items.Grant(Host.World, 2, HeldItem.Shield), "Victim selects the production rear shield.");
        await Frames(120);
        var items = Host.Items;
        ulong tick = Host.World.State.Tick, token = items.TokenHighWater;
        var pose = Host.World.GetVehicle(2).Movement.Physics;
        float hp = Host.World.GetVehicle(2).Damage.CurrentHP;
        var flights = Enumerable.Range(0, 2).Select(_ => new MissileState(++token, 1,
            pose.Position + ShieldGeometry.Center + N.Vector3.UnitZ * .6f, -N.Vector3.UnitZ * 60, 300)
            { EmpClaw = new(Host.World.GetVehicle(1).LifeId, 2, Host.World.GetVehicle(2).LifeId, tick + 300) }).ToArray();
        var shield = items.Shields.Single();
        items.Restore(new(items.Revision + 1, Host.Snapshot(), items.Slots, flights, [], shields: items.Shields, discardRevision: items.DiscardRevision), items.Revision + 1, token);
        await Frames(80);
        Check(items.EmpAttachments.Count == 0 && items.Missiles.Count == 0, "Simultaneous native shield contacts retire both claws without attachment.");
        Check(items.Shields.Single() == shield && Host.World.GetVehicle(2).Damage.CurrentHP == hp, "Cosmetic shield pops consume neither HP pool.");
        Check(_arenas.All(a => a.Driver.ItemState!.EmpAttachments.Count == 0), "Protected vehicle stays free of EMP on all delayed peers.");
        var slot = items.Slots.Single(s => s.Vehicle == 2);
        Check(items.Discard(Host.World, 2, slot.Life, slot.Active.Token, slot.SelectionRevision), "Discard shield fixture through ordinary authority.");
        Check(items.Grant(Host.World, 2, HeldItem.EmpClaw), "New EMP pickup supplies a fresh magazine.");
        await Frames(180);
        pose = Host.World.GetVehicle(2).Movement.Physics;
        N.Vector3 center = pose.Position + EmpClawGeometry.StoredNose(1) + N.Vector3.UnitY * 3.5f;
        _obstacles.Add(AddBox(_arenas[0], VehicleBody.ToGodot(center), new(1, 1, .05f)));
        token = items.TokenHighWater + 1;
        var missile = new MissileState(token, 1, center + N.Vector3.UnitZ * 120, -N.Vector3.UnitZ * 60, 300);
        items.Restore(new(items.Revision + 1, Host.Snapshot(), items.Slots, [missile], [], discardRevision: items.DiscardRevision), items.Revision + 1, token);
        await Until(() => items.Missiles.Count == 0, "Existing Missile hits the native thin wall above the exposed magazine.", 240);
        Check(items.Slots.Single(s => s.Vehicle == 2).EmpClaw!.Remaining == 0, "The qualifying original damaging blast destroys all three exposed rounds.");
        await Frames(180);
        Check(_arenas.All(a => a.Driver.ItemState!.Slots.Single(s => s.Vehicle == 2).EmpClaw!.Remaining == 0 && a.Bodies[2].Rack.EmpLauncher is null), "Blast depletion and empty retraction converge over impaired UDP.");
        Record(new { scenario = "native-shield-and-missile", simultaneousShieldPops = 2, blastDestroyed = 3, originalBlastOnly = true });
        await Reset();
    }

    private async Task StoredRoundGunfire()
    {
        _positions[1] = new(0, 108, 0); _positions[2] = new(0, 108, -18); _positions[3] = new(30, 108, 30);
        _victimOrientation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, MathF.PI);
        Configure(new() { ["items.machine_gun_spread"] = 0, ["items.machine_gun_fire_rate"] = 60 });
        Check(Host.Items.Grant(Host.World, 1, HeldItem.MachineGun), "Shooter obtains production Machine Gun.");
        Check(Host.Items.Grant(Host.World, 2, HeldItem.EmpClaw), "Target obtains production exposed EMP magazine.");
        await Frames(180);
        var launcher = _arenas[0].Bodies[2].Rack.EmpLauncher!;
        for (int i = 0; i < 3; i++)
        {
            var pose = Host.World.GetVehicle(2).Movement.Physics;
            var nose = pose.Position + N.Vector3.Transform(EmpClawGeometry.StoredNose(i), pose.Orientation);
            var anchor = launcher.NoseAnchors[i].GlobalPosition;
            Check(anchor.DistanceTo(VehicleBody.ToGodot(nose)) < .025f, "Stored rendered membrane agrees with the authoritative hit region.");
            var origin = Host.World.GetVehicle(1).Movement.Physics.Position + WeaponAim.Pivot;
            _directions[0] = N.Vector3.Normalize(nose - origin);
            await Frames(60);
            if (i == 0) { await Capture("three-exposed-rounds", anchor + new Vector3(-4, 2, -5), anchor); }
            Check(_arenas[0].Driver.RequestItemUse(), "Normal sustained-gun request accepted.");
            _hostInput = new(0, 0, 0, 0, Core.Input.InputButtons.UseItem, 0, 0);
            await Until(() => Host.Items.Slots.Single(s => s.Vehicle == 2).EmpClaw!.Remaining == 2 - i, "Native gunfire destroys the aimed stored socket.", 120);
            _hostInput = default;
            await Frames(60);
            var load = Host.Items.Slots.Single(s => s.Vehicle == 2).EmpClaw!;
            Check(load.ShotsFired == 0 && (load.RoundMask & (1 << i)) == 0, "Destruction spends ammo without firing or renumbering survivors.");
            Check(_arenas.All(a => a.Driver.ItemState!.Slots.Single(s => s.Vehicle == 2).EmpClaw!.RoundMask == load.RoundMask), "Sparse ammo state converges on both delayed peers.");
            Check(Host.Items.Missiles.Count == 0, "Stored destruction creates no projectile or chain reaction.");
        }
        await Frames(180);
        Check(_arenas.All(a => a.Bodies[2].Rack.EmpLauncher is null), "Empty cannon retracts and stays empty on every peer.");
        Record(new { scenario = "native-stored-gunfire", socketsDestroyed = 3, emptyRetracted = true });
        await Reset();
    }
}
