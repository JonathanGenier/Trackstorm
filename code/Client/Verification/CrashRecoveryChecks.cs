using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Controlled impacts through both production adapters, with rendered and per-tick evidence.</summary>
public sealed partial class CrashRecoveryChecks : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private VehicleBody? _native;
    private NetworkVehicleBody? _network;
    private Node3D _fixture = null!;
    private Camera3D? _camera;
    private bool _running;
    private bool _safe;
    private string _case = "";
    private string _output = "";
    private readonly List<object> _trace = new();
    private readonly List<string> _failures = new();
    private readonly MatchResourceLoader _resources = new(Core.Sessions.MatchMap.OldMap);
    private int _firstCrash;
    private int _recovered;
    private int _damageCount;
    private float _rotation;
    private float _peakRise;
    private float _peakRotation;
    private float _previousAngle;
    private N.Quaternion _lastOrientation;
    private bool _pitched;
    private string _filter = "";

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _Process(double delta)
    {
        if (_running) { _network?.PresentLocal((float)delta); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_running) { return; }
        ulong tick = _world.State.Tick + 1;
        // Hold deliberately conflicting controls only once the crash has begun. The
        // landing fixtures keep a neutral pilot to isolate successful tire landings.
        bool held = !_safe && _world.GetVehicle(1).Movement.CrashSeconds > 0;
        var input = new InputFrame(tick, held ? short.MaxValue : (short)0, held ? ushort.MaxValue : (ushort)0, 0,
            held ? InputButtons.AirControl | InputButtons.Drift : 0, 0, 0, held ? (short)-32767 : (short)0, 0, held ? (short)32767 : (short)0);
        var request = _native is not null ? _native.Capture(input) : new VehicleStepRequest(1, input, _network!.Observe(_world.GetVehicle(1)));
        var result = _world.Step(input, [request])[0];
        if (_native is not null) { _native.Apply(result); } else { _network!.Apply(result.Snapshot); }
        var s = result.Snapshot;
        var p = s.ObservedPhysics;
        var w = s.Movement.Wheels.Compression;
        int wheels = (w.X > 0 ? 1 : 0) + (w.Y > 0 ? 1 : 0) + (w.Z > 0 ? 1 : 0) + (w.W > 0 ? 1 : 0);
        if (s.Movement.CrashSeconds > 0 && _firstCrash == 0) { _firstCrash = (int)tick; }
        if (_firstCrash > 0 && wheels > 0 && _recovered == 0)
        {
            _recovered = (int)tick;
            if (!held || s.Movement.Throttle <= 0) { _failures.Add(_case + " did not restore held pedal input on first wheel contact"); }
        }
        _damageCount += result.DamageEvents.Count;
        if (_firstCrash > 0)
        {
            _peakRise = Math.Max(_peakRise, p.LinearVelocity.Y);
            _peakRotation = Math.Max(_peakRotation, p.AngularVelocity.Length());
        }
        float angle = 2 * MathF.Acos(Math.Clamp(Math.Abs(N.Quaternion.Dot(_lastOrientation, p.Orientation)), 0, 1));
        _rotation += angle;
        _previousAngle = Math.Max(_previousAngle, angle);
        _lastOrientation = p.Orientation;
        _pitched |= p.AngularVelocity.X < -0.4f;
        if (s.Movement.CrashSeconds > 0 && (s.Movement.Throttle != 0 || s.Movement.Air.Input != N.Vector3.Zero || s.Movement.Handbrake != 0))
        { _failures.Add(_case + " accepted control while crashing"); }
        if (_camera is not null)
        {
            Vector3 target = VehicleBody.ToGodot(p.Position);
            _camera.Position = target + new Vector3(8, 4, 8);
            _camera.LookAt(target);
        }
        _trace.Add(new { tick, position = new[] { p.Position.X, p.Position.Y, p.Position.Z },
            velocity = new[] { p.LinearVelocity.X, p.LinearVelocity.Y, p.LinearVelocity.Z },
            angular = new[] { p.AngularVelocity.X, p.AngularVelocity.Y, p.AngularVelocity.Z },
            up = N.Vector3.Transform(N.Vector3.UnitY, p.Orientation).Y, wheels,
            crash = s.Movement.CrashSeconds, hp = s.Damage.CurrentHP, damage = result.DamageEvents.Count,
            contacts = request.Observation.Contacts.Select(c => new { normal = new[] { c.Normal.X, c.Normal.Y, c.Normal.Z },
                point = new[] { c.LocalPosition.X, c.LocalPosition.Y, c.LocalPosition.Z }, c.Terrain,
                severity = VehicleDamageMath.CollisionSeverity(c.RelativeVelocity, c.Normal, c.Impulse, 3000) }).ToArray() });
    }

    private async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/crash-recovery-checks");
            System.IO.Directory.CreateDirectory(_output);
            _filter = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--crash-case=", StringComparison.Ordinal))?[13..] ?? "";
            while (!_resources.Complete) { _resources.Advance(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            if (DisplayServer.GetName() != "headless")
            {
                AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = new Color("829eb0"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.7f } });
                AddChild(new DirectionalLight3D { RotationDegrees = new(-60, -25, 0), LightEnergy = 1.3f, ShadowEnabled = true });
                _camera = new Camera3D { Current = true }; AddChild(_camera);
            }
            foreach (bool network in new[] { false, true })
            {
                foreach (float height in new[] { 13f, 25f, 45f })
                { await Scenario(network, $"wheels-{height}", new(0, height, 0), N.Quaternion.Identity, new(0, 0, -25), default, true); }
                await Scenario(network, "nose-fast", new(0, 3, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, -1.2f), new(0, -8, -32));
                await Scenario(network, "nose-slow", new(0, 3, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, -1.3f), new(0, -3, -6));
                await Scenario(network, "rear", new(0, 3, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, 1.4f), new(0, -8, 18));
                await Scenario(network, "side", new(0, 3, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, 1.6f), new(18, -8, -10));
                await Scenario(network, "roof", new(0, 2, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, MathF.PI), new(0, -7, -15));
                await Scenario(network, "roof-rest", new(0, 1.2f, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, MathF.PI), default);
                await Scenario(network, "awkward", new(0, 3, 0), N.Quaternion.CreateFromYawPitchRoll(0.3f, -1.2f, 1.7f), new(8, -10, -22));
                await Scenario(network, "multi-flip", new(0, 3, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, -1.1f), new(0, -10, -40), new(-6, 0, 0));
                await Scenario(network, "side-spin", new(0, 2, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, 1.6f), new(-18, -8, -10), new(0, 0, 6));
                await Scenario(network, "roof-spin", new(0, 1.4f, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, MathF.PI), new(-18, -8, -15), new(0, 0, 6));
            }
            if (_failures.Count > 0) { throw new InvalidOperationException(string.Join("\n", _failures.Distinct())); }
            GD.Print("Crash recovery checks passed."); GetTree().Quit();
        }
        catch (Exception exception) { _running = false; GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private async Task Scenario(bool network, string name, N.Vector3 position, N.Quaternion orientation, N.Vector3 velocity, N.Vector3 angular = default, bool safe = false)
    {
        _case = (network ? "network-" : "native-") + name;
        if (!_case.StartsWith(_filter, StringComparison.Ordinal)) { return; }
        _safe = safe; _firstCrash = 0; _recovered = 0; _damageCount = 0; _rotation = 0; _peakRise = 0; _peakRotation = 0; _previousAngle = 0; _pitched = false;
        _lastOrientation = orientation; _trace.Clear();
        _fixture = new Node3D(); AddChild(_fixture);
        var road = new StaticBody3D(); road.AddToGroup("landing_terrain");
        road.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(1000, 2, 1000) }, Position = new(0, -1, 0) });
        road.AddChild(VehicleBody.Box(new(1000, 2, 1000), new(0, -1, 0), new("847b65"))); _fixture.AddChild(road);
        if (_camera is not null)
        {
            // Visible distance references make sliding and body clearance reviewable.
            for (int mark = -20; mark <= 20; mark++)
            {
                road.AddChild(VehicleBody.Box(new(0.04f, 0.002f, 1000), new(mark * 10, 0.002f, 0), new("a2987e")));
                road.AddChild(VehicleBody.Box(new(1000, 0.002f, 0.04f), new(0, 0.002f, mark * 10), new("a2987e")));
            }
        }
        _world = new(new(60));
        var pose = new VehiclePhysicsState(position, orientation, velocity, angular);
        var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
        _world.AddVehicle(1, new(), damage, pose);
        if (network)
        {
            _network = new NetworkVehicleBody { VehicleId = 1 }; _fixture.AddChild(_network);
            _network.ApplyConfiguration(new()); _network.Apply(_world.GetVehicle(1));
        }
        else
        {
            _native = new VehicleBody { VehicleId = 1, DamageConfiguration = damage, Position = VehicleBody.ToGodot(position),
                Quaternion = VehicleBody.ToGodot(orientation), LinearVelocity = VehicleBody.ToGodot(velocity), AngularVelocity = VehicleBody.ToGodot(angular) };
            _native.Initialize(_world); _fixture.AddChild(_native);
        }
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); _running = true;
        while (_world.State.Tick < 540)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (_camera is not null && _world.State.Tick % 12 == 0 && _world.State.Tick <= 360)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var picture = GetViewport().GetTexture().GetImage();
                picture.SavePng(System.IO.Path.Combine(_output, $"{_case}-{_world.State.Tick:D3}.png"));
            }
        }
        _running = false;
        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, _case + ".json"), JsonSerializer.Serialize(_trace));
        var final = _world.GetVehicle(1);
        float up = N.Vector3.Transform(N.Vector3.UnitY, final.ObservedPhysics.Orientation).Y;
        GD.Print($"{_case}: firstCrash={_firstCrash}, wheels={_recovered}, hits={_damageCount}, hp={final.Damage.CurrentHP:F1}, rotation={_rotation:F2}, peakRise={_peakRise:F2}, peakSpin={_peakRotation:F2}, finalUp={up:F3}, maxStep={_previousAngle:F3}, forwardFlip={_pitched}");
        if (safe && (_damageCount != 0 || _firstCrash != 0)) { _failures.Add(_case + " damaged or crashed on tires"); }
        if (!safe && (_firstCrash == 0 || _recovered == 0 || _recovered - _firstCrash > 360)) { _failures.Add(_case + " failed to recover within six seconds"); }
        if (up < 0.85f || final.Movement.Wheels.Compression == N.Vector4.Zero) { _failures.Add(_case + " did not settle on tires"); }
        if (!safe && name != "roof-rest" && _damageCount == 0) { _failures.Add(_case + " missing body-impact damage"); }
        if (name == "nose-fast" && !_pitched) { _failures.Add(_case + " missing forward flip"); }
        if (name == "multi-flip" && (_rotation < MathF.Tau || _damageCount < 2)) { _failures.Add(_case + " did not exercise repeated rotation and separate body hits"); }
        if (name.EndsWith("-spin", StringComparison.Ordinal) && (_rotation < MathF.PI || _peakRotation < 3)) { _failures.Add(_case + " did not retain substantial impact rotation"); }
        if (final.ObservedPhysics.LinearVelocity.Length() > Math.Max(1, velocity.Length() * 0.25f) || final.ObservedPhysics.AngularVelocity.Length() > 0.2f) { _failures.Add(_case + " retained excessive crash energy after nine seconds"); }
        if (_previousAngle > 0.15f) { _failures.Add(_case + " abrupt orientation change"); }
        _native = null; _network = null; _fixture.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
