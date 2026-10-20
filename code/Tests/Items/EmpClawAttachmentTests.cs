using System.Numerics;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class EmpClawAttachmentTests
{
    private HostVehicleSession _host = null!;
    private VehiclePhysicsState _victim;
    private WeaponRayHit? _hit;

    [SetUp]
    public void Setup()
    {
        _host = new(298, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        _host.Join(2);
        _victim = new(new(0, 10, -100), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        _hit = null;
        Step(2);
    }

    private void Step(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var slot = _host.Items.Slots.FirstOrDefault(s => s.Vehicle == 1)?.Active;
            if (slot is { Item: HeldItem.EmpClaw })
            { _host.Items.RequestAim(_host.World, 1, slot.Life, slot.Token, 0, _host.World.State.Tick + 1, -Vector3.UnitZ); }
            _host.Step(default, v => new VehicleObservation(v.VehicleId == 2 ? _victim :
                new(new(0, 10, 0), Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY),
                raycastWeapon: (_, _, _) => _hit);
        }
    }

    private EmpClawAttachment Attach(Vector3? local = null, Quaternion? rotation = null)
    {
        _hit = null;
        _victim = new(new(0, 10, -100), Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        if (_host.Items.Slots.FirstOrDefault(s => s.Vehicle == 1)?.Active.Item != HeldItem.EmpClaw)
        { Assert.That(_host.Items.Grant(_host.World, 1, HeldItem.EmpClaw), Is.True); }
        Step(100);
        var slot = _host.Items.Slots.Single(s => s.Vehicle == 1).Active;
        Assert.That(_host.Items.RequestUse(_host.World, 1, slot.Life, slot.Token), Is.True);
        Step();
        var shot = _host.Items.Missiles.Single();
        // Zero guidance response is unavailable; a nearby target on the committed direction
        // keeps this short collision segment straight while testing arbitrary local body faces.
        var orientation = rotation ?? Quaternion.Identity;
        var point = local ?? new Vector3(0, 0, 2);
        float speed = Math.Max(_host.Items.Configuration.EmpClaw.CruiseSpeed,
            shot.Velocity.Length() - _host.Items.Configuration.EmpClaw.Deceleration / 60);
        Vector3 hit = shot.Position - Vector3.UnitZ * (speed / 120);
        _victim = new(hit - Vector3.Transform(point, orientation), orientation, Vector3.Zero, Vector3.Zero);
        _hit = new(.5f, 2) { Normal = Vector3.Transform(Vector3.UnitZ, orientation) };
        Step(); _hit = null;
        return _host.Items.EmpAttachments.Single(c => c.Id == shot.Id);
    }

    [TestCase(0, 0, 2)]
    [TestCase(0, 0, -2)]
    [TestCase(1, 0, 0)]
    [TestCase(-1, 0, 0)]
    [TestCase(0, 1, 0)]
    [TestCase(0, -1, 0)]
    public void EveryFaceKeepsActualHitInRotatingVehicleFrame(float x, float y, float z)
    {
        var claw = Attach(new(x, y, z), Quaternion.CreateFromYawPitchRoll(.8f, .5f, 2.7f));
        var contact = _host.Items.Events.Single(e => e.Impact);
        Assert.That(claw.LocalPosition, Is.EqualTo(contact.EmpClawContact!.LocalPosition));
        Assert.That(Vector3.Distance(Vector3.Transform(claw.LocalPosition, _victim.Orientation) + _victim.Position,
            contact.Position), Is.LessThan(.00001));
        Assert.That(claw.LocalNormal, Is.EqualTo(contact.EmpClawContact.LocalNormal));
        var before = claw;
        _victim = new(new(30, 20, -20), Quaternion.CreateFromYawPitchRoll(2, 1, 3), Vector3.Zero, Vector3.Zero);
        Step(5);
        Assert.That(_host.Items.EmpAttachments.Single(), Is.EqualTo(before));
        Assert.That(_host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(_host.World.GetVehicle(2).Damage.MaxHP));
    }

    [Test]
    public void ThreeClawsHaveIndependentDeadlinesAndOneCappedPenalty()
    {
        var first = Attach(); var second = Attach(); var third = Attach();
        Assert.That(_host.Items.EmpAttachments, Has.Count.EqualTo(3));
        Assert.That(new[] { first.ExpiresAtTick, second.ExpiresAtTick, third.ExpiresAtTick }, Is.Ordered.And.Unique);
        Assert.That(first.ExpiresAtTick - first.AttachedAtTick, Is.EqualTo(1800));
        foreach (var claw in new[] { first, second, third })
        {
            Step((int)(claw.ExpiresAtTick - _host.World.State.Tick - 1));
            Assert.That(_host.Items.EmpAttachments.Any(c => c.Id == claw.Id), Is.True);
            Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(.2f));
            Step();
            Assert.That(_host.Items.EmpAttachments.Any(c => c.Id == claw.Id), Is.False);
            Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(claw == third ? 1f : .2f));
        }
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.Items.Events, Is.Empty, "Expiry never produces an explosion or damage event.");
    }

    [TestCase(0UL)]
    [TestCase(2UL)]
    public void ExactEmpSweepSupersedesTheCoarseHullAndPreservesWorldOcclusion(ulong struck)
    {
        Assert.That(_host.Items.Grant(_host.World, 1, HeldItem.EmpClaw), Is.True);
        Step(100);
        var slot = _host.Items.Slots.Single(s => s.Vehicle == 1).Active;
        Assert.That(_host.Items.RequestUse(_host.World, 1, slot.Life, slot.Token), Is.True);
        Step();
        var shot = _host.Items.Missiles.Single();
        _victim = new(shot.Position - Vector3.UnitZ * 3, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        VehicleObservation Observe(VehicleSnapshot v) => new(v.VehicleId == 2 ? _victim : v.Movement.Physics, Vector3.UnitY);
        _host.Step(default, Observe, collide: (_, _) => .1f, raycastWeapon: (_, _, _) => new(.1f, 2), raycastEmpClaw: (_, _, _, _) => null);
        Assert.That(_host.Items.Missiles.Single().Id, Is.EqualTo(shot.Id), "Empty hull space cannot terminate the flight.");
        _host.Step(default, Observe, collide: (_, _) => .1f, raycastWeapon: (_, _, _) => new(.1f, 2),
            raycastEmpClaw: (_, _, _, _) => new(.5f, struck) { Normal = Vector3.UnitZ });
        Assert.That(_host.Items.Missiles, Is.Empty);
        Assert.That(_host.Items.EmpAttachments.Count, Is.EqualTo(struck == 0 ? 0 : 1));
        Assert.That(_host.Items.Events.Single(e => e.Impact).EmpClawContact?.Vehicle ?? 0, Is.EqualTo(struck));
    }

    [Test]
    public void RemovingOneSpecificClawAndExpiryRaceLeaveOthersIntact()
    {
        var first = Attach(); var second = Attach(); var third = Attach();
        Assert.That(_host.Items.RemoveEmpAttachment(second.Id), Is.True);
        Assert.That(_host.Items.RemoveEmpAttachment(second.Id), Is.False);
        Step();
        Assert.That(_host.Items.EmpAttachments.Select(c => c.Id), Is.EquivalentTo(new[] { first.Id, third.Id }));
        Assert.That(_host.World.GetVehicle(2).Movement.EmpUntilTick, Is.EqualTo(third.ExpiresAtTick));
        Step((int)(first.ExpiresAtTick - _host.World.State.Tick - 1));
        Assert.That(_host.Items.RemoveEmpAttachment(first.Id), Is.True);
        Assert.That(_host.Items.RemoveEmpAttachment(third.Id), Is.True);
        Step();
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(1));
    }

    [Test]
    public void CodecRecoveryAndFreshJoinPreserveIndividualRemainingLifetimes()
    {
        var first = Attach(); var second = Attach(); Step(45);
        var state = new ItemPublication(Math.Max(1, _host.Items.Revision), _host.Snapshot(), _host.Items.Slots,
            _host.Items.Missiles, [], empAttachments: _host.Items.EmpAttachments);
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(state));
        Assert.That(decoded.EmpAttachments, Is.EqualTo(state.EmpAttachments));
        Assert.That(decoded.World.Vehicles.Single(v => v.State.VehicleId == 2).State.Movement.EmpUntilTick, Is.EqualTo(second.ExpiresAtTick));
        var join = _host.PrepareJoin(99, _host.Items.Revision + 1);
        Assert.That(join, Is.Not.Null);
        Assert.That(ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(join!)).Items.EmpAttachments, Is.EqualTo(state.EmpAttachments));
        var checkpoint = new ResumeCheckpoint(decoded, _host.World.State.Match!, null, _host.Configuration);
        _host = HostVehicleSession.Restore(ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint)), _host.CaptureAuthority(), 1);
        Step((int)(first.ExpiresAtTick - _host.World.State.Tick));
        Assert.That(_host.Items.EmpAttachments.Single(), Is.EqualTo(second));
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(.2f));
    }

    [Test]
    public void NewLifeAndDepartureClearAttachmentsAndCannotDebuffRespawn()
    {
        Attach();
        var current = _host.World.State;
        _host.World.Restore(new SimulationState(current.Tick, current.LastInput, current.Vehicles.Select(v => v.VehicleId == 2
            ? new VehicleSnapshot(2, v.LifeId + 1, new VehicleState(current.Tick, _victim, false, false, 0, 0), v.Damage, _victim) : v), current.Match));
        Step();
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(1));
        Attach(); _host.Items.RemovePlayer(2); Step();
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(1));
    }

    [Test]
    public void MalformedOrDuplicateAttachmentCannotEnterPublication()
    {
        var claw = Attach();
        ItemPublication Publish(params EmpClawAttachment[] claws) => new(1, _host.Snapshot(), _host.Items.Slots, [], [], empAttachments: claws);
        Assert.Throws<ArgumentException>(() => Publish(claw, claw));
        Assert.Throws<ArgumentException>(() => Publish(claw with { ExpiresAtTick = claw.ExpiresAtTick + 1 }));
        Assert.Throws<ArgumentException>(() => Publish(claw with { LocalNormal = Vector3.Zero }));
        Assert.Throws<ArgumentException>(() => Publish(claw with { LocalPosition = new(float.NaN, 0, 0) }));
        Assert.Throws<ArgumentException>(() => Publish(claw with { Life = claw.Life + 1 }));
    }

    [Test]
    public void LethalBatchAndRespawnRemoveEveryClawWithoutLeavingPowerState()
    {
        Attach(); Attach(); Attach();
        ulong life = _host.World.GetVehicle(2).LifeId;
        ulong tick = _host.World.State.Tick + 1;
        var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        var requests = _host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, input,
            new(v.ObservedPhysics, Vector3.UnitY), effects: v.VehicleId == 2
                ? [new(new(10000, Vector3.Zero, Vector3.Zero), new("missile", 1, "lethal-fixture"))] : [])).ToArray();
        _host.Items.Step(_host.World, input, requests, (_, _) => null);
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.World.GetVehicle(2).CanInteract, Is.False);
        Assert.That(_host.World.GetVehicle(2).Movement.EmpUntilTick, Is.Zero);
        Step(181);
        Assert.That(_host.World.GetVehicle(2).LifeId, Is.GreaterThan(life));
        Assert.That(_host.World.GetVehicle(2).Movement.EnginePowerScale, Is.EqualTo(1));
    }

    [Test]
    public void FailedWorldBatchDoesNotCommitPendingSpecificRemoval()
    {
        var claw = Attach();
        Assert.That(_host.Items.RemoveEmpAttachment(claw.Id), Is.True);
        var before = _host.World.State;
        ulong tick = before.Tick + 1;
        var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        var requests = before.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId,
            v.VehicleId == 2 ? new(tick + 1, 0, 0, 0, 0, 0, 0) : input, new(v.ObservedPhysics, Vector3.UnitY))).ToArray();
        Assert.Throws<ArgumentException>(() => _host.Items.Step(_host.World, input, requests, (_, _) => null));
        Assert.That(_host.World.State.Tick, Is.EqualTo(before.Tick));
        Assert.That(_host.World.State.Vehicles, Is.EqualTo(before.Vehicles));
        Assert.That(_host.Items.EmpAttachments.Single(), Is.EqualTo(claw));
        Step();
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.World.GetVehicle(2).Movement.EmpUntilTick, Is.Zero);
    }

    [Test]
    public void TransportSuspensionKeepsOriginalCountdownAndFinalDepartureCleansUp()
    {
        var claw = Attach();
        _host.Suspend(2);
        Step(300);
        Assert.That(_host.Items.EmpAttachments.Single(), Is.EqualTo(claw));
        Assert.That(_host.ResumePlayer(22, 2), Is.True);
        Step();
        Assert.That(_host.Items.EmpAttachments.Single().ExpiresAtTick, Is.EqualTo(claw.ExpiresAtTick));
        _host.Leave(22);
        Assert.That(_host.Items.EmpAttachments, Is.Empty);
        Assert.That(_host.World.State.Vehicles.Any(v => v.VehicleId == 2), Is.False);
    }
}
