namespace Trackstorm.Core.Vehicles;

/// <summary>Serializable pre-solver snapshot: observed pose, next velocities, and all movement state-machine memory.</summary>
public readonly record struct VehicleState
{
    /// <summary>Constructs a movement snapshot suitable for explicit restoration.</summary>
    /// <param name="tick">Last completed movement decision tick.</param>
    /// <param name="physics">Observed pose and commanded velocities.</param>
    /// <param name="grounded">Whether support was observed this tick.</param>
    /// <param name="drifting">Whether a valid drift is active.</param>
    /// <param name="driftTicks">Accumulated valid drift duration.</param>
    /// <param name="boostTicks">Remaining boost duration.</param>
    public VehicleState(ulong tick, VehiclePhysicsState physics, bool grounded, bool drifting, int driftTicks, int boostTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(driftTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(boostTicks);
        _ = new VehiclePhysicsState(physics.Position, physics.Orientation, physics.LinearVelocity, physics.AngularVelocity);
        if (!drifting && driftTicks != 0)
        {
            throw new ArgumentException("An inactive drift cannot retain charge.");
        }

        Tick = tick;
        Physics = physics;
        Grounded = grounded;
        Drifting = drifting;
        DriftTicks = driftTicks;
        BoostTicks = boostTicks;
    }

    /// <summary>Movement decision tick.</summary>
    public ulong Tick { get; }
    /// <summary>Body state for presentation and reconciliation.</summary>
    public VehiclePhysicsState Physics { get; }
    /// <summary>Ground contact supplied by the physics adapter.</summary>
    public bool Grounded { get; }
    /// <summary>Valid active drift.</summary>
    public bool Drifting { get; }
    /// <summary>Continuous valid drift ticks.</summary>
    public int DriftTicks { get; }
    /// <summary>Boost ticks remaining.</summary>
    public int BoostTicks { get; }
    /// <summary>Unconverted total speed in metres per second.</summary>
    public float Speed => Physics.LinearVelocity.Length();
}
