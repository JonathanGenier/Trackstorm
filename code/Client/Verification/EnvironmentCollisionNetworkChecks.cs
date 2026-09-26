using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Real UDP peers verify that static scraping and crash HP remain host-authoritative.</summary>
public sealed partial class EnvironmentCollisionNetworkChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private int _frames;
    private int _boundary;
    private int _stage;
    private bool _done;
    private float _contactCorrectionMaximum;
    private readonly HashSet<ulong> _contactDamaged = new();
    private ulong _contactSampleLife;

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        for (int index = 0; index < 2; index++)
        {
            var gateway = new GameNetworkingSocketsTransport();
            ulong server = 0;
            if (index == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
            else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
            _gateways.Add(gateway);
            var view = new SubViewport { OwnWorld3D = true, Size = new(640, 360) };
            AddChild(view);
            var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
            arena.Initialize(gateway, index == 0 ? 161ul : 0, server);
            view.AddChild(arena);
            AddBox(arena, new(0, 19, 1000), new(200, 2, 1000));
            AddBox(arena, new(-5, 23, 1000), new(2, 8, 1000));
            AddBox(arena, new(11, 23, 1000), new(2, 8, 1000));
            _arenas.Add(arena);
            if (index == 1)
            {
                arena.Driver.LocalCorrected += state =>
                {
                    if (_stage == 3 && _contactSampleLife == state.LifeId)
                    {
                        _contactCorrectionMaximum = Math.Max(_contactCorrectionMaximum, arena.Driver.Prediction!.PredictionError);
                    }
                    _contactSampleLife = state.LifeId;
                };
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) { if (++_frames > _boundary + 20) { GetTree().Quit(); } return; }
        try
        {
            _frames++;
            foreach (var arena in _arenas) { arena.Advance(default); Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure); }
            Require(_frames < 1800, "network collision timeout");
            var host = _arenas[0].Driver.Host!;
            if (_stage == 3)
            {
                foreach (var vehicle in host.World.State.Vehicles)
                {
                    if (vehicle.Damage.LastDamage?.Attribution.Context == "vehicle") { _contactDamaged.Add(vehicle.VehicleId); }
                }
            }
            if (_stage == 0 && _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2))
            {
                Position(false); _stage = 1; _boundary = _frames;
            }
            else if (_stage == 1 && _frames - _boundary > 210)
            {
                Require(host.World.State.Vehicles.All(v => v.Damage.CurrentHP == v.Damage.MaxHP), "scrape must not damage host state");
                Require(_arenas[1].Driver.Latest!.Vehicles.All(v => v.State.Damage.CurrentHP == v.State.Damage.MaxHP), "client receives harmless scrape HP");
                Require(host.World.State.Vehicles.All(v => v.Movement.Physics.Position.Z < 1170), "both vehicles move along barrier");
                GD.Print("Two impaired UDP peers: native shallow scraping remains non-damaging and continues along the wall.");
                Position(true); _stage = 2; _boundary = _frames;
            }
            else if (_stage == 2 && _frames - _boundary > 180)
            {
                foreach (var vehicle in host.World.State.Vehicles)
                {
                    Require(vehicle.Damage.CurrentHP < vehicle.Damage.MaxHP && vehicle.Speed < 1, "host stops and damages direct impact");
                    var remote = _arenas[1].Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == vehicle.VehicleId).State;
                    Require(remote.Damage.CurrentHP == vehicle.Damage.CurrentHP && remote.Damage.LastDamage?.Sequence == vehicle.Damage.LastDamage?.Sequence, "client HP and damage sequence match host");
                }
                PositionContact(); _stage = 3; _boundary = _frames;
            }
            else if (_stage == 3 && _frames - _boundary > 180)
            {
                Require(_contactDamaged.Count > 0, "head-on contact must produce authoritative vehicle attribution");
                foreach (var vehicle in host.World.State.Vehicles)
                {
                    GD.Print($"Contact participant {vehicle.VehicleId}: HP {vehicle.Damage.CurrentHP}, latest source {vehicle.Damage.LastDamage?.Attribution.Context}, position {vehicle.Movement.Physics.Position}");
                    var remote = _arenas[1].Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == vehicle.VehicleId).State;
                    Require(remote.Damage == vehicle.Damage, "contact damage remains host-owned and converges");
                    Require(N.Vector3.Distance(remote.Movement.Physics.Position, vehicle.Movement.Physics.Position) < 0.2f, "settled contact boundary converges");
                }
                GD.Print($"Environment collision multiplayer passed: two native UDP peers, 30 ms delay / 5 ms jitter / 2% loss; scrape, crash and head-on vehicle damage agree; contact correction max={_contactCorrectionMaximum:F4}m (excludes deliberate new-life placement).");
                _done = true;
                _boundary = _frames;
                foreach (var arena in _arenas) { arena.QueueFree(); }
                foreach (var gateway in _gateways) { gateway.Dispose(); }
                _gateways.Clear();
            }
        }
        catch (Exception exception) { _done = true; GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private void Position(bool direct)
    {
        var host = _arenas[0].Driver.Host!;
        var world = host.World.State;
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(vehicle =>
        {
            N.Vector3 velocity = direct ? new(35, 0, 0) : new(3, 0, -35);
            var orientation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -MathF.Atan2(velocity.X, -velocity.Z));
            var pose = new VehiclePhysicsState(new(vehicle.VehicleId == 1 ? -8 : 8, 21.145f, 1200), orientation, velocity, N.Vector3.Zero);
            _arenas[0].Bodies[vehicle.VehicleId].Apply(pose);
            return new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId, new VehicleState(world.Tick, pose, true, false, 0, 0), vehicle.Damage, pose);
        }), world.Match));
    }

    private void PositionContact()
    {
        var host = _arenas[0].Driver.Host!;
        var world = host.World.State;
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(vehicle =>
        {
            float direction = vehicle.VehicleId == 1 ? 1 : -1;
            var pose = new VehiclePhysicsState(new(3 - direction * 4, 21.145f, 1000),
                N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -direction * MathF.PI / 2), new(direction * 10, 0, 0), N.Vector3.Zero);
            _arenas[0].Bodies[vehicle.VehicleId].Apply(pose);
            return new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId + 1, new VehicleState(world.Tick, pose, true, false, 0, 0),
                new VehicleDamageState(vehicle.Damage.MaxHP, vehicle.Damage.MaxHP, null, null), pose);
        }), world.Match));
    }

    private static void AddBox(Node parent, Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D { Position = position };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }

    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }

    public override void _ExitTree() { foreach (var gateway in _gateways) { gateway.Dispose(); } }
}
