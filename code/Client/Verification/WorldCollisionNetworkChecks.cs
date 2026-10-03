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

/// <summary>Actual tunnel-bank pressure, steering and escape through impaired UDP authority/prediction worlds.</summary>
public sealed partial class WorldCollisionNetworkChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<double> _times = new();
    private int _frames;
    private int _boundary;
    private int _trial = -1;
    private float _maximumCorrection;
    private int _contactFrames;
    private float _escape;
    private bool _done;

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}"; socket.Close();
        for (int index = 0; index < 2; index++)
        {
            var gateway = new GameNetworkingSocketsTransport(); ulong server = 0;
            if (index == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); gateway.ConfigureSimulation(new(30, 5, 2, 0, 0)); }
            else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
            _gateways.Add(gateway);
            var view = new SubViewport { OwnWorld3D = true, Size = new(320, 180) }; AddChild(view);
            var arena = new NetworkVehicleArena(); arena.Initialize(gateway, index == 0 ? 274ul : 0, server); view.AddChild(arena); _arenas.Add(arena);
            if (OS.GetCmdlineUserArgs().Contains("original-terrain"))
            {
                foreach (var mesh in arena.FindChildren("InfieldTerrain*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                    foreach (var shape in mesh.FindChildren("*", "CollisionShape3D", true, false).OfType<CollisionShape3D>()) { shape.Shape = mesh.Mesh.CreateTrimeshShape(); }
            }
        }
        _arenas[1].Driver.LocalCorrected += _ =>
        {
            if (_frames - _boundary > 90) { _maximumCorrection = Math.Max(_maximumCorrection, _arenas[1].Driver.Prediction!.PredictionError); }
        };
    }

    public override void _PhysicsProcess(double delta)
    {
        // Match the other UDP fixtures: drain queued arena/audio teardown before
        // exiting, otherwise native playback references survive engine shutdown.
        if (_done) { if (++_frames > _boundary + 20) { GetTree().Quit(); } return; }
        try
        {
            _frames++;
            Require(_frames < 3000, "world UDP timeout");
            int elapsed = _frames - _boundary;
            bool drive = _trial >= 0 && (elapsed < 210 || elapsed is >= 300 and < 420);
            bool reverse = _trial >= 0 && (elapsed is >= 225 and < 300 || elapsed >= 435);
            var input = new InputFrame(0, _trial == 0 ? (short)0 : short.MaxValue, drive ? ushort.MaxValue : (ushort)0, reverse ? ushort.MaxValue : (ushort)0, 0, 0, 0);
            var watch = Stopwatch.StartNew();
            foreach (var arena in _arenas) { arena.Advance(input); }
            watch.Stop();
            if (_trial >= 0 && elapsed >= 30) { _times.Add(watch.Elapsed.TotalMilliseconds); }
            var host = _arenas[0].Driver.Host!;
            if (_trial < 0 && host.World.State.Vehicles.Count == 2 && _arenas[1].Driver.LocalState is not null) { _trial = 0; Position(); return; }
            if (_trial < 0) { return; }
            if (host.World.State.Vehicles.Any(v => Math.Abs(v.ObservedPhysics.Position.X) > 6)) { _contactFrames++; }
            if (elapsed >= 435) { _escape = Math.Max(_escape, 6.5f - Math.Abs(host.World.GetVehicle(1).ObservedPhysics.Position.X)); }
            if (elapsed < 660) { return; }
            double mean = _times.Average();
            double p95 = _times.Order().ElementAt((int)((_times.Count - 1) * 0.95));
            GD.Print($"WORLD_UDP speed={Speed()} steering={_trial != 0} contactRegionFrames={_contactFrames} escape={_escape:F3} meanMs={mean:F3} p95Ms={p95:F3} correction={_maximumCorrection:F3}");
            Require(_contactFrames > 30, "sustained tunnel-bank contact region reached");
            Require(_escape > 2, "ordinary reverse leaves the original bank contact region");
            Require(mean < 30 && p95 < 30, "two-world collision/prediction does not repeatedly stall a frame");
            foreach (var vehicle in host.World.State.Vehicles)
            {
                var remote = _arenas[1].Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == vehicle.VehicleId).State;
                Require(N.Vector3.Distance(remote.ObservedPhysics.Position, vehicle.ObservedPhysics.Position) < 1.5f, "peer poses converge within publication latency");
                Require(remote.Damage == vehicle.Damage, "host-owned collision damage converges");
            }
            if (++_trial < 3) { Position(); return; }
            GD.Print("World collision multiplayer passed: actual tunnel banks, two UDP worlds, 30ms delay/5ms jitter/2% loss, 3/12/35m/s, sustained steering, reverse and re-contact.");
            _done = true; _boundary = _frames;
            foreach (var arena in _arenas) { arena.QueueFree(); }
            _arenas.Clear();
            foreach (var gateway in _gateways) { gateway.Dispose(); }
            _gateways.Clear();
        }
        catch (Exception exception) { _done = true; GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    public override void _ExitTree()
    {
        foreach (var arena in _arenas) { arena.QueueFree(); }
        foreach (var gateway in _gateways) { gateway.Dispose(); }
    }

    private float Speed() => new[] { 3f, 12f, 35f }[_trial];

    private void Position()
    {
        _boundary = _frames; _maximumCorrection = 0; _times.Clear(); _contactFrames = 0; _escape = 0;
        var host = _arenas[0].Driver.Host!; var world = host.World.State;
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(vehicle =>
        {
            float side = vehicle.VehicleId == 1 ? 1 : -1;
            var pose = new VehiclePhysicsState(new(0, VehicleDimensions.RideHeight, side * 2), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -side * MathF.PI / 2), new(side * Speed(), 0, 0), N.Vector3.Zero);
            _arenas[0].Bodies[vehicle.VehicleId].Apply(pose);
            return new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId + 1, new VehicleState(world.Tick, pose, true, false, 0, 0), new VehicleDamageState(vehicle.Damage.MaxHP, vehicle.Damage.MaxHP, null, null), pose);
        }), world.Match));
    }

    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
