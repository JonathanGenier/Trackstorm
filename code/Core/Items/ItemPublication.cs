using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Complete reliable ownership/outcome boundary and bounded projectile presentation state.</summary>
public sealed class ItemPublication
{
    /// <summary>Copies and validates every nested record before publication.</summary>
    /// <param name="revision">Strictly increasing host publication identity.</param>
    /// <param name="world">Authoritative HP/movement outcome at the same boundary.</param>
    /// <param name="slots">At most one two-slot inventory record per player.</param>
    /// <param name="missiles">Bounded active projectiles.</param>
    /// <param name="events">This step's launch/repair/impact effects.</param>
    /// <param name="spawns">Complete configured spawn state.</param>
    /// <param name="patches">Complete match-owned oil hazards.</param>
    /// <param name="balances">Per-player current-match category history.</param>
    /// <param name="oilContacts">Entry latches preserved across recovery.</param>
    /// <param name="mines">Complete magnetic hazards.</param>
    public ItemPublication(ulong revision, WorldSnapshot world, IEnumerable<ItemSlot> slots, IEnumerable<MissileState> missiles, IEnumerable<ItemEvent> events, IEnumerable<ItemSpawnState>? spawns = null, IEnumerable<OilPatch>? patches = null, IEnumerable<OilContact>? oilContacts = null, IEnumerable<PlayerItemBalance>? balances = null, IEnumerable<ProxyMineState>? mines = null)
    {
        var inventory = slots.ToArray();
        var projectiles = missiles.ToArray();
        var outcomes = events.ToArray();
        foreach (var missile in projectiles)
        {
            if (missile.Arc is not { } arc) { continue; }
            arc.Validate();
            if (missile.RemainingTicks != arc.DurationTicks - arc.ElapsedTicks || Vector3Distance(missile.Position, arc.At(arc.ElapsedTicks)) > 0.01f ||
                !world.Vehicles.Any(vehicle => vehicle.State.VehicleId == missile.Owner && vehicle.State.LifeId == arc.Life && vehicle.State.CanInteract))
            { throw new ArgumentException("Inconsistent salvo continuation."); }
        }
        if (revision == 0 || inventory.Length > 8 || inventory.Select(slot => slot.Vehicle).Distinct().Count() != inventory.Length ||
            inventory.Any(slot => slot.ActiveSlot > 1 ||
                !ValidCharge(slot.Item, slot.NitroCharge) || !ValidCharge(slot.SecondItem, slot.SecondNitroCharge) ||
                !ValidSalvo(slot.Item, slot.SalvoShots, slot.SalvoReadyTick, world.Tick) ||
                !ValidSalvo(slot.SecondItem, slot.SecondSalvoShots, slot.SecondSalvoReadyTick, world.Tick) ||
                (slot.EngagedToken != 0 && (slot.Active.Item != HeldItem.Nitro || slot.EngagedToken != slot.Active.Token)) ||
                (slot.Token == 0 && slot.Item != HeldItem.None) || (slot.SecondToken == 0 && slot.SecondItem != HeldItem.None) ||
                (slot.Item != HeldItem.None && ItemRegistry.Find(slot.Item) is null) || (slot.SecondItem != HeldItem.None && ItemRegistry.Find(slot.SecondItem) is null) ||
                !world.Vehicles.Any(vehicle => vehicle.State.VehicleId == slot.Vehicle && vehicle.State.LifeId == slot.Life)) ||
            inventory.SelectMany(slot => new[] { slot.Token, slot.SecondToken }).Where(token => token != 0).GroupBy(token => token).Any(group => group.Count() > 1) ||
            projectiles.Length > ItemAuthority.MaximumProjectiles || projectiles.Select(missile => missile.Id).Distinct().Count() != projectiles.Length ||
            projectiles.Any(missile => missile.Id == 0 || missile.Owner == 0 || !VehiclePhysicsState.IsFinite(missile.Position) || !VehiclePhysicsState.IsFinite(missile.Velocity) || missile.Velocity.Length() <= 0 || missile.Velocity.Length() > (missile.Arc is null ? 301 : 1000) || missile.RemainingTicks is < 1 or > 3600) ||
            outcomes.Length > ItemAuthority.MaximumProjectiles * 2 + ItemAuthority.MaximumMines + 8 || outcomes.Any(outcome => outcome.Token == 0 || outcome.Owner == 0 || ItemRegistry.Find(outcome.Item)?.CanUse != true || !VehiclePhysicsState.IsFinite(outcome.Position) || (outcome.Impact && outcome.Item is not (HeldItem.Missile or HeldItem.Salvo or HeldItem.ProxyMine))))
        {
            throw new ArgumentException("Invalid item publication.");
        }

        var pickups = spawns?.ToArray() ?? Array.Empty<ItemSpawnState>();
        if (pickups.Length > Arenas.ArenaConfiguration.MaximumItemSpawns || pickups.Select(spawn => spawn.Id).Distinct(StringComparer.Ordinal).Count() != pickups.Length ||
            pickups.Any(spawn => string.IsNullOrWhiteSpace(spawn.Id) || spawn.Id != spawn.Id.Trim() || System.Text.Encoding.UTF8.GetByteCount(spawn.Id) > 128 ||
                (spawn.Token == 0 ? spawn.ClaimedBy != 0 || spawn.Item != HeldItem.None || spawn.NextActivationTick != 0 || !spawn.Available :
                spawn.ClaimedBy == 0 || ItemRegistry.Find(spawn.Item) is null || spawn.NextActivationTick == 0) ||
                (spawn.Available ? spawn.NextActivationTick > world.Tick : spawn.NextActivationTick <= world.Tick)))
        {
            throw new ArgumentException("Invalid spawn publication.");
        }

        var oil = patches?.ToArray() ?? [];
        var contacts = oilContacts?.ToArray() ?? [];
        foreach (var patch in oil) { patch.Validate(); }
        if (oil.Length > ItemAuthority.MaximumPatches || oil.Select(patch => patch.Id).Distinct().Count() != oil.Length ||
            contacts.Length > ItemAuthority.MaximumPatches * 8 || contacts.Distinct().Count() != contacts.Length ||
            contacts.Any(contact => !oil.Any(patch => patch.Id == contact.Patch) ||
                !world.Vehicles.Any(vehicle => vehicle.State.VehicleId == contact.Vehicle && vehicle.State.LifeId == contact.Life && vehicle.State.CanInteract)))
        {
            throw new ArgumentException("Invalid oil continuation.");
        }
        var history = balances?.ToArray() ?? [];
        foreach (var balance in history) { balance.Validate(); }
        if (history.Length > 8 || history.Select(b => b.Player).Distinct().Count() != history.Length ||
            history.Any(b => !world.Vehicles.Any(v => v.State.VehicleId == b.Player)))
        {
            throw new ArgumentException("Invalid pickup history roster.");
        }
        var hazards = mines?.ToArray() ?? [];
        foreach (var mine in hazards) { mine.Validate(); }
        if (hazards.Length > ItemAuthority.MaximumMines || hazards.Select(mine => mine.Id).Distinct().Count() != hazards.Length ||
            hazards.Any(mine => projectiles.Any(p => p.Id == mine.Id) || oil.Any(p => p.Id == mine.Id) ||
                inventory.Any(slot => (slot.Token == mine.Id && slot.Item != HeldItem.None) || (slot.SecondToken == mine.Id && slot.SecondItem != HeldItem.None))))
        { throw new ArgumentException("Invalid mine continuation."); }
        Mines = Array.AsReadOnly(hazards);
        Balances = Array.AsReadOnly(history);
        Patches = Array.AsReadOnly(oil);
        OilContacts = Array.AsReadOnly(contacts);
        Spawns = Array.AsReadOnly(pickups);
        Revision = revision;
        World = world;
        Slots = Array.AsReadOnly(inventory);
        Missiles = Array.AsReadOnly(projectiles);
        Events = Array.AsReadOnly(outcomes);
    }

