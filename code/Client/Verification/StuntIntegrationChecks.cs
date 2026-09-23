using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native motion and UDP stunt observation on an isolated flat test platform.</summary>
public sealed partial class StuntIntegrationChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _transports = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<string> _evidence = new();
    private InputFrame _input;
    private bool _running;
    private string _output = string.Empty;
    private ulong _largestDrift;
    private double _largestDriftPoints;
    private bool _concurrent;
    private HostVehicleSession Host => _arenas[0].Driver.Host!;
    private PlayerScore Score => Host.World.State.Match!.Players.Single(player => player.Player == Host.HostPlayerId);

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (!_running) { return; }
        foreach (var arena in _arenas) { arena.Advance(arena == _arenas[0] ? _input : default); }
        if (Host.World.State.Match is not null && Score.Stunts is { } pending)
        {
            if (pending.Drift.Ticks > _largestDrift) { _largestDrift = pending.Drift.Ticks; _largestDriftPoints = pending.Drift.BasePoints; }
            _concurrent |= pending.Airtime.Ticks > 0 && pending.TopSpeed.Ticks > 0;
        }
    }

    /// <summary>Runs deterministic fixture setup followed by production native simulation and network publication.</summary>
    public async void Run()
    {
        try
        {
            Engine.PhysicsTicksPerSecond = 60;
            Engine.MaxFps = 120;
            _output = OS.GetCmdlineUserArgs().Single(arg => arg.StartsWith("--stunt-output=", StringComparison.Ordinal))[15..];
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            reservation.Close();
            for (int i = 0; i < 2; i++)
            {
                var gateway = new GameNetworkingSocketsTransport();
                _transports.Add(gateway);
                ulong server = 0;
                if (i == 0)
                {
                    gateway.Listen(TransportEndpoint.DirectIp(endpoint));
                    if (OS.GetCmdlineUserArgs().Contains("--stunt-impaired")) { gateway.ConfigureSimulation(new(30, 5, 2, 0, 0)); }
                }
                else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
                var viewport = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true };
                AddChild(viewport);
                var arena = new NetworkVehicleArena();
                arena.Initialize(gateway, i == 0 ? 103ul : 0, server);
                viewport.AddChild(arena);
                var platform = new StaticBody3D { Position = new Vector3(0, -0.5f, 1000), CollisionLayer = 1 };
                platform.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(600, 1, 600) } });
                viewport.AddChild(platform);
                _arenas.Add(arena);
            }
            _running = true;
            await Until(() => _arenas.All(arena => arena.Driver.Match?.Phase == MatchPhase.Active), 600, "two native peers enter Active");
            // Only scoring tiers change for the short native drift. Vehicle physics remains production tuning.
            Require(_arenas[0].Driver.TryConfigure(new Dictionary<string, double> { ["match.drift_tier_seconds"] = 0.1 }, out var error), error);
            Place(new(12, 0, -27));
            _input = new(0, 0, 0, 0, InputButtons.Drift, 0, 0);
            double before = Score.CircusScore;
            await Frames(150);
            _input = default;
            await Until(() => Score.Stunts is null, 300, "native drift settles and banks");
            Require(_largestDrift >= 15 && _largestDriftPoints > _largestDrift * 5d / 60, $"Physical drift escalates: {_largestDrift} ticks, {_largestDriftPoints} pending base points.");
            Require(Score.CircusScore > before, "Supported drift completion banks native motion.");
            Record($"Native drift: {_largestDrift} uninterrupted ticks, {_largestDriftPoints:F6} peak pending base points, total award {Score.CircusScore - before:F6} (includes any concurrent Top Speed).");
            await Agree();

            Place(new(0, 0, -(new VehicleConfiguration().ForwardSpeed * 1.05f)));
            _input = new(0, 0, 65535, 0, 0, 0, 0);
            await Frames(120);
            var top = Score.Stunts!.TopSpeed;
            Require(top.Ticks >= 100 && Math.Abs(top.BasePoints - top.Ticks * 5d / 60) < 1e-8, "Sustained native top-speed accrual is exactly five base points per second.");
            before = Score.CircusScore;
            _input = new(0, 0, 0, 65535, 0, 0, 0);
            await Until(() => Score.Stunts?.TopSpeed.Ticks is null or 0, 180, "braking exits top-speed threshold");
            Require(Score.CircusScore > before, "Top Speed banks on threshold exit.");
            _input = default;
            await Frames(120);
            await Agree();
            Record($"Native top speed: {top.Ticks} ticks produced {top.BasePoints:F6} pending base points; braking banked once.");

            double shortDistance = await Jump(10);
            double longDistance = await Jump(20);
            Require(longDistance > shortDistance * 1.7, "Longer native jump travels farther with unchanged vertical launch.");
            await Jump((new VehicleConfiguration().ForwardSpeed * 1.05f));
            Require(_concurrent, "Native airborne and Top Speed events coexist independently.");

            Place(new(0, 15, -(new VehicleConfiguration().ForwardSpeed * 1.05f)));
            await Frames(40);
            Require(Score.Stunts is { Airtime.Ticks: > 15 } && Score.PendingStuntScore > 0, "Death fixture begins with actual native pending flight.");
            before = Score.CircusScore;
            // Deterministic lethal gameplay effect while the native vehicle is still airborne.
            var frame = new InputFrame(Host.World.State.Tick + 1, 0, 0, 0, 0, 0, 0);
            Host.World.Step(frame, Host.World.State.Vehicles.Select(vehicle => new VehicleStepRequest(vehicle.VehicleId, frame,
                _arenas[0].Bodies[vehicle.VehicleId].Observe(vehicle), vehicle.VehicleId == Host.HostPlayerId
                    ? [new VehicleEffectRequest(new DamageEffect(10000, N.Vector3.Zero, N.Vector3.Zero), new DamageContext("explosion", 0, "native stunt death fixture"))] : null)).ToArray());
            Require(Score.Stunts is null && Score.CircusScore == before, "Lethal effect cancels pending, retaining all prior native awards.");
            await Frames(240);
            await Agree();
            Record("Native airborne death: applied lethal Core effect discarded pending flight/Top Speed; respawn and both UDP peers retained prior banked score.");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
            GD.Print("Stunt integration passed.");
            Cleanup();
            await Frames(30);
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            Cleanup();
            GetTree().Quit(1);
        }
    }

    private async Task<double> Jump(float speed)
    {
        Place(new(0, 15, -speed));
        double before = Score.CircusScore;
        await Frames(150);
        StuntState pending = Score.Stunts!;
        Require(pending.Airtime.Ticks > 120 && pending.Airtime.BasePoints > pending.Airtime.Ticks * 5d / 60, "Native uninterrupted airtime crosses its escalation tier.");
        Require(Math.Abs(pending.LongJumpBasePoints - pending.JumpDistance * Host.Configuration.Configuration.Match.JumpPointsPerMetre) < 1e-8, "Native Long Jump pending uses distance.");
        double distance = pending.JumpDistance;
        await Until(() => Score.Stunts?.Airtime.Ticks is null or 0, 180, "successful native landing");
        Require(Score.CircusScore > before, "Living landing banks native flight.");
        await Until(() => Score.Stunts is null, 300, "remaining independent top-speed event completes");
        await Agree();
        Record($"Native jump at {speed} m/s: {pending.Airtime.Ticks} airborne ticks and {distance:F6} m observed before landing; banked {Score.CircusScore - before:F6} including independent speed awards.");
        return distance;
    }

    private void Place(N.Vector3 velocity)
    {
        Require(Score.Stunts is null, "Fixture setup never clears a live pending award.");
        var state = Host.World.State;
        var vehicles = state.Vehicles.Select(vehicle =>
        {
            var pose = new VehiclePhysicsState(new N.Vector3(vehicle.VehicleId == Host.HostPlayerId ? 0 : 100, 0.9f, 1000), N.Quaternion.Identity,
                vehicle.VehicleId == Host.HostPlayerId ? velocity : N.Vector3.Zero, N.Vector3.Zero);
            return new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId, new VehicleState(state.Tick, pose, true, false, 0, 0), vehicle.Damage, pose);
        });
        Host.World.Restore(new SimulationState(state.Tick, state.LastInput, vehicles, state.Match));
    }

    private async Task Agree()
    {
        await Until(() => _arenas.All(arena => arena.Driver.Match!.Players.SequenceEqual(Host.World.State.Match!.Players)), 180, "UDP peers agree on complete pending/banked state");
    }

    private async Task Until(Func<bool> predicate, int frames, string message)
    {
        for (int i = 0; i < frames && !predicate(); i++) { await Frames(1); }
        Require(predicate(), message);
    }

    private async Task Frames(int frames)
    {
        for (int i = 0; i < frames; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private void Record(string message) { _evidence.Add(message); GD.Print(message); }

    private void Cleanup()
    {
        _running = false;
        foreach (var arena in _arenas) { arena.QueueFree(); }
        foreach (var gateway in _transports) { gateway.ConfigureSimulation(new()); gateway.Dispose(); }
    }
}
