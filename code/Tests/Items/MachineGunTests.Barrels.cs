using Trackstorm.Core.Development;
using Trackstorm.Core.Items;

namespace Trackstorm.Core.Tests.Items;

internal sealed partial class MachineGunTests
{
    [Test]
    public void BothBarrelsFireRealDamagingRoundsWithBalancedTracersAndUnchangedAggregateCadence()
    {
        var host = Create(new() { MachineGunSpread = 0 });
        Grant(host);
        var shots = new List<ItemEvent>();
        for (int tick = 0; tick < 60; tick++)
        {
            Step(host, true, (_, _, _) => new(.02f, 2));
            shots.AddRange(host.Items.Events);
        }
        Assert.That(shots.Select(s => s.MachineGunRound), Is.EqualTo(Enumerable.Range(0, 80)));
        Assert.That(shots.Select(s => s.MachineGunBarrel), Is.EqualTo(Enumerable.Range(0, 80).Select(i => i % 2)));
        foreach (int barrel in new[] { 0, 1 })
        {
            Assert.That(shots.Where(s => s.MachineGunBarrel == barrel).Select(s => s.Tracer),
                Is.EqualTo(Enumerable.Range(1, 40).Select(n => n % 3 == 0)));
            Assert.That(shots.Count(s => s.MachineGunBarrel == barrel && s.Impact), Is.EqualTo(40));
            Assert.That(shots.Count(s => s.MachineGunBarrel == barrel && s.Tracer), Is.EqualTo(13));
        }
        Assert.That(host.Items.Slots.Single().Ammo!.Remaining, Is.EqualTo(720));
        Assert.That(host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(820));
    }

    [Test]
    public void LiveAmmoCostChangesPreserveActualRoundOrderAcrossReleaseAndFinalPartialCost()
    {
        var host = Create(new() { MachineGunCapacity = 9, MachineGunFireRate = 60 });
        var slot = Grant(host);
        Step(host, true);
        Assert.That(host.Items.Events.Single().MachineGunRound, Is.Zero);
        Step(host);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["items.machine_gun_ammo_per_round"] = 3 }, out _), Is.True);
        Assert.That(host.UseItem(0, 99, slot.Life, slot.Token), Is.True);
        var remaining = new[] { 5, 2, 0 };
        for (int round = 1; round <= 3; round++)
        {
            Step(host, true);
            Assert.That(host.Items.Slots.Single().Ammo?.Remaining ?? 0, Is.EqualTo(remaining[round - 1]));
            var publication = new ItemPublication((ulong)round, host.Snapshot(), host.Items.Slots, [], host.Items.Events);
            var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(publication));
            Assert.That(decoded.Events.Single().MachineGunRound, Is.EqualTo(round));
            Assert.That(decoded.Events.Single().MachineGunBarrel, Is.EqualTo(round % 2));
            Assert.That(decoded.Slots, Is.EqualTo(publication.Slots));
            if (round < 3) { Assert.That(decoded.Slots.Single().Ammo!.RoundsFired, Is.EqualTo(round + 1)); }
        }
        Step(host, true);
        Assert.That(host.Items.Events, Is.Empty);
    }

    [TestCase(0)]
    [TestCase(11)]
    public void InvalidAmmoCostRejectsTheEntireConfiguration(int cost)
    {
        var host = Create();
        var before = host.Configuration;
        Assert.That(host.TryConfigure(0, new Dictionary<string, double>
            { ["items.machine_gun_ammo_per_round"] = cost, ["items.machine_gun_fire_rate"] = 30 }, out _), Is.False);
        Assert.That(host.Configuration, Is.EqualTo(before));
    }

    [Test]
    public void NewRoundIdentityRejectsOldWireAndImpossibleContinuation()
    {
        var host = Create();
        Grant(host); Step(host, true);
        var publication = new ItemPublication(1, host.Snapshot(), host.Items.Slots, [], host.Items.Events);
        var bytes = ItemCodec.EncodeState(publication);
        bytes[2] = 32;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeState(bytes));
        Assert.Throws<ArgumentException>(() => new MachineGunAmmo(799, 800) { RoundsFired = 2 }.Validate());
        Assert.Throws<ArgumentException>(() => new ItemPublication(1, host.Snapshot(), host.Items.Slots, [],
            [host.Items.Events.Single() with { MachineGunRound = 10000 }]));
        var config = GameplayConfigurationCodec.Encode(99, host.Configuration);
        config[2] = 55;
        Assert.Throws<ArgumentException>(() => GameplayConfigurationCodec.Decode(config));
    }
}
