using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

/// <summary>Normal hosted pickup reachability, including settings written before sustained Nitro.</summary>
[TestFixture]
internal sealed class HostedLootTests
{
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void HostedPickupsRetainBothConsumablesAcrossSettingsAndReplication(int schema)
    {
        string saved = schema < 0 ? string.Empty : $$"""
            {"schema":{{schema}}}
            {"key":"items.nitro_duration_ticks","value":300}
            {"key":"items.nitro_acceleration_multiplier","value":2}
            {"key":"spawns.nitro_weight","value":1}
            {"key":"spawns.category_consumable_weight","value":1}
            """;
        var file = DeveloperSettingsFile.Read(saved, GameplayConfiguration.HostedDefaults);
        Assert.That(file.RejectedRecords, Is.Zero);
        var tuning = DeveloperSettingsFile.Read(file.Write(file.Configuration), GameplayConfiguration.HostedDefaults).Configuration;
        Assert.That(ItemRegistry.Find(HeldItem.Nitro)!.Category, Is.EqualTo(ItemCategory.Consumable));
        Assert.That(tuning.Spawns.Weights[HeldItem.Nitro], Is.EqualTo(1));
        Assert.That(tuning.Spawns.Weights[HeldItem.Wrench], Is.EqualTo(1));
        Assert.That(tuning.Spawns.CategoryWeights[ItemCategory.Consumable], Is.EqualTo(1));
        var host = new HostVehicleSession(99, configuration: tuning);
        host.RegisterSpawns(PrototypeArena.Configuration);
        var counts = ItemRegistry.All.ToDictionary(i => i.Identity, _ => 0);
        int firstNitro = 0;
        for (int i = 0; i < 128; i++)
        {
            if (i == 32)
            {
                Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.nitro_forward_thrust"] = 19000 }, out _), Is.True);
                Assert.That(host.Spawns!.Configuration, Is.EqualTo(tuning.Spawns));
            }
            if (i == 64)
            {
                var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
                    new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, [], host.Spawns!.States,
                        balances: host.Spawns.Balances), host.World.State.Match!, null, host.Configuration)));
                host = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 1);
                Assert.That(host.Spawns!.Configuration, Is.EqualTo(tuning.Spawns));
            }
            // Clear inventory only between pairs: real grants must fill both physical slots.
            if (i % 2 == 0) { host.Items.RemovePlayer(1); }
            var marker = PrototypeArena.Configuration.Items[0];
            for (int step = 0; step < tuning.Spawns.CooldownTicks; step++)
            {
                host.Step(default, state => new(state.Movement.Physics, Vector3.UnitY));
            }
            var world = host.World.State;
            var vehicle = world.Vehicles.Single();
            ulong tick = world.Tick;
            var pose = new VehiclePhysicsState(marker.Position, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
            host.World.Restore(new(tick, world.LastInput,
                [new VehicleSnapshot(1, vehicle.LifeId, new VehicleState(tick, pose, false, false, 0, 0), vehicle.Damage, pose)], world.Match));
            host.Spawns!.Advance(host.World);
            Assert.That(host.Spawns.TryPickup(host.World, marker.Id, 1), Is.True);
            var claim = host.Spawns.States.Single(s => s.Id == marker.Id);
            counts[claim.Item]++;
            if (claim.Item == HeldItem.Nitro && firstNitro == 0) { firstNitro = i + 1; }
            host.Step(default, state => new(state.Movement.Physics, Vector3.UnitY));
            var publication = ItemCodec.DecodeState(ItemCodec.EncodeState(new(1, host.Snapshot(), host.Items.Slots,
                host.Items.Missiles, [], host.Spawns.States, balances: host.Spawns.Balances)));
            var granted = publication.Slots.Single();
            Assert.That(i % 2 == 0 ? granted.Item : granted.SecondItem, Is.EqualTo(claim.Item));
            if (claim.Item == HeldItem.Nitro)
            {
                Assert.That(i % 2 == 0 ? granted.NitroCharge : granted.SecondNitroCharge, Is.EqualTo(100));
            }
        }
        Assert.That(counts[HeldItem.Nitro], Is.Positive, "Nitro must be reachable through normal hosted loot.");
        Assert.That(counts[HeldItem.Wrench], Is.Positive);
        var balance = host.Spawns!.Balances.Single();
        Assert.That(balance.Counts[ItemCategory.Consumable], Is.EqualTo(32));
        Assert.That(balance.Counts[ItemCategory.Weapon], Is.EqualTo(64));
        Assert.That(balance.Counts[ItemCategory.Droppable], Is.EqualTo(32));
        TestContext.WriteLine($"Settings schema {schema}, first Nitro at pickup {firstNitro}, 128 authoritative pickups: {string.Join(", ", counts)}");
    }
}
