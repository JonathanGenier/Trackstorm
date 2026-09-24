using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Audio;

/// <summary>Consumes confirmed state with independent life, damage, item and match watermarks.</summary>
internal sealed class AudioEventProjection
{
    private readonly Dictionary<ulong, VehicleMemory> _vehicles = new();
    private readonly Dictionary<ulong, ulong> _slots = new();
    private readonly HashSet<(ulong Token, bool Impact)> _itemEvents = new();
    private readonly Queue<(ulong Token, bool Impact)> _eventOrder = new();
    private ulong _itemsRevision;
    private ulong _matchRevision;
    private bool _itemsInitialized;
    private MatchPhase? _phase;
    private int _countdown = -1;

    /// <summary>One selected presentation cue, independent of authoritative mutation.</summary>
    internal event Action<AudioCue, Vector3>? Cue;

    /// <summary>Host-assigned local vehicle for personal feedback.</summary>
    internal ulong LocalVehicle { get; set; }

    /// <summary>Consumes confirmed damage and life boundaries or reseeds after reconnect.</summary>
    /// <param name="states">Confirmed vehicle states.</param>
    /// <param name="seed">Suppress historical one-shots while restoring the current boundary.</param>
    internal void Vehicles(IEnumerable<VehicleSnapshot> states, bool seed = false)
    {
        foreach (VehicleSnapshot state in states)
        {
            if (seed || !_vehicles.TryGetValue(state.VehicleId, out VehicleMemory? memory))
            {
                memory = new VehicleMemory { Life = state.LifeId, DeadLife = state.CanInteract ? state.LifeId - 1 : state.LifeId };
                memory.Damage.Seed(state.Damage.LastDamage?.Sequence ?? 0);
                _vehicles[state.VehicleId] = memory;
                continue;
            }

            Vector3 position = state.Movement.Physics.Position;
            if (!state.CanInteract && state.LifeId > memory.DeadLife)
            {
                memory.DeadLife = state.LifeId;
                Emit(AudioCue.Destruction, position);
                if (state.VehicleId == LocalVehicle)
                {
                    Emit(AudioCue.Death, position);
                }
            }

            if (state.LifeId < memory.Life)
            {
                continue;
            }

            if (state.LifeId > memory.Life)
            {
                memory.Life = state.LifeId;
                memory.Damage.Seed(0);
                if (state.CanInteract)
                {
                    Emit(AudioCue.Respawn, position);
                }
            }

            if (state.Damage.LastDamage is { } damage && memory.Damage.TryAccept(damage.Sequence, damage.Tick / 60.0, 0.12))
            {
                if (damage.Attribution.Source == "collision")
                {
                    Emit(damage.Amount >= 20 ? AudioCue.HeavyCollision : AudioCue.Collision, position);
                }

                if (!damage.DestroyedTransition)
                {
                    Emit(AudioCue.Damage, position);
                }
            }
        }
    }

    /// <summary>Releases departed-player memory at an authoritative roster boundary.</summary>
    /// <param name="roster">Current authoritative vehicle identities.</param>
    internal void Prune(IEnumerable<ulong> roster)
    {
        var retained = roster.ToHashSet();
        foreach (ulong id in _vehicles.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            _vehicles.Remove(id);
            _slots.Remove(id);
        }
    }

    /// <summary>Consumes reliable item outcomes and newly granted tokens.</summary>
    /// <param name="state">Confirmed gameplay boundary.</param>
    /// <param name="seed">Suppress historical one-shots while restoring the current boundary.</param>
    internal void Items(ItemPublication state, bool seed = false)
    {
        if (!seed && state.Revision <= _itemsRevision)
        {
            return;
        }

        bool initialize = seed || !_itemsInitialized;
        _itemsInitialized = true;
        _itemsRevision = state.Revision;
        foreach (ItemSlot slot in state.Slots)
        {
            ulong previous = _slots.GetValueOrDefault(slot.Vehicle);
            _slots[slot.Vehicle] = Math.Max(slot.Token, slot.SecondToken);
            foreach (var held in new[] { (slot.Token, slot.Item), (Token: slot.SecondToken, Item: slot.SecondItem) })
            {
                if (!initialize && held.Token > previous && held.Item != HeldItem.None)
                {
                    var vehicle = state.World.Vehicles.Single(entry => entry.State.VehicleId == slot.Vehicle).State;
                    Emit(Enum.Parse<AudioCue>(ItemRegistry.Find(held.Item)!.PickupAudio), vehicle.Movement.Physics.Position);
                }
            }
        }

        foreach (ItemEvent outcome in state.Events)
        {
            var identity = (outcome.Token, outcome.Impact);
            if (!_itemEvents.Add(identity))
            {
                continue;
            }

            _eventOrder.Enqueue(identity);
            if (_eventOrder.Count > 256)
            {
                _itemEvents.Remove(_eventOrder.Dequeue());
            }

            if (initialize)
            {
                continue;
            }

            var definition = ItemRegistry.Find(outcome.Item)!;
            string? hook = outcome.Impact ? definition.ImpactAudio : definition.UseAudio;
            if (hook is not null)
            {
                Emit(Enum.Parse<AudioCue>(hook), outcome.Position);
            }
            if (outcome.Impact)
            {
                Emit(AudioCue.Explosion, outcome.Position);
            }
        }
    }

    /// <summary>Consumes authoritative phase and score changes without replaying initial state.</summary>
    /// <param name="state">Confirmed gameplay boundary.</param>
    /// <param name="seed">Suppress historical one-shots while restoring the current boundary.</param>
    internal void Match(MatchState state, bool seed = false)
    {
        if (!seed && _phase.HasValue && state.Revision <= _matchRevision)
        {
            return;
        }

        bool initialized = _phase.HasValue;
        if (!seed && initialized)
        {
            foreach (ScoredDeath change in state.Changes.Where(change => change.Killer == LocalVehicle))
            {
                Emit(AudioCue.Kill, Vector3.Zero);
            }

            if (_phase != state.Phase)
            {
                if (state.Phase == MatchPhase.Active)
                {
                    Emit(AudioCue.MatchStart, Vector3.Zero);
                }
                else if (state.Phase == MatchPhase.Finished)
                {
                    Emit(AudioCue.MatchEnd, Vector3.Zero);
                    Emit(AudioCue.EndSting, Vector3.Zero);
                }
            }
        }

        if (_phase != state.Phase || seed)
        {
            _countdown = -1;
        }

        _phase = state.Phase;
        _matchRevision = state.Revision;
    }

    /// <summary>Presents the current authoritative countdown second without changing its deadline.</summary>
    /// <param name="state">Confirmed gameplay boundary.</param>
    /// <param name="tick">Latest authoritative world tick.</param>
    internal void Countdown(MatchState? state, ulong tick)
    {
        if (state?.Phase != MatchPhase.Countdown || state.CountdownAtTick <= tick)
        {
            return;
        }

        int remaining = (int)Math.Ceiling((state.CountdownAtTick!.Value - tick) / 60.0);
        if (remaining != _countdown)
        {
            _countdown = remaining;
            Emit(AudioCue.Countdown, Vector3.Zero);
        }
    }

    private void Emit(AudioCue cue, Vector3 position) => Cue?.Invoke(cue, position);

    private sealed class VehicleMemory
    {
        /// <summary>Highest observed life.</summary>
        internal ulong Life { get; set; }
        /// <summary>Highest consumed destroyed life.</summary>
        internal ulong DeadLife { get; set; }
        /// <summary>Per-life damage sequence and cooldown.</summary>
        internal AudioCooldown Damage { get; } = new();
    }
}
