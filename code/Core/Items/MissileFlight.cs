using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Pitch-only terrain response. Missing or infeasible support preserves the committed velocity exactly.</summary>
public static class MissileFlight
{
    /// <summary>Computes one 60 Hz correction from at most two bounded host terrain rays.</summary>
    public static Vector3 Correct(MissileState missile, MissileTerrainConfiguration configuration,
        Func<Vector3, Vector3, MissileTerrainSample?>? terrain)
    {
        if (terrain is null || missile.Arc is not null || configuration.TurnRate == 0) { return missile.Velocity; }
        Vector3 direction = Vector3.Normalize(missile.Velocity);
        Vector3 horizontal = new(direction.X, 0, direction.Z);
        float horizontalLength = horizontal.Length();
        if (horizontalLength < 0.0001f) { return missile.Velocity; }
        horizontal /= horizontalLength;
        Vector3 ahead = missile.Position + direction * configuration.LookAhead;
        var support = terrain(missile.Position, missile.Position - Vector3.UnitY * configuration.ReacquisitionHeight);
        if (!Suitable(support, configuration) || support!.Position.Y > missile.Position.Y) { return missile.Velocity; }
        float height = missile.Position.Y - support.Position.Y;
        if (height >= configuration.ReacquisitionHeight) { return missile.Velocity; }
        var next = terrain(ahead + Vector3.UnitY * configuration.DetectionRange, ahead - Vector3.UnitY * configuration.DetectionRange);
        if (!Suitable(next, configuration)) { return missile.Velocity; }
        float distance = horizontalLength * configuration.LookAhead;
        float currentSlope = -Vector3.Dot(support.Normal, horizontal) / support.Normal.Y;
        float nextSlope = -Vector3.Dot(next!.Normal, horizontal) / next.Normal.Y;
        float rise = next.Position.Y - support.Position.Y;
        // A lower disconnected platform has a height discontinuity, not a downhill tangent.
        // Losing support commits the current velocity; it never remembers a remote ground target.
        if (rise < Math.Min(currentSlope, nextSlope) * distance - configuration.DropTolerance) { return missile.Velocity; }
        float pitch = MathF.Atan2(direction.Y, horizontalLength);
        float limit = configuration.PitchLimit * MathF.PI / 180;
        float desired = Math.Clamp(MathF.Atan2(next.Position.Y + configuration.Clearance - missile.Position.Y, distance), -limit, limit);
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
