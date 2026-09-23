namespace Trackstorm.Core.Vehicles;

/// <summary>Stable gameplay surface identifiers shared by observations, snapshots and prediction.</summary>
public enum SurfaceType : byte
{
    /// <summary>Default hard driving surface.</summary>
    Concrete = 0,
    /// <summary>Soft ground with reduced traction and increased resistance.</summary>
    Mud = 1,
    /// <summary>Unmodified vehicle baseline.</summary>
    Asphalt = 2,
    /// <summary>Dry, compacted off-road ground.</summary>
    Dirt = 3,
    /// <summary>Vegetated off-road ground.</summary>
    Grass = 4,
    /// <summary>Saturated, traversable soil.</summary>
    DeepMud = 5,
    /// <summary>Immersed vehicle with strong resistance.</summary>
    Water = 6,
}
