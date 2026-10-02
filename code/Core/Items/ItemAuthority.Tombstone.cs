using Trackstorm.Core.Events;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    /// <summary>Bounded live entities, including both held slots and released walls.</summary>
    public const int MaximumTombstones = 16;
    private readonly List<TombstoneState> _tombstones = new();
    /// <summary>Detached complete authoritative health/lifecycle state.</summary>
    public IReadOnlyList<TombstoneState> Tombstones => _tombstones.ToArray();

    /// <summary>Host-only released-wall lifecycle seam retained for recovery fixtures and future deployment adapters.
    /// Attached exposure is owned exclusively by slot selection; normal use commits deployment through the item step transaction.</summary>
    public bool TransitionTombstone(Simulation.Simulation world, ulong id, TombstoneStage expected, TombstoneStage next, VehiclePhysicsState? placement = null)
    {
        int index = _tombstones.FindIndex(state => state.Id == id);
        if (index < 0 || world.State.Match is { Phase: not Matches.MatchPhase.Active }) { return false; }
        var state = _tombstones[index];
        if (state.Stage != expected || !AttachedOwnerCanInteract(world, state) ||
            !(expected == TombstoneStage.RearShield && next == TombstoneStage.WorldWall && placement is not null)) { return false; }
        var candidate = state with { Stage = next, Life = 0, Token = 0, Position = placement.Value.Position, Orientation = placement.Value.Orientation, LinearVelocity = placement.Value.LinearVelocity, AngularVelocity = placement.Value.AngularVelocity, ExpiresAtTick = checked(world.State.Tick + (ulong)MathF.Ceiling(Configuration.TombstoneLifetimeSeconds * 60)) };
        candidate.Validate();
        if (!candidate.Attached) { ClearTombstoneSlot(state); }
        _tombstones[index] = candidate;
        Revision++;
        ReliableRevision++;
        return true;
    }

    /// <summary>Applies an ordered host damage observation to this item alone using the shared damage semantics.
    /// No client request accepts HP, damage or lifecycle outcomes. Sequence is scoped to the persistent entity.</summary>
    public DamageEvent? DamageTombstone(Simulation.Simulation world, ulong id, ulong sequence, float amount, DamageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!float.IsFinite(amount)) { throw new ArgumentException("Damage must be finite.", nameof(amount)); }
        int index = _tombstones.FindIndex(state => state.Id == id);
        if (index < 0 || sequence == 0 || amount <= 0 || world.State.Match is { Phase: not Matches.MatchPhase.Active }) { return null; }
        var state = _tombstones[index];
        if (sequence <= state.DamageSequence || (state.Attached && !AttachedOwnerCanInteract(world, state))) { return null; }
        var (updated, outcome) = EvaluateTombstoneDamage(state, sequence, amount, context, world.State.Tick);
        if (outcome is null) { return null; }
        if (outcome.DestroyedTransition)
        {
            ClearTombstoneSlot(state);
            _tombstones.RemoveAt(index);
            world.Events.Record(EventCategory.Item, "Destroyed", actor: context.InstigatorId, target: state.Owner, cause: "Tombstone", context: id.ToString(System.Globalization.CultureInfo.InvariantCulture), tick: world.State.Tick);
        }
        else { _tombstones[index] = updated!; }
        Revision++;
        ReliableRevision++;
        return outcome;
    }

    // Both explicit host observations and staged spatial hits use the TS-216 damage contract.
    private static (TombstoneState? State, DamageEvent? Event) EvaluateTombstoneDamage(TombstoneState state, ulong sequence, float amount, DamageContext context, ulong tick)
    {
        if (sequence <= state.DamageSequence || amount <= 0) { return (state, null); }
        var health = new VehicleHealth(new DamageConfiguration { MaxHP = TombstoneState.DefaultHP });
        health.Restore(new(TombstoneState.DefaultHP, state.HP, null, null));
        var applied = health.ApplyDamage(amount, context, tick);
        if (applied is null) { return (state, null); }
        var outcome = new DamageEvent(sequence, tick, applied.Amount, context, applied.DestroyedTransition);
        return (outcome.DestroyedTransition ? null : state with { HP = health.State.CurrentHP, DamageSequence = sequence }, outcome);
    }

    private static bool AttachedOwnerCanInteract(Simulation.Simulation world, TombstoneState state) =>
        world.State.Vehicles.Any(vehicle => vehicle.VehicleId == state.Owner && vehicle.LifeId == state.Life && vehicle.CanInteract);

    // Selection and its attached exposure are published atomically at the same reliable revision.
    private void SynchronizeTombstoneSelection(ItemSlot slot)
    {
        for (int i = 0; i < _tombstones.Count; i++)
        {
            var state = _tombstones[i];
            if (!state.Attached || state.Owner != slot.Vehicle) { continue; }
            _tombstones[i] = state with { Stage = slot.Active.Item == HeldItem.Tombstone && slot.Active.Token == state.Token
                ? TombstoneStage.RearShield : TombstoneStage.Held };
        }
    }

    private void ClearTombstoneSlot(TombstoneState state)
    {
        if (!state.Attached || !_slots.TryGetValue(state.Owner, out var slot)) { return; }
        if (slot.Item == HeldItem.Tombstone && slot.Token == state.Token) { _slots[state.Owner] = slot with { Item = HeldItem.None }; }
        else if (slot.SecondItem == HeldItem.Tombstone && slot.SecondToken == state.Token) { _slots[state.Owner] = slot with { SecondItem = HeldItem.None }; }
        if (_pending.TryGetValue(state.Owner, out var pending) && pending.Token == state.Token) { _pending.Remove(state.Owner); }
    }

    private bool ReconcileTombstones(Simulation.Simulation world, Dictionary<ulong, ItemSlot> slots)
    {
        if (_tombstones.Count == 0) { return false; }
        var previous = _tombstones.ToArray();
        if (world.State.Match?.Phase == Matches.MatchPhase.Finished)
        {
            foreach (var pair in slots.ToArray())
            {
                slots[pair.Key] = pair.Value with
                { Item = pair.Value.Item == HeldItem.Tombstone ? HeldItem.None : pair.Value.Item,
                  SecondItem = pair.Value.SecondItem == HeldItem.Tombstone ? HeldItem.None : pair.Value.SecondItem };
            }
            _tombstones.Clear();
        }
        else
        {
            for (int i = _tombstones.Count - 1; i >= 0; i--)
            {
                var state = _tombstones[i];
                if (!state.Attached) { continue; }
                if (!slots.TryGetValue(state.Owner, out var slot)) { _tombstones.RemoveAt(i); continue; }
                // Identify the physical slot by the previous capability; retained respawns replace it without replacing the pool.
                var prior = _slots.GetValueOrDefault(state.Owner);
                bool first = prior?.Item == HeldItem.Tombstone && prior.Token == state.Token;
                bool second = prior?.SecondItem == HeldItem.Tombstone && prior.SecondToken == state.Token;
                if (first && slot.Item == HeldItem.Tombstone) { _tombstones[i] = state with { Token = slot.Token, Life = slot.Life }; }
                else if (second && slot.SecondItem == HeldItem.Tombstone) { _tombstones[i] = state with { Token = slot.SecondToken, Life = slot.Life }; }
                else { _tombstones.RemoveAt(i); }
            }
            foreach (var slot in slots.Values) { SynchronizeTombstoneSelection(slot); }
        }
        return !previous.SequenceEqual(_tombstones);
    }
}
