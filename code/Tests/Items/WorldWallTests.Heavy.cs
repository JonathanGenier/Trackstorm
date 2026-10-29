using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

internal sealed partial class WorldWallTests
{
    [TestCase(3f)]
    [TestCase(40f)]
    public void HeavyDefaultSharesMomentumWithoutTipping(float speed)
    {
        var host = Start(); var wall = Deploy(host);
        Strike(host, wall, 0, speed, false);
        var pushed = host.Items.Shields.Single();
        Assert.That(pushed.WallMass, Is.EqualTo(6 * host.World.MovementTuning(1).Mass));
        Assert.That(-pushed.LinearVelocity.Z, Is.EqualTo(speed / 7).Within(.001f));
        Assert.That(pushed.Tipping, Is.False);
    }

    [TestCase(0f)]
    [TestCase(.7853982f)]
    [TestCase(1.4f)]
    public void VehicleShieldDamageMatchesSolidWallAndRetainsCooldown(float angle)
    {
        var host = Start(); var wall = Deploy(host);
        var velocity = new Vector3(MathF.Sin(angle), 0, -MathF.Cos(angle)) * 20;
        float expected = VehicleDamageMath.CollisionDamage(EnvironmentCollision.Severity(velocity, Vector3.UnitZ), host.World.DamageTuning(1));
        float hp = host.World.GetVehicle(1).Damage.CurrentHP;
        VehicleObservation Contact(VehicleSnapshot s) => new(s.ObservedPhysics, Vector3.UnitY,
            Enumerable.Repeat(new VehicleContact(velocity, Vector3.UnitZ, 0, 0, shield: wall.Id), 4));
        host.Step(default, Contact);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(hp - expected));
        for (int i = 0; i < 11; i++) { host.Step(default, Contact); }
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(hp - expected), "Repeated manifolds retain the existing victim cooldown");
        for (int i = 0; i < 60; i++)
        {
            host.Step(default, s => new(s.ObservedPhysics, Vector3.UnitY,
                [new(new(0, 0, -.5f), Vector3.UnitZ, 0, 0, shield: wall.Id)]));
        }
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(hp - expected), "Resting contact cannot tick damage after cooldown expires");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SimultaneousCarsUseCommonIncomingWallBoundary(bool opposite)
    {
        var host = Start(); var wall = Deploy(host); host.Join(42);
        host.Step(default, s =>
        {
            float sign = opposite && s.VehicleId == 2 ? -1 : 1;
            var pose = new VehiclePhysicsState(wall.Position + new Vector3(0, 0, sign * 8), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
            return new(pose, Vector3.UnitY, [new(new(0, 0, -sign * 20), Vector3.UnitZ * sign, 0, 0,
                localPosition: new(0, 0, -sign * 8), shield: wall.Id)]);
        }, observeShield: _ => new(new(wall.Position, wall.Orientation, Vector3.Zero, Vector3.Zero)));
        var pushed = host.Items.Shields.Single();
        float mass = host.World.MovementTuning(1).Mass;
        float momentum = pushed.WallMass * pushed.LinearVelocity.Z + host.World.State.Vehicles.Sum(v => mass * v.ObservedPhysics.LinearVelocity.Z);
        Assert.That(momentum, Is.EqualTo(opposite ? 0 : -40 * mass).Within(.05f));
        float energy = pushed.WallMass * pushed.LinearVelocity.LengthSquared() + host.World.State.Vehicles.Sum(v => mass * v.ObservedPhysics.LinearVelocity.LengthSquared());
        Assert.That(energy, Is.LessThan(2 * mass * 400));
        Assert.That(pushed.DamageSequence, Is.EqualTo(wall.DamageSequence + 1));
        Assert.That(host.World.State.Vehicles.All(v => v.Damage.CurrentHP < v.Damage.MaxHP), Is.True);
    }
}
