using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Tests;

/// <summary>Advances actual authority ticks for tests whose subject is a deployed Wrench.</summary>
internal static class WrenchTestPreparation
{
    internal static void Wait(HostVehicleSession host)
    {
        for (int tick = 0; tick < 1200; tick++)
        {
            if (host.Items.Slots.Where(s => s.Active.Item == HeldItem.Wrench).All(host.Items.WrenchReady)) { return; }
            host.Step(default, state => new(state.ObservedPhysics, Vector3.UnitY));
        }
        throw new InvalidOperationException("Wrench did not deploy within the fixture budget.");
    }
}
