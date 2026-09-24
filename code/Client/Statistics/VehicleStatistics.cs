using System.Globalization;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Statistics;

/// <summary>Pure allowlisted projection of an immutable vehicle snapshot; no raw contexts or item tokens.</summary>
internal static class VehicleStatistics
{
    /// <summary>Formats confirmed gameplay state at its owning tick.</summary>
    /// <param name="state">Current committed vehicle, absent before admission or after departure.</param>
    /// <param name="slots">Current inventory publication; null when inventory is unsupported/unavailable.</param>
    /// <param name="tick">Current authoritative world tick used for deadlines.</param>
    /// <returns>Read-only category text.</returns>
    internal static IReadOnlyList<StatisticSection> Capture(VehicleSnapshot? state, IReadOnlyList<ItemSlot>? slots, ulong tick)
    {
        if (state is null)
        {
            return [new("Vehicle", "Vehicle state unavailable (not in an arena or no current snapshot).")];
        }

        var movement = state.Movement;
        var damage = state.Damage;
        var position = state.ObservedPhysics.Position;
        var wheels = movement.Wheels.Compression;
        var slot = slots?.SingleOrDefault(value => value.Vehicle == state.VehicleId && value.Life == state.LifeId);
        string item = slots is null ? "Unavailable" : !state.CanInteract ? "Inactive" : $"{slot?.Active.Item ?? HeldItem.None} · Slots: {slot?.Item ?? HeldItem.None} / {slot?.SecondItem ?? HeldItem.None} · Active: {(slot?.ActiveSlot ?? 0) + 1}";
        string combat = $"Held item: {item} · Can interact: {state.CanInteract}\nOil traction remaining: {movement.OilTicks / 60f:0.00} s\nNitro charge: {slot?.NitroCharge ?? 0:0.0}% / {slot?.SecondNitroCharge ?? 0:0.0}% · Boost active: {movement.Nitro.Active} · Recovering: {movement.Nitro.Recovering} · Acceleration ×{movement.Nitro.AccelerationMultiplier:0.0} · Speed ×{movement.Nitro.SpeedMultiplier:0.0}\n" +
                (damage.LastDamage is { } hit ? FormattableString.Invariant($"Last damage: {hit.Amount:0.##} HP at tick {hit.Tick}; instigator vehicle {hit.Attribution.InstigatorId}\n") : "Last damage: none this life\n") +
                $"Last damaging collision tick: {damage.LastCollisionTick?.ToString(CultureInfo.InvariantCulture) ?? "none"}\nRespawn: {(state.RespawnAtTick is { } deadline ? Remaining(deadline, tick) : "No pending deadline")}";
        return
        [
            new("Vehicle / confirmed state", FormattableString.Invariant($"Vehicle {state.VehicleId} · Life {state.LifeId} · State tick {movement.Tick}\nHP {damage.CurrentHP:0.##}/{damage.MaxHP:0.##} · {state.Lifecycle}\nSpeed {state.Speed:0.00} m/s · Position ({position.X:0.00}, {position.Y:0.00}, {position.Z:0.00}) m")),
            new("Physics / environment", FormattableString.Invariant($"Handling profile: {movement.CurrentSurface}{(movement.Grounded ? string.Empty : " (last supported)")} · {(movement.Grounded ? "Grounded" : "Airborne")}\nHandbrake {movement.Handbrake:0.00} · Sliding {movement.Drifting} · Steering {movement.SteeringAngle:0.00} rad\nFront/rear slip {movement.FrontSlip:0.00}/{movement.RearSlip:0.00}\nLongitudinal/lateral acceleration {movement.LongitudinalAcceleration:0.00}/{movement.LateralAcceleration:0.00} m/s²\nWheel compression FL/FR/RL/RR: {wheels.X:0.00}/{wheels.Y:0.00}/{wheels.Z:0.00}/{wheels.W:0.00} m")),
            new("Combat / lifecycle", combat)
        ];
    }

    /// <summary>Formats existing 60 Hz authority deadlines without advancing gameplay time.</summary>
    /// <param name="deadline">Owning absolute deadline.</param>
    /// <param name="tick">Latest confirmed world tick.</param>
    /// <returns>Nonnegative seconds and the source deadline.</returns>
    internal static string Remaining(ulong deadline, ulong tick) => FormattableString.Invariant($"{(deadline > tick ? deadline - tick : 0) / 60.0:0.00} s (tick {deadline})");
}
