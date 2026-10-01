using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class OvalIntegrationChecks
{
    private async Task VerifyBankSeam()
    {
        bool advancing = _advance;
        _advance = false;
        // Captured immediately before a 9.56 m/s upward velocity injection on the
        // production west bank. No spring step is needed to expose the sweep defect.
        var pose = new VehiclePhysicsState(new(-200.00433f, 7.565076f, 0.7168899f),
            N.Quaternion.Normalize(new(-0.2857047f, -0.95733744f, -0.038921863f, -0.019056885f)),
            new(-0.40083277f, 0.5464482f, 44.232426f), new(-0.00052719284f, 0.49911866f, 0.013618695f));
        var world = new Core.Simulation.Simulation(new(60));
        world.AddVehicle(999, new(), new(), pose);
        var body = new NetworkVehicleBody { VehicleId = 999 };
        AddChild(body);
        body.Apply(world.GetVehicle(999));
        await Frames(2);
        var observed = body.Observe(world.GetVehicle(999));
        Check(observed.Contacts.Any(c => c.Terrain), "Reproduced west-bank pose actually exercises chassis/road contact.");
        Check(observed.Contacts.Where(c => c.Terrain).All(c => N.Vector3.Dot(c.Normal, observed.Support) > 0.999f),
            "Bank chassis contact follows the locally sampled road face, not the triangle-edge separating axis.");
        Check(observed.Physics.LinearVelocity.Y - pose.LinearVelocity.Y < 1,
            $"Bank contact cannot convert tangential racing speed into launch: delta Y velocity {observed.Physics.LinearVelocity.Y - pose.LinearVelocity.Y:F4} m/s.");
        Check(new N.Vector2(observed.Physics.LinearVelocity.X, observed.Physics.LinearVelocity.Z).Length() > 43,
            "Correcting a road seam preserves tangential racing momentum.");
        body.QueueFree();
        await Frames(2);
        _advance = advancing;
    }
}
