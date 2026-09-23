namespace Trackstorm.Core.Vehicles;

/// <summary>Dominant identity in an authored linear RGBA field: dirt, wetness, rock, water.</summary>
public static class SurfaceIdentityField
{
    /// <summary>Resolves blended material weights with a stable, explicit boundary priority.</summary>
    /// <param name="dirt">Dry route coverage.</param>
    /// <param name="wet">Wet soil coverage; saturated centers use Deep Mud.</param>
    /// <param name="rock">Exposed rock coverage.</param>
    /// <param name="water">Designated water coverage.</param>
    /// <returns>The dominant visual identity, never handling coefficients.</returns>
    public static SurfaceIdentity Resolve(float dirt, float wet, float rock, float water)
    {
        if (!Valid(dirt) || !Valid(wet) || !Valid(rock) || !Valid(water))
        {
            throw new ArgumentOutOfRangeException(nameof(dirt), "Field channels must be finite normalized weights.");
        }

        return water >= 0.5f ? SurfaceIdentity.Water : wet >= 0.65f ? SurfaceIdentity.DeepMud : wet >= 0.25f ? SurfaceIdentity.Mud : rock >= 0.5f ? SurfaceIdentity.Rock : dirt >= 0.5f ? SurfaceIdentity.Dirt : SurfaceIdentity.Grass;
    }

    private static bool Valid(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
}
