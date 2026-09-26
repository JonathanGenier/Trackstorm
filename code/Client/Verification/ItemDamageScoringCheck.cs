using Trackstorm.Core.Events;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;

namespace Trackstorm.Client.Verification;

/// <summary>Checks native weapon outcomes against committed score deltas, independently of contact damage.</summary>
internal sealed class ItemDamageScoringCheck
{
    private readonly Dictionary<ulong, MatchState> _published = new();
    internal int Hits { get; private set; }
    internal double Points { get; private set; }

    internal void Observe(MatchState match, bool host)
    {
        if (host) { _published[match.Revision] = match; return; }
        if (!_published.TryGetValue(match.Revision, out var expected) || !match.Players.SequenceEqual(expected.Players) ||
            !match.Awards.SequenceEqual(expected.Awards))
        { throw new InvalidOperationException("Peer item score publication differs from authoritative revision."); }
    }

    internal void Verify(HostVehicleSession host, SimulationState before, string cause)
    {
        var after = host.World.State;
        if (after.Tick == before.Tick || before.Match?.Phase == MatchPhase.Finished) { return; }
        var match = after.Match!;
        if (match.Revision == before.Match!.Revision) { return; }
        var expected = new Dictionary<ulong, double>();
        var scores = before.Match.Players.ToDictionary(p => p.Player);
        foreach (var victim in after.Vehicles.OrderBy(v => v.VehicleId))
        {
            foreach (var hit in host.World.Events.Entries.Where(e => e.Tick == after.Tick && e.Category == EventCategory.Damage && e.Target == victim.VehicleId && e.Cause == cause))
            {
                if (hit.Actor == hit.Target || !scores.ContainsKey(hit.Actor) || match.Phase is MatchPhase.Waiting or MatchPhase.Countdown) { continue; }
                double points = hit.Amount!.Value * host.Configuration.Configuration.Match.ItemPointsPerDamage * scores[hit.Actor].KdMultiplier;
                expected[hit.Actor] = expected.GetValueOrDefault(hit.Actor) + points;
                Hits++; Points += points;
            }
            foreach (var death in match.Changes.Where(d => d.Victim == victim.VehicleId))
            {
                scores[death.Victim] = scores[death.Victim] with { Deaths = scores[death.Victim].Deaths + 1 };
                if (death.Killer != 0) { scores[death.Killer] = scores[death.Killer] with { Kills = scores[death.Killer].Kills + 1 }; }
                if (death.Killer == match.Winner) { break; }
            }
            if (match.Winner is ulong winner && scores[winner].Kills == match.KillTarget) { break; }
        }
        foreach (var row in match.Players)
        {
            double awarded = match.Awards.Where(a => a.Player == row.Player && a.Category == CircusScoreCategory.ItemDamage).Sum(a => a.Points);
            if (Math.Abs(awarded - expected.GetValueOrDefault(row.Player)) > 1e-8)
            { throw new InvalidOperationException($"Item scoring mismatch at {after.Tick}: player {row.Player}, expected {expected.GetValueOrDefault(row.Player)}, actual {awarded}."); }
            if (scores.ContainsKey(row.Player) && Math.Abs(row.CircusScore - before.Match.Players.Single(p => p.Player == row.Player).CircusScore - match.Awards.Where(a => a.Player == row.Player).Sum(a => a.Points)) > 1e-8)
            { throw new InvalidOperationException("Banked score delta differs from separate committed awards."); }
        }
    }
}
