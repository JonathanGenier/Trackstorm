namespace Trackstorm.Core.Events;

/// <summary>Stable diagnostic systems, also usable by future filtered projections.</summary>
public enum EventCategory
{
    /// <summary>Session outcomes.</summary>
    Session,
    /// <summary>Network outcomes.</summary>
    Network,
    /// <summary>Match outcomes.</summary>
    Match,
    /// <summary>Lifecycle outcomes.</summary>
    Lifecycle,
    /// <summary>Damage outcomes.</summary>
    Damage,
    /// <summary>Healing outcomes.</summary>
    Healing,
    /// <summary>Item outcomes.</summary>
    Item,
    /// <summary>Developer outcomes.</summary>
    Developer,
}
