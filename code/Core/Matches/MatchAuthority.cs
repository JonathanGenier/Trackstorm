using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Matches;

/// <summary>Pure candidate evaluation inside the existing simulation's atomic commit.</summary>
internal static class MatchAuthority
{
    /// <summary>Adds a score row without mutating an existing or finished match.</summary>
    /// <param name="previous">Committed match boundary.</param>
    /// <param name="tick">Current simulation tick.</param>
    /// <param name="player">New participant identity.</param>
    /// <returns>Validated candidate boundary.</returns>
    internal static MatchState Join(MatchState previous, ulong tick, ulong player)
    {
        if (previous.Phase == MatchPhase.Finished || previous.Players.Any(score => score.Player == player))
        {
            return previous;
        }

        return new MatchState(tick, checked(previous.Revision + 1), previous.KillTarget, previous.Phase, previous.CountdownAtTick, previous.Winner, previous.Players.Append(new PlayerScore(player, 0, 0, 0, 0)));
    }

    /// <summary>Consumes authoritative destroyed lives once, in stable victim order.</summary>
    /// <param name="previous">Committed match boundary.</param>
    /// <param name="configuration">Validated host rules.</param>
    /// <param name="tick">Candidate simulation tick.</param>
    /// <param name="vehicles">Complete candidate vehicle roster.</param>
    /// <returns>Validated candidate match state.</returns>
    internal static MatchState Advance(MatchState previous, MatchConfiguration configuration, ulong tick, IReadOnlyList<VehicleSnapshot> vehicles)
    {
        if (previous.Phase == MatchPhase.Finished)
        {
            return previous;
        }

        GameLoopState lifecycle = previous.Lifecycle;
        if (lifecycle.Phase is GameLoopPhase.Initialization or GameLoopPhase.Countdown)
        {
            if (vehicles.Count < configuration.MinimumPlayers)
            {
                lifecycle = new GameLoopState(tick, GameLoopPhase.Initialization);
            }
            else if (lifecycle.Phase == GameLoopPhase.Initialization)
            {
                var initialized = new GameLoopState(tick, GameLoopPhase.Initialization);
                lifecycle = initialized.StartCountdown(configuration.CountdownTicks);
            }
            else
            {
                lifecycle = lifecycle.Advance(tick);
            }
        }
        else
        {
            lifecycle = lifecycle.Advance(tick);
        }

        var scores = previous.Players.ToDictionary(score => score.Player);
        var changes = new List<ScoredDeath>();
        foreach (VehicleSnapshot vehicle in vehicles.OrderBy(vehicle => vehicle.VehicleId))
        {
            PlayerScore victim = scores[vehicle.VehicleId];
            if (!vehicle.Damage.Destroyed || vehicle.LifeId <= victim.ProcessedLife)
            {
                continue;
            }

            victim = victim with { ProcessedLife = vehicle.LifeId };
            if (lifecycle.AllowsGameplay)
            {
                victim = victim with { Deaths = checked(victim.Deaths + 1) };
                scores[victim.Player] = victim;
                DamageEvent? damage = vehicle.Damage.LastDamage;
                ulong killer = damage is { DestroyedTransition: true } && damage.Tick == tick && damage.Attribution.Source is "missile" or "collision"
                    ? damage.Attribution.InstigatorId : 0;
                if (killer == victim.Player || !scores.ContainsKey(killer) || !vehicles.Any(candidate => candidate.VehicleId == killer))
                {
                    killer = 0;
                }

                if (killer != 0)
                {
                    PlayerScore credited = scores[killer] with { Kills = checked(scores[killer].Kills + 1) };
                    MatchOutcome? outcome = FirstToTargetMode.Evaluate(credited, configuration.KillTarget);
                    if (outcome is not null)
                    {
                        lifecycle = lifecycle.Finish(outcome);
                        credited = credited with { Wins = 1 };
                    }

                    scores[killer] = credited;
                }

                changes.Add(new ScoredDeath(victim.Player, vehicle.LifeId, killer));
            }

            scores[victim.Player] = victim;
            if (lifecycle.Phase == GameLoopPhase.Finished)
            {
                break;
            }
        }

        if (lifecycle.Phase == previous.Lifecycle.Phase && lifecycle.CountdownAtTick == previous.CountdownAtTick && scores.Values.OrderBy(score => score.Player).SequenceEqual(previous.Players))
        {
            return previous;
        }

        MatchPhase phase = lifecycle.Phase switch
        {
            GameLoopPhase.Initialization => MatchPhase.Waiting,
            GameLoopPhase.Countdown => MatchPhase.Countdown,
            GameLoopPhase.Active => MatchPhase.Active,
            GameLoopPhase.Finished => MatchPhase.Finished,
            _ => throw new InvalidOperationException("Unknown Game Loop phase."),
        };
        return new MatchState(tick, checked(previous.Revision + 1), configuration.KillTarget, phase, lifecycle.CountdownAtTick, lifecycle.Outcome?.Winner, scores.Values, changes);
    }
}
