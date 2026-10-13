using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

/// <summary>Portable emission and flight continuation; all parcels share the parent hazard deadline.</summary>
public sealed record OilPour
{
    public ulong Life { get; init; }
    public ulong StartTick { get; init; }
    public int DurationTicks { get; init; } = 60;
    public float IntervalTicks { get; init; } = 1.5f;
    public float Speed { get; init; } = 18;
    public float Gravity { get; init; } = 9.81f;
    public float Radius { get; init; } = 1.3f;
    public int Emitted { get; init; }
    public bool Deposited { get; init; }
    public bool Cancelled { get; init; }
    public Vector3 PreviousVelocity { get; init; }
    public Vector3 HeadOffset { get; init; }
    public Vector3 HeadVelocity { get; init; }
    public float Stiffness { get; init; } = 26;
    public float Damping { get; init; } = 4.5f;
    public float Reaction { get; init; } = .32f;
    public float Kickback { get; init; } = 1.6f;
    /// <summary>Oil-only carriage travel behind the unchanged raised rack.</summary>
    public const float RearTravel = 1.30f;
    public OilFlight Drops { get; init; } = OilFlight.Empty;

    /// <summary>Rejects malformed continuation before recovery or simulation.</summary>
    public void Validate()
    {
        if (Life == 0 || DurationTicks is < 30 or > 240 || !float.IsFinite(IntervalTicks) || IntervalTicks is < 1 or > 30 ||
            MathF.Ceiling(DurationTicks / IntervalTicks) > 48 || Emitted is < 0 or > 48 ||
            !float.IsFinite(Speed) || Speed is < 1 or > 30 || !float.IsFinite(Gravity) || Gravity is < 1 or > 40 ||
            !float.IsFinite(Radius) || Radius is < .1f or > 1.5f ||
            !float.IsFinite(Stiffness) || Stiffness is < 5 or > 100 || !float.IsFinite(Damping) || Damping is < 1 or > 20 ||
            !float.IsFinite(Reaction) || Reaction is < 0 or > .5f ||
            !float.IsFinite(Kickback) || Kickback is < 0 or > 3 ||
            !VehiclePhysicsState.IsFinite(PreviousVelocity) || !VehiclePhysicsState.IsFinite(HeadOffset) || HeadOffset.Length() > .451f ||
            !VehiclePhysicsState.IsFinite(HeadVelocity) || HeadVelocity.Length() > 5.01f || Drops is null || Drops.Length > 48 ||
            Drops.Drops.Any(d => !VehiclePhysicsState.IsFinite(d.Position) || !VehiclePhysicsState.IsFinite(d.Velocity) || d.Velocity.Length() > 1000 || d.RemainingTicks is < 1 or > 180))
        { throw new ArgumentException("Invalid Oil pour continuation."); }
    }

    /// <summary>Stable fixed-step inertial spring; firing direction never uses its displacement.</summary>
    public static (Vector3 Offset, Vector3 Velocity) Spring(Vector3 offset, Vector3 velocity, Vector3 acceleration, float stiffness, float damping, float reaction)
    {
        velocity += (-offset * stiffness - velocity * damping - acceleration * reaction) / 60;
        if (velocity.Length() > 5) { velocity = Vector3.Normalize(velocity) * 5; }
        offset += velocity / 60;
        if (offset.Length() > .45f) { offset = Vector3.Normalize(offset) * .45f; velocity *= .5f; }
        return (offset, velocity);
    }

    /// <summary>The raised rack mouth socket in vehicle coordinates, facing rearward (+Z).</summary>
    public static Vector3 Mouth(Vector3 offset) => new Vector3(0, 2.85f, 1.845f + RearTravel) + offset +
        Vector3.Transform(new Vector3(0, .025f, .225f), Quaternion.CreateFromYawPitchRoll(0, offset.Z * .8f, -offset.X * .8f));
}
