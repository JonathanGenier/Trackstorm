using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    private void BlastVehicles(Vector3 center, MissileState missile, IReadOnlyList<VehicleStepRequest> requests,
        Dictionary<ulong, List<VehicleEffectRequest>> effects, RearShieldBatch shields, ShieldState? directShield)
    {
        var context = new DamageContext(missile.Item == HeldItem.Salvo ? "salvo" : "missile", missile.Owner,
            missile.Item == HeldItem.Salvo ? $"radial-explosion:{missile.Id}" : "radial-explosion");
        // Resolve every victim against the same pre-blast armor set. A lethal blast
        // cannot remove cover for later victims in this explosion's iteration.
        foreach (var request in requests)
        {
            if (request.Reset.HasValue) { continue; }
            var pose = request.Observation.Physics;
            if (directShield is { Attached: true } && directShield.Owner == request.VehicleId) { continue; }
            var surface = VehicleHull.ClosestPoint(pose, center);
            Vector3? exposed = null;
            foreach (var point in VehicleHull.BlastSurfacePoints(pose, center))
            {
                if (shields.Occludes(center, point)) { continue; }
                if (exposed is null || Vector3.DistanceSquared(center, point) < Vector3.DistanceSquared(center, exposed.Value))
                { exposed = point; }
                if (point == surface) { break; }
            }
            if (exposed is null) { continue; }
            // Missile keeps center-distance falloff; Salvo keeps physical-surface
            // falloff, now using the nearest exposed hull candidate. No damage split.
            var effect = Explosion(center, pose.Position, missile.Item, exposed);
            if (effect.Damage > 0 || effect.Impulse != Vector3.Zero)
            { effects[request.VehicleId].Add(new(effect, context)); }
        }
        shields.BlastArmor(this, center, missile);
    }
}
