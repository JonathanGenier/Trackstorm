using Trackstorm.Core.Matches;

namespace Trackstorm.Client.Hud;

/// <summary>Presentation-only lifetime for authoritative pending state and committed award deltas.</summary>
internal sealed class CircusScoreFeedback
{
    private const double LingerMilliseconds = 1800;
    private const double FadeMilliseconds = 500;
    private readonly Dictionary<CircusScoreCategory, Recent> _recent = new();
    private ulong _player;
    private ulong _revision;
    private PlayerScore? _previous;

    /// <summary>Projects a local authoritative score boundary without calculating or mutating awards.</summary>
    internal CircusHudView? Project(MatchState? match, ulong player, double milliseconds)
    {
        if (match is null || match.Mode != MatchMode.Circus || player == 0 || !match.Players.Any(score => score.Player == player))
        {
            Reset();
            return null;
        }

        PlayerScore current = match.Players.Single(score => score.Player == player);
        if (_player != player || match.Revision < _revision)
        {
            Reset();
        }

        if (_revision != match.Revision)
        {
            if (_revision != 0)
            {
                foreach (CircusScoreAward award in match.Awards.Where(award => award.Player == player))
                {
                    _recent[award.Category] = new Recent(award.Points, CircusFeedbackKind.Banked, milliseconds + LingerMilliseconds);
                }

                if (_previous is { Stunts: { } pending } && match.Changes.Any(change => change.Victim == player))
                {
                    AddLost(pending.Drift.BasePoints, CircusScoreCategory.Drift, _previous.Value.KdMultiplier, milliseconds);
                    AddLost(pending.Airtime.BasePoints, CircusScoreCategory.Airtime, _previous.Value.KdMultiplier, milliseconds);
                    AddLost(pending.LongJumpBasePoints, CircusScoreCategory.LongJump, _previous.Value.KdMultiplier, milliseconds);
                    AddLost(pending.TopSpeed.BasePoints, CircusScoreCategory.TopSpeed, _previous.Value.KdMultiplier, milliseconds);
                }
            }

            _player = player;
            _revision = match.Revision;
            _previous = current;
        }

        foreach (CircusScoreCategory category in _recent.Where(entry => entry.Value.ExpiresAt <= milliseconds).Select(entry => entry.Key).ToArray())
        {
            _recent.Remove(category);
        }

        var rows = new Dictionary<CircusScoreCategory, CircusFeedbackRow>();
        foreach ((CircusScoreCategory category, Recent recent) in _recent)
        {
            double remaining = recent.ExpiresAt - milliseconds;
            float opacity = (float)Math.Clamp(remaining / FadeMilliseconds, 0, 1);
            rows[category] = new(category, Name(category), recent.Points, recent.Kind, opacity);
        }

        if (match.Phase == MatchPhase.Active && current.Stunts is { } stunts)
        {
            AddPending(rows, CircusScoreCategory.Drift, stunts.Drift.BasePoints, current.KdMultiplier);
            AddPending(rows, CircusScoreCategory.Airtime, stunts.Airtime.BasePoints, current.KdMultiplier);
            AddPending(rows, CircusScoreCategory.LongJump, stunts.LongJumpBasePoints, current.KdMultiplier);
            AddPending(rows, CircusScoreCategory.TopSpeed, stunts.TopSpeed.BasePoints, current.KdMultiplier);
        }

        return new CircusHudView(CircusHudView.FormatPoints(current.CircusScore), CircusHudView.FormatMultiplier(current.KdMultiplier),
            Enum.GetValues<CircusScoreCategory>().Where(rows.ContainsKey).Select(category => rows[category]).ToArray());
    }

    private void AddLost(double basePoints, CircusScoreCategory category, double multiplier, double milliseconds)
    {
        double points = basePoints * multiplier;
        if (points > 0)
        {
            _recent[category] = new Recent(points, CircusFeedbackKind.Lost, milliseconds + LingerMilliseconds);
        }
    }

    private static void AddPending(IDictionary<CircusScoreCategory, CircusFeedbackRow> rows, CircusScoreCategory category, double basePoints, double multiplier)
    {
        double points = basePoints * multiplier;
        if (points > 0)
        {
            rows[category] = new(category, Name(category), points, CircusFeedbackKind.Pending, 1);
        }
    }

    private static string Name(CircusScoreCategory category) => category switch
    {
        CircusScoreCategory.LongJump => "LONG JUMP",
        CircusScoreCategory.ItemDamage => "ITEM DAMAGE",
        CircusScoreCategory.TopSpeed => "TOP SPEED",
        _ => category.ToString().ToUpperInvariant(),
    };

    private void Reset()
    {
        _recent.Clear();
        _player = 0;
        _revision = 0;
        _previous = null;
    }

    private readonly record struct Recent(double Points, CircusFeedbackKind Kind, double ExpiresAt);
}
