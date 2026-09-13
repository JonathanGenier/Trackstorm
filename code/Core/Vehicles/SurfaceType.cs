namespace Trackstorm.Core.Vehicles;

/// <summary>Stable gameplay surface identifiers shared by observations, snapshots and prediction.</summary>
public enum SurfaceType : byte
{
    /// <summary>Default hard driving surface.</summary>
    Concrete = 0,
    /// <summary>Soft ground with reduced traction and increased resistance.</summary>
    Mud = 1,
}
