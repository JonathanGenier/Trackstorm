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
        private readonly ItemConfiguration _configuration;
        internal List<ShieldState> States { get; }

        internal RearShieldBatch(ItemAuthority items, Simulation.Simulation world, IReadOnlyList<VehicleStepRequest> requests,
            Dictionary<ulong, ItemSlot> slots, List<RuntimeEvent> journal, ulong tick)
        {
            States = new(items._shields);
            _world = world; _requests = requests; _slots = slots; _journal = journal; _tick = tick;
            _configuration = items.Configuration;
            if (world.State.Match is not { Phase: not Matches.MatchPhase.Active })
            {
                foreach (var wall in States.Where(s => !s.Attached && s.ExpiresAtTick <= tick).ToArray())
                {
                    States.Remove(wall);
                    journal.Add(new RuntimeEvent { Category = EventCategory.Item, Kind = "Expired", Target = wall.Owner,
                        Cause = "Shield", Context = wall.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Tick = tick });
                }
            }
        }

        private IEnumerable<ShieldState> Active() => States.Where(s => _world.State.Match is not { Phase: not Matches.MatchPhase.Active } && s.Stage == ShieldStage.RearShield &&
            AttachedOwnerCanInteract(_world, s) && _slots.TryGetValue(s.Owner, out var slot) && slot.Active.Item == HeldItem.Shield && slot.Active.Token == s.Token &&
            _requests.Any(r => r.VehicleId == s.Owner && !r.Reset.HasValue));

        internal (ShieldState State, float Fraction)? Intersect(Vector3 start, Vector3 end, ulong excludedOwner = 0, ulong onlyOwner = 0)
        {
            (ShieldState State, float Fraction)? closest = null;
            foreach (var state in Active().OrderBy(s => s.Id))
            {
                if (state.Owner == excludedOwner || (onlyOwner != 0 && state.Owner != onlyOwner)) { continue; }
                var pose = _requests.Single(r => r.VehicleId == state.Owner).Observation.Physics;
                if (ShieldGeometry.Intersect(pose, start, end) is float fraction && (closest is null || fraction < closest.Value.Fraction))
                { closest = (state, fraction); }
            }
            if (onlyOwner == 0 && _world.State.Match is not { Phase: not Matches.MatchPhase.Active })
            {
                foreach (var wall in States.Where(s => !s.Attached).OrderBy(s => s.Id))
                {
                    if (ShieldGeometry.Intersect(wall, start, end) is float fraction && (closest is null || fraction < closest.Value.Fraction))
                    { closest = (wall, fraction); }
                }
            }
            return closest;
        }

        internal bool Deploy(ItemSlot slot, VehiclePhysicsState pose, ItemConfiguration configuration)
        {
            int index = States.FindIndex(s => s.Owner == slot.Vehicle && s.Token == slot.Token && s.Stage == ShieldStage.RearShield);
            if (index < 0) { return false; }
            var candidate = States[index] with { Stage = ShieldStage.WorldWall, Life = 0, Token = 0,
                Position = pose.Position, Orientation = pose.Orientation, LinearVelocity = pose.LinearVelocity, AngularVelocity = pose.AngularVelocity,
                WallSize = new(configuration.ShieldWidth, configuration.ShieldHeight, configuration.ShieldDepth), WallMass = configuration.ShieldMass,
                ExpiresAtTick = checked(_tick + (ulong)MathF.Ceiling(configuration.ShieldLifetimeSeconds * 60)) };
            candidate.Validate();
            if (States.Any(s => !s.Attached && ShieldGeometry.Overlaps(candidate, s))) { return false; }
            States[index] = candidate;
            return true;
        }

        internal void ObserveWalls(Func<ShieldState, ShieldObservation?>? observe)
        {
            if (observe is null || _world.State.Match is { Phase: not Matches.MatchPhase.Active }) { return; }
            for (int i = States.Count - 1; i >= 0; i--)
            {
                var wall = States[i];
                if (wall.Attached || observe(wall) is not { } observation) { continue; }
                var pose = observation.Physics;
                if (observation.GroundContactNormal is { } ground &&
                    (!VehiclePhysicsState.IsFinite(ground) || Math.Abs(ground.LengthSquared() - 1) > 0.001f || ground.Y < 0.55f))
                { throw new ArgumentException("Invalid wall ground contact."); }
                var candidate = wall with { Position = pose.Position, Orientation = pose.Orientation,
                    LinearVelocity = pose.LinearVelocity, AngularVelocity = pose.AngularVelocity };
                candidate.Validate();
                States[i] = candidate;
                if (wall.Tipping && observation.GroundContactNormal is { } support &&
                    Vector3.Dot(Vector3.Transform(Vector3.UnitY, pose.Orientation), support) < 0.2f)
                { Damage(wall.Id, wall.HP, new("world", 0, "toppled-wall")); }
            }
        }

        internal void BlastWalls(ItemAuthority items, Vector3 center, MissileState missile)
        {
            foreach (var wall in States.Where(s => !s.Attached).ToArray())
            {
                var effect = items.Explosion(center, wall.Position, missile.Item);
                Damage(wall.Id, effect.Damage, new(missile.Arc is null ? "missile" : "salvo", missile.Owner, "world-wall-blast"));
                int index = States.FindIndex(s => s.Id == wall.Id);
                if (index >= 0) { States[index] = States[index] with { LinearVelocity = States[index].LinearVelocity + effect.Impulse / wall.WallMass }; }
            }
        }

        internal void Damage(ulong id, float amount, DamageContext context, bool collision = false)
        {
            int index = States.FindIndex(s => s.Id == id);
            if (index < 0 || _world.State.Match is { Phase: not Matches.MatchPhase.Active }) { return; }
            var state = States[index];
            if (collision && state.LastCollisionTick is ulong previous &&
                (_tick <= previous || _tick - previous < (state.Attached ? _world.DamageTuning(state.Owner).CollisionCooldownTicks : new DamageConfiguration().CollisionCooldownTicks))) { return; }
            var (updated, outcome) = EvaluateShieldDamage(state, checked(state.DamageSequence + 1), amount, context, _tick);
            if (outcome is null) { return; }
            if (updated is not null) { States[index] = collision ? updated with { LastCollisionTick = _tick } : updated; return; }
            States.RemoveAt(index);
            if (state.Attached && _slots.TryGetValue(state.Owner, out var slot))
            { _slots[state.Owner] = slot.Token == state.Token ? slot with { Item = HeldItem.None } : slot with { SecondItem = HeldItem.None }; }
            _journal.Add(new RuntimeEvent { Category = EventCategory.Item, Kind = "Destroyed", Actor = context.InstigatorId,
                Target = state.Owner, Cause = "Shield", Context = id.ToString(System.Globalization.CultureInfo.InvariantCulture), Tick = _tick });
        }

        internal Dictionary<ulong, VehicleObservation> Collisions()
        {
            var pushed = PushWalls();
            // Each native vehicle reports the struck wall ID. Collapse manifold points before damage.
            foreach (var hits in _requests.Where(r => _world.GetVehicle(r.VehicleId).CanInteract && !r.Reset.HasValue)
                .SelectMany(r => r.Observation.Contacts.Where(c => c.Shield != 0).Select(c => (Request: r, Contact: c))).GroupBy(h => h.Contact.Shield))
            {
                var strongestHit = hits.Select(h => (h.Request.VehicleId, Amount: VehicleDamageMath.CollisionDamage(
                    VehicleDamageMath.CollisionSeverity(h.Contact.RelativeVelocity, h.Contact.Normal, h.Contact.Impulse, _world.MovementTuning(h.Request.VehicleId).Mass),
                    _world.DamageTuning(h.Request.VehicleId)))).OrderByDescending(h => h.Amount).First();
                if (States.Any(s => s.Id == hits.Key && !s.Attached))
                { Damage(hits.Key, strongestHit.Amount, new("collision", strongestHit.VehicleId, "world-wall"), collision: true); }
            }
            var shields = Active().OrderBy(s => s.Id).ToArray();
            if (shields.Length == 0) { return pushed; }
            var observations = new Dictionary<ulong, VehicleObservation>();
            var strongest = new Dictionary<ulong, (float Amount, ulong Other)>();
            var blockedVehicleImpacts = new HashSet<(ulong Owner, ulong Other)>();
            foreach (var request in _requests)
            {
                var observation = pushed[request.VehicleId];
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
                        if (!ShieldGeometry.Contains(local) || !received.Add(shield.Owner)) { continue; }
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

        private Dictionary<ulong, VehicleObservation> PushWalls()
        {
            var observations = _requests.ToDictionary(r => r.VehicleId, r => r.Observation);
            if (_world.State.Match is { Phase: not Matches.MatchPhase.Active }) { return observations; }
            foreach (var request in _requests.Where(r => !r.Reset.HasValue && _world.GetVehicle(r.VehicleId).CanInteract).OrderBy(r => r.VehicleId))
            {
                var observation = observations[request.VehicleId];
                var velocity = observation.Physics.LinearVelocity;
                bool yielded = false;
                foreach (var group in observation.Contacts.Where(c => c.Shield != 0 && Math.Abs(c.Normal.Y) < 0.55f).GroupBy(c => c.Shield).OrderBy(g => g.Key))
                {
                    int index = States.FindIndex(s => s.Id == group.Key && !s.Attached);
                    if (index < 0) { continue; }
                    var wall = States[index];
                    var contact = group.OrderBy(c => Vector3.Dot(c.RelativeVelocity, c.Normal)).First();
                    var normal = Vector3.Normalize(new Vector3(contact.Normal.X, 0, contact.Normal.Z));
                    float relative = Vector3.Dot(contact.RelativeVelocity, normal);
                    var points = group.Select(c => observation.Physics.Position + Vector3.Transform(c.LocalPosition, observation.Physics.Orientation)).Distinct().ToArray();
                    var arm = points.Aggregate(Vector3.Zero, (sum, p) => sum + p) / points.Length - wall.Position;
                    float incoming = Vector3.Dot(wall.LinearVelocity + Vector3.Cross(wall.AngularVelocity, arm), normal) + relative;
                    if (incoming >= 0) { continue; }
                    yielded = true;
                    float mass = _world.MovementTuning(request.VehicleId).Mass;
                    float impulse = Math.Max(0, -relative) / (1 / mass + 1 / wall.WallMass);
                    bool tipping = wall.Tipping || impulse / wall.WallMass >= _configuration.ShieldTipSpeed;
                    var torquePerImpulse = Vector3.Cross(arm, -normal);
                    var localTorque = Vector3.Transform(torquePerImpulse, Quaternion.Conjugate(wall.Orientation));
                    var size = wall.WallSize;
                    var localResponse = new Vector3(localTorque.X / (size.Y * size.Y + size.Z * size.Z),
                        localTorque.Y / (size.X * size.X + size.Z * size.Z), localTorque.Z / (size.X * size.X + size.Y * size.Y)) * (12 / wall.WallMass);
                    var angularResponse = Vector3.Transform(localResponse, wall.Orientation);
                    if (!tipping) { angularResponse = new(0, angularResponse.Y, 0); }
                    impulse = Math.Max(0, -relative) / (1 / mass + 1 / wall.WallMass + Math.Max(0, Vector3.Dot(torquePerImpulse, angularResponse)));
                    var angular = wall.AngularVelocity + angularResponse * impulse;
                    // A yielding wall shares normal momentum instead of retaining the
                    // generic sweep's stationary-obstacle stop. Keep its original contact
                    // observations for the existing damage and cooldown rules.
                    velocity += normal * (incoming + impulse / mass - Vector3.Dot(velocity, normal));
                    States[index] = wall with { LinearVelocity = wall.LinearVelocity - normal * (impulse / wall.WallMass), AngularVelocity = angular, Tipping = tipping };
                }
                if (!yielded) { continue; }
                // Unrelated solid/vehicle contacts retain the normal motion already
                // resolved by the ordinary solver in mixed manifolds.
                foreach (var contact in observation.Contacts.Where(c => c.Shield == 0 && Math.Abs(c.Normal.Y) < 0.55f))
                {
                    float closing = Vector3.Dot(velocity - observation.Physics.LinearVelocity, contact.Normal);
                    if (closing < 0) { velocity -= contact.Normal * closing; }
                }
                var pose = observation.Physics;
                observations[request.VehicleId] = new(new(pose.Position, pose.Orientation, velocity, pose.AngularVelocity), observation.Support,
                    observation.Contacts, observation.Surface, observation.Wheels, observation.TerrainSupport, observation.WaterDepth);
            }
            return observations;
        }
    }
}
