using System.Numerics;
using Trackstorm.Core.Input;

namespace Trackstorm.Core.Vehicles;

/// <summary>Pure fixed-step arcade movement decisions. A runtime supplies collision-resolved body observations.</summary>
public sealed class VehicleMovement
{
    /// <summary>Creates a movement owner with validated immutable tuning and initial pose.</summary>
    /// <param name="configuration">Shared fixed-step tuning.</param>
    /// <param name="initial">Initial body observation.</param>
    public VehicleMovement(VehicleConfiguration configuration, VehiclePhysicsState initial)
    {
        configuration.Validate();
        Configuration = configuration;
        State = new VehicleState(0, initial, false, false, 0, 0);
    }

    /// <summary>Validated movement tuning.</summary>
    public VehicleConfiguration Configuration { get; }
    /// <summary>Latest pre-solver snapshot.</summary>
    public VehicleState State { get; private set; }

    /// <summary>Speed-sensitive yaw rate. Steering is normalized; reversing reverses steering direction.</summary>
    /// <param name="steering">Signed normalized input.</param>
    /// <param name="forwardSpeed">Signed longitudinal speed.</param>
    /// <param name="maximumRate">Configured peak yaw rate.</param>
    /// <returns>Target world-up yaw rate.</returns>
    public static float Steering(float steering, float forwardSpeed, float maximumRate)
    {
        if (!float.IsFinite(steering) || !float.IsFinite(forwardSpeed) || !float.IsFinite(maximumRate) || maximumRate < 0)
        {
            throw new ArgumentException("Steering requires finite inputs and a nonnegative rate.");
        }

        float speed = Math.Abs(forwardSpeed);
        return -Math.Clamp(steering, -1, 1) * Math.Sign(forwardSpeed) * maximumRate * Math.Min(speed / 4, 1) / (1 + (speed / 45));
    }

    /// <summary>Bounds external velocity without changing direction.</summary>
    /// <param name="value">Finite vector.</param>
    /// <param name="maximum">Positive magnitude bound.</param>
    /// <returns>Clamped vector.</returns>
    public static Vector3 Limit(Vector3 value, float maximum)
    {
        if (!VehiclePhysicsState.IsFinite(value) || !float.IsFinite(maximum) || maximum <= 0)
        {
            throw new ArgumentException("Velocity bounds require finite inputs.");
        }

        return value.LengthSquared() > maximum * maximum ? Vector3.Normalize(value) * maximum : value;
    }

    /// <summary>Advances exactly one input tick, with no render delta, device polling, or native body access.</summary>
    /// <param name="input">Next sequential logical frame.</param>
    /// <param name="observed">Collision-resolved pose and velocities.</param>
    /// <param name="groundNormal">Unit support normal, or zero while airborne.</param>
    /// <param name="driveEnabled">Authority-controlled drive permission.</param>
    /// <returns>Next movement snapshot and commanded velocities.</returns>
    public VehicleState Step(InputFrame input, VehiclePhysicsState observed, Vector3 groundNormal, bool driveEnabled = true)
    {
        if (input.Tick != checked(State.Tick + 1))
        {
            throw new ArgumentException("Movement input must target the next tick.", nameof(input));
        }

        _ = new VehiclePhysicsState(observed.Position, observed.Orientation, observed.LinearVelocity, observed.AngularVelocity);
        if (!VehiclePhysicsState.IsFinite(groundNormal) || (groundNormal != Vector3.Zero && Math.Abs(groundNormal.LengthSquared() - 1) > 0.001f))
        {
            throw new ArgumentException("Ground support must be a unit normal or zero.", nameof(groundNormal));
        }

        VehicleConfiguration c = Configuration;
        float dt = 1f / c.TicksPerSecond;
        bool grounded = groundNormal.Y >= 0.55f;
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, observed.Orientation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, observed.Orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, observed.Orientation);
        Vector3 velocity = Limit(observed.LinearVelocity, c.MaximumPhysicsSpeed);
        float longitudinal = Vector3.Dot(velocity, forward);
        float steering = driveEnabled ? input.Steering / 32767f : 0;
        bool held = driveEnabled && (input.Held & InputButtons.Drift) != 0;
        bool validDrift = grounded && longitudinal >= c.DriftMinimumSpeed && Math.Abs(steering) >= 0.2f;
        bool drifting = held && validDrift;
        int chargeNeeded = (int)MathF.Ceiling(c.DriftChargeSeconds * c.TicksPerSecond);
        int driftTicks = drifting ? Math.Min(State.DriftTicks + 1, chargeNeeded) : 0;
        int boostTicks = Math.Max(0, State.BoostTicks - 1);
        if (driveEnabled && !held && State.Drifting && State.DriftTicks >= chargeNeeded && validDrift)
        {
            boostTicks = (int)MathF.Ceiling(c.BoostSeconds * c.TicksPerSecond);
        }

