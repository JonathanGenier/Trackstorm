using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Rock pressure and separation through real impaired UDP authority and prediction worlds.</summary>
public sealed partial class RockCollisionNetworkChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<double> _stepTimes = new();
    private int _frames;
    private int _boundary;
    private int _trial = -1;
    private float _closest = float.MaxValue;
    private float _maximumCorrection;
    private bool _done;
    private readonly Dictionary<ulong, List<float>> _pressureHeights = new();

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}"; socket.Close();
        for (int index = 0; index < 2; index++)
        {
            var gateway = new GameNetworkingSocketsTransport(); ulong server = 0;
            if (index == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
            else server = gateway.Connect(TransportEndpoint.DirectIp(endpoint));
            _gateways.Add(gateway);
            var view = new SubViewport { OwnWorld3D = true, Size = new(320, 180) }; AddChild(view);
            var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
            arena.Initialize(gateway, index == 0 ? 267ul : 0, server); view.AddChild(arena); _arenas.Add(arena);
            var floor = new StaticBody3D { Position = new(0, 19, 1000) }; floor.AddToGroup("landing_terrain");
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 2, 1000) } }); arena.AddChild(floor);
            foreach (float x in new[] { -15f, 15f })
            {
                var rock = GD.Load<PackedScene>("res://assets/environment/models/RockCluster.glb").Instantiate<Node3D>();
                rock.Position = new(x, 20, 1200); rock.Scale = Vector3.One * 2; arena.AddChild(rock);
                // Prototype worlds have no destructible authority; retain these
                // intact fixtures while exercising the production rock contact path.
                foreach (var collider in rock.FindChildren("*", "StaticBody3D", true, false).OfType<StaticBody3D>())
                {
                    collider.SetMeta("environment_rock", 1);
                    collider.CollisionLayer |= Arenas.DestructibleEnvironment.RockContactLayer;
                }
            }
        }
        _arenas[1].Driver.LocalCorrected += _ =>
        {
            if (_frames - _boundary > 90) _maximumCorrection = Math.Max(_maximumCorrection, _arenas[1].Driver.Prediction!.PredictionError);
        };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) { if (++_frames > _boundary + 20) GetTree().Quit(); return; }
        try
        {
            _frames++;
            Require(_frames < 5300, "rock UDP timeout");
            int elapsed = _frames - _boundary;
            bool drive = _trial >= 0 && (elapsed < 180 || elapsed is >= 240 and < 330 || elapsed is >= 390 and < 480);
            bool reverse = _trial >= 0 && (elapsed is >= 195 and < 240 || elapsed is >= 345 and < 390 || elapsed >= 495);
            if (_trial == 6) { drive = elapsed < 420; reverse = elapsed >= 435; }
            var input = new InputFrame(0, 0, drive ? ushort.MaxValue : (ushort)0, reverse ? ushort.MaxValue : (ushort)0, 0, 0, 0);
            var watch = Stopwatch.StartNew();
            foreach (var arena in _arenas) { arena.Advance(input); Require(arena.Driver.Failure.Length == 0, arena.Driver.Failure); }
            watch.Stop();
            if (_trial >= 0 && elapsed > 90) _stepTimes.Add(watch.Elapsed.TotalMilliseconds);
            if (!_arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2)) return;
            if (_trial < 0) { _trial = 0; Position(); return; }
            var host = _arenas[0].Driver.Host!;
            if (_trial == 6 && elapsed is >= 120 and < 420)
            {
                foreach (var vehicle in host.World.State.Vehicles)
                {
                    if (!_pressureHeights.TryGetValue(vehicle.VehicleId, out var heights)) { heights = new(); _pressureHeights.Add(vehicle.VehicleId, heights); }
                    heights.Add(vehicle.Movement.Physics.Position.Y);
                }
            }
            foreach (var vehicle in host.World.State.Vehicles)
                _closest = Math.Min(_closest, N.Vector3.Distance(vehicle.Movement.Physics.Position, new(vehicle.VehicleId == 1 ? -15 : 15, 21.145f, 1200)));
            if (elapsed < 705) return;
            Require(_closest < 7, "vehicles reached rock contact region");
            foreach (var vehicle in host.World.State.Vehicles)
            {
                var remote = _arenas[1].Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == vehicle.VehicleId).State;
                Require(N.Vector3.Distance(vehicle.Movement.Physics.Position, new(vehicle.VehicleId == 1 ? -15 : 15, 21.145f, 1200)) > 12, "reverse separates from rock");
                Require(N.Vector3.Distance(remote.Movement.Physics.Position, vehicle.Movement.Physics.Position) < 1.5f, "moving peer poses converge within publication latency");
                Require(remote.Damage == vehicle.Damage, "rock damage remains host-owned and converges");
            }
            double mean = _stepTimes.Average();
            double p95 = _stepTimes.Order().ElementAt((int)((_stepTimes.Count - 1) * 0.95));
            GD.Print($"Rock UDP trial={_trial} speed={Speed()} closest={_closest:F3} meanStepMs={mean:F3} p95StepMs={p95:F3} maxCorrection={_maximumCorrection:F3}");
            if (_trial == 6)
            {
                foreach (var (id, heights) in _pressureHeights)
                {
                    float range = heights.Max() - heights.Min();
                    float lift = heights.Zip(heights.Skip(1), (first, second) => second - first).Max();
                    GD.Print($"Rock UDP stationary pressure vehicle={id} range={range:F6} peakLiftStep={lift:F6}");
                    Require(heights.Count == 300 && range < 0.05f && lift < 0.005f, "stationary pressure settles without repeated lift");
                }
                Require(_pressureHeights.Count == 2, "both drivers exercised stationary rock pressure");
            }
            Require(mean < 30, "two-world collision/prediction mean step budget");
            Require(p95 < 30, "sustained collision/prediction must not repeatedly stall a frame");
            if (++_trial < 7) { Position(); return; }
            GD.Print("Rock collision multiplayer passed: two UDP worlds, 30ms delay/5ms jitter/2% loss, direct/offset impacts at 3/12/35m/s, stationary throttle pressure, repeated contact and reverse separation.");
            _done = true;
            _boundary = _frames;
            foreach (var arena in _arenas) arena.QueueFree();
            foreach (var gateway in _gateways) gateway.Dispose();
            _gateways.Clear();
        }
        catch (Exception exception) { _done = true; GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private float Speed() => _trial == 6 ? 0 : new[] { 3f, 12f, 35f }[_trial / 2];

    private void Position()
    {
        _boundary = _frames; _closest = float.MaxValue; _maximumCorrection = 0; _stepTimes.Clear();
        var host = _arenas[0].Driver.Host!; var world = host.World.State;
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(vehicle =>
        {
            var pose = new VehiclePhysicsState(new((vehicle.VehicleId == 1 ? -15 : 15) + (_trial % 2 == 0 ? 0 : 2), 21.145f, 1210),
                N.Quaternion.Identity, new(0, 0, -Speed()), N.Vector3.Zero);
            if (_trial == 6)
            {
                using var query = new PhysicsTestMotionParameters3D { From = new(Basis.Identity, VehicleBody.ToGodot(pose.Position)), Motion = new(0, 0, -15), Margin = 0.005f };
                using var hit = new PhysicsTestMotionResult3D();
                Require(PhysicsServer3D.BodyTestMotion(_arenas[0].Bodies[vehicle.VehicleId].GetRid(), query, hit), "pressure setup reaches rock");
                pose = new(pose.Position + VehicleBody.ToCore(hit.GetTravel()) + new N.Vector3(0, 0, 0.01f), pose.Orientation, N.Vector3.Zero, N.Vector3.Zero);
            }
            _arenas[0].Bodies[vehicle.VehicleId].Apply(pose);
            return new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId + 1, new VehicleState(world.Tick, pose, true, false, 0, 0),
                new VehicleDamageState(vehicle.Damage.MaxHP, vehicle.Damage.MaxHP, null, null), pose);
        }), world.Match));
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
