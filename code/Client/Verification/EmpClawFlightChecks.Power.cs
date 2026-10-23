using Godot;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class EmpClawFlightChecks
{
    private InputFrame _hostInput;
    private bool _powerFixture;

    private async Task NativePowerComparison()
    {
        Host.DrivePracticeCar(4, true);
        _powerFixture = true;
        _shooterOrientation = _victimOrientation = N.Quaternion.Identity;
        _hostInput = _victimInput = new(0, 0, ushort.MaxValue, 0, 0, 0, 0);
        var starts = new Dictionary<ulong, N.Vector3> { [1] = new(-50, 101.25f, -100), [2] = new(0, 101.25f, -100), [4] = new(50, 101.25f, 91) };
        var baseline = new Dictionary<ulong, float>();
        var one = new Dictionary<ulong, float>();
        foreach (int count in new[] { 0, 1, 2, 3 })
        {
            foreach (var pair in starts) { _positions[pair.Key] = pair.Value; }
            EmpClawVerificationState.Seed(Host, starts.Keys, count);
            // Warm the same reliable state and held input on every peer before freeing the cars.
            await Frames(90);
            foreach (ulong id in starts.Keys) { _positions.Remove(id); }
            await Frames(120);
            foreach (ulong id in starts.Keys)
            {
                var state = Host.World.GetVehicle(id);
                Check(state.Movement.EnginePowerScale == (count == 0 ? 1 : .2f), "Actual native driver receives the capped power state.");
                Check(_arenas.All(a => a.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == id).State.Movement.EnginePowerScale == state.Movement.EnginePowerScale), "Host, predicted client and practice-car observers agree on suppression.");
                if (count == 0) { baseline[id] = state.Speed; }
                else
                {
                    Check(state.Speed < baseline[id] * .5f, "EMP substantially reduces the same car's two-second native launch.");
                    if (count == 1) { one[id] = state.Speed; }
                    else { Check(Math.Abs(state.Speed - one[id]) < .25f, "Two/three claws do not add another launch penalty."); }
                }
                Record(new { scenario = "native-power", path = id == 1 ? "host" : id == 2 ? "remote-input" : "single-player-practice-driver",
                    claws = count, seconds = 2, speedMetresPerSecond = state.Speed, distance = N.Vector3.Distance(state.Movement.Physics.Position, starts[id]),
                    throttle = state.Movement.Throttle, power = state.Movement.EnginePowerScale });
            }
            if (count == 3)
            {
                var car = _arenas[0].Bodies[2];
                await Capture("driving-distance-three-claws", car.VisualPosition + new Vector3(0, 1.25f, 7.36f), car.VisualPosition + Vector3.Up * .5f);
            }
        }
        _hostInput = _victimInput = default;
        EmpClawVerificationState.Seed(Host, [2], 1);
        ulong life = Host.World.GetVehicle(2).LifeId;
        ulong tick = Host.World.State.Tick + 1;
        var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        var requests = Host.World.State.Vehicles.Select(v => new VehicleStepRequest(v.VehicleId, input,
            new(v.Movement.Physics, N.Vector3.UnitY), effects: v.VehicleId == 2
                ? [new(new(10000, N.Vector3.Zero, N.Vector3.Zero), new("missile", 1, "lethal-emp-fixture"))] : [])).ToArray();
        Host.Items.Step(Host.World, input, requests, (_, _) => null);
        Check(!Host.World.GetVehicle(2).CanInteract && Host.Items.EmpAttachments.Count == 0, "Lethal combat boundary clears EMP immediately.");
        await Until(() => Host.World.GetVehicle(2).LifeId > life, "Real respawn completes after the seeded lethal combat effect.", 600);
        await Frames(120);
        Check(Host.Items.EmpAttachments.Count == 0 && _arenas.All(a => a.Driver.ItemState!.EmpAttachments.Count == 0), "Death clears attachment membership on every peer.");
        Check(Host.World.GetVehicle(2).Movement.EnginePowerScale == 1 && _arenas[1].Driver.LocalState!.Movement.EnginePowerScale == 1, "Respawn clears authoritative and predicted power state.");
        Check(Enumerable.Range(0, 3).All(p => Attachments(p).Views.Count == 0 && Attachments(p).ReleasedCount == 0), "Native respawn reclaims all attachment effects.");
        Record(new { scenario = "native-emp-death-respawn", beforeLife = life, afterLife = Host.World.GetVehicle(2).LifeId });
    }
}
