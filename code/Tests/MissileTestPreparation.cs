using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests;

/// <summary>Advances real empty ticks before tests focused on already-deployed missile behavior.</summary>
internal static class MissileTestPreparation
{
    internal static void Wait(HostVehicleSession host) => Wait(host.Items, host.World,
        () => host.Step(default, state => new(state.ObservedPhysics, Vector3.UnitY)));

    internal static void Wait(ItemAuthority items, Trackstorm.Core.Simulation.Simulation world, Action? step = null)
    {
        ulong boundary = items.Slots.Select(slot => slot.MissileReadyTick).DefaultIfEmpty().Max();
        while (world.State.Tick < boundary)
        {
            if (step is not null) { step(); continue; }
            var input = new Trackstorm.Core.Input.InputFrame(world.State.Tick + 1, 0, 0, 0, 0, 0, 0);
            items.Step(world, input, world.State.Vehicles.Select(state =>
                new VehicleStepRequest(state.VehicleId, input, new(state.ObservedPhysics, Vector3.UnitY))).ToArray(), (_, _) => null);
        }
    }
}
