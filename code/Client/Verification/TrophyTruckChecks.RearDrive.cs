using Godot;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native stationary/moving RWD steering and grade-start observations.</summary>
public sealed partial class TrophyTruckChecks
{
    private async Task RearDrive(bool network)
    {
        foreach (int direction in new[] { -1, 1 })
        foreach (float entry in new[] { 0f, 16f })
        {
            await Setup(network, $"rwd-{direction}-{entry}", new(0, VehicleDimensions.RideHeight, 0), N.Quaternion.Identity, new(0, 0, -entry));
            _pilot = (tick, _) => new(tick, (short)(32767 * direction), ushort.MaxValue, 0, 0, 0, 0);
            // Allow the approved slower wheel buildup before asserting full available lock.
            await Frames(entry == 0 ? 480 : 120);
            var state = _world.GetVehicle(1);
            GD.Print($"{_case}: turn={_yawTravel:F3}, speed={state.Speed:F3}, yaw={state.Movement.Physics.AngularVelocity.Y:F3}, up={_minimumUp:F3}, travel={state.ObservedPhysics.Position.Length():F3}, wheel={state.Movement.SteeringAngle:F3}");
            Check(Math.Abs(state.Movement.SteeringAngle) > .89f && _minimumUp > .9f, _case + " full wheel range remains planted");
            if (entry == 0) { Check(Math.Abs(_yawTravel) > MathF.Tau * 2, _case + " sustains multiple RWD donuts from rest"); }
            _pilot = (tick, _) => new(tick, 0, 25000, 0, 0, 0, 0);
            await Frames(180);
            state = _world.GetVehicle(1);
            float side = N.Vector3.Dot(state.ObservedPhysics.LinearVelocity, N.Vector3.Transform(N.Vector3.UnitX, state.ObservedPhysics.Orientation));
            GD.Print($"{_case}: release side={side:F3}, yaw={state.Movement.Physics.AngularVelocity.Y:F3}, speed={state.Speed:F3}");
            Check(Math.Abs(side) < .5f && Math.Abs(state.Movement.Physics.AngularVelocity.Y) < .1f, _case + " releases into aligned forward travel");
            await Finish();
        }
        foreach (float grade in new[] { 30f, 40f })
        {
            float pitch = Mathf.DegToRad(grade);
            var orientation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, pitch);
            var normal = N.Vector3.Transform(N.Vector3.UnitY, orientation);
            await Setup(network, $"rwd-grade-{grade}", normal * VehicleDimensions.RideHeight, orientation);
            var road = _fixture.GetChildren().OfType<StaticBody3D>().Single(body => body is not Networking.NetworkVehicleBody);
            road.Quaternion = new Quaternion(Vector3.Right, pitch);
            road.Position = Vehicles.VehicleBody.ToGodot(normal * -.5f);
            _pilot = (tick, _) => new(tick, 0, ushort.MaxValue, 0, 0, 0, 0);
            await Frames(180);
            var state = _world.GetVehicle(1);
            float climb = N.Vector3.Dot(state.ObservedPhysics.LinearVelocity, N.Vector3.Transform(-N.Vector3.UnitZ, orientation));
            GD.Print($"{_case}: uphill speed={climb:F3}, height={state.ObservedPhysics.Position.Y:F3}");
            Check(climb > 3, _case + " accelerates uphill from rest using rear drive");
            await Finish();
        }
    }
    private async Task RepeatedContacts(bool network)
    {
        foreach (float mass in new[] { 1400f, 700f })
        {
            await Setup(network, $"repeated-contact-{mass}", new(0, VehicleDimensions.RideHeight, 4), N.Quaternion.Identity);
            AddCar(network, 2, new(0, VehicleDimensions.RideHeight, -4), N.Quaternion.Identity, N.Vector3.Zero, new() { Mass = mass });
            _pilot = (tick, id) => new(tick, 0, id == 1 ? ushort.MaxValue : (ushort)0, 0, 0, 0, 0);
            await Frames(360);
            var source = _world.GetVehicle(1).ObservedPhysics;
            var target = _world.GetVehicle(2).ObservedPhysics;
            float separation = N.Vector3.Distance(source.Position, target.Position);
            float vertical = _movementTrace.Max(s => Math.Abs(s.Physics.LinearVelocity.Y));
            float angular = _movementTrace.Max(s => s.Physics.AngularVelocity.Length());
            GD.Print($"{_case}: contact ticks={_pairContactTicks}, separation={separation:F3}, vertical={vertical:F3}, angular={angular:F3}, target travel={-target.Position.Z - 4:F3}");
            Check(_pairContactTicks >= 30, _case + " exercises sustained/repeated physical car contact");
            Check(target.Position.Z < -14 && separation > 2 && separation < 8, _case + " pushes the target without artificial separation");
            Check(vertical < 2 && angular < 2, _case + " avoids vertical/angular launch during sustained pushing");
            await Finish();
        }
    }
}
