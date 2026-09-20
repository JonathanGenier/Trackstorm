using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Matches;

/// <summary>Observes committed candidate motion without modifying movement or accepting player score claims.</summary>
internal static class StuntScoring
{
    internal static PlayerScore Advance(PlayerScore score, VehicleSnapshot before, VehicleStepResult result, MatchConfiguration rules, VehicleConfiguration vehicleRules)
    {
        VehicleSnapshot vehicle = result.Snapshot;
        ulong tick = vehicle.Movement.Tick;
        if (!vehicle.CanInteract || !before.CanInteract || result.Reset || before.LifeId != vehicle.LifeId)
        {
            return score with { Stunts = null };
        }

        StuntState pending = score.Stunts is { } old && old.Life == vehicle.LifeId && old.Tick == tick - 1
            ? old : new StuntState { Life = vehicle.LifeId };
        bool grounded = vehicle.Movement.Grounded;
        bool upright = Vector3.Transform(Vector3.UnitY, vehicle.ObservedPhysics.Orientation).Y >= 0.55f;
        double rate = vehicleRules.TicksPerSecond;
        bool drifting = grounded && upright && vehicle.Movement.Drifting && vehicle.Speed >= rules.DriftMinimumSpeed;
        StuntProgress drift = pending.Drift;
        if (drifting)
        {
            drift = Accumulate(drift, rate, rules.DriftRate, rules.DriftTierStep, rules.DriftTierSeconds, rules.StuntMaximumTier);
        }
        else
        {
            if (grounded && upright && drift.Ticks / rate >= rules.DriftMinimumSeconds)
            {
                score = CircusScoring.Bank(score, drift.BasePoints);
            }
            drift = default;
        }

        StuntProgress air = pending.Airtime;
        Vector3 origin = pending.JumpOrigin;
        double distance = pending.JumpDistance;
        double jumpPoints = pending.LongJumpBasePoints;
        // A supported departure arms a jump. Spawning/restoring an untracked airborne pose does not.
        if (!grounded && (air.Ticks != 0 || before.Movement.Grounded))
        {
            if (air.Ticks == 0) { origin = before.ObservedPhysics.Position; }
            air = Accumulate(air, rate, rules.AirtimeRate, rules.AirtimeTierStep, rules.AirtimeTierSeconds, rules.StuntMaximumTier);
            distance = HorizontalDistance(origin, vehicle.ObservedPhysics.Position);
            jumpPoints = distance * rules.JumpPointsPerMetre;
        }
        else if (grounded && air.Ticks != 0)
        {
            if (upright && air.Ticks / rate >= rules.AirtimeMinimumSeconds)
            {
                score = CircusScoring.Bank(score, air.BasePoints);
                score = CircusScoring.Bank(score, HorizontalDistance(origin, vehicle.ObservedPhysics.Position) * rules.JumpPointsPerMetre);
            }
            air = default;
            origin = default;
            distance = jumpPoints = 0;
        }

        StuntProgress top = pending.TopSpeed;
        double threshold = vehicleRules.ForwardSpeed * (top.Ticks == 0 ? rules.TopSpeedEnterRatio : rules.TopSpeedExitRatio);
        if (vehicle.Speed >= threshold)
        {
            top = new StuntProgress(checked(top.Ticks + 1), top.BasePoints + (5d / rate));
        }
        else
        {
            score = CircusScoring.Bank(score, top.BasePoints);
            top = default;
        }

        var next = new StuntState { Life = vehicle.LifeId, Tick = tick, Drift = drift, Airtime = air, TopSpeed = top,
            JumpOrigin = origin, JumpDistance = distance, LongJumpBasePoints = jumpPoints };
        return score with { Stunts = next.Active ? next : null };
    }

    private static StuntProgress Accumulate(StuntProgress previous, double rate, double initial, double step, double tierSeconds, int maximumTier)
    {
        double tier = Math.Min(maximumTier, Math.Floor(previous.Ticks / (rate * tierSeconds)));
        return new StuntProgress(checked(previous.Ticks + 1), previous.BasePoints + ((initial + (tier * step)) / rate));
    }

    private static double HorizontalDistance(Vector3 from, Vector3 to)
    {
        double x = (double)to.X - from.X;
        double z = (double)to.Z - from.Z;
        return Math.Sqrt((x * x) + (z * z));
    }
}
