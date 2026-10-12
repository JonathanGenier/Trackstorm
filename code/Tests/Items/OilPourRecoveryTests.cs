using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class OilPourRecoveryTests
{
    [Test]
    public void InFlightCheckpointContinuesWithoutReplayingUseOrRestartingDeadline()
    {
        var host = new HostVehicleSession(245);
        host.JoinPlayer(10, 2);
        Assert.That(host.Items.Grant(host.World, 1, HeldItem.Oil), Is.True);
        var slot = host.Items.Slots.Single();
        Assert.That(host.UseItem(0, 245, slot.Life, slot.Token), Is.True);
        OilPatch Place(ItemSlot s, VehiclePhysicsState pose) => new(s.Token, s.Vehicle, pose.Position, Vector3.UnitY, .65f)
        { Pour = new() { Life = s.Life } };
        for (int i = 0; i < 75; i++) { host.Step(default, Observe, placeOil: Place, oilCollision: Ground); }
        var patch = host.Items.Patches.Single();
        Assert.That(patch.Pour!.Drops.Length, Is.GreaterThan(0));
        var boundary = new ItemPublication(host.Items.Revision, host.Snapshot(), host.Items.Slots, [], [], patches: host.Items.Patches);
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(boundary));
        Assert.That(decoded.Patches, Is.EqualTo(boundary.Patches));
        var checkpoint = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(new(boundary, host.World.State.Match!, null, host.Configuration)));
        var restored = HostVehicleSession.Restore(checkpoint, host.CaptureAuthority(), 2);
        for (int i = 0; i < 220; i++)
        {
            host.Step(default, Observe, oilCollision: Ground); restored.Step(default, Observe, oilCollision: Ground);
            Assert.That(restored.Items.Patches, Is.EqualTo(host.Items.Patches));
        }
        Assert.That(restored.Items.Patches.Single().ExpiresAtTick, Is.EqualTo(patch.ExpiresAtTick));
        Assert.That(restored.Items.Patches.Single().Pour!.Emitted, Is.EqualTo(40));
        Assert.That(restored.Items.Patches.Single().Pour!.Drops.Length, Is.Zero);
        Assert.That(restored.Items.Slots.Single().Item, Is.EqualTo(HeldItem.None));
        for (int i = 0; restored.World.State.Tick < patch.ExpiresAtTick; i++) { restored.Step(default, Observe); }
        Assert.That(restored.Items.Patches, Is.Empty);
    }

    [Test]
    public void ASecondActivationDuringPourRetainsItsCapabilityAndCannotCreateAnotherEmitter()
    {
        var host = new HostVehicleSession(245);
        OilPatch Place(ItemSlot s, VehiclePhysicsState pose) => new(s.Token,s.Vehicle,pose.Position,Vector3.UnitY,.65f) { Pour = new() { Life = s.Life } };
        for (int i = 0; i < 2; i++)
        {
            Assert.That(host.Items.Grant(host.World,1,HeldItem.Oil),Is.True);
            var slot=host.Items.Slots.Single();
            Assert.That(host.UseItem(0,245,slot.Life,slot.Active.Token),Is.True);
            host.Step(default,Observe,placeOil:Place);
        }
        Assert.That(host.Items.Patches.Count,Is.EqualTo(1));
        Assert.That(host.Items.Slots.Single().Active.Item,Is.EqualTo(HeldItem.Oil));
    }

    private static VehicleObservation Observe(VehicleSnapshot state) => new(new(new(0,10,0),Quaternion.Identity,Vector3.Zero,Vector3.Zero),Vector3.Zero);
    private static OilImpact? Ground(Vector3 from,Vector3 to,float radius)
    {
        if(from.Y>=0 && to.Y<=0) { return new(Vector3.Lerp(from,to,from.Y/(from.Y-to.Y)),Vector3.UnitY,radius); }
        return null;
    }
}
