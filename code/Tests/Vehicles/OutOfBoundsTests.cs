using System.Numerics;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

[TestFixture]
internal sealed class OutOfBoundsTests
{
    private static ArenaConfiguration Arena()
    {
        var a = PrototypeArena.Configuration;
        return new(a.Minimum, a.Maximum, a.Players, a.Items, a.Surfaces,
            boundary: new ArenaBoundary([new(-60,-50),new(60,-50),new(60,50),new(-60,50)], -30));
    }

    [Test]
    public void AuthoredPolygonHandlesTranslatedConcaveEdgesAltitudeAndFloor()
    {
        var boundary = new ArenaBoundary([new(100,100),new(120,100),new(120,110),new(110,110),new(110,120),new(100,120)], -30);
        Assert.That(boundary.Contains(new(105,10000,105)), Is.True);
        Assert.That(boundary.Contains(new(115,0,115)), Is.False);
        Assert.That(boundary.Contains(new(100,0,110)), Is.True);
        Assert.That(boundary.Contains(new(105,-31,105)), Is.False);
        Assert.That(boundary.Contains(new(10000,-1000,10000)), Is.False);
    }

    [Test]
    public void IndependentSimultaneousExposureDealsDefaultDamageUntilDeathAndClearsOnRespawn()
    {
        var host = new HostVehicleSession(9, arena: Arena(), configuration: GameplayConfiguration.HostedDefaults);
        host.Join(42); host.Join(43);
        void Step(int phase)
        {
            var input = new InputFrame(host.World.State.Tick+1,0,0,0,0,0,0);
            host.World.Step(input, host.World.State.Vehicles.Select(v =>
            {
                Vector3 p = v.VehicleId == 3 ? v.ObservedPhysics.Position : phase == 0 ? new(80,20,0) : phase == 1 ? new(10000,-1000,0) : new(0,0,0);
                return new VehicleStepRequest(v.VehicleId,input,new(new(p,Quaternion.Identity,Vector3.Zero,Vector3.Zero),Vector3.Zero));
            }).ToArray());
        }
        for(int i=0;i<60;i++) Step(i<30?0:1);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(900).Within(.01));
        Assert.That(host.World.GetVehicle(2).Damage.CurrentHP, Is.EqualTo(900).Within(.01));
        Assert.That(host.World.GetVehicle(3).Damage.CurrentHP, Is.EqualTo(1000));
        var state = host.World.GetVehicle(1);
        Assert.That(VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(state)).OutOfBounds, Is.True);
        Assert.That(VehicleNetworkCodec.DecodeSnapshot(VehicleNetworkCodec.EncodeSnapshot(host.Snapshot())).Vehicles[0].State.OutOfBounds, Is.True);
        var checkpoint = new ResumeCheckpoint(new Core.Items.ItemPublication(1,host.Snapshot(),[],[],[]),host.World.State.Match!,null,host.Configuration);
        var recovered = ResumeCheckpointCodec.Decode(ResumeCheckpointCodec.Encode(checkpoint));
        Assert.That(recovered.Items.World.Vehicles.Count(v=>v.State.OutOfBounds), Is.EqualTo(2));
        var restored = new Core.Simulation.Simulation(new(60),new RespawnConfiguration(),Arena());
        restored.AddVehicle(1,host.Configuration.Configuration.Vehicle,host.Configuration.Configuration.Damage,state.ObservedPhysics);
        restored.Restore(new Core.Simulation.SimulationState(state.Movement.Tick,host.World.State.LastInput,[VehicleSnapshotCodec.Decode(VehicleSnapshotCodec.Encode(state))]));
        var next = new InputFrame(state.Movement.Tick+1,0,0,0,0,0,0);
        restored.Step(next,[new VehicleStepRequest(1,next,new(new(Vector3.Zero,Quaternion.Identity,Vector3.Zero,Vector3.Zero),Vector3.Zero))]);
        Assert.That(restored.GetVehicle(1).OutOfBounds, Is.True);
        Assert.That(restored.GetVehicle(1).Damage.CurrentHP, Is.LessThan(state.Damage.CurrentHP));
        var prediction = new PredictedVehicle(new(state,0),host.Configuration.Configuration);
        prediction.Predict(new(state.Movement.Tick+1,0,0,0,0,0,0), _ => new(state.ObservedPhysics,Vector3.Zero));
        Assert.That(prediction.State.OutOfBounds, Is.True);
        Assert.That(prediction.State.Damage, Is.EqualTo(state.Damage));
        // Returning inside cannot cancel the life-scoped hazard.
        for(int i=0;i<541 && host.World.GetVehicle(1).CanInteract;i++) Step(2);
        var dead = host.World.GetVehicle(1);
        Assert.That(dead.Lifecycle, Is.EqualTo(VehicleLifecycle.Dead));
        Assert.That(dead.OutOfBounds, Is.False);
        Assert.That(dead.Damage.LastDamage!.Attribution, Is.EqualTo(new DamageContext("out-of-bounds",0,"arena-exterior")));
        Assert.That(host.World.State.Match!.Players.All(p => p.Kills == 0 && p.CircusScore == 0), Is.True);
        while(host.World.State.Tick < dead.RespawnAtTick) Step(2);
        Assert.That(host.World.GetVehicle(1).LifeId, Is.EqualTo(2));
        Assert.That(host.World.GetVehicle(1).OutOfBounds, Is.False);
        Assert.That(host.World.GetVehicle(1).Damage.CurrentHP, Is.EqualTo(1000));
    }

    [Test]
    public void HostValidatedClientTuningPersistsAndZeroDamageStillClassifiesBelowMap()
    {
        var host = new HostVehicleSession(9, arena: Arena(), configuration: GameplayConfiguration.HostedDefaults);
        host.Join(42);
        var edits = new Dictionary<string,double>{{"vehicle.oob.damage",0}};
        Assert.That(host.TryConfigure(999,edits,out _),Is.False);
        Assert.That(host.TryConfigure(42,edits,out _),Is.True);
        Assert.That(host.TryConfigure(0,edits,out _),Is.True);
        var config = host.Configuration.Configuration;
        Assert.That(GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(9,host.Configuration)).State.Configuration,Is.EqualTo(config));
        Assert.That(DeveloperSettingsFile.Read(DeveloperSettingsFile.Read("").Write(config)).Configuration,Is.EqualTo(config));
        var input = new InputFrame(1,0,0,0,0,0,0);
        host.World.Step(input,host.World.State.Vehicles.Select(v=>new VehicleStepRequest(v.VehicleId,input,new(new(new(0,-31,0),Quaternion.Identity,Vector3.Zero,Vector3.Zero),Vector3.Zero))).ToArray());
        Assert.That(host.World.State.Vehicles.All(v=>v.OutOfBounds && v.Damage.CurrentHP==1000),Is.True);
        foreach(double value in new[]{-1,double.NaN,10001})
            Assert.That(GameplayOptions.TryApply(config,new Dictionary<string,double>{{"vehicle.oob.damage",value}},out _,out _),Is.False);
    }
}