        if (!driveEnabled)
        {
            boostTicks = 0;
        }

        float throttle = driveEnabled ? input.Accelerate / 65535f : 0;
        float brake = driveEnabled ? input.Brake / 65535f : 0;
        float target = longitudinal;
        float authority = grounded ? 1 : 0.12f;
        if (brake > 0)
        {
            target = longitudinal > 0 ? Math.Max(0, longitudinal - (c.Braking * brake * dt))
                : throttle > 0 ? Math.Min(0, longitudinal + (c.Braking * throttle * dt))
                : Math.Max(-c.ReverseSpeed, longitudinal - (c.ReverseAcceleration * brake * dt));
        }
        else if (throttle > 0 || boostTicks > 0)
        {
            float cap = c.ForwardSpeed + (boostTicks > 0 ? c.BoostSpeed : 0);
            target = longitudinal < 0 ? Math.Min(0, longitudinal + (c.Braking * throttle * dt))
                : longitudinal < cap ? Math.Min(cap, longitudinal + (((c.Acceleration * throttle) + (boostTicks > 0 ? c.Acceleration : 0)) * dt))
                : Math.Max(cap, longitudinal - (3 * dt));
        }
        else if (grounded)
        {
            target *= MathF.Exp(-0.35f * dt);
        }

        velocity += forward * ((target - longitudinal) * authority);
        if (grounded)
        {
            velocity -= right * (Vector3.Dot(velocity, right) * (1 - MathF.Exp(-(drifting ? c.DriftGrip : c.Grip) * dt)));
        }

        velocity -= Vector3.UnitY * (c.Gravity * dt);
        Vector3 angular = Limit(observed.AngularVelocity, c.MaximumAngularSpeed);
        float yaw = Steering(steering, longitudinal, c.SteeringRate) * (grounded ? drifting ? 1.3f : 1 : 0.2f);
        angular.Y += (yaw - angular.Y) * (1 - MathF.Exp(-(grounded ? 9 : 1.5f) * dt));
        Vector3 upright = Vector3.Cross(up, grounded ? groundNormal : Vector3.UnitY);
        angular += upright * ((grounded ? 22 : 3) * dt);
        float damping = MathF.Exp(-(grounded ? 5 : 0.5f) * dt);
        angular.X *= damping;
        angular.Z *= damping;
        var physics = new VehiclePhysicsState(observed.Position, observed.Orientation, Limit(velocity, c.MaximumPhysicsSpeed), Limit(angular, c.MaximumAngularSpeed));
        State = new VehicleState(input.Tick, physics, grounded, drifting, driftTicks, boostTicks);
        return State;
    }

    /// <summary>Restores every movement memory field for replay/reconciliation without hidden timers.</summary>
    /// <param name="state">Validated saved snapshot.</param>
    public void Restore(VehicleState state)
    {
        _ = new VehicleState(state.Tick, state.Physics, state.Grounded, state.Drifting, state.DriftTicks, state.BoostTicks);
        if (state.DriftTicks > MathF.Ceiling(Configuration.DriftChargeSeconds * Configuration.TicksPerSecond) || state.BoostTicks > MathF.Ceiling(Configuration.BoostSeconds * Configuration.TicksPerSecond))
        {
            throw new ArgumentException("Snapshot timers exceed this vehicle's tuning.", nameof(state));
        }

        State = state;
    }
}
