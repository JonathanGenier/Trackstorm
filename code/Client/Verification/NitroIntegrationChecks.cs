using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native two-peer driving, repeated Nitro use, score publication and clean expiry.</summary>
public sealed partial class NitroIntegrationChecks : Node
{
    private readonly List<ReplicationTrafficGateway> _gateways = new();
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<SubViewport> _views = new();
    private string _endpoint = "";
    private int _frames;
    private int _stage;
    private int _boundary;
    private float _baseline;
    private bool _done;
    private bool _held;
    private ushort _throttle = ushort.MaxValue;
    private ushort _reverse;
    private int _rocketScenario;
    private float _rocketOnlySpeed;
    private float _rocketStartSpeed;
    private double[] _rocketStartingScores = [];
    private readonly bool[] _press = new bool[2];
    private double[] _charges = [];
    private double[] _scores = [];
    private float _releaseSpeed;
    private readonly List<string> _evidence = new();
    private readonly HashSet<Vehicles.BoostExhaust> _releaseTails = new();
    private readonly HashSet<Vehicles.BoostExhaust> _depletionBursts = new();
    private readonly Dictionary<Vehicles.BoostExhaust, double> _inactiveSeconds = new();
    private readonly HashSet<Vehicles.BoostExhaust> _terminalActive = new();
    private readonly HashSet<string> _deploymentCaptures = new();

    public override async void _Process(double delta)
    {
        if (_done || _arenas.Count != 2) { return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (_done) { return; }
        try
        {
            foreach (var arena in _arenas)
            foreach (var body in arena.Bodies.Values)
            {
                if (body.Rack.Boost.Source()?.Movement.Nitro.Active != true) { continue; }
                Check(body.Rack.Progress >= .999f && body.Rack.Boost.Deployment >= .999f,
                    "rendered thruster fully deployed whenever accepted thrust is active");
            }
            var slot = _arenas[0].Driver.Host?.Items.Slots.FirstOrDefault(slot => slot.Vehicle == 1);
            if (slot?.Active.Item != HeldItem.Nitro) { return; }
            string phase = slot.NitroDeploymentTicks switch { > 28 => "replace", > 9 => "rise", > 0 => "extend", _ => "ready" };
            if (_deploymentCaptures.Add(phase))
            {
                Capture("deployment-" + phase + ".png");
                GD.Print($"Deployment {phase}: remaining={slot.NitroDeploymentTicks} ticks; charge={slot.Active.NitroCharge:F2}%; thrust={_arenas[0].Driver.Host!.World.GetVehicle(1).Movement.Nitro.Active}.");
            }
        }
        catch (Exception error) { GD.PrintErr(error); GetTree().Quit(1); }
    }

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        AddPeer();
        AddPeer();
    }

