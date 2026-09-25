using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Complete match-owned magnetic hazard; position is the collider centre above its support.</summary>
public sealed record ProxyMineState(ulong Id, ulong Owner, Vector3 Position, Vector3 Velocity, Vector3 Normal, int SeatingTicks)
{
    /// <summary>Readable 1.3 metre diameter, shared by the native collider and presentation.</summary>
    public const float Radius = 0.65f;
    /// <summary>Half the installed body's height.</summary>
    public const float HalfHeight = 0.25f;
    /// <summary>Fixed mine mass used to convert magnetic Newtons into acceleration.</summary>
    public const float Mass = 20;

    /// <summary>Rejects corrupt checkpoint or native-query state before commit.</summary>
    public void Validate()
    {
        if (Id == 0 || Owner == 0 || !VehiclePhysicsState.IsFinite(Position) || !VehiclePhysicsState.IsFinite(Velocity) ||
            Velocity.Length() > 40.01f || !VehiclePhysicsState.IsFinite(Normal) || Math.Abs(Normal.LengthSquared() - 1) > 0.01f || SeatingTicks is < 0 or > 30)
        {
            throw new ArgumentException("Invalid Proxy Mine state.");
        }
    }

    /// <summary>Continuous outer-edge ramp and tunable close-range power curve, in Newtons.</summary>
    public static float AttractionForce(float distance, ItemConfiguration configuration)
    {
        float proximity = Math.Clamp(1 - distance / configuration.MineAttractionRadius, 0, 1);
        // The minimum is the weak-field baseline reached through the outermost ten percent.
        return Math.Min(1, proximity * 10) * configuration.MineMinimumForce +
            (configuration.MineMaximumForce - configuration.MineMinimumForce) * MathF.Pow(proximity, configuration.MineFalloff);
    }

    /// <summary>Force integration only; the host adapter resolves the candidate's terrain collisions.</summary>
    internal ProxyMineState Advance(Vector3? target, ItemConfiguration configuration)
    {
        if (SeatingTicks > 0) { return this with { SeatingTicks = SeatingTicks - 1 }; }
        Vector3 force = Vector3.Zero;
        if (target is Vector3 point)
        {
            Vector3 offset = point - Position;
            if (offset.LengthSquared() > 0.0001f) { force = Vector3.Normalize(offset) * AttractionForce(offset.Length(), configuration); }
        }
        // A seated installation remains at rest until the field actually pulls it. Once moving,
        // gravity and drag continue normally even when the target leaves the field.
        if (force == Vector3.Zero && Velocity == Vector3.Zero) { return this; }
        // Gravity plus viscous drag retain momentum rather than snapping toward the target.
        Vector3 velocity = VehicleMovement.Limit(Velocity + (force / Mass - Vector3.UnitY * 9.81f - Velocity * 1.8f) / 60, 40);
        return this with { Position = Position + velocity / 60, Velocity = velocity };
    }
}
