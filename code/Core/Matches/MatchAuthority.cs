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

        return new MatchState(tick, checked(previous.Revision + 1), previous.KillTarget, previous.Phase, previous.CountdownAtTick, previous.Winner, previous.Players.Append(new PlayerScore(player, 0, 0, 0, 0)), mode: previous.Mode);
    }

    /// <summary>Consumes authoritative destroyed lives once, in stable victim order.</summary>
    /// <param name="previous">Committed match boundary.</param>
    /// <param name="configuration">Validated host rules.</param>
    /// <param name="tick">Candidate simulation tick.</param>
    /// <param name="results">Complete candidate vehicle roster and applied damage outcomes.</param>
    /// <param name="previousVehicles">Previous authoritative poses and support.</param>
    /// <param name="vehicleRules">Registered per-vehicle movement tuning.</param>
    /// <param name="oilTriggers">New entries staged in this authoritative batch.</param>
    /// <returns>Validated candidate match state.</returns>
    internal static MatchState Advance(MatchState previous, MatchConfiguration configuration, ulong tick, IReadOnlyList<VehicleStepResult> results, IReadOnlyDictionary<ulong, VehicleSnapshot> previousVehicles, Func<ulong, VehicleConfiguration> vehicleRules, IReadOnlyList<Items.OilTrigger>? oilTriggers = null)
    {
        if (previous.Phase == MatchPhase.Finished)
        {
            return previous;
        }

        VehicleSnapshot[] vehicles = results.Select(result => result.Snapshot).ToArray();
        GameLoopState lifecycle = previous.Lifecycle;
        if (lifecycle.Phase is GameLoopPhase.Initialization or GameLoopPhase.Countdown)
        {
            if (vehicles.Length < configuration.MinimumPlayers)
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
        var awards = new List<CircusScoreAward>();
        foreach (VehicleStepResult result in results.OrderBy(result => result.Snapshot.VehicleId))
        {
            VehicleSnapshot vehicle = result.Snapshot;
            PlayerScore victim = scores[vehicle.VehicleId];
            // Outcomes are generated only by the candidate health authority. Consume every source,
            // including outside Active, so restoring/repeating an outcome can never bank it again.
            foreach (DamageEvent applied in result.DamageEvents.OrderBy(damage => damage.Sequence))
            {
                if (applied.Tick != tick || vehicle.LifeId < victim.ProcessedDamageLife ||
                    (vehicle.LifeId == victim.ProcessedDamageLife && applied.Sequence <= victim.ProcessedDamageSequence))
                {
                    continue;
                }

                victim = victim with { ProcessedDamageLife = vehicle.LifeId, ProcessedDamageSequence = applied.Sequence };
                scores[victim.Player] = victim;
                ulong attacker = applied.Attribution.InstigatorId;
                if (configuration.Mode == MatchMode.Circus && lifecycle.AllowsGameplay && applied.Attribution.Source == "collision" && attacker != victim.Player &&
                    scores.ContainsKey(attacker) && vehicles.Any(candidate => candidate.VehicleId == attacker))
                {
                    scores[attacker] = CircusScoring.Bank(scores[attacker], applied.Amount * configuration.CollisionPointsPerDamage, awards, CircusScoreCategory.Collision);
                }
            }

            scores[victim.Player] = victim;
            if (!vehicle.Damage.Destroyed || vehicle.LifeId <= victim.ProcessedLife)
            {
                continue;
            }

            victim = victim with { ProcessedLife = vehicle.LifeId };
            if (lifecycle.AllowsGameplay)
            {
                victim = victim with { Deaths = checked(victim.Deaths + 1), KillStreak = 0 };
                scores[victim.Player] = victim;
                DamageEvent? damage = vehicle.Damage.LastDamage;
                ulong killer = damage is { DestroyedTransition: true } && damage.Tick == tick && damage.Attribution.Source is "missile" or "proxy-mine" or "salvo" or "machine-gun" or "collision"
                    ? damage.Attribution.InstigatorId : 0;
                if (killer == victim.Player || !scores.ContainsKey(killer) || !vehicles.Any(candidate => candidate.VehicleId == killer))
                {
                    killer = 0;
                }

                if (killer != 0)
                {
                    PlayerScore credited = scores[killer] with { Kills = checked(scores[killer].Kills + 1) };
                    if (configuration.Mode == MatchMode.Circus)
                    {
                        credited = credited with { KillStreak = checked(credited.KillStreak + 1) };
                        credited = CircusScoring.Bank(credited, configuration.BaseKillPoints + ((credited.KillStreak - 1) * configuration.KillStreakBonusStep), awards, CircusScoreCategory.Kill);
                    }
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

        // Retained Oil owners need a score identity, not a living or connected vehicle.
        if (configuration.Mode == MatchMode.Circus && lifecycle.AllowsGameplay)
        {
            foreach (var trigger in (oilTriggers ?? []).Distinct())
            {
                var target = results.Single(result => result.Snapshot.VehicleId == trigger.Vehicle);
                if (trigger.Owner != trigger.Vehicle && scores.TryGetValue(trigger.Owner, out var owner) &&
                    !target.Reset && target.Snapshot.CanInteract && target.Snapshot.LifeId == trigger.Life &&
                    target.Snapshot.Movement.OilTicks == 120)
                {
                    scores[trigger.Owner] = CircusScoring.Bank(owner, 50, awards, CircusScoreCategory.Oil);
                }
            }
        }

        // Resolve combat/life boundaries first: a lethal landing or same-tick death cannot bank.
        foreach (var result in results.OrderBy(result => result.Snapshot.VehicleId))
        {
            ulong id = result.Snapshot.VehicleId;
            if (scores.TryGetValue(id, out var score))
            {
                scores[id] = configuration.Mode == MatchMode.Circus && lifecycle.AllowsGameplay
                    ? StuntScoring.Advance(score, previousVehicles[id], result, configuration, vehicleRules(id), awards)
                    : score with { Stunts = null };
                if (configuration.Mode == MatchMode.Circus && lifecycle.AllowsGameplay && result.Snapshot.CanInteract && result.Snapshot.Speed > vehicleRules(id).ForwardSpeed && !result.Reset)
                {
                    scores[id] = CircusScoring.Bank(scores[id], configuration.NitroPointsPerSecond / vehicleRules(id).TicksPerSecond, awards, CircusScoreCategory.Nitro);
                }
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
        var publishedAwards = awards.GroupBy(award => (award.Player, award.Category))
            .Select(group => new CircusScoreAward(group.Key.Player, group.Key.Category, group.Sum(award => award.Points)));
        return new MatchState(tick, checked(previous.Revision + 1), configuration.KillTarget, phase, lifecycle.CountdownAtTick, lifecycle.Outcome?.Winner, scores.Values, changes, publishedAwards, configuration.Mode);
    }
}
