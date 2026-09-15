using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Eight real UDP peers with isolated Godot worlds exercising production item use and collision adapters.</summary>
public sealed partial class ItemIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<Hud.CombatHud> _huds = new();
    private readonly List<List<ItemEvent>> _events = new();
    private readonly List<Dictionary<ulong, ItemPublication>> _outcomes = new();
    private readonly List<string> _evidence = new();
    private string _output = string.Empty;
    private SubViewport _view = null!;
    private double _elapsed;
    private double _started;
    private int _stage;
    private int _scenario;
    private ulong _token;
    private double _impactSeen;
    private float _propPeakSpeed;
    private bool _finished;
    private int _cleanupFrames;
    private PlayerInput _input = null!;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        _input = new PlayerInput();
        AddChild(_input);
        _input.SetPhysicsProcess(false);
        var legacy = new Core.Settings.PlayerSettings { BindingDefaultsVersion = 0 }
            .WithBindings(InputAction.UseItem, new[] { "key:69", "button:0:2" })
            .WithBindings(InputAction.Drift, new[] { "key:32", "button:0:0" });
        InputBindingPreferences.Apply(_input.Adapter, legacy);
        _output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--item-output=", StringComparison.Ordinal))?[14..] ?? ProjectSettings.GlobalizePath("res://.godot/item-checks");
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
                if (OS.GetCmdlineUserArgs().Contains("--item-impaired"))
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

            var arena = new NetworkVehicleArena();
            arena.Initialize(gateway, index == 0 ? 88ul : 0, server);
            viewport.AddChild(arena);
            _arenas.Add(arena);
            var hud = new Hud.CombatHud { Vehicle = () => arena.LocalState, Slot = () => arena.Driver.LocalItem };
            viewport.AddChild(hud);
            _huds.Add(hud);
            var events = new List<ItemEvent>();
            _events.Add(events);
            var outcomes = new Dictionary<ulong, ItemPublication>();
            _outcomes.Add(outcomes);
            arena.Driver.ItemsReceived += publication =>
            {
                events.AddRange(publication.Events);
                foreach (var impact in publication.Events.Where(outcome => outcome.Impact))
                {
                    outcomes.Add(impact.Token, publication);
                }
            };
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
                arena.Advance(arena == _arenas[1] ? _input.Adapter.Capture(0) : default);
                Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
            }

            Require(_elapsed - _started < 25, $"Item stage {_stage}, scenario {_scenario} timed out.");
            if (_scenario == 1 && _stage == 6)
            {
                _propPeakSpeed = Math.Max(_propPeakSpeed, _arenas[0].Layout.Props[2].LinearVelocity.Length());
            }

            Scenario();
            foreach (var hud in _huds)
            {
                hud.Refresh();
                if (hud.Vehicle() is VehicleSnapshot state)
                {
                    Require(hud.HealthText == Hud.CombatHudView.FormatHealth(state.Damage.CurrentHP, state.Damage.MaxHP), "HUD receives replicated health immediately.");
                    Require(Math.Abs(hud.HealthFill - Hud.CombatHudView.NormalizeHealth(state.Damage.CurrentHP, state.Damage.MaxHP)) < 0.00001, "HUD replicated health fill.");
                    Require(hud.Displayed!.Item == (state.CanInteract ? hud.Slot()?.Item ?? HeldItem.None : HeldItem.None), "HUD confirmed inventory follows pickup/use.");
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
            case 0 when _arenas.All(arena => arena.Driver.Latest?.Vehicles.Count == 8 && arena.Driver.LocalState is not null):
                _arenas[0].GrantItems(HeldItem.Wrench);
                Next("Eight native peers assigned and initialized.");
                break;
            case 1 when AllHeld(HeldItem.Wrench):
                UseAll();
                Next("All eight use Wrench at full HP, including repeated requests.");
                break;
            case 2 when AllHeld(HeldItem.None):
                Require(host.World.State.Vehicles.All(vehicle => vehicle.Damage.CurrentHP == vehicle.Damage.MaxHP), "Full HP Wrench heals zero.");
                Require(_events.All(events => events.Count(outcome => outcome.Item == HeldItem.Wrench) == 8), "Exactly eight repair outcomes on every peer.");
                SetWorld(40);
                _arenas[0].GrantItems(HeldItem.Wrench);
                Next("Every full-health use consumed once and replicated; preparing damaged Wrench use.");
                break;
            case 3 when AllHeld(HeldItem.Wrench):
                UseAll();
                Next("All eight damaged players use Wrench.");
                break;
            case 4 when AllHeld(HeldItem.None):
                Require(_arenas.All(arena => arena.Driver.ItemState!.World.Vehicles.All(vehicle => vehicle.State.Damage.CurrentHP == 75)), "35 HP healing replicates to every peer.");
                PrepareMissile();
                Next("Damaged Wrench outcomes match on all eight peers.");
                break;
            case 5 when _arenas[1].Driver.LocalItem?.Token == _token && _elapsed - _started > 0.5:
                ItemInput(true);
                if (_scenario == 2)
                {
                    ItemInput(false);
                }

                InputFrame input = _input.Adapter.Capture(0);
                Require((input.Pressed & InputButtons.UseItem) != 0 && (input.Held & InputButtons.Drift) == 0, "Physical item press reaches its semantic action without handbrake.");
                _arenas[1].Advance(input);
                Require((_input.Adapter.Capture(0).Pressed & InputButtons.UseItem) == 0, "One physical press produces only one command edge.");
                Require(host.Items.Missiles.Count == 0, "Remote input cannot create a host projectile synchronously.");
                Next($"Remote client requested missile scenario {_scenario}; host owns creation.");
                break;
            case 6 when _events.All(events => events.Count(outcome => outcome.Token == _token && outcome.Impact) == 1):
                if (_impactSeen == 0)
                {
                    _impactSeen = _elapsed;
                }

                if (_elapsed - _impactSeen < 0.2)
                {
                    break;
                }

                var impacts = _events.Select(events => events.Single(outcome => outcome.Token == _token && outcome.Impact)).ToArray();
                Require(impacts.All(impact => impact == impacts[0]), "All peers see the same impact point and identity.");
                var expectedHP = _outcomes[0][_token].World.Vehicles.Select(vehicle => (vehicle.State.VehicleId, vehicle.State.Damage.CurrentHP)).ToArray();
                Require(_outcomes.All(outcomes => outcomes[_token].World.Vehicles.Select(vehicle => (vehicle.State.VehicleId, vehicle.State.Damage.CurrentHP)).SequenceEqual(expectedHP)), "Every peer receives identical authoritative damage outcomes for all eight vehicles.");
                Require(_events.All(events => events.Count(outcome => outcome.Token == _token && !outcome.Impact) == 1), "Duplicate request cannot duplicate launch.");
                if (_scenario == 0)
                {
                    float near = host.World.GetVehicle(1).Damage.CurrentHP;
                    ulong farId = _arenas[2].Driver.LocalVehicleId;
                    float far = host.World.GetVehicle(farId).Damage.CurrentHP;
                    Require(near < far && far < 100, $"Distance falloff differs: near={near}, far={far}.");
                    Require(host.World.GetVehicle(1).Movement.Physics.LinearVelocity.Length() > 0.1f, "Explosion physically pushes the target.");
                }
                else if (_scenario == 1)
                {
                    Require(_propPeakSpeed > 0.1f, "Explosion physically pushes movable prop.");
                }

                Capture($"impact-{_scenario}.png");
                ItemInput(false);
                _input.Adapter.Capture(0);
                ItemInput(true);
                _arenas[1].Advance(_input.Adapter.Capture(0));
                Require(!_arenas[1].Driver.RequestItemUse(), "Clicking without a held item safely rejects use.");
                ItemInput(false);
                _input.Adapter.Capture(0);
                _evidence.Add($"Scenario {_scenario}: one remote launch/impact on every peer at {impacts[0].Position}; host-only damage/impulse.");
                _scenario++;
                if (_scenario < 4)
                {
                    PrepareMissile();
                    _stage = 5;
                    _started = _elapsed;
                }
                else
                {
                    _evidence.Add("Vehicles, movable prop, static container and arena boundary all stop missiles. VFX consume replicated events only.");
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
                    GD.Print("Item integration passed: " + string.Join("\n", _evidence));
                    Cleanup();
                }

                break;
        }
    }

    private bool AllHeld(HeldItem item) => _arenas.All(arena => arena.Driver.ItemState?.Slots.Count == 8 && arena.Driver.ItemState.Slots.All(slot => slot.Item == item));

    private void ItemInput(bool pressed)
    {
        using InputEvent input = _scenario == 1
            ? new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = pressed }
            : new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed };
        Godot.Input.ParseInputEvent(input);
        Godot.Input.FlushBufferedEvents();
    }

    private void UseAll()
    {
        foreach (var arena in _arenas)
        {
            if (arena == _arenas[0])
            {
                ItemInput(true);
                ItemInput(false);
                arena.Advance(_input.Adapter.Capture(0));
                Require(arena.Driver.LocalItem?.Item == HeldItem.None, "Host LMB tap consumes the equipped Wrench through authority.");
                continue;
            }

            Require(arena.Driver.RequestItemUse(), "Valid item request submitted.");
            arena.Driver.RequestItemUse();
        }
    }

    private void PrepareMissile()
    {
        _impactSeen = 0;
        _propPeakSpeed = 0;
        SetWorld(100);
        var host = _arenas[0].Driver.Host!;
        ulong shooter = _arenas[1].Driver.LocalVehicleId;
        host.Items.Grant(host.World, shooter, HeldItem.Missile);
        _token = host.Items.Slots.Single(slot => slot.Vehicle == shooter).Token;
    }

    private void SetWorld(float hp)
    {
        var world = _arenas[0].Driver.Host!.World;
        ulong shooter = _arenas[1].Driver.LocalVehicleId;
        ulong farId = _arenas[2].Driver.LocalVehicleId;
        var states = world.State.Vehicles.Select(vehicle =>
        {
            Numerics.Vector3 position = Core.Arenas.PrototypeArena.Configuration.Spawn((int)(vehicle.VehicleId - 1)).Position;
            if (_stage >= 4)
            {
                if (vehicle.VehicleId == shooter)
                {
                    position = _scenario switch { 0 => new(-40, 0.6f, 5), 1 => new(8, 0.6f, 4), 2 => new(-17, 0.6f, 10), _ => new(-45, 0.6f, -40) };
                }
                else if (_scenario == 0 && vehicle.VehicleId == 1)
                {
                    position = new(-40, 0.6f, -5);
                }
                else if (_scenario == 0 && vehicle.VehicleId == farId)
                {
                    position = new(-36, 0.6f, -5);
                }
            }

            var pose = new VehiclePhysicsState(position, Numerics.Quaternion.Identity, Numerics.Vector3.Zero, Numerics.Vector3.Zero);
            return new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId, new VehicleState(world.State.Tick, pose, false, false, 0, 0), new VehicleDamageState(vehicle.Damage.MaxHP, hp, null, null), pose);
        }).ToArray();
        world.Restore(new SimulationState(world.State.Tick, world.State.LastInput, states));
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
