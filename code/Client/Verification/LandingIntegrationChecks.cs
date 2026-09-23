using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Real collision adapters on the unchanged TS-75 landing zone, with controlled crash initial conditions.</summary>
public sealed partial class LandingIntegrationChecks : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private VehicleBody? _native;
    private NetworkVehicleBody? _network;
    private Node3D? _target;
    private Camera3D? _camera;
    private bool _advance;
    private bool _tumble;
    private bool _kicked;
    private readonly List<string> _lines = new();
    private readonly HashSet<LandingPhase> _phases = new();
    private int _contacts;
    private int _forgiven;
    private string _case = "";
    private string _output = "";

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _PhysicsProcess(double delta)
    {
        if (!_advance) { return; }
        ulong tick = _world.State.Tick + 1;
        var input = new InputFrame(tick, 0, 0, 0, 0, 0, 0);
        var request = _native is not null ? _native.Capture(input) : new VehicleStepRequest(1, input, _network!.Observe(_world.GetVehicle(1)));
        if (_tumble && !_kicked && _forgiven > 0 && _world.GetVehicle(1).Landing.Phase is LandingPhase.Recovery or LandingPhase.Recovered)
        {
            _kicked = true;
            request = new VehicleStepRequest(1, input, request.Observation, [new VehicleEffectRequest(new DamageEffect(0, new N.Vector3(0, 22000, 0), new N.Vector3(0, 0, 3)), new DamageContext("test", 0, "recovery-tumble"))]);
        }
        var requests = new List<VehicleStepRequest> { request };
        if (_target is not null)
        {
            requests.Add(new VehicleStepRequest(2, input, new VehicleObservation(_world.GetVehicle(2).ObservedPhysics, N.Vector3.Zero)));
        }
        if (tick == 1) { Log($"{_case} initial observed velocity={request.Observation.Physics.LinearVelocity}, angular={request.Observation.Physics.AngularVelocity}"); }
        var result = _world.Step(input, requests)[0];
        if (_native is not null) { _native.Apply(result); }
        else { _network!.Apply(result.Snapshot.Movement.Physics); }
        _phases.Add(result.Snapshot.Landing.Phase);
        if (request.Observation.Contacts.Count > 0)
        {
            _contacts += request.Observation.Contacts.Count;
            if (result.Snapshot.Landing.Phase is LandingPhase.Recovery or LandingPhase.Recovered && result.DamageEvents.Count == 0) { _forgiven++; }
            if (_contacts < 24 || result.DamageEvents.Count > 0)
            {
                var c = request.Observation.Contacts[0];
                Log($"{_case} tick={tick} phase={result.Snapshot.Landing.Phase} hp={result.Snapshot.Damage.CurrentHP:F2} terrain={c.Terrain} local={c.LocalPosition} normal={c.Normal} severity={VehicleDamageMath.CollisionSeverity(c.RelativeVelocity, c.Normal, c.Impulse, 900):F2}");
            }
        }
    }

    public async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/landing-checks");
            System.IO.Directory.CreateDirectory(_output);
            AddChild(Arenas.ActiveMap.Load());
            await Frames(3);
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment = GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") });
                AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-65, -25, 0), LightEnergy = 1.4f });
                _camera = new Camera3D { Current = true, Position = new Vector3(-45, 15, 18) };
                AddChild(_camera);
                _camera.LookAt(new Vector3(-60, 2, 0));
            }
            foreach (bool network in new[] { false, true })
            {
                foreach (var scenario in new[] { ("upright", 0f, 0f, 0f), ("yawed", 90f, 0f, 0f), ("spin", 90f, 0f, 0f), ("bank", 0f, 0f, 0f), ("roll35", 0f, 35f, 0f), ("pitch25", 0f, 0f, 25f), ("roll45", 0f, 45f, 0f), ("pitch35", 0f, 0f, 35f), ("side", 0f, 90f, 0f), ("roof", 0f, 180f, 0f), ("front", 0f, 0f, 90f), ("rear", 0f, 0f, -90f), ("tumble", 0f, 0f, 0f), ("obstacle", 0f, 0f, 0f), ("vehicle", 0f, 0f, 0f) })
                {
                    await Drop(network, scenario.Item1, scenario.Item2, scenario.Item3, scenario.Item4);
                }
            }
            GD.Print("Landing integration passed. Evidence: " + _output);
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            _advance = false;
            Log(exception.ToString());
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task Drop(bool network, string name, float yaw, float roll, float pitch)
    {
        _case = (network ? "network-" : "native-") + name;
        _world = new(new Core.Simulation.SimulationConfiguration(60));
        _phases.Clear();
        _contacts = 0;
        _forgiven = 0;
        _tumble = name == "tumble";
        _kicked = false;
        Vector3 sample = name == "bank" ? GetNode<Node3D>("OvalFoundation/PlayerSpawns/player-01").GlobalPosition : new Vector3(-62, 0, 0);
        using var ray = PhysicsRayQueryParameters3D.Create(sample + Vector3.Up * 20, sample + Vector3.Down * 10);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not Node terrain || !terrain.IsInGroup("landing_terrain")) { throw new InvalidOperationException("TS-75 landing terrain metadata missing."); }
        Vector3 point = hit["position"].AsVector3();
        Quaternion rotation = (new Quaternion(Vector3.Up, Mathf.DegToRad(yaw - 90)) * new Quaternion(Vector3.Forward, Mathf.DegToRad(roll)) * new Quaternion(Vector3.Right, Mathf.DegToRad(pitch)));
        if (name == "bank") { Vector3 normal = hit["normal"].AsVector3(); rotation = Basis.LookingAt(Vector3.Right.Slide(normal).Normalized(), normal).GetRotationQuaternion(); }
        Vector3 position = point + Vector3.Up * 8;
        Vector3 velocity = new(name is "yawed" or "spin" ? 8 : 0, -12, 0);
        Vector3 angular = name == "spin" ? Vector3.Up * 3 : Vector3.Zero;
        var physics = new VehiclePhysicsState(VehicleBody.ToCore(position), new N.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(velocity), VehicleBody.ToCore(angular));
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        _world.AddVehicle(1, new(), damage, physics);
        if (name == "obstacle")
        {
            var obstacle = new StaticBody3D { Position = point + Vector3.Up * 3 };
            obstacle.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6, 1, 6) } });
            AddChild(obstacle);
            _target = obstacle;
        }
        if (name == "vehicle")
        {
            if (network) { _target = new NetworkVehicleBody { VehicleId = 2, Position = point + Vector3.Up * 3 }; }
            else { _target = new VehicleBody { VehicleId = 2, Position = point + Vector3.Up * 3, Freeze = true }; }
            AddChild(_target);
        }
        if (_target is not null)
        {
            var targetPhysics = new VehiclePhysicsState(VehicleBody.ToCore(_target.Position), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
            _world.AddVehicle(2, new(), damage, targetPhysics);
            if (_target is VehicleBody body) { body.Initialize(_world); }
        }
        if (network)
        {
            _network = new NetworkVehicleBody { VehicleId = 1 };
            AddChild(_network);
            _network.Apply(physics);
        }
        else
        {
            _native = new VehicleBody { Position = position, Quaternion = rotation, LinearVelocity = velocity, AngularVelocity = angular, DamageConfiguration = damage };
            _native.Initialize(_world);
            AddChild(_native);
        }
        await Frames(3);
        _advance = true;
        await Frames(name == "tumble" ? 360 : 100);
        _advance = false;
        var state = _world.GetVehicle(1);
        Log($"RESULT {_case}: HP={state.Damage.CurrentHP:F2}, phases={string.Join(',', _phases)}, contacts={_contacts}, forgivenFrames={_forgiven}, kick={_kicked}");
        if (_camera is not null)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(_output, _case + ".png"));
        }
        bool safe = name is "upright" or "yawed" or "spin" or "bank" or "roll35" or "pitch25" or "roll45" or "pitch35";
        if (safe && (state.Damage.CurrentHP != 1000 || !_phases.Contains(LandingPhase.Recovery))) { throw new InvalidOperationException(_case + " valid landing was not forgiven."); }
        if (!safe && state.Damage.CurrentHP >= 1000) { throw new InvalidOperationException(_case + " meaningful crash did not damage."); }
        if (name == "tumble" && (!_kicked || !_phases.Contains(LandingPhase.Crash))) { throw new InvalidOperationException(_case + " did not exercise secondary crash."); }
        _native?.QueueFree();
        _network?.QueueFree();
        _target?.QueueFree();
        _native = null;
        _network = null;
        _target = null;
        await Frames(3);
    }

    private void Log(string line)
    {
        _lines.Add(line);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _lines);
    }
    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    }
}
