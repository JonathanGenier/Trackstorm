using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Verification;

/// <summary>Production oval bank, drift and infield-transition scenarios using logical inputs only.</summary>
public sealed partial class OvalIntegrationChecks
{
    private async Task VerifyHandling()
    {
        int bank = Array.IndexOf(_centers, _centers.MaxBy(point => point.X));
        Vector3 center = _centers[bank];
        using var ray = PhysicsRayQueryParameters3D.Create(center + (Vector3.Up * 3), center + Vector3.Down);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        Vector3 normal = hit["normal"].AsVector3().Normalized();
        Vector3 tangent = (_centers[(bank + 1) % _centers.Length] - _centers[(bank - 1 + _centers.Length) % _centers.Length]).Normalized();
        Basis bankBasis = Basis.LookingAt(tangent, normal);
        foreach (bool network in new[] { false, true })
        {
            foreach (int direction in new[] { 1, -1 })
            {
                foreach ((float entry, short steering) in new (float, short)[] { (42, 700), (42, 1400), (42, 32767), (44.44f, 32767) })
                {
                    // End recovery before the faster exit reaches the opposite steep bank;
                    // separate probes below cover those terrain crossings.
                    var corner = await HandlingProbe(network, center + (normal * VehicleDimensions.RideHeight), Basis.LookingAt(tangent * direction, normal), tangent * (entry * direction), steering == short.MaxValue ? 180 : 60, tick => new InputFrame(tick,
                        steering == short.MaxValue && tick > 90 ? tick <= 108 ? (short)(7000 * direction) : (short)0 : (short)(-steering * direction),
                        ushort.MaxValue, 0, 0, 0, 0));
                    var samples = corner.Skip(2).Take(steering == short.MaxValue ? 88 : 58).ToArray();
                    if (steering == short.MaxValue)
                    {
                        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, $"bank-{network}-{direction}-{entry}.json"), System.Text.Json.JsonSerializer.Serialize(corner.Select(state => new {
                            state.Tick, state.CommandSpeed, state.SteeringAngle, state.Grounded, state.CrashSeconds,
                            position = new[] { state.Physics.Position.X, state.Physics.Position.Y, state.Physics.Position.Z }, surface = state.CurrentSurface.ToString(),
                            speed = state.Physics.LinearVelocity.Length(), angular = state.Physics.AngularVelocity.Length(),
                            up = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, state.Physics.Orientation).Y,
                            air = state.Air.Seconds
                        })));
                    }
                    float slipAngle = samples.Max(state =>
                    {
                        var forward = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, state.Physics.Orientation);
                        var side = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, state.Physics.Orientation);
                        return MathF.Abs(MathF.Atan2(System.Numerics.Vector3.Dot(state.Physics.LinearVelocity, side), System.Numerics.Vector3.Dot(state.Physics.LinearVelocity, forward)));
                    });
                    float yaw = samples.Max(state => state.Physics.AngularVelocity.Length());
                    float rearSlip = samples.Max(state => state.RearSlip);
                    bool fullInput = steering == short.MaxValue;
                    var exit = corner[^1];
                    float exitYaw = Math.Abs(System.Numerics.Vector3.Dot(exit.Physics.AngularVelocity, System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, exit.Physics.Orientation)));
                    var exitPose = new Transform3D(new Basis(VehicleBody.ToGodot(exit.Physics.Orientation)), VehicleBody.ToGodot(exit.Physics.Position));
                    Vector3 exitNormal = WheelSuspension.Observe(_vehicle, exitPose, _vehicle.Configuration).Normal;
                    Check(!exitNormal.IsZeroApprox(), "Corner recovery retains native wheel support");
                    Vector3 exitForward = -exitPose.Basis.Z;
                    exitForward = (exitForward - exitNormal * exitForward.Dot(exitNormal)).Normalized();
                    float exitSide = Math.Abs(VehicleBody.ToGodot(exit.Physics.LinearVelocity).Dot(exitForward.Cross(exitNormal).Normalized()));
                    float bodySide = Math.Abs(System.Numerics.Vector3.Dot(exit.Physics.LinearVelocity, System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, exit.Physics.Orientation)));
                    // Recovery yaw excludes pitch/roll needed to follow the sculpted infield.
                    // Lateral recovery uses the same support plane as Core tire forces;
                    // chassis roll on a changing grade is not lateral tire sliding.
                    // Terrain-normal stability is checked by the crossing probes below.
                    // Full lock deliberately exceeds available racing grip. Verify a supported,
                    // dissipative turn and recovery, with the speed-sensitive wheel range.
                    // The wheel filter retains rate-bounded range changes as the vehicle slows.
                    bool stable = fullInput
                        ? corner.Skip(2).All(state => state.Grounded && state.CrashSeconds == 0) && Math.Abs(samples[^1].SteeringAngle) > 0.2f && yaw < _vehicle.Configuration.MaximumAngularSpeed && samples[^1].CommandSpeed < entry && exit.CommandSpeed > 8 && exitSide < 1 && exitYaw < 0.2f
                        : slipAngle < 0.15f && yaw < 1.2f && rearSlip < 0.5f && samples.All(state => state.Grounded) && samples[^1].CommandSpeed > 40;
                    Check(stable,
                        $"{(network ? "Network" : "Practice")} {entry} m/s bank turn, direction {direction}, steering {steering}: slip angle {slipAngle:F3} rad, angular speed {yaw:F3} rad/s, rear slip {rearSlip:F3}, continuously supported, final speed {samples[^1].CommandSpeed:F3} m/s; recovery speed {exit.CommandSpeed:F3}, support-plane side {exitSide:F3} (body-axis {bodySide:F3}), yaw {exitYaw:F3}.");
                }
            }
        }

        Vector3 downhill = (Vector3.Down - (normal * Vector3.Down.Dot(normal))).Normalized();
        foreach (float speed in new[] { 6f, 12f, 20f })
        {
            var transition = await Probe(center + (normal * VehicleDimensions.RideHeight), Basis.LookingAt(downhill, normal), downhill * speed, 240, tick => new InputFrame(tick, 0, 0, 0, 0, 0, 0));
            VerifyTransition(transition.Skip(1).ToList(), $"Practice bank crossing at {speed} m/s");
        }

        // Four seconds reaches full speed while remaining inside the straight. The
        // trophy-truck drivetrain reaches the curved banking in the old 5.5-second window.
        var launch = await Probe(new Vector3(-60, VehicleDimensions.RideHeight, 91), new Basis(Vector3.Up, -Mathf.Pi / 2), Vector3.Zero, 240, tick => new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0));
        VerifyStraight(launch.Skip(1).ToList(), "Practice asphalt acceleration");
        _advance = false;
        _vehicle.CollisionLayer = 0;
        foreach (float speed in new[] { 6f, 12f, 20f })
        {
            var transition = await NetworkProbe(center + (normal * VehicleDimensions.RideHeight), Basis.LookingAt(downhill, normal), downhill * speed, 240, tick => new InputFrame(tick, 0, 0, 0, 0, 0, 0));
            VerifyTransition(transition, $"Network bank crossing at {speed} m/s");
        }

        var onlineLaunch = await NetworkProbe(new Vector3(-60, VehicleDimensions.RideHeight, 91), new Basis(Vector3.Up, -Mathf.Pi / 2), Vector3.Zero, 240, tick => new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0));
        VerifyStraight(onlineLaunch, "Network asphalt acceleration");
        _vehicle.CollisionLayer = 1;
        _advance = true;
        foreach (bool network in new[] { false, true })
        {
            Vector3 diagonal = (downhill + tangent).Normalized();
            var transition = await HandlingProbe(network, center + (normal * VehicleDimensions.RideHeight), Basis.LookingAt(diagonal, normal), diagonal * 16, 240, tick => new InputFrame(tick, 0, 0, 0, 0, 0, 0));
            VerifyTransition(transition.Skip(network ? 0 : 1).ToList(), $"{(network ? "Network" : "Practice")} diagonal bank crossing at 16 m/s");
        }

        var resting = await Probe(center + (normal * VehicleDimensions.RideHeight), bankBasis, Vector3.Zero, 180, tick => new InputFrame(tick, 0, 0, 0, 0, 0, 0));
        float descent = (VehicleBody.ToGodot(resting[^1].Physics.Position) - VehicleBody.ToGodot(resting[0].Physics.Position)).Dot(downhill);
        Check(normal.Y < 0.83f && descent > 0.5f && resting.Count(state => state.Grounded) > 170, $"Low-speed 35-degree banking descends {descent:F3} m in three seconds with stable support.");
        var crawling = await Probe(center + (normal * VehicleDimensions.RideHeight), bankBasis, Vector3.Zero, 120, tick => new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0));
        Check(crawling[^1].CommandSpeed > 4 && crawling.Count(state => state.Grounded) > 110, $"Banked standing start reaches {crawling[^1].CommandSpeed:F3} m/s without losing support.");

        Vector3 start = _centers[_start] + (Vector3.Up * VehicleDimensions.RideHeight);
        Basis straight = new(Vector3.Up, -Mathf.Pi / 2);
        var pulse = await Probe(start, straight, Vector3.Right * 16, 120, tick => new InputFrame(tick, tick <= 12 ? (short)12000 : tick <= 30 ? (short)-5000 : (short)0, tick > 12 ? ushort.MaxValue : (ushort)0, 0, tick <= 12 ? InputButtons.Drift : 0, 0, 0));
        float side = Math.Abs(System.Numerics.Vector3.Dot(pulse[^1].Physics.LinearVelocity, System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, pulse[^1].Physics.Orientation)));
        Check(pulse.Max(state => state.Handbrake) is > 0.15f and < 0.25f && pulse.Max(state => Math.Abs(state.Physics.AngularVelocity.Y)) > 0.2f && pulse.Max(state => Math.Abs(state.Physics.AngularVelocity.Y)) < 2 && side < 1 && pulse[^1].Handbrake == 0, $"Oval handbrake pulse/countersteer/throttle recovery: peak yaw {pulse.Max(state => Math.Abs(state.Physics.AngularVelocity.Y)):F3}, final side {side:F3} m/s.");
        var excessive = await Probe(start, straight, Vector3.Right * 26, 120, tick => new InputFrame(tick, short.MaxValue, 0, 0, InputButtons.Drift, 0, 0));
        float minimumForward = excessive.Min(state => (-new Basis(VehicleBody.ToGodot(state.Physics.Orientation)).Z).Dot(Vector3.Right));
        Check(minimumForward < 0 && excessive.Max(state => state.RearSlip) > 0.7f, $"Excessive held steering/handbrake permits a spin: minimum forward dot entry {minimumForward:F3}.");

        var crossing = await Probe(start, Basis.Identity, new Vector3(0, 0, -15), 150, tick => new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0));
        Check(crossing[^1].Physics.Position.Z < 75 && crossing[^1].Grounded && crossing.All(state => VehiclePhysicsState.IsFinite(state.Physics.Position)), $"Oval-to-infield crossing finishes supported at {crossing[^1].Physics.Position}; surface identity remains the authored Concrete baseline.");
        await VerifyBumpsAndCoasting();
    }

    private void VerifyStraight(List<VehicleState> states, string name)
    {
        float yaw = states.Max(state => Math.Abs(state.Physics.AngularVelocity.Y));
        float deviation = states.Max(state => Math.Abs(state.Physics.Position.Z - 91));
        Check(yaw < 0.02f && deviation < 0.3f && states[^1].CommandSpeed > 35 && states.All(state => state.Grounded), $"{name}: neutral steering, peak yaw {yaw:F4} rad/s, lateral deviation {deviation:F3} m, speed {states[^1].CommandSpeed:F3} m/s, continuously supported.");
    }

    private void VerifyTransition(List<VehicleState> states, string name)
    {
        // World-Y velocity includes legitimate travel up/down sculpted terrain.
        // Across a concave transition the center triangle is not the wheel support
        // plane. Measure separation from the same four supports as the suspension.
        float NormalSpeed(VehicleState state)
        {
            Vector3 position = VehicleBody.ToGodot(state.Physics.Position);
            var pose = new Transform3D(new Basis(VehicleBody.ToGodot(state.Physics.Orientation)), position);
            Vector3 normal = WheelSuspension.Observe(_vehicle, pose, _vehicle.Configuration).Normal;
            Check(!normal.IsZeroApprox(), name + ": terrain below wheel support samples.");
            return VehicleBody.ToGodot(state.Physics.LinearVelocity).Dot(normal);
        }

        float[] normalSpeeds = states.Select(NormalSpeed).ToArray();
        float rebound = normalSpeeds.Max();
        float angular = states.Max(state => state.Physics.AngularVelocity.Length());
        float settling = normalSpeeds.TakeLast(60).Max(Math.Abs);
        var peak = states[Array.IndexOf(normalSpeeds, rebound)];
        // Long travel bridges the bank/infield curvature while the chassis is still
        // descending. Bound relative separation energy to available static droop;
        // continuous support and final settling remain mandatory. Isolated bumps
        // and flat landings below separately enforce small chassis rise/rebound.
        float droop = _vehicle.Configuration.SuspensionLength - VehicleDimensions.RideHeight;
        float reboundLimit = MathF.Sqrt(2 * _vehicle.Configuration.Gravity * droop);
        Check(states.All(state => state.Grounded) && rebound < reboundLimit && angular < 3 && settling < 0.3f, $"{name}: terrain-normal separation {rebound:F3} m/s at {peak.Physics.Position}, velocity {peak.Physics.LinearVelocity}, compression {peak.Wheels.Compression}, angular {angular:F3} rad/s, unsupported {states.Count(state => !state.Grounded)}, final-second normal speed {settling:F4} m/s.");
    }

    private async Task<List<VehicleState>> Probe(Vector3 position, Basis basis, Vector3 velocity, int ticks, Func<ulong, InputFrame> source)
    {
        var states = new List<VehicleState>();
        ulong start = _tick;
        _vehicle.InputSource = tick =>
        {
            InputFrame input = source(tick - start);
            return new InputFrame(tick, input.Steering, input.Accelerate, input.Brake, input.Held, 0, 0);
        };
        void Observe(VehicleState state) => states.Add(state);
        _vehicle.Advanced += Observe;
        Quaternion rotation = basis.GetRotationQuaternion();
        _vehicle.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(position), new System.Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(velocity), System.Numerics.Vector3.Zero));
        await Frames(ticks);
        _vehicle.Advanced -= Observe;
        _vehicle.InputSource = null;
        return states;
    }

    private async Task<List<VehicleState>> NetworkProbe(Vector3 position, Basis basis, Vector3 velocity, int ticks, Func<ulong, InputFrame> source)
    {
        var proxy = new NetworkVehicleBody { VehicleId = 1 };
        AddChild(proxy);
        var simulation = new Core.Simulation.Simulation(new Core.Simulation.SimulationConfiguration(60));
        Quaternion rotation = basis.GetRotationQuaternion();
        simulation.AddVehicle(1, new(), new(), new VehiclePhysicsState(VehicleBody.ToCore(position), new System.Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(velocity), System.Numerics.Vector3.Zero));
        proxy.Apply(simulation.GetVehicle(1));
        await Frames(2);
        var states = new List<VehicleState>();
        for (ulong tick = 1; tick <= (ulong)ticks; tick++)
        {
            var observation = proxy.Observe(simulation.GetVehicle(1));
            var input = source(tick);
            simulation.Step(input, new[] { new VehicleStepRequest(1, input, observation) });
            proxy.Apply(simulation.GetVehicle(1));
            states.Add(simulation.GetVehicle(1).Movement);
        }

        proxy.QueueFree();
        await Frames(2);
        return states;
    }
}
