namespace Trackstorm.Core.Vehicles;

/// <summary>Maps authored material observations to the existing portable handling contract.</summary>
public static class SurfaceHandling
{
    /// <summary>Rock and Water retain the previous baseline until their own handling is specified.</summary>
    /// <param name="identity">Material from the shared support query, or unavailable.</param>
    /// <param name="fallback">Existing explicit fixture profile for unauthored colliders.</param>
    /// <returns>The Core profile to simulate and replicate.</returns>
    public static SurfaceType Resolve(SurfaceIdentity? identity, SurfaceType fallback = SurfaceType.Asphalt) => identity switch
    {
        SurfaceIdentity.Asphalt or SurfaceIdentity.Rock or SurfaceIdentity.Water => SurfaceType.Asphalt,
        SurfaceIdentity.Concrete => SurfaceType.Concrete,
        SurfaceIdentity.Dirt => SurfaceType.Dirt,
        SurfaceIdentity.Grass => SurfaceType.Grass,
        SurfaceIdentity.Mud => SurfaceType.Mud,
        SurfaceIdentity.DeepMud => SurfaceType.DeepMud,
        null => fallback,
        _ => throw new ArgumentOutOfRangeException(nameof(identity)),
    };
}
