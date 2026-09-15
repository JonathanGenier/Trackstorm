using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Eight native UDP peers repeatedly kill and respawn a remote player through real missile and wall queries.</summary>
public sealed partial class DeathRespawnIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<List<VehicleSnapshot>> _boundaries = new();
    private readonly List<string> _evidence = new();
    private string _output = string.Empty;
    private SubViewport _view = null!;
    private double _elapsed;
    private double _started;
    private int _stage;
    private int _cycle;
    private ulong _victim;
    private ulong _life;
    private ulong _deadline;
    private bool _finished;
    private int _cleanupFrames;
    private bool _captured;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        _output = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith("--death-output=", StringComparison.Ordinal))?[15..] ?? ProjectSettings.GlobalizePath("res://.godot/death-checks");
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
                if (OS.GetCmdlineUserArgs().Contains("--death-impaired"))
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
            arena.Initialize(gateway, index == 0 ? 240ul : 0, server);
            viewport.AddChild(arena);
            _arenas.Add(arena);
            var outcomes = new List<VehicleSnapshot>();
            _boundaries.Add(outcomes);
            arena.Driver.LifecycleReceived += snapshot =>
            {
                VehicleSnapshot? state = snapshot.Vehicles.SingleOrDefault(vehicle => vehicle.State.VehicleId == _victim)?.State;
                if (state is not null)
                {
                    outcomes.Add(state);
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
                // Actively try driving/using during the wait; authority and prediction must suppress it.
                bool inactive = arena.Driver.LocalState?.CanInteract == false;
                arena.Advance(inactive ? new InputFrame(0, 32767, 65535, 0, InputButtons.Drift | InputButtons.UseItem, InputButtons.UseItem, 0) : default);
                Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
            }

            Require(_elapsed - _started < 20, $"Death stage {_stage}, cycle {_cycle} timed out.");
            Scenario();
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
        HostVehicleSession host = _arenas[0].Driver.Host!;
        switch (_stage)
        {
            case 0 when _arenas.All(arena => arena.Driver.Latest?.Vehicles.Count == 8 && arena.Driver.LocalState is not null):
                _victim = _arenas[1].Driver.LocalVehicleId;
                Prepare();
                break;
            case 1:
                // Allow setup snapshots and inventory grants to reach every peer before launching.
                if (_elapsed - _started > 0.4)
                {
                    if (_cycle % 2 == 0)
                    {
                        Require(_arenas[2].Driver.RequestItemUse(), "Remote shooter submits its issued missile.");
                    }

                    _stage = 2;
                    _started = _elapsed;
                }

                break;
            case 2 when _boundaries.All(outcomes => outcomes.Any(state => state.LifeId == _life && state.Lifecycle == VehicleLifecycle.Dead)):
                VehicleSnapshot death = _boundaries[0].Single(state => state.LifeId == _life && state.Lifecycle == VehicleLifecycle.Dead);
                _deadline = death.RespawnAtTick!.Value;
                Require(death.Damage.LastDamage!.Attribution.Source == (_cycle % 2 == 0 ? "missile" : "collision"), "Production damage source causes the death.");
                foreach (var arena in _arenas)
                {
                    VehicleSnapshot state = arena.Driver.Latest!.Vehicles.Single(vehicle => vehicle.State.VehicleId == _victim).State;
                    Require(!state.CanInteract && state.Damage.CurrentHP == 0, "Every peer agrees on zero HP and inactivity.");
                    Require(arena.Bodies[_victim].CollisionLayer == 0 && !arena.Bodies[_victim].IsPresented, "Inactive native bodies are hidden and non-colliding.");
                }

                Require(host.Items.Slots.All(slot => slot.Vehicle != _victim), "Held item cleared in the lethal batch.");
                Require(!host.Items.Grant(host.World, _victim, HeldItem.Wrench), "Dead pickup/grant rejected.");
                _stage = 3;
                _started = _elapsed;
                break;
            case 3:
                if (!_captured && _elapsed - _started >= 0.12)
                {
                    Capture($"death-{_cycle}.png");
                    _captured = true;
                }

                VehicleSnapshot current = host.World.GetVehicle(_victim);
                if (host.World.State.Tick < _deadline)
                {
                    Require(!current.CanInteract && current.Movement.Physics.LinearVelocity == Numerics.Vector3.Zero && current.Movement.Physics.AngularVelocity == Numerics.Vector3.Zero, "No early respawn or inactive driving.");
                    Require(!_arenas[1].Driver.RequestItemUse(), "Dead local item button rejected.");
                }

                if (_boundaries.All(outcomes => outcomes.Any(state => state.LifeId == _life + 1 && state.CanInteract)))
                {
                    foreach (var outcomes in _boundaries)
                    {
                        VehicleSnapshot spawn = outcomes.First(state => state.LifeId == _life + 1 && state.CanInteract);
                        Require(spawn.Movement.Tick == _deadline, "All peers receive the exact respawn threshold.");
                        Require(Enumerable.Range(0, 8).Any(slot => spawn.ObservedPhysics == host.World.Arena.Spawn(slot)), "All peers receive a configured spawn and zero linear/angular velocity.");
                        Require(spawn.ObservedPhysics == _boundaries[0].First(state => state.LifeId == _life + 1 && state.CanInteract).ObservedPhysics, "Peers agree on the selected spawn.");
                        Require(spawn.Damage.CurrentHP == spawn.Damage.MaxHP && spawn.Damage.LastDamage is null && spawn.Damage.LastCollisionTick is null, "Full HP and clean damage memory.");
                        Require(spawn.Movement.SteeringAngle == 0 && spawn.Movement.Handbrake == 0 && spawn.Movement.Wheels == default && spawn.Movement.FrontSlip == 0 && spawn.Movement.RearSlip == 0 && !spawn.Movement.Grounded && spawn.Movement.CurrentSurface == SurfaceType.Concrete && spawn.Effects.Count == 0, "Clean transient movement/physics state.");
                        Require(outcomes.Count(state => state.LifeId == _life && state.Lifecycle == VehicleLifecycle.Dead) == 1, "Exactly one reliable death per life.");
                        Require(outcomes.Any(state => state.LifeId == _life && state.Lifecycle == VehicleLifecycle.Respawning), "Waiting lifecycle reliably observed.");
                    }

                    foreach (var arena in _arenas)
                    {
                        Require(arena.Bodies[_victim].CollisionLayer == 2 && arena.Bodies[_victim].IsPresented, "Respawn re-enables native collision and presentation.");
                        Require(arena.Destruction.BurstCount == _cycle + 1 && arena.Destruction.ActiveBursts == 0, "One VFX per death and no accumulated transient bursts.");
                        Require(arena.Bodies[_victim].Smoothing.Offset.Length() < 0.01f, $"Respawn does not retain a correction offset: {arena.Bodies[_victim].Smoothing.Offset}.");
                    }

                    _stage = 4;
                    _started = _elapsed;
                }

                break;
            case 4 when _elapsed - _started > 0.25:
                // Capture after the renderer has presented the new life, before arranging the next scenario.
                Capture($"respawn-{_cycle}.png");
                string evidence = $"Cycle {_cycle + 1}: {(_cycle % 2 == 0 ? "missile" : "collision")} death, all eight peers Dead/Respawning/Alive, respawn tick {_deadline}, reset physics/HP/items/VFX verified.";
                _evidence.Add(evidence);
                GD.Print(evidence);
                if (++_cycle == 4)
                {
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
                    GD.Print("Death/respawn integration passed.");
                    Cleanup();
                }
                else
                {
                    Prepare();
                }

                break;
        }
    }

    private void Prepare()
    {
        var host = _arenas[0].Driver.Host!;
        ulong shooter = _arenas[2].Driver.LocalVehicleId;
        _life = host.World.GetVehicle(_victim).LifeId;
        foreach (var outcomes in _boundaries)
        {
            outcomes.Clear();
        }

        var states = host.World.State.Vehicles.Select(state =>
        {
            VehiclePhysicsState pose = host.World.Arena.Spawn((int)(state.VehicleId - 1));
            if (state.VehicleId == _victim)
            {
                pose = new VehiclePhysicsState(_cycle % 2 == 0 ? new Numerics.Vector3(-40, 0.6f, -5) : new Numerics.Vector3(-57.5f, 0.6f, -35), Numerics.Quaternion.Identity, _cycle % 2 == 0 ? Numerics.Vector3.Zero : new Numerics.Vector3(-60, 0, 0), Numerics.Vector3.Zero);
            }
            else if (state.VehicleId == shooter)
            {
                pose = new VehiclePhysicsState(new Numerics.Vector3(-40, 0.6f, 5), Numerics.Quaternion.Identity, Numerics.Vector3.Zero, Numerics.Vector3.Zero);
            }

            return new VehicleSnapshot(state.VehicleId, state.LifeId, new VehicleState(host.World.State.Tick, pose, false, false, 0, 0), new VehicleDamageState(state.Damage.MaxHP, state.VehicleId == _victim ? 20 : state.Damage.MaxHP, null, null), pose);
        }).ToArray();
        host.World.Restore(new SimulationState(host.World.State.Tick, host.World.State.LastInput, states));
        Require(host.Items.Grant(host.World, _victim, HeldItem.Wrench), "Victim holds an item before death.");
        if (_cycle % 2 == 0)
        {
            Require(host.Items.Grant(host.World, shooter, HeldItem.Missile), "Shooter receives a missile.");
        }

        _stage = 1;
        _started = _elapsed;
        _captured = false;
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
