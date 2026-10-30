using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Sessions;
using Trackstorm.Core.Matches;

namespace Trackstorm.Client.Verification;

/// <summary>Application lobby, entry and moving practice car over native impaired UDP.</summary>
public sealed partial class HostedPracticeChecks : Node
{
    private readonly List<DevelopmentSession> _sessions = new();
    private string _endpoint = string.Empty;
    private bool _done;

    public override void _Ready() { Engine.MaxFps = 60; CallDeferred(MethodName.Run); }
    public override void _PhysicsProcess(double delta)
    {
        if (!_done) { foreach (var session in _sessions) { session.Advance(default); } }
    }

    private DevelopmentSession AddPeer(bool host)
    {
        var viewport = new SubViewport { OwnWorld3D = true, Size = new(640, 360), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(viewport);
        var session = new DevelopmentSession { AutomaticPracticeCar = true };
        viewport.AddChild(session); _sessions.Add(session);
        session.Open(host, _endpoint, host ? "Host" : "Observer");
        session.Gateway!.ConfigureSimulation(new(30, 5, 2));
        return session;
    }

    private async void Run()
    {
        try
        {
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            reservation.Close();
            var host = AddPeer(true);
            var remote = AddPeer(false);
            await Until(() => _sessions.All(session => session.Lobby?.State?.Players.Count == 6), "host, four practice targets and observer roster");
            if (OS.GetCmdlineUserArgs().Contains("--old-map")) { host.Lobby!.Authority!.SelectMap(0, MatchMap.OldMap); }
            int targetCount = OS.GetCmdlineUserArgs().Contains("--old-map") ? 2 : 4;
            await Until(() => _sessions.All(session => session.Lobby?.State?.Players.Count == targetCount + 2), "map target roster converged");
            remote.Lobby!.Request(LobbyCommand.Ready, true);
            await Until(() => host.Lobby!.State!.CanStart, "ready");
            Require(host.StartFromLobby(), "ordinary host Start accepted");
            await Until(() => _sessions.All(session => session.Arena?.Driver.Match?.Phase == MatchPhase.Active), "active gameplay");
            ulong[] cars = host.Lobby!.State!.Players.Where(player => player.PracticeCar).Select(player => player.Id).Order().ToArray();
            ulong car = cars[0], follower = cars[1];
            if (targetCount == 4) { await WaitForPondTrip(host, cars[3]); }
            if (OS.GetCmdlineUserArgs().Contains("--pond-cycle"))
            {
                await CheckParkedTarget(host, remote, cars[3]);
                if (OS.GetCmdlineUserArgs().Contains("--visual")) { await CaptureTargets(host, [cars[3]]); }
                foreach (var session in _sessions) { session.Leave(); }
                await Until(() => _sessions.All(session => session.LeaveComplete), "pond check departure");
                _done = true; GD.Print("Hosted practice passed: pond pickup, wait, reverse, park and repeated lives.");
                GetTree().Quit(); return;
            }
            await Frames(600);
            if (OS.GetCmdlineUserArgs().Contains("--controller-emp"))
            {
                await CheckControllerEmp(host, remote, cars);
                foreach (var session in _sessions) { session.Leave(); }
                await Until(() => _sessions.All(session => session.LeaveComplete), "clean departure after controller EMP");
                _done = true;
                foreach (var session in _sessions) { session.GetParent().QueueFree(); }
                await Frames(4);
                GD.Print("Hosted controller EMP passed: repeated tabletop three-car acquisition, switching, actual shots and deliberate release; SCRIPTED input, not physical-controller evidence.");
                GetTree().Quit(); return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--missile-aim"))
            {
                await CheckMissilePracticePair(host, remote, cars);
                foreach (var session in _sessions) { session.Leave(); }
                await Until(() => _sessions.All(session => session.LeaveComplete), "clean departure after Missile aiming");
                _done = true;
                foreach (var session in _sessions) { session.GetParent().QueueFree(); }
                await Frames(4);
                GD.Print("Hosted Missile aiming passed: all lobby practice cars, switching, lock markers and remote launch.");
                GetTree().Quit(); return;
            }
            var authority = host.Arena!.Driver.Host!;
            var parkedPose = targetCount == 4 ? authority.World.GetVehicle(cars[3]).Movement.Physics : default;
            var previous = authority.World.GetVehicle(car).Movement.Physics.Position;
            float travel = 0, min = float.MaxValue, max = 0;
            float minGap = float.MaxValue, maxGap = 0, followerMin = float.MaxValue, followerMax = 0;
            float farMin = float.MaxValue, farMax = 0;
            int frames = OS.GetCmdlineUserArgs().Contains("--old-map") ? 2400 : 9000;
            for (int frame = 0; frame < frames; frame++)
            {
                await Frames(1);
                var state = authority.World.GetVehicle(car);
                travel += System.Numerics.Vector3.Distance(previous, state.Movement.Physics.Position);
                previous = state.Movement.Physics.Position;
                min = Math.Min(min, state.Speed); max = Math.Max(max, state.Speed);
                var following = authority.World.GetVehicle(follower);
                float gap = System.Numerics.Vector3.Distance(state.Movement.Physics.Position, following.Movement.Physics.Position);
                minGap = Math.Min(minGap, gap); maxGap = Math.Max(maxGap, gap);
                followerMin = Math.Min(followerMin, following.Speed); followerMax = Math.Max(followerMax, following.Speed);
                if (targetCount == 4)
                {
                    var third = authority.World.GetVehicle(cars[2]);
                    float routeGap = RouteGap(third.Movement.Physics.Position, following.Movement.Physics.Position);
                    farMin = Math.Min(farMin, routeGap); farMax = Math.Max(farMax, routeGap);
                    Require(authority.World.GetVehicle(cars[3]).Movement.Physics == parkedPose, "parked pose and zero velocities remain exact");
                    Require(third.Damage.CurrentHP == third.Damage.MaxHP, $"third car takes no route collision damage: {third.Damage.CurrentHP}, {third.Damage.LastDamage}, pose={third.Movement.Physics.Position}, tick={authority.World.State.Tick}");
                }
            }
            GD.Print($"HOSTED_PRACTICE frames={frames} travel={travel:F2}m speed={min:F2}..{max:F2}m/s HP={authority.World.GetVehicle(car).Damage.CurrentHP:F2}");
            Require(travel > frames / 60f * 12 && min > 11 && max < 17, "steady native driving");
            GD.Print($"PRACTICE_PAIR gap={minGap:F2}..{maxGap:F2}m followerSpeed={followerMin:F2}..{followerMax:F2}m/s HP={authority.World.GetVehicle(follower).Damage.CurrentHP:F2}");
            Require(minGap > 6 && maxGap < 15 && followerMin > 10 && followerMax < 18,
                "second car follows closely at matching speed without overtaking or touching");
            Require(remote.Arena!.Bodies.ContainsKey(follower), "remote receives second target");
            if (targetCount == 4)
            {
                GD.Print($"PRACTICE_THIRD routeGap={farMin:F3}..{farMax:F3}m parked={parkedPose.Position}");
                Require(farMin > 94 && farMax < 106, "third car stays approximately 100 route metres behind second across full oval laps");
                if (OS.GetCmdlineUserArgs().Contains("--visual")) { await CaptureTargets(host, cars); }
                await CheckParkedTarget(host, remote, cars[3]);
            }
            Require(remote.Arena!.Bodies.ContainsKey(car) && remote.Arena.Driver.Latest!.Vehicles.Single(vehicle => vehicle.State.VehicleId == car).State.Speed > 11, "remote receives moving car");
            if (!OS.GetCmdlineUserArgs().Contains("--old-map"))
            {
                ulong life = authority.World.GetVehicle(car).LifeId;
                var world = authority.World.State;
                var pose = new Core.Vehicles.VehiclePhysicsState(new(1000, 1, 1000), System.Numerics.Quaternion.Identity, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);
                authority.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(state => state.VehicleId != car ? state :
                    new Core.Vehicles.VehicleSnapshot(car, state.LifeId, new(world.Tick, pose, false, false, 0, 0), state.Damage, pose)), world.Match));
                await Until(() => authority.World.GetVehicle(car).LifeId > life, "ordinary out-of-bounds death and respawn");
                await Frames(600);
                Require(authority.World.GetVehicle(car).Speed > 11, "new life resumes practice driving");
                GD.Print($"HOSTED_PRACTICE respawn life={life}->{authority.World.GetVehicle(car).LifeId} speed={authority.World.GetVehicle(car).Speed:F2}m/s");
            }
            var late = AddPeer(false);
            await Until(() => late.Arena?.Driver.EntryReady == true && late.Arena.Bodies.ContainsKey(car), "late join receives existing practice car");
            Require(host.Lobby.State.Players.Count(player => player.PracticeCar) == targetCount && authority.World.State.Vehicles.Count == targetCount + 3, "late join never duplicates targets");
            Require(cars.All(id => late.Arena!.Bodies.ContainsKey(id)), "late join receives every target");
            host.Lobby.Request(LobbyCommand.Return);
            await Until(() => _sessions.All(session => session.Lobby?.State?.Phase == SessionPhase.Lobby && session.Arena is null), "return");
            foreach (var session in _sessions.Skip(1)) { session.Lobby!.Request(LobbyCommand.Ready, true); }
            await Until(() => host.Lobby.State!.CanStart, "ready after return");
            Require(host.StartFromLobby(), "repeat Start");
            await Until(() => _sessions.All(session => session.Arena?.Driver.Match?.Phase == MatchPhase.Active), "repeat active");
            await Frames(600);
            Require(host.Arena!.Driver.Host!.World.GetVehicle(car).Speed > 11, "car drives in next match");
            if (targetCount == 4)
            {
                await Until(() => host.Arena.Driver.Host.CaptureAuthority().PondPractice?.Phase == Core.Vehicles.PondPracticePhase.Parked, "repeat match pond trip");
                Require(System.Numerics.Vector3.Distance(host.Arena.Driver.Host.World.GetVehicle(cars[3]).Movement.Physics.Position, parkedPose.Position) < .3f, "repeat start returns pond target home");
            }
            foreach (var session in _sessions) { session.Leave(); }
            await Until(() => _sessions.All(session => session.LeaveComplete), "clean session departure");
            _done = true;
            foreach (var session in _sessions) { session.GetParent().QueueFree(); }
            for (int frame = 0; frame < 4; frame++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            GD.Print("Hosted practice passed: steady laps, remote presentation, late join, return and repeat start.");
        }
        catch (Exception error) { GD.PushError(error.ToString()); _done = true; GetTree().Quit(1); return; }
        _done = true;
        GetTree().Quit();
    }

    private async Task Frames(int count) { for (int frame = 0; frame < count; frame++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
    private async Task Until(Func<bool> condition, string phase)
    {
        for (int frame = 0; frame < 2400 && !condition(); frame++) { await Frames(1); }
        Require(condition(), phase + ": " + string.Join(" | ", _sessions.Select(session => $"{session.Stage}/{session.Lobby?.Failure}/{session.Arena?.Driver.Failure}")));
    }
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