    private void AddPeer()
    {
        int index = _arenas.Count;
        var gateway = new ReplicationTrafficGateway();
        ulong server = 0;
        if (index == 0)
        {
            gateway.Listen(TransportEndpoint.DirectIp(_endpoint));
            if (OS.GetCmdlineUserArgs().Contains("--nitro-impaired")) { gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
        }
        else { server = gateway.Connect(TransportEndpoint.DirectIp(_endpoint)); }
        _gateways.Add(gateway);
        var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        if (index == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
        else { AddChild(view); }
        _views.Add(view);
        var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
        arena.Initialize(gateway, index == 0 ? 88ul : 0, server);
        view.AddChild(arena);
        var hud = new Hud.CombatHud { Vehicle = () => arena.Driver.LocalState, Slot = () => arena.Driver.LocalItem,
            Match = () => arena.Driver.Match, Player = () => arena.Driver.LocalVehicleId };
        view.AddChild(hud);
        var bank = new StaticBody3D { Position = new Vector3(0, 20, 0), Rotation = Vector3.Zero, CollisionLayer = 1 };
        bank.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(120, 1, 5000) } });
        bank.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(120, 1, 5000) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.34f, 0.36f) } });
        arena.AddChild(bank);
        // Keep the production local chase camera current so this fixture exercises
        // exhaust, wind wisps and FOV together on both independently owned peer views.
        _arenas.Add(arena);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) { if (++_frames > _boundary + 20) { GetTree().Quit(); } return; }
        try
        {
            _frames++;
            foreach (var arena in _arenas)
            {
                int peerIndex = _arenas.IndexOf(arena);
                bool press = _press[peerIndex];
                _press[peerIndex] = false;
                arena.Advance(new Core.Input.InputFrame(0, 0, _throttle, _reverse, _held ? Core.Input.InputButtons.UseItem : 0, press ? Core.Input.InputButtons.UseItem : 0, 0));
                Check(arena.Driver.Failure.Length == 0, arena.Driver.Failure);
            }
            Check(_frames - _boundary < 1800, $"Nitro stage {_stage} timeout");
            foreach (var arena in _arenas)
            foreach (var pair in arena.Bodies)
            {
                var exhaust = pair.Value.Rack.Boost;
                var accepted = arena.Driver.Latest?.Vehicles.SingleOrDefault(v => v.State.VehicleId == pair.Key)?.State;
                // The unreliable inactive movement and reliable confirmed depletion
                // can arrive separately. Give a newly confirmed terminal cue its own
                // bounded .60-second tail, but never refresh the clock while it stays on.
                _inactiveSeconds.TryGetValue(exhaust, out double inactive);
                inactive = accepted is { CanInteract: true, Movement.Nitro.Active: true } ? 0 : inactive + delta;
                if (exhaust.DepletionBurst && _terminalActive.Add(exhaust)) { inactive = 0; }
                if (!exhaust.DepletionBurst) { _terminalActive.Remove(exhaust); }
                _inactiveSeconds[exhaust] = inactive;
                if (inactive > .75)
                {
                    Check(!exhaust.FlameVisible && !exhaust.SmokeEmitting && !exhaust.SparksEmitting,
                        $"Boost emission stuck after {inactive:F3}s of inactive peer state");
                }
                if (_stage is 4 or 7 && exhaust.FlameEnergy is > .01f and < .95f)
                {
                    Check(!exhaust.DepletionBurst, "ordinary release does not report exhaustion");
                    _releaseTails.Add(exhaust);
                }
                if (_stage is 8 or 9 && exhaust.DepletionBurst) { _depletionBursts.Add(exhaust); }
            }
            var host = _arenas[0].Driver.Host!;
            foreach (var slot in host.Items.Slots.Where(slot => slot.NitroDeploymentTicks > 0))
            {
                Check(slot.Active.NitroCharge == 100, "deployment never consumes charge");
                Check(!host.World.GetVehicle(slot.Vehicle).Movement.Nitro.Active, "deployment never produces thrust");
            }
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2):
                    Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1 }, out _), "short countdown");
                    _stage = 100; _boundary = _frames;
                    break;
                case 100:
                    PrepareRocketScenario();
                    _stage = 101; _boundary = _frames;
                    break;
                case 101 when _frames - _boundary > ItemSlot.NitroDeploymentDurationTicks:
                    _throttle = _rocketScenario == 2 ? ushort.MaxValue : (ushort)0;
                    _reverse = _rocketScenario == 3 ? ushort.MaxValue : (ushort)0;
                    Position(_rocketScenario == 1 ? 10 : 0, _rocketScenario >= 4 ? 100 : 21.4f);
                    _stage = 104; _boundary = _frames;
                    break;
                case 104 when _frames - _boundary > 30:
                    _rocketStartSpeed = host.World.GetVehicle(1).Speed;
                    _rocketStartingScores = host.World.State.Match!.Players.Select(p => p.CircusScore).ToArray();
                    UseBoth();
                    _stage = 102; _boundary = _frames;
                    break;
                case 102 when _frames - _boundary > 60:
                    VerifyRocketScenario();
                    _held = false;
                    _stage = 103; _boundary = _frames;
                    break;
                case 103 when _frames - _boundary > 30:
                    Check(host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active), "rocket release ends thrust on both peers");
                    if (++_rocketScenario < 6) { _stage = 100; _boundary = _frames; break; }
                    foreach (ulong id in new ulong[] { 1, 2 }) { host.Items.RemovePlayer(id); }
                    _throttle = ushort.MaxValue; _reverse = 0;
                    Position();
                    _stage = 0;
                    Next("Rocket scenarios complete; normal-drive and sustained-resource comparison begins.");
                    break;
                case 1 when _frames - _boundary > 360:
                    _baseline = host.World.GetVehicle(1).Speed;
                    Check(_baseline > 40 && _baseline < 45, $"baseline {_baseline}");
                    UseBoth();
                    Next($"Normal drive settles at {_baseline:0.00} m/s; both players request Nitro.");
                    break;
                case 2 when host.World.State.Vehicles.All(v => v.Movement.Nitro.Active) && _arenas[1].Driver.LocalState!.Movement.Nitro.Active:
                    Check(host.Items.Slots.All(s => s.Item == HeldItem.Nitro && s.NitroCharge is > 0 and < 100 && s.SecondItem == HeldItem.Wrench), "independent retained Nitro and second slot");
                    Next("Both peers sustain Nitro; charge drains while Wrench remains in the second slot.");
                    break;
                case 3 when _frames - _boundary > 180:
                    CheckCameras(true);
                    _releaseSpeed = host.World.GetVehicle(1).Speed;
                    Check(_releaseSpeed > _baseline * 1.2f, $"boosted speed {_releaseSpeed}");
                    Check(_arenas[1].Driver.LocalState!.Speed > _baseline * 1.2f, "remote boost motion");
                    _charges = host.Items.Slots.Select(s => s.NitroCharge).ToArray();
                    _scores = host.World.State.Match!.Players.Select(p => p.CircusScore).ToArray();
                    Check(_scores.All(s => s > 0), "actual overspeed score");
                    Capture("partial-nitro.png");
                    _held = false;
                    Next($"Boost reaches {_releaseSpeed:0.00} m/s; released at {_charges[0]:0.00}% charge.");
                    break;
                case 4 when _frames - _boundary > 30:
                    Check(host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active), "release removes boost");
                    Check(Math.Abs(host.Items.Slots[0].NitroCharge - _charges[0]) < 0.00001, "host release preserves charge");
                    Check(host.Items.Slots[1].NitroCharge <= _charges[1] && host.Items.Slots[1].NitroCharge > _charges[1] - 10, "remote release bounded by input delivery");
                    _charges = host.Items.Slots.Select(s => s.NitroCharge).ToArray();
                    float recovering = host.World.GetVehicle(1).Speed;
                    Check(recovering > _baseline && recovering < _releaseSpeed && _releaseSpeed - recovering < 3, "smooth recovery without snapping or sudden braking");
                    Check(host.World.State.Match!.Players.Zip(_scores).All(p => p.First.CircusScore > p.Second), "overspeed scoring continues after release");
                    Capture("released-nitro.png");
                    Next($"Release recovery remains at {recovering:0.00} m/s with continuing authoritative overspeed awards.");
                    break;
                case 5 when _frames - _boundary > 360:
                    CheckCameras(false);
                    Check(host.World.State.Vehicles.All(v => v.Speed <= host.Configuration.Configuration.Vehicle.ForwardSpeed + 0.01f), "recovery reaches normal speed");
                    Check(host.Items.Slots.Select(s => s.NitroCharge).SequenceEqual(_charges), "idle charge retained on both peers");
                    Check(!host.World.State.Match!.Awards.Any(a => a.Category == Core.Matches.CircusScoreCategory.Nitro), "no Nitro scoring at normal speed");
                    UseBoth();
                    Next("Recovery reaches normal speed and overspeed scoring stops; reuse begins with retained charge.");
                    break;
                case 6 when _frames - _boundary > 30:
                    Check(host.World.State.Vehicles.All(v => v.Movement.Nitro.Active), "repeat activation");
                    CheckExhaust(true);
                    Check(host.Items.Slots.Zip(_charges).All(p => p.First.NitroCharge < p.Second), "reuse drains same resources");
                    _held = false;
                    Next("Second activation drains the same grant tokens, followed by another release.");
                    break;
                // Allow publication/input transit plus the accepted .45-second tail;
                // the per-view timer above independently bounds the actual VFX lifetime.
                case 7 when _frames - _boundary > 60:
                    Check(host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active), "second release");
                    CheckExhaust(false);
                    UseBoth();
                    Next("Third activation continues until resource exhaustion.");
                    break;
                case 8 when host.Items.Slots.All(s => s.Item == HeldItem.None && s.NitroCharge == 0):
                    Check(host.Items.Slots.All(s => s.SecondItem == HeldItem.Wrench), "depletion leaves second slot intact");
                    _held = false;
                    Next("Both Nitro resources reach zero and clear only their physical slots.");
                    break;
                case 9 when _frames - _boundary > 60:
                    CheckCameras(false, .025f);
                    CheckExhaust(false);
                    Check(_releaseTails.Count == 4, "both vehicles wind down on both peer views");
                    Check(_depletionBursts.Count == 4, "both vehicles signal confirmed exhaustion on both peer views");
                    Check(_arenas[1].Driver.LocalItem is { Item: HeldItem.None, NitroCharge: 0, SecondItem: HeldItem.Wrench }, "remote depletion publication");
                    Check(host.World.State.Vehicles.All(v => !v.Movement.Nitro.Active), "exhaustion ends boost");
                    Check(_arenas[1].Driver.Match!.Players.All(p => p.CircusScore > 0), "remote score publication");
                    Check(host.World.Events.Entries.Count(e => e.Kind == "Exhausted" && e.Cause == "Nitro") == 2, "one disposal per grant");
                    Capture("exhausted-nitro.png");
                    CheckDeploymentPublicationOrdering();
                    GD.Print("Nitro integration passed: " + string.Join("\n", _evidence));
                    _done = true; _boundary = _frames;
                    foreach (var arena in _arenas) { arena.QueueFree(); }
                    foreach (var gateway in _gateways) { gateway.Dispose(); }
                    break;
            }
        }
        catch (Exception error)
        {
            GD.PrintErr(error);
            foreach (var gateway in _gateways) { gateway.Dispose(); }
            GetTree().Quit(1);
        }
    }

    private void CheckDeploymentPublicationOrdering()
    {
        // Controlled channel-order regression on production nodes after driving:
        // the previous capability's active movement must not skip a new deployment.
        foreach (var arena in _arenas)
        foreach (var body in arena.Bodies.Values)
        {
            var prior = body.Rack.Boost.Source()!;
            var pose = prior.ObservedPhysics;
            var oldActive = new VehicleSnapshot(prior.VehicleId, prior.LifeId,
                new VehicleState(prior.Movement.Tick, pose, true, false, 0, 0, nitro: new NitroState(60, 18000, 1.4f, 1)), prior.Damage, pose);
            body.Apply(oldActive);
            body.Rack.Reset();
            var inventory = new ItemSlot(prior.VehicleId, prior.LifeId, 999, HeldItem.Nitro)
                { EngagedToken = 999, NitroDeploymentTicks = ItemSlot.NitroDeploymentDurationTicks };
            ulong start = prior.Movement.Tick + 1;
            body.Rack.Observe(prior.LifeId, true, inventory, [], tick: start);
            body.Rack._Process(.3);
            float progress = body.Rack.Progress;
            Check(progress is > 0 and < 1 && body.Rack.Boost.Deployment < .999f,
                "previous active movement cannot bypass a newly selected thruster deployment");
            body.Rack.Observe(prior.LifeId, true, inventory with { NitroDeploymentTicks = 35 }, [], tick: start + 1);
            Check(body.Rack.Progress >= progress, "delayed inventory cannot rewind the mechanical animation");
            body.Apply(new VehicleSnapshot(prior.VehicleId, prior.LifeId,
                new VehicleState(start + ItemSlot.NitroDeploymentDurationTicks, pose, true, false, 0, 0, nitro: new NitroState(60, 18000, 1.4f, 1)), prior.Damage, pose));
            body.Rack._Process(0);
            Check(body.Rack.Progress >= .999f && body.Rack.Boost.Deployment >= .999f,
                "current active movement completes readiness despite a delayed final inventory publication");
        }
        _evidence.Add("Deployment channel ordering verified: old active snapshot cannot skip a new deployment; delayed publications cannot rewind it; current thrust resolves readiness.");
    }

    private void UseBoth()
    {
        var host = _arenas[0].Driver.Host!;
        _held = true;
        foreach (ulong id in new ulong[] { 1, 2 })
        {
            if (!host.Items.Slots.Any(s => s.Vehicle == id))
            {
                Check(host.Items.Grant(host.World, id, HeldItem.Nitro), "grant");
                Check(host.Items.Grant(host.World, id, HeldItem.Wrench), "second slot grant");
            }
        }
        _press[0] = true;
        // Remote acquisition is published on the next step, so send the same authenticated request
        // via its transport only after the confirmed slot arrives.
        if (_arenas[1].Driver.LocalItem?.Item == HeldItem.Nitro)
        {
            _press[1] = true;
        }
        else { _arenas[1].Driver.ItemsReceived += RequestRemote; }
    }

    private void RequestRemote(ItemPublication publication)
    {
        if (_arenas[1].Driver.LocalItem?.Item != HeldItem.Nitro) { return; }
        _arenas[1].Driver.ItemsReceived -= RequestRemote;
        _press[1] = true;
    }

    private void PrepareRocketScenario()
    {
        var host = _arenas[0].Driver.Host!;
        foreach (ulong id in new ulong[] { 1, 2 }) { host.Items.RemovePlayer(id); }
        foreach (ulong id in new ulong[] { 1, 2 })
        {
            Check(host.Items.Grant(host.World, id, HeldItem.Nitro), "rocket scenario Nitro grant");
            Check(host.Items.Grant(host.World, id, HeldItem.Wrench), "rocket scenario second slot");
        }
        _held = false;
        _throttle = _reverse = 0;
        Check(host.TryConfigure(0, new Dictionary<string, double> { ["items.nitro_airborne_thrust_scale"] = _rocketScenario == 5 ? 0 : 1 }, out _), "airborne live tuning");
        Position(_rocketScenario == 1 ? 10 : 0, _rocketScenario >= 4 ? 100 : 21.4f);
    }

    private void VerifyRocketScenario()
    {
        var host = _arenas[0].Driver.Host!;
        float speed = -host.World.GetVehicle(1).Movement.Physics.LinearVelocity.Z;
        Check(host.Items.Slots.All(s => s.NitroCharge is > 50 and < 100), "activation-time consumption independent of pedals/support");
        // A preceding powered scenario can cross normal top speed during release.
        // Compare this scenario's score delta, retaining legitimate earlier points.
        Check(host.World.State.Match!.Players.Select(p => p.CircusScore).SequenceEqual(_rocketStartingScores), "no new points for thrust/airborne motion below normal top speed");
        switch (_rocketScenario)
        {
            case 0: Check(_rocketStartSpeed < 0.1f && speed > 8, "Nitro launches from complete rest without throttle"); _rocketOnlySpeed = speed; break;
            case 1: Check(speed > _rocketStartSpeed + 5, "Nitro accelerates coasting car without throttle"); break;
            case 2: Check(speed > _rocketOnlySpeed + 1, "drivetrain and rocket combine"); break;
            case 3: Check(speed > 0 && speed < _rocketOnlySpeed, "reverse opposes but never reverses rocket force"); break;
            case 4: Check(!host.World.GetVehicle(1).Movement.Grounded && speed > 8, "airborne rocket propels without wheels"); break;
            case 5: Check(!host.World.GetVehicle(1).Movement.Grounded && Math.Abs(speed) < 0.1f, "runtime zero airborne scale disables thrust while charge drains"); break;
        }
        string line = $"Rocket scenario {_rocketScenario}: start {_rocketStartSpeed:0.00}, forward {speed:0.00} m/s; charge {host.Items.Slots[0].NitroCharge:0.00}%; Circus unchanged.";
        _evidence.Add(line); GD.Print(line);
        Capture($"rocket-{_rocketScenario}.png");
    }

    private void Position(float speed = 40, float height = 21.4f)
    {
        var host = _arenas[0].Driver.Host!;
        var w = host.World.State;
        host.World.Restore(new(w.Tick, w.LastInput, w.Vehicles.Select(v =>
        {
            var pose = new VehiclePhysicsState(new N.Vector3(v.VehicleId == 1 ? -8 : 8, height, 1200), N.Quaternion.Identity, new N.Vector3(0, 0, -speed), N.Vector3.Zero);
            _arenas[0].Bodies[v.VehicleId].Apply(pose);
            return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(w.Tick, pose, true, false, 0, 0), v.Damage, pose);
        }), w.Match));
    }

    private void Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        string path = ProjectSettings.GlobalizePath("res://.godot/nitro-checks");
        System.IO.Directory.CreateDirectory(path);
        _views[0].GetTexture().GetImage().SavePng(System.IO.Path.Combine(path, name));
    }

    private void CheckExhaust(bool active)
    {
        foreach (var arena in _arenas)
        {
            foreach (var body in arena.Bodies.Values)
            {
                var exhaust = body.FindChild("BoostExhaust", true, false) as Vehicles.BoostExhaust;
                Check(exhaust is not null, "each peer reconstructs shared Boost presentation");
                if (!active) { Check(!exhaust!.FlameVisible && !exhaust.SmokeEmitting, "release/depletion finishes bounded flame and smoke emission on both peers"); }
                else if (arena == _arenas[0]) { Check(exhaust!.FlameVisible, "host view renders simultaneous local and remote Boost"); }
            }
        }
        _evidence.Add($"Boost VFX {(active ? "activation" : "cutoff")} observed in native peer presentation state.");
    }

    private void CheckCameras(bool active, float cutoff = .005f)
    {
        foreach (var arena in _arenas)
        {
            var camera = arena.GetNode<Vehicles.VehicleChaseCamera>("ChaseCamera");
            Check(camera.Current && camera.GlobalTransform.IsFinite(), "each peer owns a finite current chase camera");
            Check(camera.Fov is >= 65 and <= 73.01f, "peer camera remains within presentation bounds");
            Check(active ? camera.BoostMotion.PullBack > .5f : camera.BoostMotion.PullBack < cutoff,
                active ? "both local peer cameras respond to sustained Boost" : "release/depletion returns both peer cameras toward normal despite overspeed");
        }
        _evidence.Add($"Integrated peer camera {(active ? "sustain" : "cutoff")} verified alongside authoritative Nitro and exhaust.");
    }

    private void Next(string text) { _evidence.Add(text); GD.Print(text); _stage++; _boundary = _frames; }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
