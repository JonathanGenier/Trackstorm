using Trackstorm.Core.Events;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    /// <summary>Bounded live entities, including both held slots and released walls.</summary>
    public const int MaximumShields = 16;
    private readonly List<ShieldState> _shields = new();
    /// <summary>Detached complete authoritative health/lifecycle state.</summary>
    public IReadOnlyList<ShieldState> Shields => _shields.ToArray();

    /// <summary>Host-only released-wall lifecycle seam retained for recovery fixtures and future deployment adapters.
    /// Attached exposure is owned exclusively by slot selection; normal use commits deployment through the item step transaction.</summary>
    public bool TransitionShield(Simulation.Simulation world, ulong id, ShieldStage expected, ShieldStage next, VehiclePhysicsState? placement = null)
    {
        int index = _shields.FindIndex(state => state.Id == id);
        if (index < 0 || world.State.Match is { Phase: not Matches.MatchPhase.Active }) { return false; }
        var state = _shields[index];
        if (state.Stage != expected || !AttachedOwnerCanInteract(world, state) ||
            !(expected == ShieldStage.RearShield && next == ShieldStage.WorldWall && placement is not null)) { return false; }
        var candidate = state with { Stage = next, Life = 0, Token = 0, Position = placement.Value.Position, Orientation = placement.Value.Orientation, LinearVelocity = placement.Value.LinearVelocity, AngularVelocity = placement.Value.AngularVelocity, ExpiresAtTick = checked(world.State.Tick + (ulong)MathF.Ceiling(Configuration.ShieldLifetimeSeconds * 60)) };
        candidate.Validate();
        if (!candidate.Attached) { ClearShieldSlot(state); }
        _shields[index] = candidate;
        Revision++;
        ReliableRevision++;
        return true;
    }

    /// <summary>Applies an ordered host damage observation to this item alone using the shared damage semantics.
    /// No client request accepts HP, damage or lifecycle outcomes. Sequence is scoped to the persistent entity.</summary>
    public DamageEvent? DamageShield(Simulation.Simulation world, ulong id, ulong sequence, float amount, DamageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!float.IsFinite(amount)) { throw new ArgumentException("Damage must be finite.", nameof(amount)); }
        int index = _shields.FindIndex(state => state.Id == id);
        if (index < 0 || sequence == 0 || amount <= 0 || world.State.Match is { Phase: not Matches.MatchPhase.Active }) { return null; }
        var state = _shields[index];
        if (sequence <= state.DamageSequence || (state.Attached && !AttachedOwnerCanInteract(world, state))) { return null; }
        var (updated, outcome) = EvaluateShieldDamage(state, sequence, amount, context, world.State.Tick);
        if (outcome is null) { return null; }
        if (outcome.DestroyedTransition)
        {
            ClearShieldSlot(state);
            _shields.RemoveAt(index);
            world.Events.Record(EventCategory.Item, "Destroyed", actor: context.InstigatorId, target: state.Owner, cause: "Shield", context: id.ToString(System.Globalization.CultureInfo.InvariantCulture), tick: world.State.Tick);
        }
        else { _shields[index] = updated!; }
        Revision++;
        ReliableRevision++;
        return outcome;
    }

    // Both explicit host observations and staged spatial hits use the TS-216 damage contract.
    private static (ShieldState? State, DamageEvent? Event) EvaluateShieldDamage(ShieldState state, ulong sequence, float amount, DamageContext context, ulong tick)
    {
        if (sequence <= state.DamageSequence || amount <= 0) { return (state, null); }
        var health = new VehicleHealth(new DamageConfiguration { MaxHP = ShieldState.DefaultHP });
        health.Restore(new(ShieldState.DefaultHP, state.HP, null, null));
        var applied = health.ApplyDamage(amount, context, tick);
        if (applied is null) { return (state, null); }
        var outcome = new DamageEvent(sequence, tick, applied.Amount, context, applied.DestroyedTransition);
        return (outcome.DestroyedTransition ? null : state with { HP = health.State.CurrentHP, DamageSequence = sequence }, outcome);
    }

    private static bool AttachedOwnerCanInteract(Simulation.Simulation world, ShieldState state) =>
        world.State.Vehicles.Any(vehicle => vehicle.VehicleId == state.Owner && vehicle.LifeId == state.Life && vehicle.CanInteract);

    // Selection and its attached exposure are published atomically at the same reliable revision.
    private void SynchronizeShieldSelection(ItemSlot slot)
    {
        for (int i = 0; i < _shields.Count; i++)
        {
            var state = _shields[i];
            if (!state.Attached || state.Owner != slot.Vehicle) { continue; }
            _shields[i] = state with { Stage = slot.Active.Item == HeldItem.Shield && slot.Active.Token == state.Token
                ? ShieldStage.RearShield : ShieldStage.Held };
        }
    }

    private void ClearShieldSlot(ShieldState state)
    {
        if (!state.Attached || !_slots.TryGetValue(state.Owner, out var slot)) { return; }
        if (slot.Item == HeldItem.Shield && slot.Token == state.Token) { _slots[state.Owner] = slot with { Item = HeldItem.None }; }
        else if (slot.SecondItem == HeldItem.Shield && slot.SecondToken == state.Token) { _slots[state.Owner] = slot with { SecondItem = HeldItem.None }; }
        if (_pending.TryGetValue(state.Owner, out var pending) && pending.Token == state.Token) { _pending.Remove(state.Owner); }
    }

    private bool ReconcileShields(Simulation.Simulation world, Dictionary<ulong, ItemSlot> slots)
    {
        if (_shields.Count == 0) { return false; }
        var previous = _shields.ToArray();
        if (world.State.Match?.Phase == Matches.MatchPhase.Finished)
        {
            foreach (var pair in slots.ToArray())
            {
                slots[pair.Key] = pair.Value with
                { Item = pair.Value.Item == HeldItem.Shield ? HeldItem.None : pair.Value.Item,
                  SecondItem = pair.Value.SecondItem == HeldItem.Shield ? HeldItem.None : pair.Value.SecondItem };
            }
            _shields.Clear();
        }
        else
        {
            for (int i = _shields.Count - 1; i >= 0; i--)
            {
                var state = _shields[i];
                if (!state.Attached) { continue; }
                if (!slots.TryGetValue(state.Owner, out var slot)) { _shields.RemoveAt(i); continue; }
                // Identify the physical slot by the previous capability; retained respawns replace it without replacing the pool.
                var prior = _slots.GetValueOrDefault(state.Owner);
                bool first = prior?.Item == HeldItem.Shield && prior.Token == state.Token;
                bool second = prior?.SecondItem == HeldItem.Shield && prior.SecondToken == state.Token;
                if (first && slot.Item == HeldItem.Shield) { _shields[i] = state with { Token = slot.Token, Life = slot.Life }; }
                else if (second && slot.SecondItem == HeldItem.Shield) { _shields[i] = state with { Token = slot.SecondToken, Life = slot.Life }; }
                else { _shields.RemoveAt(i); }
            }
            foreach (var slot in slots.Values) { SynchronizeShieldSelection(slot); }
        }
        return !previous.SequenceEqual(_shields);
    }
}
