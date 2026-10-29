using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Sessions;

[TestFixture]
internal sealed class FourCarPracticeTests
{
    [Test]
    public void ThirdCarStartsOneHundredRouteMetresBehindSecondAcrossLapSeam()
    {
        var first = PracticeVehicleInput.InitialPose(true, 0, 1.3f);
        var second = PracticeVehicleInput.InitialPose(true, 1, 1.3f);
        var third = PracticeVehicleInput.InitialPose(true, 2, 1.3f);
        Assert.That(first.Position, Is.EqualTo(new Vector3(-7, 1.3f, 91)));
        Assert.That(second.Position, Is.EqualTo(new Vector3(-17, 1.3f, 91)));
        float remainingArc = 91 * MathF.Atan2(-107 - third.Position.X, third.Position.Z);
        Assert.That(90 + remainingArc, Is.EqualTo(100).Within(.001f));
        Assert.That(Vector3.Transform(-Vector3.UnitZ, third.Orientation).Z, Is.GreaterThan(0));
    }

    [Test]
    public void MapSwitchPreservesPairAndReconcilesExtraTargetsWithoutReusingIds()
    {
        var lobby = new LobbyAuthority(100, "Host");
        lobby.AddPracticeCar();
        var original = lobby.State.Players.Where(p => p.PracticeCar).ToArray();
        Assert.That(lobby.SelectMap(0, MatchMap.OldMap), Is.True);
        Assert.That(lobby.State.Players.Where(p => p.PracticeCar), Is.EqualTo(original.Take(2)));
        Assert.That(lobby.SelectMap(0, MatchMap.NewMap), Is.True);
        lobby.AddPracticeCar();
        Assert.That(lobby.State.Players.Count, Is.EqualTo(5));
        Assert.That(lobby.State.Players.Where(p => p.PracticeCar).Take(2), Is.EqualTo(original.Take(2)));
        Assert.That(lobby.State.Players.Last().Id, Is.GreaterThan(original.Last().Id));
        Assert.That(lobby.SelectMap(0, MatchMap.OldMap), Is.True);
        for (ulong peer = 10; peer < 15; peer++) { lobby.Join(peer, GameVersion.Current.ToString(), "Human"); }
        Assert.That(lobby.SelectMap(0, MatchMap.NewMap), Is.False);
        Assert.That(lobby.State.Map, Is.EqualTo(MatchMap.OldMap));
    }

    [Test]
    public void ParkedCarAcceptsDamageAndWaitsForClearanceBeforeRespawningAtAnchor()
    {
        var prototype = PrototypeArena.Configuration;
        var map = new ArenaConfiguration(new(-500), new(500), prototype.Players,
            [new ArenaSpawn("item-infield-04-water", new(77, .101f, -43), 0)], prototype.Surfaces);
        var host = new HostVehicleSession(100, arena: map, respawnConfiguration: new() { DelayTicks = 2 });
        for (ulong id = 2; id <= 5; id++) { host.DrivePracticeCar(id, true); }
        var pose = host.World.GetVehicle(5).Movement.Physics;
        void Damage(float amount)
        {
            ulong tick = host.World.State.Tick + 1;
            var input = new Trackstorm.Core.Input.InputFrame(tick, 0, 0, 0, 0, 0, 0);
            host.World.Step(input, host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, input,
                new(v.Movement.Physics, Vector3.UnitY), v.VehicleId == 5
                    ? [new VehicleEffectRequest(new(amount, Vector3.One * 20000, Vector3.One), new("test-weapon", 1, "target"))] : [])).ToArray());
        }
        Damage(10);
        Assert.That(host.World.GetVehicle(5).Damage.CurrentHP, Is.EqualTo(host.World.GetVehicle(5).Damage.MaxHP - 10));
        Assert.That(host.World.GetVehicle(5).Movement.Physics, Is.EqualTo(pose));
        Assert.That(host.World.GetVehicle(5).Effects, Is.Empty);
        Damage(host.World.GetVehicle(5).Damage.MaxHP);
        Assert.That(host.World.GetVehicle(5).CanInteract, Is.False);
        var world = host.World.State;
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId == 1
            ? new VehicleSnapshot(1, v.LifeId, new(world.Tick, pose, false, false, 0, 0), v.Damage, pose) : v), world.Match));
        for (int step = 0; step < 3; step++) { host.Step(default, s => new(s.Movement.Physics, Vector3.UnitY)); }
        Assert.That(host.World.GetVehicle(5).CanInteract, Is.False);
        world = host.World.State;
        var away = map.Spawn(0);
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId == 1
            ? new VehicleSnapshot(1, v.LifeId, new(world.Tick, away, false, false, 0, 0), v.Damage, away) : v), world.Match));
        host.Step(default, s => new(s.Movement.Physics, Vector3.UnitY));
        Assert.That(host.World.GetVehicle(5).CanInteract, Is.True);
        Assert.That(host.World.GetVehicle(5).LifeId, Is.EqualTo(2));
        Assert.That(host.World.GetVehicle(5).Movement.Physics, Is.EqualTo(pose));
    }

    [Test]
    public void ParkedCarRemainsFixedAndCollectsOncePerCooldownAcrossAuthorityRestore()
    {
        var prototype = PrototypeArena.Configuration;
        var map = new ArenaConfiguration(new(-500), new(500), prototype.Players,
            [new ArenaSpawn("item-infield-04-water", new(77, .101f, -43), MathF.PI / 2)], prototype.Surfaces);
        var host = new HostVehicleSession(100, arena: map);
        host.RegisterSpawns(map);
        Assert.That(host.TryConfigure(0, new Dictionary<string, double> { ["spawns.cooldown_ticks"] = 3 }, out _), Is.True);
        for (ulong id = 2; id <= 5; id++) { host.DrivePracticeCar(id, true); host.DrivePracticeCar(id, true); }
        Assert.Throws<InvalidOperationException>(() => host.DrivePracticeCar(6, true));
        Assert.That(host.ResumePlayer(20, 5), Is.False);
        var pose = host.World.GetVehicle(5).Movement.Physics;
        for (int cycle = 0; cycle < 12; cycle++)
        {
            if (cycle == 6)
            {
                var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(
                    new ItemPublication(1, host.Snapshot(), host.Items.Slots, host.Items.Missiles, [], host.Spawns!.States,
                        balances: host.Spawns.Balances, discardRevision: host.Items.DiscardRevision), host.World.State.Match!, null, host.Configuration)));
                host = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 1, arena: map);
                for (ulong id = 2; id <= 5; id++) { host.DrivePracticeCar(id, true); }
            }
            for (int tick = 0; tick < 3; tick++)
            {
                host.Step(default, state => new(new(state.Movement.Physics.Position + Vector3.One * .1f,
                    state.Movement.Physics.Orientation, Vector3.One * 10, Vector3.One), Vector3.UnitY));
                host.CollectPickups(); host.CollectPickups();
                Assert.That(host.World.GetVehicle(5).Movement.Physics, Is.EqualTo(pose));
                Assert.That(host.World.GetVehicle(5).ObservedPhysics, Is.EqualTo(pose));
            }
            Assert.That(host.Spawns!.Balances.Single(b => b.Player == 5).Total, Is.EqualTo(cycle + 1));
            var slot = host.Items.Slots.Single(s => s.Vehicle == 5);
            Assert.That(host.Items.Discard(host.World, 5, slot.Life, slot.Active.Token, slot.SelectionRevision), Is.True);
        }
    }
}
