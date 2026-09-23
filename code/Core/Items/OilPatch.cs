using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Persistent match-owned surface hazard. The grant token is its stable identity.</summary>
public sealed record OilPatch(ulong Id, ulong Owner, Vector3 Position, Vector3 Normal, float Radius)
{
    /// <summary>Rejects invalid portable placement data.</summary>
    public void Validate()
    {
        if (Id == 0 || Owner == 0 || !VehiclePhysicsState.IsFinite(Position) ||
            !VehiclePhysicsState.IsFinite(Normal) || Math.Abs(Normal.LengthSquared() - 1) > 0.001f ||
            Normal.Y < 0.55f || !float.IsFinite(Radius) || Radius is < 1 or > 5)
        {
            throw new ArgumentException("Invalid oil patch.");
        }
    }

    /// <summary>Tests supported vehicle contact in the patch plane, excluding flight and stacked roads.</summary>
    /// <param name="observation">Host-observed vehicle support and pose.</param>
    /// <returns>Whether the vehicle occupies this hazardous surface.</returns>
    public bool Contains(VehicleObservation observation)
    {
        Vector3 offset = observation.Physics.Position - Position;
        float height = Vector3.Dot(offset, Normal);
        return Vector3.Dot(observation.Support, Normal) > 0.8f && height is >= 0 and <= 1.6f &&
            (offset - Normal * height).LengthSquared() <= Radius * Radius;
    }
}
