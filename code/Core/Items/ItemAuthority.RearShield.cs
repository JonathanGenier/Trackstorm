using System.Numerics;
using Trackstorm.Core.Events;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    private sealed class RearShieldBatch
    {
        private readonly Simulation.Simulation _world;
        private readonly IReadOnlyList<VehicleStepRequest> _requests;
        private readonly Dictionary<ulong, ItemSlot> _slots;
        private readonly List<RuntimeEvent> _journal;
        private readonly ulong _tick;
        internal List<TombstoneState> States { get; }

        internal RearShieldBatch(ItemAuthority items, Simulation.Simulation world, IReadOnlyList<VehicleStepRequest> requests,
            Dictionary<ulong, ItemSlot> slots, List<RuntimeEvent> journal, ulong tick)
        {
            States = new(items._tombstones);
            _world = world; _requests = requests; _slots = slots; _journal = journal; _tick = tick;
        }

        private IEnumerable<TombstoneState> Active() => States.Where(s => _world.State.Match is not { Phase: not Matches.MatchPhase.Active } && s.Stage == TombstoneStage.RearShield &&
            AttachedOwnerCanInteract(_world, s) && _slots.TryGetValue(s.Owner, out var slot) && slot.Active.Item == HeldItem.Tombstone && slot.Active.Token == s.Token &&
            _requests.Any(r => r.VehicleId == s.Owner && !r.Reset.HasValue));

        internal (TombstoneState State, float Fraction)? Intersect(Vector3 start, Vector3 end, ulong excludedOwner = 0, ulong onlyOwner = 0)
        {
            (TombstoneState State, float Fraction)? closest = null;
            foreach (var state in Active().OrderBy(s => s.Id))
            {
                if (state.Owner == excludedOwner || (onlyOwner != 0 && state.Owner != onlyOwner)) { continue; }
                var pose = _requests.Single(r => r.VehicleId == state.Owner).Observation.Physics;
                if (TombstoneGeometry.Intersect(pose, start, end) is float fraction && (closest is null || fraction < closest.Value.Fraction))
                { closest = (state, fraction); }
            }
            return closest;
        }

        internal void Damage(ulong id, float amount, DamageContext context, bool collision = false)
        {
            int index = States.FindIndex(s => s.Id == id);
            if (index < 0) { return; }
            var state = States[index];
            if (collision && state.LastCollisionTick is ulong previous &&
                (_tick <= previous || _tick - previous < _world.DamageTuning(state.Owner).CollisionCooldownTicks)) { return; }
            var (updated, outcome) = EvaluateTombstoneDamage(state, checked(state.DamageSequence + 1), amount, context, _tick);
            if (outcome is null) { return; }
            if (updated is not null) { States[index] = collision ? updated with { LastCollisionTick = _tick } : updated; return; }
            States.RemoveAt(index);
            var slot = _slots[state.Owner];
            _slots[state.Owner] = slot.Token == state.Token ? slot with { Item = HeldItem.None } : slot with { SecondItem = HeldItem.None };
            _journal.Add(new RuntimeEvent { Category = EventCategory.Item, Kind = "Destroyed", Actor = context.InstigatorId,
                Target = state.Owner, Cause = "Tombstone", Context = id.ToString(System.Globalization.CultureInfo.InvariantCulture), Tick = _tick });
        }

        internal Dictionary<ulong, VehicleObservation> Collisions()
        {
            var shields = Active().OrderBy(s => s.Id).ToArray();
            if (shields.Length == 0) { return _requests.ToDictionary(r => r.VehicleId, r => r.Observation); }
            var observations = new Dictionary<ulong, VehicleObservation>();
            var strongest = new Dictionary<ulong, (float Amount, ulong Other)>();
            var blockedVehicleImpacts = new HashSet<(ulong Owner, ulong Other)>();
            foreach (var request in _requests)
            {
                var observation = request.Observation;
                var remaining = new List<VehicleContact>();
                foreach (var contact in observation.Contacts)
                {
                    bool blocked = false;
                    var received = new HashSet<ulong>();
                    var worldPoint = observation.Physics.Position + Vector3.Transform(contact.LocalPosition, observation.Physics.Orientation);
                    foreach (var shield in shields)
                    {
                        if (shield.Owner != request.VehicleId && shield.Owner != contact.OtherVehicleId) { continue; }
                        var pose = _requests.Single(r => r.VehicleId == shield.Owner).Observation.Physics;
                        var local = Vector3.Transform(worldPoint - pose.Position, Quaternion.Conjugate(pose.Orientation));
                        if (!TombstoneGeometry.Contains(local) || !received.Add(shield.Owner)) { continue; }
                        blocked |= shield.Owner == request.VehicleId;
                        ulong other = shield.Owner == request.VehicleId ? contact.OtherVehicleId : request.VehicleId;
                        if (other != 0) { blockedVehicleImpacts.Add((shield.Owner, other)); }
                        float severity = contact.StaticObstacle
                            ? EnvironmentCollision.Severity(contact.RelativeVelocity, EnvironmentCollision.ResponseNormal(contact.Normal, observation.Support))
                            : VehicleDamageMath.CollisionSeverity(contact.RelativeVelocity, contact.Normal, contact.Impulse, _world.MovementTuning(shield.Owner).Mass);
                        float amount = VehicleDamageMath.CollisionDamage(severity, _world.DamageTuning(shield.Owner));
                        if (amount > strongest.GetValueOrDefault(shield.Id).Amount)
                        { strongest[shield.Id] = (amount, other); }
                    }
                    if (!blocked) { remaining.Add(contact); }
                }
                observations.Add(request.VehicleId, new(observation.Physics, observation.Support, remaining, observation.Surface,
                    observation.Wheels, observation.TerrainSupport, observation.WaterDepth));
            }
            // Native sweep/slide may report both the plate and chassis for one vehicle pair.
            // Once a spatial plate contact intercepted that impact, its other manifold points
            // cannot also damage the protected car. Unrelated colliders remain independent.
            foreach (var pair in observations.ToArray())
            {
                var o = pair.Value;
                observations[pair.Key] = new(o.Physics, o.Support,
                    o.Contacts.Where(c => !blockedVehicleImpacts.Contains((pair.Key, c.OtherVehicleId))),
                    o.Surface, o.Wheels, o.TerrainSupport, o.WaterDepth);
            }
            foreach (var hit in strongest.OrderBy(p => p.Key))
            { Damage(hit.Key, hit.Value.Amount, new("collision", hit.Value.Other, "rear-shield"), collision: true); }
            return observations;
        }
    }
}
