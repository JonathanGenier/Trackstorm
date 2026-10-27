using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Reconstructs cosmetic barrel parity from confirmed magazine consumption, never render frames.</summary>
internal sealed class MachineGunFiringSequence
{
    private ulong _token;
    private int _next;

    internal int Observe(ItemSlot? inventory, ulong token, int shots)
    {
        if (_token != token) { _token = token; _next = 0; }
        var ammo = inventory?.Token == token ? inventory.Ammo : inventory?.SecondToken == token ? inventory.SecondAmmo : null;
        // Exhaustion clears Ammo in the same publication as the final rounds.
        // In that case the preceding confirmed boundary supplies the continuation.
        int first = ammo is null ? _next : Math.Max(0, ammo.Capacity - ammo.Remaining - shots);
        _next = first + shots;
        return first;
    }
}
