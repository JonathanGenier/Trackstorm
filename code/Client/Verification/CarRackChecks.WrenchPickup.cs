using Godot;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class CarRackChecks
{
    private async Task CheckWrenchPickupRaces()
    {
        var host = _arenas[0].Driver.Host!;
        var hud = new Hud.CombatHud { Vehicle = () => _arenas[1].LocalState,
            Slot = () => _arenas[1].PresentedLocalItem };
        _arenas[1].AddChild(hud);
        // Repeat all four boundaries, with motion on the second pass. Use the real
        // pickup authority at a controlled marker, not a direct inventory overwrite.
        for (int round = 0; round < 8; round++)
        {
            Items.WrenchRepairSettings.Current = new() { DeploymentDuration = round % 2 == 0 ? .85f : .95f, RetractionDuration = round % 2 == 0 ? .85f : .95f };
            host.Items.RemovePlayer(Shooter);
            Position(500, true);
            await Frames(150);
            Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Race Wrench granted " + round);
            await Until(() => Arm(0)?.IsReady == true && Arm(1)?.IsReady == true &&
                host.Items.WrenchReady(host.Items.Slots.Single(s => s.Vehicle == Shooter)), "Race deployment ready");
            int phase = round % 4;
            HeldItem incoming = round % 2 == 0 ? HeldItem.Salvo : HeldItem.Wrench;
            bool picked = false;
            ulong incomingToken = 0;
            void Pickup()
            {
                var map = _arenas[0].MapConfiguration;
                var marker = new ArenaSpawn("wrench-race", host.World.GetVehicle(Shooter).ObservedPhysics.Position, 0);
                var arena = new ArenaConfiguration(map.Minimum, map.Maximum, map.Players, [marker], map.Surfaces);
                var spawns = new ItemSpawnAuthority(arena, host.Items, new ItemSelectionRandom(293), selector: () => incoming);
                spawns.Advance(host.World);
                Check(spawns.TryPickup(host.World, marker.Id, Shooter), "Actual pickup accepted at boundary " + phase);
                Check(!spawns.TryPickup(host.World, marker.Id, Shooter), "Same marker cannot award twice");
                incomingToken = spawns.States.Single().Token;
                picked = true;
            }
            if (phase == 0) { Pickup(); await Frames(2); }
            int before = _events.Count(e => e.Item == HeldItem.Wrench);
            Check(_arenas[1].Driver.RequestItemUse(), "Race use accepted");
            var contacts = new List<int>[] { [], [] };
            bool[] owned = new bool[2], closed = new bool[2];
            bool switched = false;
            float speed = 0;
            for (int frame = 0; frame < 600 && !closed.All(value => value); frame++)
            {
                await Frames(1, throttle: round >= 4 ? (ushort)30000 : (ushort)0);
                speed = Math.Max(speed, Math.Abs(_arenas[1].LocalState!.Movement.CommandSpeed));
                if (!picked && ((phase == 3 && _events.Count(e => e.Item == HeldItem.Wrench) > before) ||
                    (phase == 1 && Arm(1)?.ContactIndex == 0) ||
                    (phase == 2 && Arm(1) is { Repairing: false, Deployment: < .9f }))) { Pickup(); }
                if (phase == 0 && !switched && Arm(1)?.ContactIndex == 0)
                {
                    Check(_arenas[1].Driver.RequestItemSwitch(), "Selection during repair is accepted for the next handoff");
                    switched = true;
                }
                for (int peer = 0; peer < 2; peer++)
                {
                    var rack = _arenas[peer].Bodies[Shooter].Rack;
                    if (Arm(peer) is { ContactIndex: >= 0 } arm &&
                        (contacts[peer].Count == 0 || contacts[peer][^1] != arm.ContactIndex)) { contacts[peer].Add(arm.ContactIndex); }
                    if (rack.WrenchOwnsRack)
                    {
                        owned[peer] = true;
                        var inventory = _arenas[peer].Driver.ItemState!.Slots.Single(s => s.Vehicle == Shooter);
                        Check(rack.PresentedInventory(inventory)!.Item == HeldItem.Wrench, "Spent slot stays represented through actual arm/rack return");
                        Check(rack.PresentedItem == HeldItem.Wrench, "Pickup/selection cannot replace the active repair arm");
                        if (peer == 1)
                        {
                            hud._Process(0);
                            Check(hud.Displayed!.Item == HeldItem.Wrench, "Actual HUD shares the installed capability lifecycle");
                            Check(!_arenas[1].Driver.RequestItemUse(), "Next use cannot bypass the occupied mechanism");
                            Check(!_arenas[1].Driver.RequestItemDiscard(), "Discard cannot delete an incoming pickup behind the returning Wrench");
                        }
                    }
                    else if (owned[peer] && !closed[peer])
                    {
                        Check(rack.Progress == 0 && rack.PresentedItem == HeldItem.None, "Handoff releases only after full trunk closure");
                        closed[peer] = true;
                    }
                }
            }
            Check(picked && closed.All(value => value), "Pickup race reaches full stow on both peers " + round);
            Check(contacts.All(values => values.SequenceEqual(new[] { 0, 1, 2 })), "Pickup and switching preserve all three repair beats");
            Check(_events.Count(e => e.Item == HeldItem.Wrench) == before + 1, "Race applies exactly one Wrench use");
            Check(host.World.GetVehicle(Shooter).Damage.CurrentHP == 1000, "Race preserves one 500 HP repair");
            var accepted = host.Items.Slots.Single(s => s.Vehicle == Shooter);
            Check(phase == 0 ? accepted.SecondToken == incomingToken && accepted.SecondItem == incoming :
                accepted.Token == incomingToken && accepted.Item == incoming, "Incoming pickup capability survives unchanged");
            await Until(() => AllPresent(incoming), "Incoming item deploys after the Wrench handoff", 720);
            Check(_arenas[1].PresentedLocalItem == _arenas[1].Driver.LocalItem, "Slot representation converges to accepted inventory after closure");
            if (round >= 4) { Check(speed > 2, "Pickup race exercised while driving"); }
        }
        hud.QueueFree();
    }
}
