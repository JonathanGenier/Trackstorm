using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Eight real UDP peers with isolated Godot worlds exercising production pickup contact and replication.</summary>
public sealed partial class ItemSpawnIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<Hud.CombatHud> _huds = new();
    private readonly List<string> _evidence = new();
    private string _output = string.Empty;
    private SubViewport _view = null!;
    private double _elapsed;
    private double _started;
    private int _stage;
    private ulong _winner;
    private ItemSlot[] _distributed = Array.Empty<ItemSlot>();
    private bool _finished;
    private int _cleanupFrames;
    private bool Oval => OS.GetCmdlineUserArgs().Contains("--spawn-oval");
    private int PickupCount => Oval ? 20 : 8;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        _output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--spawn-output=", StringComparison.Ordinal))?[15..] ?? ProjectSettings.GlobalizePath("res://.godot/spawn-checks");
        System.IO.Directory.CreateDirectory(_output);
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
        reservation.Close();
        for (int index = 0; index < 8; index++)
        {
            var gateway = new GameNetworkingSocketsTransport();
            _gateways.Add(gateway);
            ulong server = 0;
            if (index == 0)
            {
                gateway.Listen(TransportEndpoint.DirectIp(endpoint));
                if (OS.GetCmdlineUserArgs().Contains("--spawn-impaired"))
                {
                    gateway.ConfigureSimulation(new Core.Networking.Transport.NetworkSimulation(30, 5, 2, 0, 0));
                }
            }
            else
            {
                server = gateway.Connect(TransportEndpoint.DirectIp(endpoint));
            }

            var viewport = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = index == 1 ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled };
            if (index == 1)
            {
                _view = viewport;
                var display = new SubViewportContainer();
                AddChild(display);
                display.AddChild(viewport);
            }
            else
            {
                AddChild(viewport);
            }

            var arena = new NetworkVehicleArena { PrototypeMapForVerification = !Oval, SpawnConfiguration = new ItemSpawnConfiguration { CooldownTicks = 180, Seed = 6 } };
            arena.Initialize(gateway, index == 0 ? 88ul : 0, server);
            viewport.AddChild(arena);
            _arenas.Add(arena);
            var hud = new Hud.CombatHud { Vehicle = () => arena.LocalState, Slot = () => arena.Driver.LocalItem };
            viewport.AddChild(hud);
            _huds.Add(hud);
        }
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
        {
            if (++_cleanupFrames == 30)
            {
                GetTree().Quit();
            }

            return;
        }

        try
        {
            _elapsed += delta;
            foreach (var arena in _arenas)
            {
                arena.Advance(default);
                Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
            }

            Require(_elapsed - _started < 25, $"Pickup stage {_stage} timed out.");
            Scenario();
            foreach (var hud in _huds)
            {
                hud.Refresh();
                if (hud.Vehicle() is VehicleSnapshot state)
                {
                    Require(hud.Displayed!.Item == (state.CanInteract ? hud.Slot()?.Item ?? HeldItem.None : HeldItem.None), "HUD shows confirmed pickup/use ownership.");
                }
            }
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            Cleanup();
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void Scenario()
    {
        var host = _arenas[0].Driver.Host!;
        switch (_stage)
        {
            case 0 when _arenas.All(arena => arena.Driver.Latest?.Vehicles.Count == 8 && arena.Pickups.ActiveCount == PickupCount):
                Require(_arenas.All(arena => arena.Driver.ItemState!.Spawns.Select(spawn => spawn.Id).SequenceEqual(arena.MapConfiguration.Items.Select(marker => marker.Id))), "All actual marker IDs replicate to every peer.");
                PositionPlayers(true, false);
                Next($"Eight peers see exactly {PickupCount} configured active pickups; two remote vehicles enter one pickup together.");
                break;
            case 1 when _arenas.All(arena => arena.Pickups.ActiveCount == PickupCount - 1) && _elapsed - _started > 1.2:
                Require(_arenas.All(arena => arena.Driver.ItemState!.Slots.Count == 1), "Only one slot is awarded on every peer.");
                var claim = host.Spawns!.States[0];
                _winner = claim.ClaimedBy;
                Require(_winner == _arenas[1].Driver.LocalVehicleId || _winner == _arenas[2].Driver.LocalVehicleId, "A contesting remote player wins.");
                Require(_arenas.All(arena => arena.Driver.ItemState!.Spawns[0] == claim && arena.Driver.ItemState.Slots.Single().Token == claim.Token), "Claim, cooldown and grant token match everywhere.");
                Require(_arenas.All(arena => arena.Driver.ItemState!.Slots.Single().Item == HeldItem.Wrench), "Seeded first normal pickup awards Wrench.");
                Capture("contested.png");
                PositionPlayers(false, false);
                Require(_arenas.Single(arena => arena.Driver.LocalVehicleId == _winner).Driver.RequestItemUse(), "Winner consumes acquired Wrench through production remote use.");
                Next("Exactly one remote contestant won; all eight peers show matching inactive spawn, cooldown and item token.");
                break;
            case 2 when _arenas.All(arena => arena.Pickups.ActiveCount == PickupCount && arena.Driver.ItemState!.Slots.All(slot => slot.Item == HeldItem.None)):
                Capture("reactivated.png");
                PositionPlayers(false, true);
                Next("Authoritative cooldown reactivates on every peer; all eight vehicles approach separate pickups.");
                break;
            case 3 when _arenas.All(arena => arena.Pickups.ActiveCount == PickupCount - 8):
                var expected = host.Items.Slots;
                _distributed = expected.ToArray();
                Require(expected.Count == 8 && ItemRegistry.All.All(item => expected.Any(slot => slot.Item == item.Identity)), "Normal weighted pickups distribute all four registered items across eight slots.");
                Require(_arenas.All(arena => arena.Driver.ItemState!.Slots.SequenceEqual(expected) && arena.Driver.ItemState.Spawns.SequenceEqual(host.Spawns!.States)), "All inventory and spawn outcomes agree across eight peers.");
                foreach (var arena in _arenas.Where(arena => ItemRegistry.Find(arena.Driver.LocalItem!.Item)?.CanUse == false))
                {
                    var slot = arena.Driver.LocalItem!;
                    for (int i = 0; i < 3; i++)
                    {
                        arena.Driver.RequestItemUse();
                    }

                    Require(host.Items.Slots.Single(value => value.Vehicle == slot.Vehicle) == slot, "Unavailable item use preserves the issued capability.");
                }

                Capture("all-claimed.png");
                // Stay in range: occupied slots must not reclaim when the cooldown elapses.
                Next("All eight spawn locations awarded once, with all four item types replicated normally.");
                break;
            case 4 when _arenas.All(arena => arena.Pickups.ActiveCount == PickupCount) && _elapsed - _started > 4:
                Require(host.Items.Slots.All(slot => slot.Item != HeldItem.None), "Occupied slots retain their grants.");
                Require(host.Spawns!.States.All(spawn => spawn.Available), "Occupied players cannot consume reactivated spawns while remaining in range.");
                Require(_arenas.All(arena => arena.Driver.ItemState!.Slots.SequenceEqual(_distributed)), "Occupied grants and tokens remain unchanged on all peers after reactivation.");
                Require(_arenas.All(arena => arena.GetChildren().OfType<Items.ItemPresentation>().Single().GetChildCount() == 0), "Held inventory creates no world presentation for local or remote vehicles.");
                Capture("occupied-slots.png");
                _evidence.Add($"All {PickupCount} pickups are available after cooldown, including under occupied vehicles. No duplicate awards or overwritten grants.");
                System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
                GD.Print("Item spawn integration passed: " + string.Join("\n", _evidence));
                Cleanup();
                break;
        }
    }

    private void PositionPlayers(bool contest, bool distribute)
    {
        var world = _arenas[0].Driver.Host!.World;
        ulong first = _arenas[1].Driver.LocalVehicleId;
        ulong second = _arenas[2].Driver.LocalVehicleId;
        var config = _arenas[0].MapConfiguration;
        var states = world.State.Vehicles.Select(vehicle =>
        {
            int index = (int)vehicle.VehicleId - 1;
            Numerics.Vector3 position = distribute ? config.Items[index].Position + new Numerics.Vector3(0, 0.6f, 0) : config.Spawn(index).Position;
            if (contest && (vehicle.VehicleId == first || vehicle.VehicleId == second))
            {
                position = config.Items[0].Position + new Numerics.Vector3(vehicle.VehicleId == first ? -1.5f : 1.5f, 0.6f, 0);
            }

            var pose = new VehiclePhysicsState(position, Numerics.Quaternion.Identity, Numerics.Vector3.Zero, Numerics.Vector3.Zero);
            return new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId, new VehicleState(world.State.Tick, pose, false, false, 0, 0), vehicle.Damage, pose);
        }).ToArray();
        world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, states, world.State.Match));
    }

    private void Next(string message)
    {
        _evidence.Add(message);
        GD.Print(message);
        _stage++;
        _started = _elapsed;
    }

    private void Capture(string name)
    {
        if (DisplayServer.GetName() != "headless")
        {
            _view.GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, name));
        }
    }

    private void Cleanup()
    {
        _finished = true;
        foreach (var arena in _arenas)
        {
            arena.QueueFree();
        }

        foreach (var gateway in _gateways)
        {
            gateway.ConfigureSimulation(new());
            gateway.Dispose();
        }
    }
}
