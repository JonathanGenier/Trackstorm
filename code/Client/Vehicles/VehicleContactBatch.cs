using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Stages native observations for the shared pair solve without replaying world-contact effects.</summary>
internal static class VehicleContactBatch
{
    internal static VehicleStepRequest[] Resolve(IReadOnlyList<VehicleStepRequest> requests,
        Func<ulong, VehicleSnapshot> previous, Func<ulong, VehicleConfiguration> configuration, bool contactsComplete = true)
    {
        var observations = requests.ToDictionary(request => request.VehicleId, request => request.Observation);
        var pairs = observations.SelectMany(pair => pair.Value.Contacts
            .Where(contact => contact.OtherVehicleId != 0 && observations.ContainsKey(contact.OtherVehicleId))
            .Select(contact => (First: pair.Key, Second: contact.OtherVehicleId))).ToArray();
        var worldContacts = observations.Where(pair => pair.Value.Contacts.Any(contact => contact.OtherVehicleId == 0))
            .Select(pair => pair.Key).ToHashSet();
        // A full native report may have omitted a world contact or connecting pair.
        // Keep all observed states rather than guessing which incomplete group is isolated.
        if (!contactsComplete) { worldContacts.UnionWith(observations.Keys); }
        // A native mixed-contact solve couples both sides (and chains) of a vehicle impact.
        // Rewinding only the partner would apply its native momentum transfer a second time.
        bool expanded;
        do
        {
            expanded = false;
            foreach (var pair in pairs)
            {
                if (!worldContacts.Contains(pair.First) && !worldContacts.Contains(pair.Second)) { continue; }
                expanded |= worldContacts.Add(pair.First);
                expanded |= worldContacts.Add(pair.Second);
            }
        } while (expanded);

        foreach (ulong id in pairs.SelectMany(pair => new[] { pair.First, pair.Second }).Distinct())
        {
            // Keep the complete observed result of mixed contact, including torque, friction
            // and already accepted effects. Native impulse reports lag a boundary and cannot
            // safely separate the current vehicle impulse from its world-contact response.
            if (worldContacts.Contains(id)) { continue; }
            var observation = observations[id];
            var snapshot = previous(id);
            var tuning = configuration(id);
            Vector3 velocity = snapshot.Movement.Physics.LinearVelocity, angular = snapshot.Movement.Physics.AngularVelocity;
            foreach (var effect in snapshot.Effects)
            {
                velocity += effect.Effect.Impulse / tuning.Mass;
                angular += Vector3.Cross(effect.Effect.Offset, effect.Effect.Impulse) /
                    (tuning.Mass * tuning.Wheelbase * tuning.Wheelbase / 3);
            }
            // Pure vehicle contact has no world response to discard; retain its existing
            // common incoming boundary so the shared solver owns the full PIT impulse.
            observations[id] = new(new(observation.Physics.Position, observation.Physics.Orientation, velocity, angular),
                observation.Support, observation.Contacts, observation.Surface, observation.Wheels, observation.TerrainSupport, observation.WaterDepth);
        }
        var resolved = VehicleCollision.ResolveContacts(observations, configuration);
        return requests.Select(request => new VehicleStepRequest(request.VehicleId, request.Input, resolved[request.VehicleId],
            request.Effects, request.Reset, request.Repair, request.RepairCause, request.OilContact, request.Nitro, request.ClearNitro)).ToArray();
    }
}
