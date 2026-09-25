using System.Globalization;
using Trackstorm.Core.Items;
using Trackstorm.Core.Settings;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Hud;

/// <summary>Detached, read-only combat presentation; no Godot dependency or gameplay authority.</summary>
/// <param name="Health">Formatted current/capacity.</param>
/// <param name="HealthFill">Remaining health fraction.</param>
/// <param name="Speed">Rounded presentation speed.</param>
/// <param name="Unit">Selected suffix.</param>
/// <param name="SpeedFill">Fraction of 200 km/h.</param>
/// <param name="Item">Confirmed supported held item.</param>
internal sealed record CombatHudView(string Health, double HealthFill, string Speed, string Unit, double SpeedFill, HeldItem Item)
{
    /// <summary>Position supplied by the same authoritative standings projection as the board.</summary>
    internal string Standing { get; init; } = "--";
    /// <summary>Intentional timer placeholder; no presentation clock masquerades as a match clock.</summary>
    internal string Timer => "--:--";
    /// <summary>Accessible item name, also used below its silhouette.</summary>
    internal string ItemName => Name(Item, NitroCharge);
    internal double NitroCharge { get; init; }
    internal double SecondNitroCharge { get; init; }
    private static string Name(HeldItem item, double charge) => ItemRegistry.Find(item)?.Sustained == true ? $"{ItemRegistry.Find(item)!.DisplayName.ToUpperInvariant()} {Math.Ceiling(charge):0}%" : ItemRegistry.Find(item)?.DisplayName.ToUpperInvariant() ?? "EMPTY";
    /// <summary>Confirmed second physical slot.</summary>
    internal HeldItem SecondItem { get; init; }
    /// <summary>Confirmed selection, independent of occupancy.</summary>
    internal byte ActiveSlot { get; init; }
    internal string SecondItemName => Name(SecondItem, SecondNitroCharge);

    /// <summary>Projects one existing local/replicated boundary without changing it.</summary>
    /// <param name="state">Local vehicle boundary.</param>
    /// <param name="slot">Confirmed slot, if available.</param>
    /// <param name="unit">Local preference.</param>
    /// <returns>Detached presentation values.</returns>
    internal static CombatHudView From(VehicleSnapshot state, ItemSlot? slot, SpeedUnit unit)
    {
        HeldItem item = state.CanInteract && slot?.Vehicle == state.VehicleId && slot.Life == state.LifeId ? slot.Item : HeldItem.None;
        bool valid = state.CanInteract && slot?.Vehicle == state.VehicleId && slot.Life == state.LifeId;
        return new CombatHudView(FormatHealth(state.Damage.CurrentHP, state.Damage.MaxHP), NormalizeHealth(state.Damage.CurrentHP, state.Damage.MaxHP), ConvertSpeed(state.Speed, unit).ToString("0", CultureInfo.InvariantCulture), UnitSuffix(unit), NormalizeSpeed(state.Speed), ItemRegistry.Find(item) is not null ? item : HeldItem.None)
        { NitroCharge = valid ? slot!.ResourcePercentage : 0, SecondNitroCharge = valid ? slot!.SecondResourcePercentage : 0, SecondItem = valid && ItemRegistry.Find(slot!.SecondItem) is not null ? slot.SecondItem : HeldItem.None, ActiveSlot = valid ? slot!.ActiveSlot : (byte)0 };
    }

    /// <summary>Presentation conversion from metres per second.</summary>
    /// <param name="speed">Unconverted speed.</param>
    /// <param name="unit">Local preference.</param>
    /// <returns>Nonnegative preferred-unit speed.</returns>
    internal static double ConvertSpeed(double speed, SpeedUnit unit) => SafeSpeed(speed) * (unit == SpeedUnit.MilesPerHour ? 2.2369362920544 : 3.6);
    /// <summary>Shared suffix for diagnostics and combat speed.</summary>
    /// <param name="unit">Local preference.</param>
    /// <returns>Unit suffix.</returns>
    internal static string UnitSuffix(SpeedUnit unit) => unit == SpeedUnit.MilesPerHour ? "mph" : "km/h";
    /// <summary>200 km/h fills the gauge regardless of selected units or physical drive cap.</summary>
    /// <param name="speed">Speed in metres per second.</param>
    /// <returns>Clamped visual fraction.</returns>
    internal static double NormalizeSpeed(double speed) => Math.Clamp(SafeSpeed(speed) / (200.0 / 3.6), 0, 1);
    /// <summary>Capacity-driven health fraction, including safe unavailable input.</summary>
    /// <param name="current">Remaining HP.</param>
    /// <param name="maximum">Capacity.</param>
    /// <returns>Clamped visual fraction.</returns>
    internal static double NormalizeHealth(double current, double maximum) => double.IsFinite(current) && double.IsFinite(maximum) && maximum > 0 ? Math.Clamp(current / maximum, 0, 1) : 0;
    /// <summary>Whole-HP display using the supplied capacity, independent of local culture.</summary>
    /// <param name="current">Remaining HP.</param>
    /// <param name="maximum">Capacity.</param>
    /// <returns>Current/max text.</returns>
    internal static string FormatHealth(double current, double maximum) => string.Create(CultureInfo.InvariantCulture, $"{current:0}/{maximum:0}");

    private static double SafeSpeed(double speed) => double.IsFinite(speed) ? Math.Max(0, speed) : 0;
}
