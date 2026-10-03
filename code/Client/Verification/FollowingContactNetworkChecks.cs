using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Actual UDP prediction during sustained rear contact, with bounded per-tick traces.</summary>
public sealed partial class FollowingContactNetworkChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<object> _trace = new();
    private int _frame;
    private int _start;
    private bool _placed;
    private bool _done;
    private float _correction;
    private N.Vector3 _previous;
    private readonly List<float> _corrections = new();
    private int _stopped;
    private int _backwards;
    private int _closeFrames;
    private float _minimumVisualGap = float.MaxValue;

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}"; socket.Close();
        for (int index = 0; index < 4; index++)
        {
            var gateway = new GameNetworkingSocketsTransport(); ulong server = 0;
            if (index == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); gateway.ConfigureSimulation(new(30, 5, 2, 0, 0)); }
            else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
            _gateways.Add(gateway);
            var view = new SubViewport { OwnWorld3D = true, Size = new(960, 540), RenderTargetUpdateMode = SubViewport.UpdateMode.Always }; AddChild(view);
            var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
            arena.Initialize(gateway, index == 0 ? 280ul : 0, server); view.AddChild(arena); _arenas.Add(arena);
            var floor = new StaticBody3D { Position = new(0, 19, 1000) }; floor.AddToGroup("landing_terrain");
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 2, 2000) } }); arena.AddChild(floor);
            floor.AddChild(Vehicles.VehicleBody.Box(new(200, 2, 2000), Vector3.Zero, new Color("80634b")));
        }
        _arenas[1].Driver.LocalCorrected += _ =>
        {
            if (_placed && _frame - _start > 90) _correction = _arenas[1].Driver.Prediction!.PredictionError;
        };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) { if (++_frame > _start + 20) GetTree().Quit(); return; }
        try
        {
            _frame++;
            if (_frame > 1800) throw new InvalidOperationException("Following contact timed out.");
            int elapsed = _frame - _start;
            for (int index = 0; index < _arenas.Count; index++)
            {
                float throttle = _placed && elapsed < 720 ? (index % 2 == 0 ? 0.25f : 0.4f) : 0;
                _arenas[index].Advance(new InputFrame(0, 0, (ushort)(ushort.MaxValue * throttle), 0, 0, 0, 0));
                if (_arenas[index].Driver.Failure.Length != 0) throw new InvalidOperationException(_arenas[index].Driver.Failure);
            }
            if (!_arenas.All(a => a.Driver.Latest?.Vehicles.Count == 4)) return;
            var host = _arenas[0].Driver.Host!;
            if (!_placed)
            {
                _start = _frame; _placed = true;
                ulong follower = _arenas[1].Driver.LocalVehicleId;
                ulong secondLeader = _arenas[2].Driver.LocalVehicleId;
                var world = host.World.State;
                host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(vehicle =>
                {
                    bool firstLane = vehicle.VehicleId == 1 || vehicle.VehicleId == follower;
                    bool leader = vehicle.VehicleId == 1 || vehicle.VehicleId == secondLeader;
                    var pose = new VehiclePhysicsState(new(firstLane ? -15 : 15, 21.145f, leader ? 1200 : 1205.2f), N.Quaternion.Identity, new(0, 0, -10), N.Vector3.Zero);
                    _arenas[0].Bodies[vehicle.VehicleId].Apply(pose);
                    return new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId + 1, new VehicleState(world.Tick, pose, true, false, 0, 0),
                        new VehicleDamageState(vehicle.Damage.MaxHP, vehicle.Damage.MaxHP, null, null), pose);
                }), world.Match));
                return;
            }
            var local = _arenas[1].LocalState!;
            var authority = host.World.GetVehicle(local.VehicleId);
            var lead = host.World.GetVehicle(1);
            var position = local.Movement.Physics.Position;
            float visualGap = _arenas[1].Bodies[local.VehicleId].VisualPosition.Z - _arenas[1].Bodies[1].VisualPosition.Z;
            if (elapsed is > 120 and < 650)
            {
                _corrections.Add(_correction);
                _minimumVisualGap = Math.Min(_minimumVisualGap, visualGap);
                if (Math.Abs(position.Z - _previous.Z) < 0.03f) _stopped++;
                if (position.Z - _previous.Z > 0.1f) _backwards++;
                if (Math.Abs(authority.Movement.Physics.Position.Z - lead.Movement.Physics.Position.Z) < 5.9f) _closeFrames++;
            }
            _trace.Add(new { elapsed, tick = host.World.State.Tick, z = position.Z, y = position.Y,
                travel = position.Z - _previous.Z, speed = local.ObservedPhysics.LinearVelocity.Z, correction = _correction,
                hostZ = authority.Movement.Physics.Position.Z, hostSpeed = authority.ObservedPhysics.LinearVelocity.Z,
                leadZ = lead.Movement.Physics.Position.Z, visualGap, hp = authority.Damage.CurrentHP });
            _previous = position; _correction = 0;
            if (elapsed < 960) return;
            string directory = ProjectSettings.GlobalizePath("res://.godot/ts-280"); System.IO.Directory.CreateDirectory(directory);
            string label = OS.GetCmdlineUserArgs().FirstOrDefault() ?? "following-network";
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, label + ".json"), JsonSerializer.Serialize(_trace));
            float p95 = _corrections.Order().ElementAt((int)(_corrections.Count * 0.95));
            GD.Print($"Following contact: closeFrames={_closeFrames}, stopped={_stopped}, backwards={_backwards}, correctionP95={p95:F6}, correctionMax={_corrections.Max():F6}, minimumVisualGap={_minimumVisualGap:F3}");
            if (_closeFrames < 300 || _stopped != 0 || _backwards != 0 || p95 > 0.15f || _corrections.Max() > 0.75f)
                throw new InvalidOperationException("Repeated contact must maintain continuous predicted travel and bounded correction.");
            if (_minimumVisualGap < VehicleDimensions.Length)
                throw new InvalidOperationException("Buffered presentation must not overlap the predicted following vehicle.");
            foreach (var arena in _arenas.Skip(1))
            foreach (var vehicle in host.World.State.Vehicles)
            {
                var remote = arena.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == vehicle.VehicleId).State;
                if (remote.Damage != vehicle.Damage) throw new InvalidOperationException("Following contact damage must converge on all four peers.");
            }
            if (DisplayServer.GetName() != "headless")
            {
                using var image = _arenas[1].GetViewport().GetTexture().GetImage();
                image.SavePng(System.IO.Path.Combine(directory, label + ".png"));
            }
            GD.Print("Following contact network checks passed: four UDP worlds, two following pairs, 30ms delay/5ms jitter/2% loss.");
            _done = true; _start = _frame;
            foreach (var arena in _arenas) arena.QueueFree();
            foreach (var gateway in _gateways) gateway.Dispose();
        }
        catch (Exception exception) { _done = true; GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
}
