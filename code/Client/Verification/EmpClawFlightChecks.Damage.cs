using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class EmpClawFlightChecks
{
    private async Task AttachedWeaponCounterplay()
    {
        Configure(new() { ["items.machine_gun_spread"] = 0, ["items.machine_gun_fire_rate"] = 60 });
        _victimOrientation = N.Quaternion.Identity;
        _positions[1] = new(0, 109, 0); _positions[2] = new(0, 108, -20); _positions[3] = new(30, 108, 30);
        Check(Host.Items.Grant(Host.World, 1, HeldItem.MachineGun), "Gun fixture obtains actual sustained weapon.");
        await Frames(180);
        EmpClawVerificationState.Seed(Host, [2], 3);
        await Frames(70);
        var claws = Host.Items.EmpAttachments.ToArray();
        var nose = EmpClawGeometry.Nose(claws[0], Host.World.GetVehicle(2).Movement.Physics);
        _directions[0] = N.Vector3.Normalize(nose - (Host.World.GetVehicle(1).Movement.Physics.Position + WeaponAim.Pivot));
        await Frames(60);
        var views = Enumerable.Range(0, 3).Select(p => Attachments(p).Views.ToArray()).ToArray();
        Check(_arenas[0].Driver.RequestItemUse(), "Production Machine Gun use targets the attached inflated nose.");
        _hostInput = new(0, 0, 0, 0, Core.Input.InputButtons.UseItem, 0, 0);
        await Until(() => Host.Items.EmpAttachments.Count == 2, "Native gunfire pops one attached nose.");
        _hostInput = default;
        Check(Host.Items.EmpAttachments.All(c => c.Id != claws[0].Id) && Host.Items.EmpAttachments.All(claws.Contains), "Gunfire removes only the aimed identity without refreshing survivors.");
        await ObserveRetirement(views, "machine-gun", moving: true);
        await Reset();

        var configuration = Host.Items.Configuration;
        Configure(new() { ["items.explosion_radius"] = 1, ["items.salvo_blast_radius"] = 1 });
        foreach (var item in new[] { HeldItem.Missile, HeldItem.Salvo })
        {
            _positions[1] = new(15, 108, 15); _positions[2] = new(0, 108, -20); _positions[3] = new(30, 108, 30);
            await Frames(5);
            EmpClawVerificationState.Seed(Host, [2], 3);
            await Frames(70);
            claws = Host.Items.EmpAttachments.ToArray();
            nose = EmpClawGeometry.Nose(claws[0], Host.World.GetVehicle(2).Movement.Physics);
            N.Vector3 center = nose - N.Vector3.UnitX * 1.1f;
            var items = Host.Items;
            ulong token = items.TokenHighWater + 1;
            // Both damaging projectiles hit actual scenery; an aimed endpoint is not a contact.
            var missile = new MissileState(token, 1, center + N.Vector3.UnitY * 2, -N.Vector3.UnitY * 60, 300);
            _obstacles.Add(AddBox(_arenas[0], VehicleBody.ToGodot(center - N.Vector3.UnitY * .025f), new(.1f, .05f, .1f)));
            if (item == HeldItem.Salvo)
            {
                var arc = new SalvoFlight(center + N.Vector3.UnitY * 2, center, 1, 2, 1, Host.World.GetVehicle(1).LifeId);
                missile = missile with { Arc = arc, Position = arc.At(1), Velocity = arc.VelocityAt(1), RemainingTicks = SalvoFlight.LifetimeTicks - 1 };
            }
            await Frames(2);
            views = Enumerable.Range(0, 3).Select(p => Attachments(p).Views.ToArray()).ToArray();
            items.Restore(new(items.Revision + 1, Host.Snapshot(), items.Slots, [missile], [], discardRevision: items.DiscardRevision,
                empAttachments: claws), items.Revision + 1, token);
            await Until(() => items.Missiles.Count == 0, "Damaging projectile completes through the production impact path.");
            Check(items.EmpAttachments.Count == 2 && items.EmpAttachments.All(c => c.Id != claws[0].Id), "Existing blast reaches only the first inflated membrane edge.");
            Check(items.EmpAttachments.All(claws.Contains) && Host.World.GetVehicle(2).Movement.EnginePowerScale == .2f, "Blast leaves other clocks and capped power intact.");
            await ObserveRetirement(views, item.ToString().ToLowerInvariant());
            Record(new { scenario = "attached-damaging-blast", weapon = item.ToString(), survivors = 2, nativeWorldHit = true });
            await Reset();
        }
        Configure(new() { ["items.explosion_radius"] = configuration.ExplosionRadius, ["items.salvo_blast_radius"] = configuration.SalvoBlastRadius });
    }
}
