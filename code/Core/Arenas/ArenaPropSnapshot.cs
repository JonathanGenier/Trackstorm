using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Arenas;

/// <summary>Complete host-observed prop state in stable prop-01 through prop-03 order.</summary>
public sealed class ArenaPropSnapshot
{
    /// <summary>Validates an atomic native observation or received full publication.</summary>
    /// <param name="session">Nonzero host arena generation.</param>
    /// <param name="tick">Host observation tick.</param>
    /// <param name="bodies">Three stable prop observations.</param>
    public ArenaPropSnapshot(ulong session, ulong tick, IEnumerable<VehiclePhysicsState> bodies)
    {
        ArgumentOutOfRangeException.ThrowIfZero(session);
        ArgumentNullException.ThrowIfNull(bodies);
        VehiclePhysicsState[] copy = bodies.ToArray();
        if (copy.Length != 3)
        {
            throw new ArgumentException("The prototype has exactly three stable props.");
        }

        foreach (VehiclePhysicsState body in copy)
        {
            _ = new VehiclePhysicsState(body.Position, body.Orientation, body.LinearVelocity, body.AngularVelocity);
            if (body.Position.LengthSquared() > 1000000 || body.LinearVelocity.LengthSquared() > 10000 || body.AngularVelocity.LengthSquared() > 10000)
            {
                throw new ArgumentException("Prop observation exceeds the prototype simulation envelope.");
            }
        }

        Session = session;
        Tick = tick;
        Bodies = Array.AsReadOnly(copy);
    }

    /// <summary>Arena generation; old matches cannot update a new scene.</summary>
    public ulong Session { get; }
    /// <summary>Host fixed tick ordering.</summary>
    public ulong Tick { get; }
    /// <summary>Copied physics observations; only the host simulates these bodies.</summary>
    public IReadOnlyList<VehiclePhysicsState> Bodies { get; }
}