    private static bool ValidCharge(HeldItem item, double charge) => double.IsFinite(charge) &&
        (item == HeldItem.Nitro ? charge is > 0 and <= 100 : charge == 0);

    private static bool ValidSalvo(HeldItem item, int shots, ulong ready, ulong tick) => item == HeldItem.Salvo
        ? shots is >= 1 and <= 16 && (ready <= tick || ready - tick <= 60)
        : shots == 0 && ready == 0;

    private static float Vector3Distance(System.Numerics.Vector3 a, System.Numerics.Vector3 b) => System.Numerics.Vector3.Distance(a, b);

    /// <summary>Complete per-player category continuation and pickup diagnostics.</summary>
    public IReadOnlyList<PlayerItemBalance> Balances { get; }

    /// <summary>Complete persistent hazards.</summary>
    public IReadOnlyList<OilPatch> Patches { get; }
    /// <summary>Complete magnetic hazards, including velocity and initial seating timer.</summary>
    public IReadOnlyList<ProxyMineState> Mines { get; }
    /// <summary>Per-life entry latches.</summary>
    public IReadOnlyList<OilContact> OilContacts { get; }
    /// <summary>Complete marker state and last claim.</summary>
    public IReadOnlyList<ItemSpawnState> Spawns { get; }

    /// <summary>Monotonic delivery identity.</summary>
    public ulong Revision { get; }
    /// <summary>Complete authoritative vehicle outcome.</summary>
    public WorldSnapshot World { get; }
    /// <summary>Both fixed slots and selection in one record per player.</summary>
    public IReadOnlyList<ItemSlot> Slots { get; }
    /// <summary>Authoritative projectiles.</summary>
    public IReadOnlyList<MissileState> Missiles { get; }
    /// <summary>Presentation-only events.</summary>
    public IReadOnlyList<ItemEvent> Events { get; }
}
