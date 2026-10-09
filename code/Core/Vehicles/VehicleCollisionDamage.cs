using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Pairs one-sided native reports before either participant's health is evaluated.</summary>
internal static class VehicleCollisionDamage
{
    internal static Dictionary<ulong, IReadOnlyList<VehicleImpactDamage>> Collect(
        IReadOnlyList<VehicleStepRequest> requests, Func<ulong, VehicleSnapshot> previous,
        Func<ulong, VehicleConfiguration> configuration, IReadOnlySet<(ulong Owner, ulong Other)>? protectedImpacts = null)
    {
        var active = requests.Where(request => !request.Reset.HasValue && previous(request.VehicleId).CanInteract)
            .ToDictionary(request => request.VehicleId);
        var incoming = active.ToDictionary(pair => pair.Key, pair => Incoming(previous(pair.Key), pair.Value.Observation.Physics, configuration(pair.Key)));
        var pairs = new Dictionary<(ulong, ulong), (float Severity, float Disadvantage)>();
        foreach (var request in active.Values.OrderBy(request => request.VehicleId))
        foreach (var contact in request.Observation.Contacts.Where(contact => contact.OtherVehicleId != 0))
        {
            ulong id = request.VehicleId, other = contact.OtherVehicleId;
            if (id == other || !active.ContainsKey(other)) { continue; }
            var first = incoming[id]; var second = incoming[other];
            Vector3 point = request.Observation.Physics.Position +
                Vector3.Transform(contact.LocalPosition, request.Observation.Physics.Orientation);
            Vector3 relative = first.LinearVelocity + Vector3.Cross(first.AngularVelocity, point - first.Position) -
                second.LinearVelocity - Vector3.Cross(second.AngularVelocity, point - second.Position);
            // The common incoming commands retain an impact that native solving has already
            // stopped. Raw relative approach also captures externally observed motion. Solver
            // support/friction impulse alone cannot turn matched movement into damaging contact.
            float severity = Math.Max(0, -Vector3.Dot(relative, contact.Normal));
            severity = Math.Max(severity, Math.Max(0, -Vector3.Dot(contact.RelativeVelocity, contact.Normal)));
            float ownMomentum = configuration(id).Mass * Math.Abs(Vector3.Dot(first.LinearVelocity, contact.Normal));
            float otherMomentum = configuration(other).Mass * Math.Abs(Vector3.Dot(second.LinearVelocity, contact.Normal));
            float total = ownMomentum + otherMomentum;
            float disadvantage = total > 0.0001f ? (otherMomentum - ownMomentum) / total : 0;
            var key = (Math.Min(id, other), Math.Max(id, other));
            if (!pairs.TryGetValue(key, out var retained) || severity > retained.Severity)
            { pairs[key] = (severity, id == key.Item1 ? disadvantage : -disadvantage); }
        }

        var impacts = requests.ToDictionary(request => request.VehicleId, _ => new List<VehicleImpactDamage>());
        foreach (var pair in pairs.OrderBy(pair => pair.Key.Item1).ThenBy(pair => pair.Key.Item2))
        {
            impacts[pair.Key.Item1].Add(new(pair.Key.Item2, protectedImpacts?.Contains((pair.Key.Item1, pair.Key.Item2)) == true ? 0 : pair.Value.Severity, pair.Value.Disadvantage));
            impacts[pair.Key.Item2].Add(new(pair.Key.Item1, protectedImpacts?.Contains((pair.Key.Item2, pair.Key.Item1)) == true ? 0 : pair.Value.Severity, -pair.Value.Disadvantage));
        }
        return impacts.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<VehicleImpactDamage>)pair.Value);
    }

    private static VehiclePhysicsState Incoming(VehicleSnapshot previous, VehiclePhysicsState observed, VehicleConfiguration tuning)
    {
        Vector3 velocity = previous.Movement.Physics.LinearVelocity;
        Vector3 angular = previous.Movement.Physics.AngularVelocity;
        foreach (var effect in previous.Effects)
        {
            velocity += effect.Effect.Impulse / tuning.Mass;
            angular += Vector3.Cross(effect.Effect.Offset, effect.Effect.Impulse) / (tuning.Mass * tuning.Wheelbase * tuning.Wheelbase / 3);
        }
        return new(observed.Position, observed.Orientation, velocity, angular);
    }
}
