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
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    public ResumeCheckpoint(ItemPublication items, MatchState match, ArenaPropSnapshot? props, Development.GameplayConfigurationState? configuration = null)
    {
        configuration ??= new(items.World.ConfigurationRevision, new() { Damage = new() { MaxHP = items.World.Vehicles[0].State.Damage.MaxHP }, Match = new() { KillTarget = match.KillTarget } });
        if (items.Events.Count != 0 || match.Changes.Count != 0 || match.Tick > items.World.Tick ||
            (props is not null && (props.Session != items.World.Session || props.Tick != items.World.Tick)) ||
            configuration.Revision != items.World.ConfigurationRevision || configuration.Configuration.Match.KillTarget != match.KillTarget ||
            items.World.Vehicles.Any(vehicle => vehicle.State.Damage.MaxHP != configuration.Configuration.Damage.MaxHP ||
                Math.Abs(vehicle.State.Movement.SteeringAngle) > configuration.Configuration.Vehicle.SteeringAngle))
        {
            throw new ArgumentException("Inconsistent resume checkpoint.");
        }

        Items = items;
        Match = match;
        Props = props;
        Configuration = configuration;
    }

    /// <summary>Complete authoritative vehicle/item boundary.</summary>
    public ItemPublication Items { get; }
    /// <summary>Current match totals without historical score events.</summary>
    public MatchState Match { get; }
    /// <summary>Host-observed movable arena state.</summary>
    public ArenaPropSnapshot? Props { get; }
    /// <summary>Effective tuning installed before reconstructing prediction or gameplay state.</summary>
    public Development.GameplayConfigurationState Configuration { get; }
}
