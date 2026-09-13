namespace Trackstorm.Core.Vehicles;

/// <summary>Host-owned damage tuning; collision severity is measured in metres per second.</summary>
public sealed record DamageConfiguration
{
    /// <summary>Health on spawn or explicit reset.</summary>
    public float MaxHP { get; init; } = 100;
    /// <summary>Contacts at or below this severity are harmless.</summary>
    public float CollisionThreshold { get; init; } = 4;
    /// <summary>HP lost per excess unit of collision severity.</summary>
    public float CollisionScale { get; init; } = 3;
    /// <summary>Maximum HP loss from one collision.</summary>
    public float MaximumCollisionDamage { get; init; } = 100;
    /// <summary>Minimum fixed ticks between damaging contacts on one vehicle.</summary>
    public ulong CollisionCooldownTicks { get; init; } = 12;

    /// <summary>Rejects nonfinite, negative, or unusable tuning.</summary>
    public void Validate()
    {
        if (!float.IsFinite(MaxHP) || MaxHP <= 0 || MaxHP > 1_000_000 ||
            !float.IsFinite(CollisionThreshold) || CollisionThreshold < 0 ||
            !float.IsFinite(CollisionScale) || CollisionScale < 0 ||
            !float.IsFinite(MaximumCollisionDamage) || MaximumCollisionDamage < 0 || CollisionCooldownTicks == 0)
        {
            throw new ArgumentException("Invalid vehicle damage configuration.");
        }
    }
}
