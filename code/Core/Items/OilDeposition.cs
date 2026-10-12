using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Fixed-step host flight, swept impact and bounded surface deposition.</summary>
internal static class OilDeposition
{
    internal static OilPatch Advance(OilPatch patch, ulong tick, VehiclePhysicsState? pose, Func<Vector3, Vector3, float, OilImpact?>? collision)
    {
        if (patch.Pour is not { } pour) { return patch; }
        var drops = pour.Drops.Drops.ToList();
        if (!pour.Cancelled && pose is not null && tick >= pour.StartTick && tick - pour.StartTick < (ulong)pour.DurationTicks)
        {
            Vector3 acceleration = Vector3.Transform((pose.Value.LinearVelocity - pour.PreviousVelocity) * 60, Quaternion.Conjugate(pose.Value.Orientation));
            var spring = OilPour.Spring(pour.HeadOffset, pour.HeadVelocity, acceleration, pour.Stiffness, pour.Damping, pour.Reaction);
            pour = pour with { PreviousVelocity = pose.Value.LinearVelocity, HeadOffset = spring.Offset, HeadVelocity = spring.Velocity };
            if ((tick - pour.StartTick) % (ulong)pour.IntervalTicks == 0 && pour.Emitted < 48)
            {
                Vector3 origin = pose.Value.Position + Vector3.Transform(OilPour.Mouth(pour.HeadOffset), pose.Value.Orientation);
                // Head motion moves the origin only. Pitch follows the chassis, never spring aim.
                Vector3 direction = Vector3.Transform(Vector3.Normalize(new Vector3(0, -.22f, 1)), pose.Value.Orientation);
                drops.Add(new(origin, pose.Value.LinearVelocity + direction * pour.Speed, 180));
                pour = pour with { Emitted = pour.Emitted + 1 };
            }
        }
        else if (pose is null && !pour.Cancelled) { pour = pour with { Cancelled = true }; }

        var airborne = new List<OilDrop>();
        foreach (var drop in drops)
        {
            Vector3 velocity = drop.Velocity - Vector3.UnitY * (pour.Gravity / 60);
            Vector3 end = drop.Position + (drop.Velocity + velocity) / 120;
            if (Math.Abs(end.X) >= 4096 || Math.Abs(end.Y) >= 4096 || Math.Abs(end.Z) >= 4096) { continue; }
            var hit = collision?.Invoke(drop.Position, end, pour.Radius);
            if (hit is not null)
            {
                if (!VehiclePhysicsState.IsFinite(hit.Position) || !VehiclePhysicsState.IsFinite(hit.Normal) ||
                    Math.Abs(hit.Normal.LengthSquared() - 1) > .001f || Vector3.Distance(hit.Position, drop.Position) > Vector3.Distance(end, drop.Position) + .02f)
                { throw new ArgumentException("Invalid host Oil collision observation."); }
                if (!float.IsFinite(hit.Radius) || hit.Radius < 0 || hit.Radius > pour.Radius) { throw new ArgumentException("Invalid Oil impact radius."); }
                if (hit.Radius == 0) { continue; }
                float radius = Math.Max(.05f, hit.Radius * (.68f + .22f * MathF.Sin(pour.Emitted * 2.399f + drop.RemainingTicks)));
                // Each impact creates one supported piece; every third parcel reserves one satellite.
                var pieces = patch.AdditionalSegments.Segments.ToList();
                if (!pour.Deposited)
                { patch = patch with { Position = hit.Position, Normal = hit.Normal, Radius = radius }; pour = pour with { Deposited = true }; }
                else if (pieces.Count < OilFootprint.MaximumSegments)
                {
                    Vector3 start = hit.Position;
                    Vector3 prior = pieces.Count == 0 ? patch.Position : pieces[^1].End;
                    Vector3 normal = hit.Normal;
                    if (Vector3.Distance(prior, start) < radius * 2 && Math.Abs(Vector3.Dot(prior - start, normal)) < .01f)
                    { pieces.Add(new(prior, start, normal, radius)); }
                    else { pieces.Add(new(start, start, normal, radius)); }
                }
                // Infer the parcel's emission ordinal from its retained lifetime, not
                // the current total (which stops changing after the pour finishes).
                ulong ordinal = (tick - pour.StartTick - (ulong)(180 - drop.RemainingTicks)) / (ulong)pour.IntervalTicks;
                if (pieces.Count < OilFootprint.MaximumSegments && ordinal % 3 == 0 && hit.Radius >= .25f)
                {
                    Vector3 tangent = Vector3.Normalize(Vector3.Cross(hit.Normal, Math.Abs(hit.Normal.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX));
                    float angle = drop.RemainingTicks * 2.399f;
                    Vector3 center = hit.Position + hit.Radius * .8f * (tangent * MathF.Cos(angle) + Vector3.Cross(hit.Normal, tangent) * MathF.Sin(angle));
                    pieces.Add(new(center, center, hit.Normal, Math.Max(.05f, hit.Radius * .14f)));
                }
                patch = patch with { AdditionalSegments = new OilFootprint(pieces) };
                continue;
            }
            if (drop.RemainingTicks > 1 && Math.Abs(end.X) < 4096 && Math.Abs(end.Y) < 4096 && Math.Abs(end.Z) < 4096)
            { airborne.Add(new(end, velocity, drop.RemainingTicks - 1)); }
        }
        return patch with { Pour = pour with { Drops = new OilFlight(airborne) } };
    }
}
