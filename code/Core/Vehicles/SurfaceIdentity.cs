namespace Trackstorm.Core.Vehicles;

/// <summary>Material identity only; independent of legacy SurfaceType handling profiles.</summary>
public enum SurfaceIdentity : byte
{
    /// <summary>Paved oval.</summary>
    Asphalt,
    /// <summary>Vegetated ground.</summary>
    Grass,
    /// <summary>Dry off-road ground.</summary>
    Dirt,
    /// <summary>Wet soil.</summary>
    Mud,
    /// <summary>Saturated basin soil.</summary>
    DeepMud,
    /// <summary>Exposed stone.</summary>
    Rock,
    /// <summary>Cast structural surface.</summary>
    Concrete,
    /// <summary>Designated water bed; no depth or gameplay behavior is implied.</summary>
    Water,
}
