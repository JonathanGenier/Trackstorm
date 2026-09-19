using Godot;
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
        Vector3 downhill = (Vector3.Down - (normal * Vector3.Down.Dot(normal))).Normalized();
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
}
