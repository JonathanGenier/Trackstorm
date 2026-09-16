using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Development;

/// <summary>One effective session tuning boundary composed from the existing gameplay owners.</summary>
public sealed record GameplayConfiguration
{
    /// <summary>Existing vehicle and surface configuration.</summary>
    public VehicleConfiguration Vehicle { get; init; } = new();
    /// <summary>Existing health and collision configuration.</summary>
    public DamageConfiguration Damage { get; init; } = new();
    /// <summary>Existing Wrench and Missile configuration.</summary>
    public ItemConfiguration Items { get; init; } = new();
    /// <summary>Existing item distribution configuration.</summary>
    public ItemSpawnConfiguration Spawns { get; init; } = new();
    /// <summary>Existing death and respawn configuration.</summary>
    public RespawnConfiguration Respawn { get; init; } = new();
    /// <summary>Existing authoritative match rules.</summary>
    public MatchConfiguration Match { get; init; } = new();

    /// <summary>Validates all owning rules and the production fixed-rate/timer boundary.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Vehicle);
        ArgumentNullException.ThrowIfNull(Damage);
        ArgumentNullException.ThrowIfNull(Items);
        ArgumentNullException.ThrowIfNull(Spawns);
        ArgumentNullException.ThrowIfNull(Respawn);
        ArgumentNullException.ThrowIfNull(Match);
        Vehicle.Validate();
        Damage.Validate();
        Items.Validate();
        Spawns.Validate();
        Respawn.Validate();
        Match.Validate();
        _ = new SurfaceModifiers(Vehicle.Concrete.Grip, Vehicle.Concrete.Drag, Vehicle.Concrete.Acceleration);
        _ = new SurfaceModifiers(Vehicle.Mud.Grip, Vehicle.Mud.Drag, Vehicle.Mud.Acceleration);
        // Positive subnormal mass, axle lengths or force scales pass legacy scalar validation but can overflow
        // fixed-step divisions. Zero remains allowed wherever the owning configuration explicitly permits it.
        if (GameplayOptions.All.Where(option => !option.Integral).Any(option => option.Read(this) is > 0 and < 0.0001f))
        {
            throw new ArgumentException("Live tuning requires positive scalar values of at least 0.0001 for stable physics arithmetic.");
        }

        if (Vehicle.TicksPerSecond != Networking.Replication.HostVehicleSession.TickRate ||
            Damage.CollisionCooldownTicks > 216000 || Respawn.DelayTicks > 216000)
        {
            throw new ArgumentException("Live tuning keeps the fixed 60 Hz clock and bounds timers to one hour.");
        }
    }
}
