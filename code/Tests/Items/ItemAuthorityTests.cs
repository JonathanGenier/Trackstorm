using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Deterministic inventory, trajectory, radial effects and hostile request coverage.</summary>
[TestFixture]
internal sealed class ItemAuthorityTests
{
    /// <summary>Repair is configurable, clamps, and consumes even when no HP changes.</summary>
    /// <param name="hp">Starting health.</param>
    /// <param name="expected">Resulting health.</param>
    [TestCase(40, 75)]
    [TestCase(90, 100)]
    [TestCase(100, 100)]
    public void WrenchConsumesAndClamps(float hp, float expected)
    {
        var host = Host(hp);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Wrench), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 99, 1, slot.Token), Is.True);
        host.Step(default, Observe);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(expected));
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        Assert.That(host.Items.Events.Single().Item, Is.EqualTo(HeldItem.Wrench));
    }

    /// <summary>The network host accepts explicit immutable repair tuning.</summary>
    [Test]
    public void HostUsesConfiguredWrenchAmount()
    {
        var host = new HostVehicleSession(99, new ItemConfiguration { WrenchHeal = 7 });
        var state = host.World.GetVehicle(1);
        host.World.Restore(new SimulationState(0, default, new[] { new VehicleSnapshot(1, 1, state.Movement, new VehicleDamageState(100, 50, null, null), state.ObservedPhysics) }, host.World.State.Match));
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        host.UseItem(0, 99, 1, host.Items.Slots.Single().Token);
        host.Step(default, Observe);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(57));
    }

    /// <summary>No second slot, stale-token consumption, duplicate launch, or client-selected identity is possible.</summary>
    [Test]
    public void SlotCapabilitiesRejectInvalidAndRepeatedRequests()
    {
        var host = Host();
        host.Join(42);
        Assert.That(host.Items.Grant(host.World, 2, HeldItem.Missile), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.Items.Grant(host.World, 2, HeldItem.Wrench), Is.False);
        Assert.That(host.Items.Grant(host.World, 99, HeldItem.Wrench), Is.False);
        Assert.That(host.Items.Grant(host.World, 1, (HeldItem)255), Is.False);
        Assert.That(host.UseItem(99, 99, 1, slot.Token), Is.False);
        Assert.That(host.UseItem(42, 98, 1, slot.Token), Is.False);
        Assert.That(host.UseItem(42, 99, 2, slot.Token), Is.False);
        Assert.That(host.UseItem(42, 99, 1, slot.Token + 1), Is.False);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.Items.Events, Is.Empty);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.UseItem(42, 99, 1, slot.Token), Is.True);
        Assert.That(host.UseItem(42, 99, 1, slot.Token), Is.False);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles.Count, Is.EqualTo(1));
        Assert.That(host.UseItem(42, 99, 1, slot.Token), Is.False);
        Assert.That(host.Items.Grant(host.World, 2, HeldItem.Wrench), Is.True);
        Assert.That(host.UseItem(42, 99, 1, slot.Token), Is.False);
        Assert.That(host.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Wrench));
    }

    /// <summary>Destroyed and departed players cannot gain or consume items.</summary>
    [Test]
    public void DeadAndDepartedPlayersAreRejected()
    {
        var dead = Host(0);
        Assert.That(dead.Items.Grant(dead.World, 1, HeldItem.Wrench), Is.False);
        Assert.That(dead.UseItem(0, 99, 1, 1), Is.False);
        var host = Host();
        host.Join(42);
        host.Items.Grant(host.World, 2, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        host.UseItem(42, 99, 1, slot.Token);
        host.Leave(42);
        host.Step(default, Observe);
        Assert.That(host.Items.Slots, Is.Empty);
        Assert.That(host.Items.Missiles, Is.Empty);
    }

    /// <summary>Launch velocity uses forward only, never vehicle velocity or later steering; expiry does not explode.</summary>
    [Test]
    public void StraightTrajectoryAndLifetime()
    {
        var world = Host().World;
        var items = new ItemAuthority(new ItemConfiguration { MissileSpeed = 60, MissileLifetimeTicks = 3 });
        var state = world.GetVehicle(1);
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f), new Vector3(20, 0, 0), Vector3.Zero);
        world.Restore(new SimulationState(0, default, new[] { new VehicleSnapshot(1, 1, new VehicleState(0, pose, false, false, 0, 0), state.Damage, pose) }, world.State.Match));
        items.Grant(world, 1, HeldItem.Missile);
        items.RequestUse(world, 1, 1, items.Slots.Single().Token);
        Step(items, world);
        Vector3 velocity = Vector3.Transform(-Vector3.UnitZ, pose.Orientation) * 60;
        Assert.That(items.Missiles.Single().Velocity, Is.EqualTo(velocity));
        Assert.That(items.Missiles.Single().Position, Is.EqualTo(velocity / 60));
        Step(items, world);
        Assert.That(items.Missiles.Single().Position, Is.EqualTo(velocity / 30));
        Step(items, world);
        Assert.That(items.Missiles, Is.Empty);
        Assert.That(items.Events, Is.Empty);
    }

    /// <summary>Damage and impulse monotonically fade together and remain finite at/near the center.</summary>
    [Test]
    public void RadialFalloffAndSafeCenter()
    {
        var items = new ItemAuthority();
        float damage = float.MaxValue;
        float impulse = float.MaxValue;
        for (int i = 0; i <= 100; i++)
        {
            var effect = items.Explosion(Vector3.Zero, new Vector3(i / 10f, 0, 0));
            Assert.That(effect.Damage, Is.LessThanOrEqualTo(damage));
            Assert.That(effect.Impulse.Length(), Is.LessThanOrEqualTo(impulse + 0.01f));
            damage = effect.Damage;
            impulse = effect.Impulse.Length();
        }

        Assert.That(damage, Is.Zero);
        Assert.That(impulse, Is.Zero);
        Assert.That(items.Explosion(Vector3.Zero, Vector3.Zero).Damage, Is.EqualTo(55));
        Assert.That(items.Explosion(Vector3.Zero, Vector3.Zero).Impulse, Is.EqualTo(Vector3.UnitY * 15000));
        Assert.That(float.IsFinite(items.Explosion(Vector3.Zero, Vector3.One * 0.000001f).Impulse.Length()), Is.True);
        Assert.That(items.Explosion(Vector3.Zero, Vector3.UnitX).Impulse.X, Is.GreaterThan(0));
    }

    /// <summary>A swept collision explodes once; direct and nearby targets use precisely one radial effect.</summary>
    [Test]
    public void ImpactDamagesEveryTargetOnceAndSerializesImpulse()
    {
        var host = Host();
        host.Join(42);
        var states = host.World.State.Vehicles.Select(state =>
        {
            var pose = new VehiclePhysicsState(new Vector3(state.VehicleId == 1 ? 0 : 4, 0, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
            return new VehicleSnapshot(state.VehicleId, 1, new VehicleState(0, pose, false, false, 0, 0), state.Damage, pose);
        });
        host.World.Restore(new SimulationState(0, default, states, host.World.State.Match));
        host.Items.Grant(host.World, 1, HeldItem.Missile);
        host.UseItem(0, 99, 1, host.Items.Slots.Single().Token);
        host.Step(default, Observe, (_, _) => 0);
        Assert.That(host.Items.Events.Count(outcome => outcome.Impact), Is.EqualTo(1));
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(45));
        Assert.That(host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(72.5f));
        Assert.That(host.World.State.Vehicles.All(state => state.Effects.Count == 1), Is.True);
        var decoded = VehicleNetworkCodec.DecodeSnapshot(VehicleNetworkCodec.EncodeSnapshot(host.Snapshot()));
        Assert.That(decoded.Vehicles[0].State.Effects.Single(), Is.EqualTo(host.World.GetVehicle(1).Effects.Single()));
        host.Step(default, Observe);
        Assert.That(host.Items.Events, Is.Empty);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(45));
    }

    /// <summary>Bad native observations cannot partially consume inventory or emit effects.</summary>
    [Test]
    public void RejectedStepIsAtomic()
    {
        var host = Host();
        host.Items.Grant(host.World, 1, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        host.UseItem(0, 99, 1, slot.Token);
        Assert.Throws<ArgumentException>(() => host.Step(default, Observe, (_, _) => float.NaN));
        Assert.That(host.World.State.Tick, Is.Zero);
        Assert.That(host.Items.Slots.Single(), Is.EqualTo(slot));
        Assert.That(host.Items.Missiles, Is.Empty);
        host.Step(default, Observe);
        Assert.That(host.Items.Missiles.Count, Is.EqualTo(1));
    }

    /// <summary>Wire state preserves ownership/outcomes and rejects truncation/trailing bytes and invalid versions.</summary>
    [Test]
    public void ReliableCodecRoundTripAndMalformedPayloads()
    {
        var host = Host();
        host.Items.Grant(host.World, 1, HeldItem.Wrench);
        byte[] bytes = ItemCodec.EncodeState(new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, host.Items.Events));
        Assert.That(ItemCodec.DecodeState(bytes).Slots, Is.EqualTo(host.Items.Slots));
        for (int length = 0; length < bytes.Length; length++)
        {
            int truncated = length;
            Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes.AsSpan(0, truncated)));
        }

        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes.Concat(new byte[] { 0 }).ToArray()));
        bytes[2] = 99;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes));
        Assert.That(ItemCodec.DecodeUse(ItemCodec.EncodeUse(99, 1, 4)), Is.EqualTo((99ul, 1ul, 4ul)));
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeUse(new byte[28]));
        Assert.Throws<ArgumentException>(() => new ItemAuthority(new ItemConfiguration { MissileSpeed = float.NaN }));
    }

    /// <summary>The maximum simultaneous blast batch remains serializable for all eight players.</summary>
    [Test]
    public void ProjectileCapacityRetainsHeldItemsAndBoundsReliableOutcomes()
    {
        var host = Host();
        for (ulong peer = 1; peer <= 7; peer++)
        {
            host.Join(peer);
        }

        for (int volley = 0; volley < 3; volley++)
        {
            foreach (var state in host.World.State.Vehicles)
            {
                host.Items.Grant(host.World, state.VehicleId, HeldItem.Missile);
                var slot = host.Items.Slots.Single(slot => slot.Vehicle == state.VehicleId);
                host.Items.RequestUse(host.World, state.VehicleId, state.LifeId, slot.Token);
            }

            host.Step(default, _ => new VehicleObservation(new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY));
        }

        Assert.That(host.Items.Missiles.Count, Is.EqualTo(ItemAuthority.MaximumProjectiles));
        Assert.That(host.Items.Slots.All(slot => slot.Item == HeldItem.Missile), Is.True, "Capacity cannot consume unlaunched items.");
        host.Step(default, Observe, (_, _) => 0);
        Assert.That(host.Items.Events.Count, Is.EqualTo(ItemAuthority.MaximumProjectiles));
        byte[] bytes = ItemCodec.EncodeState(new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, host.Items.Events));
        Assert.That(ItemCodec.DecodeState(bytes).Events.Count, Is.EqualTo(ItemAuthority.MaximumProjectiles));
        Assert.That(bytes.Length, Is.LessThan(16384));
    }

    /// <summary>A new life cannot consume a capability retained from the previous life.</summary>
    [Test]
    public void ResetDiscardsPendingOldLifeUse()
    {
        var host = Host();
        host.Items.Grant(host.World, 1, HeldItem.Missile);
        var slot = host.Items.Slots.Single();
        host.UseItem(0, 99, 1, slot.Token);
        var input = new InputFrame(1, 0, 0, 0, 0, 0, 0);
        var pose = host.World.GetVehicle(1).Movement.Physics;
        host.Items.Step(host.World, input, new[] { new VehicleStepRequest(1, input, new VehicleObservation(pose, Vector3.UnitY), reset: pose) }, (_, _) => null);
        Assert.That(host.Items.Slots, Is.Empty);
        Assert.That(host.Items.Missiles, Is.Empty);
        Assert.That(host.UseItem(0, 99, 1, slot.Token), Is.False);
    }

    private static HostVehicleSession Host(float hp = 100)
    {
        var host = new HostVehicleSession(99);
        var state = host.World.GetVehicle(1);
        host.World.Restore(new SimulationState(0, default, new[] { new VehicleSnapshot(1, 1, state.Movement, new VehicleDamageState(100, hp, null, null), state.ObservedPhysics) }, host.World.State.Match));
        return host;
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(state.Movement.Physics, Vector3.UnitY);

    private static void Step(ItemAuthority items, Trackstorm.Core.Simulation.Simulation world)
    {
        var input = new InputFrame(world.State.Tick + 1, 0, 0, 0, 0, 0, 0);
        items.Step(world, input, world.State.Vehicles.Select(state => new VehicleStepRequest(state.VehicleId, input, Observe(state))).ToArray(), (_, _) => null);
    }
}
