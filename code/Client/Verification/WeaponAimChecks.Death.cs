using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckAimDeath()
    {
        var host = _arenas[0].Driver.Host!;
        Position(new(0, Ground, 35), new(0, Ground, 45), new(80, Ground, 0));
        host.Items.Grant(host.World, Shooter, HeldItem.MachineGun);
        await Until(() => _arenas[1].AimOverlay.Marker.HasValue, "Fresh weapon restores aiming before lethal damage");
        Require(host.TryConfigure(0, new Dictionary<string, double> { ["items.maximum_damage"] = 10000, ["respawn.delay_ticks"] = 120 }, out _), "Lethal damage and timed respawn fixture tuning accepted");
        host.Items.RemovePlayer(1);
        Require(host.Items.Grant(host.World, 1, HeldItem.Missile), "Other vehicle receives a real missile");
        await Until(() => host.Items.Slots.Single(slot => slot.Vehicle == 1) is { MissileReadyTick: > 0 } missile &&
            host.World.State.Tick >= missile.MissileReadyTick && _arenas[0].Driver.MissileReady?.Invoke(missile) != false,
            "Missile authority and presentation are ready before the death/respawn probe");
        ulong lifeBeforeDeath = host.World.GetVehicle(Shooter).LifeId;
        Require(_arenas[0].Driver.RequestItemUse(), "Real host missile use accepted");
        await Until(() => !host.World.GetVehicle(Shooter).CanInteract, "Actual missile explosion kills the aiming vehicle");
        Require(host.World.GetVehicle(Shooter).Damage.LastDamage?.Attribution.Source == "missile", "Death carries authoritative missile attribution");
        Require(!host.Items.Aims.Any(aim => aim.Vehicle == Shooter), "Actual death immediately clears authoritative aim");
        await Until(() => _arenas[1].LocalState is { CanInteract: false } && !_arenas[1].AimOverlay.Marker.HasValue,
            "Confirmed client death clears the camera cursor before respawn", 90);
        await Until(() => host.World.GetVehicle(Shooter).LifeId > lifeBeforeDeath && host.World.GetVehicle(Shooter).CanInteract,
            "Ordinary timed respawn creates a fresh living vehicle", 240);
        Require(!_arenas[1].AimOverlay.Marker.HasValue, "Respawn cannot retain a dead weapon's marker");
    }
}
