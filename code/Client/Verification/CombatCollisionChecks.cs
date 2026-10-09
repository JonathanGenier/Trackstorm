using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises combat impact geometry, damage and loaded propulsion through both native adapters.</summary>
public sealed partial class CombatCollisionChecks : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private readonly Dictionary<ulong, VehicleBody> _native = new();
    private readonly Dictionary<ulong, NetworkVehicleBody> _network = new();
    private readonly List<object> _trace = new();
    private readonly List<object> _summary = new();
    private readonly Dictionary<string, (float Yaw, float Travel)> _comparisons = new();
    private Node3D _fixture = null!;
    private Camera3D? _camera;
    private bool _running;
    private string _case = "", _output = "";
    private float _peakYaw, _yawTravel, _peakVertical, _firstDamage, _secondDamage;
    private int _contacts;
    private N.Vector3 _start;

    public override void _Ready() => CallDeferred(MethodName.Run);
    public override void _Process(double delta)
    {
        foreach (var body in _network.Values) { body.PresentLocal((float)delta); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_running) { return; }
        try
        {
            ulong tick = _world.State.Tick + 1;
            InputFrame Input(ulong id) => new(tick, 0,
                (ushort)((_case.StartsWith("push", StringComparison.Ordinal) && id == 2 ||
                    _case.StartsWith("hill", StringComparison.Ordinal)) ? ushort.MaxValue : 0), 0, 0, 0, 0);
            var observations = _network.Count > 0 ? NetworkVehicleBody.ObserveBatch(_network, _world.State.Vehicles) : null;
            var requests = _native.Count > 0 ? VehicleBody.CaptureBatch(_native.Values, body => Input(body.VehicleId)) :
                observations!.Select(pair => new VehicleStepRequest(pair.Key, Input(pair.Key), pair.Value)).ToArray();
            foreach (var result in _world.Step(Input(1), requests))
            {
                ulong id = result.Snapshot.VehicleId;
                if (_native.TryGetValue(id, out var body)) { body.Apply(result); } else { _network[id].Apply(result.Snapshot); }
                var p = result.Snapshot.Movement.Physics;
                var contacts = requests.Single(r => r.VehicleId == id).Observation.Contacts;
                _contacts += contacts.Count(c => c.OtherVehicleId != 0);
                if (id == 1) { _peakYaw = Math.Max(_peakYaw, Math.Abs(p.AngularVelocity.Y)); _yawTravel += p.AngularVelocity.Y / 60; }
                _peakVertical = Math.Max(_peakVertical, Math.Abs(p.LinearVelocity.Y));
                float damage = result.DamageEvents.Where(e => e.Attribution.Context == "vehicle").Sum(e => e.Amount);
                if (id == 1) { _firstDamage += damage; } else { _secondDamage += damage; }
                Require(p.AngularVelocity.Length() <= 8.01f && p.LinearVelocity.Length() <= 65.01f, "unbounded collision response");
                _trace.Add(new { tick, id, position = p.Position, velocity = p.LinearVelocity, angular = p.AngularVelocity,
                    orientation = p.Orientation, hp = result.Snapshot.Damage.CurrentHP,
                    sequence = result.Snapshot.Damage.LastDamage?.Sequence, contactCount = contacts.Count,
                    throttle = result.Snapshot.Movement.Throttle, drive = result.Snapshot.Movement.LongitudinalAcceleration });
            }
            if (_camera is not null)
            {
                var center = VehicleBody.ToGodot(_world.GetVehicle(1).Movement.Physics.Position);
                _camera.Position = center + new Vector3(11, 12, 15); _camera.LookAt(center);
            }
        }
        catch (Exception exception) { _running = false; GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/ts-288/combat");
            System.IO.Directory.CreateDirectory(_output);
            if (DisplayServer.GetName() != "headless")
            {
                Engine.MaxFps = 60;
                AddChild(new DirectionalLight3D { RotationDegrees = new(-60, -25, 0), LightEnergy = 1.5f });
                _camera = new Camera3D { Current = true }; AddChild(_camera);
            }
            foreach (bool network in new[] { false, true })
            foreach (string scenario in new[] { "30-60", "60-30", "stationary", "equal", "rear", "heavy-target", "heavy-striker",
                "center", "front", "rear-side", "push-baseline", "push", "hill-baseline", "hill" })
            {
                string? selected = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--combat-case=", StringComparison.Ordinal));
                if (selected is not null && scenario != selected[14..]) { continue; }
                await Exercise(network, scenario);
                // Drain freed wrappers before the next fixture reuses native resource handles.
                GC.Collect(); GC.WaitForPendingFinalizers();
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "summary.json"), JsonSerializer.Serialize(_summary));
            GD.Print("Combat collision integration passed."); GetTree().Quit();
        }
        catch (Exception exception) { _running = false; GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Exercise(bool network, string scenario)
    {
        _case = scenario; _trace.Clear(); _peakYaw = 0; _yawTravel = 0; _contacts = 0; _peakVertical = 0; _firstDamage = 0; _secondDamage = 0;
        _fixture = new Node3D(); AddChild(_fixture);
        bool hill = scenario.StartsWith("hill", StringComparison.Ordinal);
        bool pushing = scenario.StartsWith("push", StringComparison.Ordinal);
        bool side = scenario is "center" or "front" or "rear-side";
        float slope = hill ? .35f : 0;
        var floor = new StaticBody3D { Rotation = new(slope, 0, 0) };
        floor.SetMeta("surface_identity", "Asphalt"); floor.AddToGroup("landing_terrain");
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(2000, 2, 2000) }, Position = new(0, -1, 0) });
        floor.AddChild(VehicleBody.Box(new(2000, 2, 2000), new(0, -1, 0), new("525d67"))); _fixture.AddChild(floor);
        _world = new(new(60));
        float targetSpeed = scenario switch { "30-60" => 30 / 3.6f, "60-30" => 60 / 3.6f, "equal" => 30 / 3.6f, "rear" => -30 / 3.6f, _ => 0 };
        float strikerSpeed = scenario is "60-30" or "equal" ? 30 / 3.6f : 60 / 3.6f;
        float offset = scenario == "front" ? -1.8f : scenario == "rear-side" ? 1.8f : 0;
        var target = new VehiclePhysicsState(new(0, VehicleDimensions.RideHeight, 0),
            N.Quaternion.CreateFromYawPitchRoll(targetSpeed > 0 ? MathF.PI : 0, slope, 0), new(0, 0, targetSpeed), N.Vector3.Zero);
        var striker = new VehiclePhysicsState(new(side ? -4 : 0, VehicleDimensions.RideHeight, side ? offset : 5.35f),
            N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, side ? -MathF.PI / 2 : 0),
            pushing ? N.Vector3.Zero : side ? new(strikerSpeed, 0, 0) : new(0, 0, -strikerSpeed), N.Vector3.Zero);
        var tuning = new VehicleConfiguration { LowSpeedDriveMultiplier = scenario.EndsWith("baseline", StringComparison.Ordinal) ? 1 : 1.6f };
        AddCar(network, 1, target, tuning with { Mass = scenario == "heavy-target" ? 4500 : 3000,
            Wheelbase = scenario == "heavy-target" ? 3.2f : VehicleDimensions.Wheelbase });
        if (!hill) { AddCar(network, 2, striker, tuning with { Mass = scenario == "heavy-striker" ? 4500 : 3000,
            Wheelbase = scenario == "heavy-striker" ? 3.2f : VehicleDimensions.Wheelbase }); }
        _start = target.Position;
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _running = true;
        foreach (int boundary in new[] { 15, 30, 60, 120, 240 })
        {
            while (_world.State.Tick < (ulong)boundary) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
            if (_camera is not null)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng(System.IO.Path.Combine(_output, $"{network}-{scenario}-{boundary}.png"));
            }
        }
        _running = false;
        string adapter = network ? "network" : "native", label = $"{adapter}-{scenario}";
        float travel = N.Vector3.Distance(_world.GetVehicle(1).Movement.Physics.Position, _start);
        var sample = new { label, contacts = _contacts, firstDamage = _firstDamage, secondDamage = _secondDamage,
            peakYaw = _peakYaw, yawTravel = _yawTravel, peakVertical = _peakVertical, targetTravel = travel };
        _summary.Add(sample); GD.Print(JsonSerializer.Serialize(sample));
        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, label + ".json"),
            JsonSerializer.Serialize(_trace, new JsonSerializerOptions { IncludeFields = true }));
        _comparisons[label] = (_peakYaw, travel);
        if (!hill) { Require(_contacts > 0, label + " did not contact"); }
        if (scenario is "30-60" or "stationary" or "rear" or "heavy-striker")
        { Require(_firstDamage > _secondDamage && _secondDamage > 0, label + " must damage the disadvantaged target more, with striker consequence"); }
        if (scenario == "60-30") { Require(_secondDamage > _firstDamage && _firstDamage > 0, "reversed roles must reverse damage advantage"); }
        if (scenario == "equal") { Require(Math.Abs(_firstDamage - _secondDamage) < 3, "equal impacts must remain balanced"); }
        if (scenario == "center") { Require(_peakYaw < .3f, "center-side impact should primarily translate"); }
        if (scenario is "front" or "rear-side" && _comparisons.TryGetValue(adapter + "-center", out var center))
        { Require(_peakYaw > center.Yaw + .35f, "eccentric side impact should visibly yaw the target"); }
        if (pushing || hill)
        {
            Require(travel > 2, label + " must make sustained progress");
            if (!scenario.EndsWith("baseline", StringComparison.Ordinal) &&
                _comparisons.TryGetValue(label + "-baseline", out var baseline))
            { Require(travel > baseline.Travel, label + " should improve loaded low-speed progress"); }
        }
        _native.Clear(); _network.Clear(); _fixture.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void AddCar(bool network, ulong id, VehiclePhysicsState pose, VehicleConfiguration tuning)
    {
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        _world.AddVehicle(id, tuning, damage, pose);
        if (network)
        {
            var body = new NetworkVehicleBody { VehicleId = id };
            _fixture.AddChild(body); body.ApplyConfiguration(tuning); body.Apply(_world.GetVehicle(id)); _network.Add(id, body);
        }
        else
        {
            var body = new VehicleBody { VehicleId = id, Configuration = tuning, DamageConfiguration = damage,
                Paint = id == 1 ? new("2fd4df") : new("f0ab3c"), Position = VehicleBody.ToGodot(pose.Position),
                Quaternion = VehicleBody.ToGodot(pose.Orientation), LinearVelocity = VehicleBody.ToGodot(pose.LinearVelocity) };
            body.Initialize(_world); _fixture.AddChild(body); _native.Add(id, body);
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } }
}
