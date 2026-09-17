namespace Trackstorm.Core.Networking.Replication;

/// <summary>Bounded complete active roster at one host tick; absence means departure.</summary>
public sealed class WorldSnapshot
{
    /// <summary>Validates and detaches the entire snapshot before publication.</summary>
    /// <param name="session">Host session identity negotiated over the reliable control path.</param>
    /// <param name="tick">Nonwrapping Core tick; its low 32 bits also provide wire ordering.</param>
    /// <param name="vehicles">One through eight uniquely identified active vehicles.</param>
    /// <param name="configurationRevision">Authoritative tuning revision used by this snapshot.</param>
    public WorldSnapshot(ulong session, ulong tick, IEnumerable<ReplicatedVehicle> vehicles, ulong configurationRevision = 0)
    {
        ReplicatedVehicle[] copy = vehicles.ToArray();
        if (session == 0 || copy.Length is < 1 or > 8 || copy.Any(vehicle => vehicle is null || vehicle.State.Movement.Tick != tick || vehicle.State.Effects.Count > 64) || copy.Select(vehicle => vehicle.State.VehicleId).Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("Invalid vehicle snapshot roster, tick or session.");
        }

        Session = session;
        Tick = tick;
        ConfigurationRevision = configurationRevision;
        Vehicles = Array.AsReadOnly(copy);
    }

    /// <summary>Session identity prevents prior-session snapshots from being accepted.</summary>
    public ulong Session { get; }
    /// <summary>Host fixed tick at the command boundary.</summary>
    public ulong Tick { get; }
    /// <summary>Tuning used to produce this boundary; mismatched revisions cannot enter prediction.</summary>
    public ulong ConfigurationRevision { get; }
    /// <summary>Complete active roster, detached from the caller.</summary>
    public IReadOnlyList<ReplicatedVehicle> Vehicles { get; }
}
