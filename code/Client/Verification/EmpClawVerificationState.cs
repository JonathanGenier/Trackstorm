using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Client.Verification;

/// <summary>Explicit saved-state fixtures for physics/recovery; surface-hit creation is tested separately.</summary>
internal static class EmpClawVerificationState
{
    internal static void Seed(HostVehicleSession host, IEnumerable<ulong> targets, int count)
    {
        ulong token = host.Items.TokenHighWater;
        var claws = new List<EmpClawAttachment>();
        foreach (ulong target in targets)
        for (int index = 0; index < count; index++)
        {
            ulong attached = host.World.State.Tick - (ulong)(index * 15);
            claws.Add(new(++token, target == 1 ? 2UL : 1UL, target, host.World.GetVehicle(target).LifeId,
                new Vector3(index * .3f, .9725f, 0), Vector3.UnitY, attached, attached + 1800));
        }
        var items = host.Items;
        var state = new ItemPublication(items.Revision + 1, host.Snapshot(), items.Slots, items.Missiles, [], host.Spawns?.States,
            items.Patches, items.OilContacts, host.Spawns?.Balances, items.Mines, items.Shields, items.DiscardRevision, claws);
        items.Restore(state, state.Revision, token);
    }
}
