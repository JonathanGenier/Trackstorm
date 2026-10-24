using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    private sealed record EmpNoseHit(float Fraction, ulong Attachment, ulong Vehicle, ulong Magazine, int Socket);

    private EmpNoseHit? IntersectEmpNoses(Vector3 start, Vector3 end, ulong shooter,
        IReadOnlyList<VehicleStepRequest> requests, Dictionary<ulong, ItemSlot> slots, List<EmpClawAttachment> claws)
    {
        EmpNoseHit? nearest = IntersectAttachedNoses(start, end, requests, claws);
        void Consider(Vector3 center, float radius, ulong attachment, ulong vehicle, ulong magazine, int socket)
        {
            if (EmpClawGeometry.Intersect(start, end, center, radius) is float fraction &&
                (nearest is null || fraction < nearest.Fraction))
            { nearest = new(fraction, attachment, vehicle, magazine, socket); }
        }
        foreach (var request in requests.OrderBy(r => r.VehicleId))
        {
            if (request.VehicleId == shooter || request.Reset.HasValue || !slots.TryGetValue(request.VehicleId, out var inventory) ||
                !EmpClawReady(inventory) || inventory.Active.EmpClaw is not { } load) { continue; }
            for (int i = 0; i < 3; i++)
            {
                if ((load.RoundMask & (1 << i)) == 0) { continue; }
                var pose = request.Observation.Physics;
                Consider(pose.Position + Vector3.Transform(EmpClawGeometry.StoredNose(i), pose.Orientation),
                    Configuration.EmpClaw.StoredNoseRadius, 0, request.VehicleId, load.Magazine, i);
            }
        }
        return nearest;
    }

    private EmpNoseHit? IntersectAttachedNoses(Vector3 start, Vector3 end,
        IReadOnlyList<VehicleStepRequest> requests, List<EmpClawAttachment> claws)
    {
        EmpNoseHit? nearest = null;
        foreach (var claw in claws.OrderBy(c => c.Id))
        {
            var pose = requests.Single(r => r.VehicleId == claw.Vehicle).Observation.Physics;
            if (EmpClawGeometry.Intersect(start, end, EmpClawGeometry.Nose(claw, pose), Configuration.EmpClaw.AttachedNoseRadius) is float fraction &&
                (nearest is null || fraction < nearest.Fraction))
            { nearest = new(fraction, claw.Id, claw.Vehicle, 0, -1); }
        }
        return nearest;
    }

    private static void PopEmpNose(EmpNoseHit hit, Vector3 position, Dictionary<ulong, ItemSlot> slots,
        List<EmpClawAttachment> claws, List<ItemEvent> events)
    {
        if (hit.Attachment != 0)
        {
            claws.RemoveAll(c => c.Id == hit.Attachment);
            // Complete membership releases the existing model; no second cosmetic claw is spawned.
            return;
        }
        var inventory = slots[hit.Vehicle];
        var slot = inventory.Active;
        var load = slot.EmpClaw!;
        if (load.Magazine != hit.Magazine || (load.RoundMask & (1 << hit.Socket)) == 0) { return; }
        byte mask = (byte)(load.RoundMask & ~(1 << hit.Socket));
        byte remaining = (byte)BitOperations.PopCount(mask);
        load = load with { RoundMask = mask, Remaining = remaining, ReadyTick = remaining == 0 ? 0 : load.ReadyTick };
        slots[hit.Vehicle] = inventory.ActiveSlot == 0
            ? inventory with { EmpClaw = load, Item = remaining == 0 ? HeldItem.None : HeldItem.EmpClaw }
            : inventory with { SecondEmpClaw = load, SecondItem = remaining == 0 ? HeldItem.None : HeldItem.EmpClaw };
        events.Add(new(load.Magazine, hit.Vehicle, HeldItem.EmpClaw, position, true) { EmpPop = true });
    }

    private void BlastEmpNoses(Vector3 center, MissileState missile, IReadOnlyList<VehicleStepRequest> requests,
        Dictionary<ulong, ItemSlot> slots, List<EmpClawAttachment> claws, List<ItemEvent> events, RearShieldBatch shields)
    {
        // Every damaging projectile uses the existing shared blast contract. Check the
        // nearest membrane point, not the car or metal body; no secondary blast is created.
        bool Reaches(Vector3 point) => Explosion(center, point, missile.Item).Damage > 0 && shields.Intersect(center, point) is null;
        claws.RemoveAll(claw =>
        {
            var pose = requests.Single(r => r.VehicleId == claw.Vehicle).Observation.Physics;
            Vector3 nose = EmpClawGeometry.Nose(claw, pose), offset = center - nose;
            float distance = offset.Length();
            Vector3 point = distance <= Configuration.EmpClaw.AttachedNoseRadius ? center : nose + offset * (Configuration.EmpClaw.AttachedNoseRadius / distance);
            return Reaches(point);
        });
        // Stored ammo retains its approved Missile-only blast behavior.
        if (missile.Item != HeldItem.Missile) { return; }
        foreach (var request in requests)
        {
            if (request.Reset.HasValue || !slots.TryGetValue(request.VehicleId, out var inventory) || !EmpClawReady(inventory) ||
                inventory.Active.EmpClaw is not { } load) { continue; }
            for (int i = 0; i < 3; i++)
            {
                if ((load.RoundMask & (1 << i)) == 0) { continue; }
                var pose = request.Observation.Physics;
                Vector3 nose = pose.Position + Vector3.Transform(EmpClawGeometry.StoredNose(i), pose.Orientation);
                if (!Reaches(nose)) { continue; }
                PopEmpNose(new(0, 0, request.VehicleId, load.Magazine, i), nose, slots, [], events);
            }
        }
    }
}
