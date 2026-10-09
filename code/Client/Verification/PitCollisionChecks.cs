using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Real two-car contacts through both production adapters, with rendered and per-tick evidence.</summary>
public sealed partial class PitCollisionChecks : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private readonly Dictionary<ulong, VehicleBody> _native = new();
    private readonly Dictionary<ulong, NetworkVehicleBody> _network = new();
    private readonly List<object> _trace = new();
    private Camera3D? _camera;
    private Node3D _fixture = null!;
    private string _case = "";
    private string _output = "";
    private bool _running;
    private float _peak, _yaw, _travelError, _speedChange;
    private int _contacts;

    public override void _Ready() => CallDeferred(MethodName.Run);
    public override void _Process(double delta)
    {
        foreach (var body in _network.Values) { body.PresentLocal((float)delta); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_running) { return; }
        ulong tick = _world.State.Tick + 1;
        InputFrame Input(ulong id) => new(tick, 0, (ushort)(_case == "following" && tick < 660 ? ushort.MaxValue * (id == 1 ? .25f : .4f) : 0), 0, 0, 0, 0);
        var before = _world.GetVehicle(2).Movement.Physics;
        var observations = _network.Count > 0 ? NetworkVehicleBody.ObserveBatch(_network, _world.State.Vehicles) : null;
        var requests = _native.Count > 0 ? VehicleBody.CaptureBatch(_native.Values, body => Input(body.VehicleId)) :
            observations!.Select(pair => new VehicleStepRequest(pair.Key, Input(pair.Key), pair.Value)).ToArray();
        var results = _world.Step(Input(1), requests);
        foreach (var result in results)
        {
            ulong id = result.Snapshot.VehicleId;
            if (_native.TryGetValue(id, out var body)) { body.Apply(result); } else { _network[id].Apply(result.Snapshot); }
            var p = result.Snapshot.Movement.Physics;
            var observation = requests.Single(r => r.VehicleId == id).Observation;
            int contacts = observation.Contacts.Count(c => c.OtherVehicleId != 0);
            if (id == 1)
            {
                _peak = Math.Max(_peak, Math.Abs(p.AngularVelocity.Y));
                _yaw += p.AngularVelocity.Y / 60;
            }
            if (contacts > 0)
            {
                _contacts++;
                if (id == 2 && tick > 60 && tick < 600)
                {
                    _travelError = Math.Max(_travelError, Math.Abs(p.Position.Z - before.Position.Z - before.LinearVelocity.Z / 60));
                    _speedChange = Math.Max(_speedChange, Math.Abs(p.LinearVelocity.Z - before.LinearVelocity.Z));
                }
            }
            _trace.Add(new { tick, id, position = new[] { p.Position.X, p.Position.Y, p.Position.Z },
                velocity = new[] { p.LinearVelocity.X, p.LinearVelocity.Y, p.LinearVelocity.Z },
                angular = new[] { p.AngularVelocity.X, p.AngularVelocity.Y, p.AngularVelocity.Z },
                orientation = new[] { p.Orientation.X, p.Orientation.Y, p.Orientation.Z, p.Orientation.W }, contacts,
                details = observation.Contacts.Where(c => c.OtherVehicleId != 0).Select(c => new { c.Normal, c.LocalPosition, c.RelativeVelocity }),
                hp = result.Snapshot.Damage.CurrentHP });
        }
        if (_camera is not null)
        {
            Vector3 center = VehicleBody.ToGodot((_world.GetVehicle(1).Movement.Physics.Position + _world.GetVehicle(2).Movement.Physics.Position) / 2);
            _camera.Position = center + new Vector3(11, 12, 15); _camera.LookAt(center);
        }
    }

    private async void Run()
    {
        try
        {
            if (DisplayServer.GetName() != "headless") { Engine.MaxFps = 60; }
            _output = ProjectSettings.GlobalizePath("res://.godot/ts-283/pit"); System.IO.Directory.CreateDirectory(_output);
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new DirectionalLight3D { RotationDegrees = new(-60, -25, 0), LightEnergy = 1.5f });
                _camera = new Camera3D { Current = true }; AddChild(_camera);
            }
            foreach (bool network in new[] { false, true })
            foreach (string scenario in new[] { "low", "medium", "high", "glancing", "matched", "rubbing", "following", "heavy-target", "heavy-striker" })
            {
                string? selected = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--pit-case=", StringComparison.Ordinal));
                if (selected is not null && scenario != selected[11..]) { continue; }
                await Exercise(network, scenario);
            }
            GD.Print("PIT collision integration passed."); GetTree().Quit();
        }
        catch (Exception exception) { _running = false; GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Exercise(bool network, string scenario)
    {
        _case = scenario; _trace.Clear(); _peak = 0; _yaw = 0; _contacts = 0; _travelError = 0; _speedChange = 0;
        _fixture = new Node3D(); AddChild(_fixture);
        var floor = new StaticBody3D(); floor.SetMeta("surface_identity", "Dirt"); floor.AddToGroup("landing_terrain");
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(2000, 2, 2000) }, Position = new(0, -1, 0) });
        floor.AddChild(VehicleBody.Box(new(2000, 2, 2000), new(0, -1, 0), new("947454"))); _fixture.AddChild(floor);
        _world = new(new(60));
        float closing = scenario switch { "low" => .5f, "medium" => 6, "high" => 14, "glancing" => 3, "matched" => 0, "rubbing" => .2f, "following" => 0, _ => 7 };
        float speed = scenario == "following" ? 10 : 15;
        var target = new VehiclePhysicsState(new(0, VehicleDimensions.RideHeight, 0), N.Quaternion.Identity, new(0, 0, -speed), N.Vector3.Zero);
        float yaw = scenario is "matched" or "rubbing" or "following" ? 0 : -0.15f;
        var position = scenario == "following" ? new N.Vector3(0, VehicleDimensions.RideHeight, 5.2f) :
            new N.Vector3(scenario is "matched" or "rubbing" ? -2.64f : -3.1f, VehicleDimensions.RideHeight, scenario is "matched" or "rubbing" ? 0 : 3.9f);
        var striker = new VehiclePhysicsState(position, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, yaw), new(closing, 0, -speed), N.Vector3.Zero);
        AddCar(network, 1, target, new() { Mass = scenario == "heavy-target" ? 4500 : 3000, Wheelbase = scenario == "heavy-target" ? 3.2f : VehicleDimensions.Wheelbase });
        AddCar(network, 2, striker, new() { Mass = scenario == "heavy-striker" ? 4500 : 3000, Wheelbase = scenario == "heavy-striker" ? 3.2f : VehicleDimensions.Wheelbase });
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _running = true;
        foreach (int boundary in new[] { 30, 60, 120, scenario is "following" or "rubbing" or "matched" ? 900 : 240 })
        {
            while (_world.State.Tick < (ulong)boundary) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
            if (_camera is not null)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(_output, $"{network}-{scenario}-{boundary}.png"));
            }
        }
        _running = false;
        string label = $"{(network ? "network" : "native")}-{scenario}";
        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, label + ".json"), JsonSerializer.Serialize(_trace, new JsonSerializerOptions { IncludeFields = true }));
        string evidence = $"{label}: contacts={_contacts} targetPeakYaw={_peak:F4} yawTravel={_yaw:F4} travelError={_travelError:F4} speedChange={_speedChange:F4}";
        GD.Print(evidence); System.IO.File.AppendAllText(System.IO.Path.Combine(_output, "evidence.txt"), evidence + "\n");
        if (scenario is "medium" or "high" or "heavy-target" or "heavy-striker" && (_peak < .5f || Math.Abs(_yaw) < .08f))
        { throw new InvalidOperationException("Rear-quarter impact did not destabilize the target."); }
        if (scenario == "low" && _peak > .2f) { throw new InvalidOperationException("Light rear-quarter tap causes excessive yaw."); }
        if (scenario is not "matched" && _contacts == 0) { throw new InvalidOperationException(label + " did not exercise vehicle contact."); }
        if (_world.State.Vehicles.Any(v => v.Movement.Physics.AngularVelocity.Length() > 8.01f)) { throw new InvalidOperationException("Unbounded contact rotation."); }
        if (scenario is "matched" or "rubbing" && _peak > .2f) { throw new InvalidOperationException("Incidental side contact causes excessive yaw."); }
        if (scenario == "following" && (_contacts < 120 || _travelError > .04f || _speedChange > .75f)) { throw new InvalidOperationException("Following contact is not stable."); }
        _native.Clear(); _network.Clear(); _fixture.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void AddCar(bool network, ulong id, VehiclePhysicsState pose, VehicleConfiguration tuning)
    {
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        _world.AddVehicle(id, tuning, damage, pose);
        if (network)
        {
            var body = new NetworkVehicleBody { VehicleId = id }; _fixture.AddChild(body); body.ApplyConfiguration(tuning); body.Apply(_world.GetVehicle(id)); _network.Add(id, body);
        }
        else
        {
            var body = new VehicleBody { VehicleId = id, Configuration = tuning, DamageConfiguration = damage,
                Paint = id == 1 ? new("2fd4df") : new("f0ab3c"), Position = VehicleBody.ToGodot(pose.Position),
                Quaternion = VehicleBody.ToGodot(pose.Orientation), LinearVelocity = VehicleBody.ToGodot(pose.LinearVelocity) };
            body.Initialize(_world); _fixture.AddChild(body); _native.Add(id, body);
        }
    }
}
