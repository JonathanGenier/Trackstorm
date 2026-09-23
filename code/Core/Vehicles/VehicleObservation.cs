using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Immutable solved physics and support/contact observations at the start of one Core step.</summary>
public sealed class VehicleObservation
{
    /// <summary>Validates and copies native observations before they can affect gameplay.</summary>
    /// <param name="physics">Solved pose and velocities.</param>
    /// <param name="support">Unit support normal, or zero.</param>
    /// <param name="contacts">Contact observations; copied rather than retaining the caller's collection.</param>
    /// <param name="surface">Detected supporting surface; ignored while airborne.</param>
    /// <param name="wheels">Optional per-wheel spring compression from fixed-step queries.</param>
    /// <param name="terrainSupport">Wheel support normal from tagged terrain.</param>
    /// <param name="waterDepth">Immersion from nominal tire level, independently of wheel contact.</param>
    public VehicleObservation(VehiclePhysicsState physics, Vector3 support, IEnumerable<VehicleContact>? contacts = null, SurfaceType surface = SurfaceType.Concrete, WheelSupport? wheels = null, Vector3 terrainSupport = default, float waterDepth = 0)
    {
        _ = new VehiclePhysicsState(physics.Position, physics.Orientation, physics.LinearVelocity, physics.AngularVelocity);
        if (!VehiclePhysicsState.IsFinite(support) || (support != Vector3.Zero && Math.Abs(support.LengthSquared() - 1) > 0.001f))
        {
            throw new ArgumentException("Support must be a unit normal or zero.", nameof(support));
        }

        VehicleContact[] copy = contacts?.ToArray() ?? [];
        foreach (VehicleContact contact in copy)
        {
            _ = new VehicleContact(contact.RelativeVelocity, contact.Normal, contact.Impulse, contact.OtherVehicleId, contact.Terrain, contact.LocalPosition);
        }

        if (!Enum.IsDefined(surface))
        {
            throw new ArgumentOutOfRangeException(nameof(surface));
        }

        if (!VehiclePhysicsState.IsFinite(terrainSupport) || (terrainSupport != Vector3.Zero && Math.Abs(terrainSupport.LengthSquared() - 1) > 0.001f)) { throw new ArgumentException("Invalid terrain support."); }
        TerrainSupport = terrainSupport;
        if (!float.IsFinite(waterDepth) || waterDepth is < 0 or > 1000) { throw new ArgumentOutOfRangeException(nameof(waterDepth)); }
        WaterDepth = waterDepth;
        Wheels = wheels;
        Surface = surface;
        Physics = physics;
        Support = support;
        Contacts = Array.AsReadOnly(copy);
    }

    /// <summary>Wheel-ray normal from explicitly driveable terrain, or zero.</summary>
    public Vector3 TerrainSupport { get; }
    /// <summary>Current immersion; zero outside authored Water or above its surface.</summary>
    public float WaterDepth { get; }
    /// <summary>Optional independent wheel support observations.</summary>
    public WheelSupport? Wheels { get; }
    /// <summary>Supporting surface from the fixed-step adapter.</summary>
    public SurfaceType Surface { get; }
    /// <summary>Latest collision-solved native body data.</summary>
    public VehiclePhysicsState Physics { get; }
    /// <summary>Support observation, without a hidden Client grounding timer.</summary>
    public Vector3 Support { get; }
    /// <summary>Raw contact observations for Core decisions.</summary>
    public IReadOnlyList<VehicleContact> Contacts { get; }
}
