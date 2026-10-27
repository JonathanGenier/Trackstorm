using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    /// <summary>Complete rack and captive loading readiness; never queues an early trigger.</summary>
    public bool SalvoReady(ItemSlot slot) => slot.Active is { Item: HeldItem.Salvo, SalvoReadyTick: 0 } &&
        _aimDeployment.TryGetValue(slot.Vehicle, out var deployment) && deployment.Life == slot.Life &&
        deployment.Token == slot.Active.Token && deployment.Selection == slot.SelectionRevision &&
        deployment.Item == HeldItem.Salvo && !deployment.Retracting && deployment.Progress >= 1 && deployment.Arm >= 1;

    private bool SalvoOwnsSelection(ItemSlot slot) => slot.ReservedSalvoShots > 0 ||
        slot.Active.Item == HeldItem.Salvo && _pending.TryGetValue(slot.Vehicle, out var pending) && pending.Token == slot.Active.Token;

    private void AdvanceSalvos(Simulation.Simulation world, InputFrame input, IReadOnlyList<VehicleStepRequest> requests,
        Dictionary<ulong, ItemSlot> slots, List<MissileState> missiles, Func<ulong> nextToken,
        Func<Vector3, Vector3?>? ground, Func<ulong, Vector3, Vector3, WeaponRayHit?>? raycast)
    {
        foreach (var pair in slots.OrderBy(pair => pair.Key).ToArray())
        {
            var inventory = pair.Value;
            var slot = inventory.Active;
            if (!slot.SalvoCommitted || input.Tick < slot.SalvoReadyTick || !world.GetVehicle(pair.Key).CanInteract ||
                world.State.Match is { Phase: not Matches.MatchPhase.Active }) { continue; }
            var request = requests.Single(r => r.VehicleId == pair.Key);
            var pose = request.Observation.Physics;
            // Aim expiry, loss of terrain and release of the trigger cannot cancel a
            // committed magazine. Fresh shared aim is sampled separately at each launch.
            var aim = _aims.GetValueOrDefault(pair.Key);
            if (aim?.Token != slot.Token || aim.Life != slot.Life) { aim = null; }
            Vector3 direction = aim?.Direction ?? Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
            float yaw = aim?.Yaw ?? 0;
            Vector3 origin = SalvoFlight.LaunchOrigin(pose, yaw, Configuration.SalvoLaunchHeight);
            float range = SalvoFlight.AimRange(Configuration, aim is not null && (request.Input.Held & InputButtons.AimItem) != 0);
            Vector3 target = SalvoFlight.ResolveTarget(pose, direction, range, ground,
                raycast is null ? null : (from, to) => raycast(slot.Vehicle, from, to));
            if (Vector3.DistanceSquared(origin, target) < 1) { target = origin + direction * 2; }
            var arc = new SalvoFlight(origin, target, Configuration.SalvoArcHeight, 60, 0, slot.Life).Launch(origin, Configuration.SalvoSpeed);
            missiles.Add(new(slot.Token, slot.Vehicle, origin, arc.VelocityAt(0), SalvoFlight.LifetimeTicks) { Arc = arc });
            int remaining = slot.SalvoShots - 1;
            ulong token = remaining > 0 ? nextToken() : slot.Token;
            ulong deadline = remaining > 0 ? checked(input.Tick + (ulong)Configuration.SalvoIntervalTicks) : 0;
            slots[pair.Key] = inventory.ActiveSlot == 0
                ? inventory with { Item = remaining > 0 ? HeldItem.Salvo : HeldItem.None, Token = token, SalvoShots = remaining, SalvoReadyTick = deadline }
                : inventory with { SecondItem = remaining > 0 ? HeldItem.Salvo : HeldItem.None, SecondToken = token, SecondSalvoShots = remaining, SecondSalvoReadyTick = deadline };
            if (remaining > 0) { ContinueSalvoAim(slot.Vehicle, slot.Token, token); }
        }
    }
}
