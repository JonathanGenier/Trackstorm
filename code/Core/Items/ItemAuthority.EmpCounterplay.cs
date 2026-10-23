using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    private sealed record EmpNoseHit(float Fraction, ulong Attachment, ulong Vehicle, ulong Magazine, int Socket);

    private EmpNoseHit? IntersectEmpNoses(Vector3 start, Vector3 end, ulong shooter,
        IReadOnlyList<VehicleStepRequest> requests, Dictionary<ulong, ItemSlot> slots, List<EmpClawAttachment> claws)
    {
        EmpNoseHit? nearest = null;
        void Consider(Vector3 center, float radius, ulong attachment, ulong vehicle, ulong magazine, int socket)
        {
            if (EmpClawGeometry.Intersect(start, end, center, radius) is float fraction &&
                (nearest is null || fraction < nearest.Fraction))
            { nearest = new(fraction, attachment, vehicle, magazine, socket); }
        }
        foreach (var claw in claws.OrderBy(c => c.Id))
        {
            var pose = requests.Single(r => r.VehicleId == claw.Vehicle).Observation.Physics;
            Consider(EmpClawGeometry.Nose(claw, pose), EmpClawGeometry.InflatedRadius, claw.Id, claw.Vehicle, 0, -1);
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
                    EmpClawGeometry.StowedRadius, 0, request.VehicleId, load.Magazine, i);
            }
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

    private void BlastEmpRounds(Vector3 center, MissileState missile, IReadOnlyList<VehicleStepRequest> requests,
        Dictionary<ulong, ItemSlot> slots, List<ItemEvent> events, RearShieldBatch shields)
    {
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
                if (Explosion(center, nose, HeldItem.Missile).Damage <= 0 || shields.Intersect(center, nose) is not null) { continue; }
                PopEmpNose(new(0, 0, request.VehicleId, load.Magazine, i), nose, slots, [], events);
            }
        }
    }
}
