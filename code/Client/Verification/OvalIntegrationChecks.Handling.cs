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
                foreach ((float entry, short steering) in new (float, short)[] { (42, 8000), (42, 16000), (42, 32767), (44.44f, 32767) })
                {
                    var corner = await HandlingProbe(network, center + (normal * VehicleDimensions.RideHeight), Basis.LookingAt(tangent * direction, normal), tangent * (entry * direction), 60, tick => new InputFrame(tick, (short)(-steering * direction), ushort.MaxValue, 0, 0, 0, 0));
                    var samples = corner.Skip(2).ToArray();
                    float slipAngle = samples.Max(state =>
                    {
                        var forward = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, state.Physics.Orientation);
                        var side = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, state.Physics.Orientation);
                        return MathF.Abs(MathF.Atan2(System.Numerics.Vector3.Dot(state.Physics.LinearVelocity, side), System.Numerics.Vector3.Dot(state.Physics.LinearVelocity, forward)));
                    });
                    float yaw = samples.Max(state => state.Physics.AngularVelocity.Length());
                    float rearSlip = samples.Max(state => state.RearSlip);
                    Check(slipAngle < 0.15f && yaw < 0.8f && rearSlip < 0.4f && samples.All(state => state.Grounded) && samples[^1].CommandSpeed > 40,
                        $"{(network ? "Network" : "Practice")} {entry} m/s bank turn, direction {direction}, steering {steering}: slip angle {slipAngle:F3} rad, angular speed {yaw:F3} rad/s, rear slip {rearSlip:F3}, continuously supported, final speed {samples[^1].CommandSpeed:F3} m/s.");
                }
            }
        }

        Vector3 downhill = (Vector3.Down - (normal * Vector3.Down.Dot(normal))).Normalized();
        foreach (float speed in new[] { 6f, 12f, 20f })
        {
            var transition = await Probe(center + (normal * VehicleDimensions.RideHeight), Basis.LookingAt(downhill, normal), downhill * speed, 240, tick => new InputFrame(tick, 0, 0, 0, 0, 0, 0));
            VerifyTransition(transition.Skip(1).ToList(), $"Practice bank crossing at {speed} m/s");
        }

        var launch = await Probe(new Vector3(-60, VehicleDimensions.RideHeight, 91), new Basis(Vector3.Up, -Mathf.Pi / 2), Vector3.Zero, 330, tick => new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0));
        VerifyStraight(launch.Skip(1).ToList(), "Practice asphalt acceleration");
        _advance = false;
        _vehicle.CollisionLayer = 0;
        foreach (float speed in new[] { 6f, 12f, 20f })
        {
            var transition = await NetworkProbe(center + (normal * VehicleDimensions.RideHeight), Basis.LookingAt(downhill, normal), downhill * speed, 240, tick => new InputFrame(tick, 0, 0, 0, 0, 0, 0));
            VerifyTransition(transition, $"Network bank crossing at {speed} m/s");
        }

        var onlineLaunch = await NetworkProbe(new Vector3(-60, VehicleDimensions.RideHeight, 91), new Basis(Vector3.Up, -Mathf.Pi / 2), Vector3.Zero, 330, tick => new InputFrame(tick, 0, ushort.MaxValue, 0, 0, 0, 0));
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
        Check(pulse.Max(state => state.RearSlip) > 0.3f && pulse.Max(state => Math.Abs(state.Physics.AngularVelocity.Y)) > 0.2f && pulse.Max(state => Math.Abs(state.Physics.AngularVelocity.Y)) < 2 && side < 1 && pulse[^1].Handbrake == 0, $"Oval handbrake pulse/countersteer/throttle recovery: peak yaw {pulse.Max(state => Math.Abs(state.Physics.AngularVelocity.Y)):F3}, final side {side:F3} m/s.");
        var excessive = await Probe(start, straight, Vector3.Right * 26, 120, tick => new InputFrame(tick, short.MaxValue, 0, 0, InputButtons.Drift, 0, 0));
        Vector3 finalForward = -new Basis(VehicleBody.ToGodot(excessive[^1].Physics.Orientation)).Z;
        Check(finalForward.Dot(Vector3.Right) < 0 && excessive.Max(state => state.RearSlip) > 0.7f, $"Excessive held steering/handbrake permits a spin: final forward dot entry {finalForward.Dot(Vector3.Right):F3}.");

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
        // Measure separation speed normal to the actual support surface instead.
        float NormalSpeed(VehicleState state)
        {
            Vector3 position = VehicleBody.ToGodot(state.Physics.Position);
            using var query = PhysicsRayQueryParameters3D.Create(position, position + Vector3.Down * 5);
            query.Exclude = new Godot.Collections.Array<Rid> { _vehicle.GetRid() };
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            Check(hit.Count > 0, name + ": terrain below transition sample.");
            return VehicleBody.ToGodot(state.Physics.LinearVelocity).Dot(hit["normal"].AsVector3());
        }

        float[] normalSpeeds = states.Select(NormalSpeed).ToArray();
        float rebound = normalSpeeds.Max();
        float angular = states.Max(state => state.Physics.AngularVelocity.Length());
        float settling = normalSpeeds.TakeLast(60).Max(Math.Abs);
        // A 1.5 m/s separation impulse corresponds to <12 cm free rebound under
        // gravity; continuous support is still required on every sampled frame.
        Check(states.All(state => state.Grounded) && rebound < 1.5f && angular < 3 && settling < 0.3f, $"{name}: terrain-normal rebound {rebound:F3} m/s, angular {angular:F3} rad/s, unsupported {states.Count(state => !state.Grounded)}, final-second normal speed {settling:F4} m/s.");
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
