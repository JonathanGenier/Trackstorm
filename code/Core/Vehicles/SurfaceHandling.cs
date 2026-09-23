namespace Trackstorm.Core.Vehicles;

/// <summary>Maps authored material observations to the existing portable handling contract.</summary>
public static class SurfaceHandling
{
    /// <summary>Maps material identity to the shared handling profile.</summary>
    /// <param name="identity">Material from the shared support query, or unavailable.</param>
    /// <param name="fallback">Existing explicit fixture profile for unauthored colliders.</param>
    /// <returns>The Core profile to simulate and replicate.</returns>
    public static SurfaceType Resolve(SurfaceIdentity? identity, SurfaceType fallback = SurfaceType.Asphalt) => identity switch
    {
        SurfaceIdentity.Asphalt or SurfaceIdentity.Rock => SurfaceType.Asphalt,
        SurfaceIdentity.Water => SurfaceType.Water,
        SurfaceIdentity.Concrete => SurfaceType.Concrete,
        SurfaceIdentity.Dirt => SurfaceType.Dirt,
        SurfaceIdentity.Grass => SurfaceType.Grass,
        SurfaceIdentity.Mud => SurfaceType.Mud,
        SurfaceIdentity.DeepMud => SurfaceType.DeepMud,
        null => fallback,
        _ => throw new ArgumentOutOfRangeException(nameof(identity)),
    };
}
