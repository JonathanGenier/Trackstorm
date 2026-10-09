using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Pitch-only terrain response. Missing or infeasible support preserves the committed velocity exactly.</summary>
public static class MissileFlight
{
    /// <summary>Computes one 60 Hz correction from at most four bounded host terrain rays.</summary>
    public static Vector3 Correct(MissileState missile, MissileTerrainConfiguration configuration,
        Func<Vector3, Vector3, MissileTerrainSample?>? terrain)
    {
        if (terrain is null || missile.Arc is not null || configuration.TurnRate == 0) { return missile.Velocity; }
        Vector3 direction = Vector3.Normalize(missile.Velocity);
        Vector3 horizontal = new(direction.X, 0, direction.Z);
        float horizontalLength = horizontal.Length();
        if (horizontalLength < 0.0001f) { return missile.Velocity; }
        horizontal /= horizontalLength;
        var support = terrain(missile.Position, missile.Position - Vector3.UnitY * configuration.ReacquisitionHeight);
        if (!Suitable(support, configuration) || support!.Position.Y > missile.Position.Y) { return missile.Velocity; }
        float height = missile.Position.Y - support.Position.Y;
        if (height >= configuration.ReacquisitionHeight) { return missile.Velocity; }
        float currentSlope = -Vector3.Dot(support.Normal, horizontal) / support.Normal.Y;
        float pitch = MathF.Atan2(direction.Y, horizontalLength);
        // Actual rising support remains usable when every forward ray lands beyond
        // the road's outer edge. This cannot invent support across a gap.
        float supportedPitch = MathF.Atan(currentSlope + (configuration.Clearance - height) /
            (horizontalLength * configuration.LookAhead * 0.25f));
        float desired = currentSlope > 0.01f && supportedPitch > pitch ? supportedPitch : float.NegativeInfinity;
        // Inspect the profile, not only its remote endpoint: the far probe can lie
        // beyond a narrow bank or see downhill while the missile is still on a deck.
        for (int probe = 0; probe < 3; probe++)
        {
            float fraction = 1f / (1 << probe);
            float length = configuration.LookAhead * fraction;
            Vector3 ahead = missile.Position + direction * length;
            var next = terrain(ahead + Vector3.UnitY * configuration.DetectionRange, ahead - Vector3.UnitY * configuration.DetectionRange);
            if (!Suitable(next, configuration)) { continue; }
            float distance = horizontalLength * length;
            float nextSlope = -Vector3.Dot(next!.Normal, horizontal) / next.Normal.Y;
            float rise = next.Position.Y - support.Position.Y;
            if (rise < Math.Min(currentSlope, nextSlope) * distance - configuration.DropTolerance) { continue; }
            // Near samples can anticipate an actual rise, but cannot attract a
            // steep sky/downward shot to flat ground when its long probe misses.
            if (fraction < 1 && rise <= 0.01f) { continue; }
            float target = MathF.Atan2(next.Position.Y + configuration.Clearance - missile.Position.Y, distance);
            // A distant downslope must not pull flight through still-flat support.
            float supported = MathF.Atan(currentSlope + (configuration.Clearance - height) / distance);
            desired = Math.Max(desired, Math.Max(target, supported));
        }
        if (!float.IsFinite(desired)) { return missile.Velocity; }
        float limit = configuration.PitchLimit * MathF.PI / 180;
        desired = Math.Clamp(desired, -limit, limit);
        // Proximity fades correction continuously; there is no launch-angle classification.
        float proximity = 1 - Math.Clamp((height - configuration.Clearance) /
            (configuration.ReacquisitionHeight - configuration.Clearance), 0, 1);
        float step = (desired - pitch) * (1 - MathF.Exp(-configuration.Response / 60)) * proximity;
        float maximum = configuration.TurnRate * MathF.PI / (180 * 60);
        float corrected = pitch + Math.Clamp(step, -maximum, maximum);
        if (Math.Abs(corrected - pitch) < 0.0000001f) { return missile.Velocity; }
        return (horizontal * MathF.Cos(corrected) + Vector3.UnitY * MathF.Sin(corrected)) * missile.Velocity.Length();
    }

    private static bool Suitable(MissileTerrainSample? sample, MissileTerrainConfiguration configuration) =>
        sample is not null && VehiclePhysicsState.IsFinite(sample.Position) && VehiclePhysicsState.IsFinite(sample.Normal) &&
        Math.Abs(sample.Normal.LengthSquared() - 1) < 0.01f &&
        sample.Normal.Y >= MathF.Cos(configuration.SlopeLimit * MathF.PI / 180);
}
