using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>One validated resume boundary: current state only, with historical one-shot outcomes omitted.</summary>
public sealed class ResumeCheckpoint
{
    /// <summary>Validates common world identity/tick before any client component is reset.</summary>
    /// <param name="items">World, health/lives, slots, missiles and spawn deadlines.</param>
    /// <param name="match">Current score totals, winner and consumed-life watermarks.</param>
    /// <param name="props">Optional current native prop poses.</param>
    public ResumeCheckpoint(ItemPublication items, MatchState match, ArenaPropSnapshot? props)
    {
        if (items.Events.Count != 0 || match.Changes.Count != 0 || match.Tick > items.World.Tick ||
            (props is not null && (props.Session != items.World.Session || props.Tick != items.World.Tick)))
        {
            throw new ArgumentException("Inconsistent resume checkpoint.");
        }

        Items = items;
        Match = match;
        Props = props;
    }

    /// <summary>Complete authoritative vehicle/item boundary.</summary>
    public ItemPublication Items { get; }
    /// <summary>Current match totals without historical score events.</summary>
    public MatchState Match { get; }
    /// <summary>Host-observed movable arena state.</summary>
    public ArenaPropSnapshot? Props { get; }
}
