using Godot;
using Trackstorm.Client.Input;
using Trackstorm.Client.Settings;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Repeatable native driving and aerial framing observation with real production cars.</summary>
public sealed partial class CameraResponsivenessPlaytest : Node3D
{
    private static readonly string[] Phases = ["low-speed", "high-speed", "sharp-reversal", "handbrake-slide", "bumps", "short-hop", "jump", "front-flip", "barrel-roll", "backflip", "crash-recovery", "minimum-settings", "maximum-settings"];
    private readonly List<VehicleBody> _cars = [];
    private readonly List<string> _trace = ["frame,phase,speed,grounded,air_seconds,up,pullback,inertia_x,inertia_z,camera_x,camera_y,camera_z"];
    private Core.Simulation.Simulation _world = null!;
    private VehicleChaseCamera _camera = null!;
    private PlayerSettingsController _settings = null!;
    private PlayerInput _input = null!;
    private Label _label = null!;
    private string _output = "";
    private int _frame;
    private bool _done;
    private int _airFrames;
    private int _groundFrames;
    private int _slidingFrames;
    private readonly HashSet<int> _inverted = [];
    private float _maximumPullback;

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _output = OS.GetCmdlineUserArgs().First(value => value.StartsWith("--camera-output="))[16..];
        Directory.CreateDirectory(_output);
        AddChild(new DirectionalLight3D { RotationDegrees = new(-50, -25, 0), LightEnergy = 1.4f });
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new(.3f, .43f, .6f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .65f } });
        Solid(new(0, -.5f, 0), new(700, 1, 700), new(.23f, .28f, .27f));
        for (int i = 0; i < 8; i++) Solid(new(0, .06f, -12 - i * 8), new(8, .12f, 1), new(.65f, .55f, .25f));
        Solid(new(50, 1.5f, -25), new(12, 3, 1), new(.5f, .28f, .22f));
        _world = new(new(60));
        for (int i = 0; i < 2; i++)
        {
            _world.AddVehicle((ulong)i + 1, new(), new() { MaxHP = 100000 }, Pose(i, 0));
            var car = new VehicleBody { VehicleId = (ulong)i + 1, DamageConfiguration = new() { MaxHP = 100000 } };
            car.Initialize(_world); AddChild(car); _cars.Add(car);
        }
        _input = new PlayerInput(); AddChild(_input); _input.SetPhysicsProcess(false);
        _input.GameplayAvailable = () => true;
        _settings = new PlayerSettingsController(); _settings.Initialize(_input.Adapter, _output + "/settings.json"); AddChild(_settings);
        _camera = new VehicleChaseCamera { Current = true, Fov = 65, Far = 1000, SettingsSource = _settings, InputSource = _input.Adapter }; AddChild(_camera);
        var layer = new CanvasLayer(); AddChild(layer);
        _label = new Label { Position = new(20, 20) }; layer.AddChild(_label);
        AddChild(new Hud.CombatHud { Vehicle = () => _cars[0].Snapshot });
    }

    private void Solid(Vector3 position, Vector3 size, Color color)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = 1 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = color } });
        AddChild(body);
    }

    private static VehiclePhysicsState Pose(int car, int phase)
    {
        bool air = car == 0 && phase is >= 6 and <= 9 or >= 11;
        float height = air ? 10 : phase == 5 && car == 0 ? 1.4f : 1.2f;
        float speed = phase == 0 ? 5 : phase == 1 ? 58 : 26;
        N.Quaternion rotation = phase == 10 && car == 0 ? N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, 2.8f) : N.Quaternion.Identity;
        return new(new(car * 9 + (phase == 4 ? 0 : 30), height, 25), rotation, new(0, air ? 10 : 0, -speed), N.Vector3.Zero);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            int phase = _frame / 360;
            int local = _frame % 360;
            if (phase >= Phases.Length) { Finish(); return; }
            if (local == 0)
            {
                _settings.UpdateSettings(_settings.Current with { CameraDistance = phase == 12 ? 1.5 : 1, CameraInertia = phase == 11 ? 0 : phase == 12 ? 1 : .5, CameraAerialPullback = phase == 11 ? 0 : phase == 12 ? 1.5 : 1 });
                for (int i = 0; i < _cars.Count; i++) _cars[i].ResetBody(Pose(i, phase));
            }
            var requests = new List<VehicleStepRequest>();
            for (int i = 0; i < _cars.Count; i++)
            {
                short steer = i == 0 && phase is 2 or 3 ? (short)(local % 120 < 60 ? 28000 : -28000) : (short)0;
                bool rotate = i == 0 && phase is >= 7 and <= 9 && local < 135;
                InputButtons buttons = rotate ? InputButtons.AirControl : i == 0 && phase == 3 && local < 160 ? InputButtons.Drift : 0;
                var input = new InputFrame(_world.State.Tick + 1, steer, phase <= 4 ? ushort.MaxValue : (ushort)0, 0, buttons, 0, 0,
                    airPitch: rotate && phase != 8 ? (short)(phase == 7 ? -32767 : 32767) : (short)0, airRoll: rotate && phase == 8 ? (short)32767 : (short)0);
                requests.Add(_cars[i].Capture(input));
            }
            var result = _world.Step(requests[0].Input, requests);
            for (int i = 0; i < _cars.Count; i++) _cars[i].Apply(result[i]);
            _frame++;
        }
        catch (Exception ex) { Fail(ex); }
    }

    public override void _Process(double delta)
    {
        if (_done || _frame == 0) return;
        try
        {
            int phase = Math.Min(Phases.Length - 1, (_frame - 1) / 360);
            var state = _cars[0].Snapshot;
            Transform3D pose = _cars[0].GetGlobalTransformInterpolated();
            _camera.Follow(pose, state, (float)delta, _cars[0].GetRid());
            Require(_camera.GlobalTransform.IsFinite(), "Finite camera throughout native motion");
            Require(Math.Abs(_camera.Motion.Offset.X) <= .241f && Math.Abs(_camera.Motion.Offset.Y) <= .401f, "Inertia remains tight at every supported setting");
            using var sphere = new SphereShape3D { Radius = .2f };
            using var query = new PhysicsShapeQueryParameters3D { Shape = sphere, Transform = new(Basis.Identity, _camera.GlobalPosition), CollisionMask = 1, Margin = 0 };
            Require(GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0, "Lens clears world and followed chassis");
            if (state.Movement.Grounded) _groundFrames++; else _airFrames++;
            if (state.Movement.Drifting) _slidingFrames++;
            if (pose.Basis.Y.Y < -.5f) _inverted.Add(phase);
            _maximumPullback = Math.Max(_maximumPullback, _camera.AerialMotion.Pullback);
            _label.Text = $"TS-279 | {Phases[phase]}\n{state.Speed * 3.6:F0} km/h | air {state.Movement.Air.Seconds:F2}s | pullback {_camera.AerialMotion.Pullback:F2}m\nDistance {_settings.Current.CameraDistance:F2} / inertia {_settings.Current.CameraInertia:F2} / aerial {_settings.Current.CameraAerialPullback:F2}";
            Vector3 p = _camera.GlobalPosition;
            _trace.Add(FormattableString.Invariant($"{_frame},{Phases[phase]},{state.Speed},{state.Movement.Grounded},{state.Movement.Air.Seconds},{pose.Basis.Y.Y},{_camera.AerialMotion.Pullback},{_camera.Motion.Offset.X},{_camera.Motion.Offset.Y},{p.X},{p.Y},{p.Z}"));
            if (_frame % 15 == 0 && OS.GetCmdlineUserArgs().Contains("--camera-captures")) Capture(_frame);
        }
        catch (Exception ex) { Fail(ex); }
    }

    private async void Capture(int frame)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        image.SavePng($"{_output}/{frame:D5}.png");
    }

    private void Finish()
    {
        File.WriteAllLines(_output + "/trace.csv", _trace);
        Require(_groundFrames > 300 && _airFrames > 300 && _slidingFrames > 30, "Ground, sustained air and real sliding exercised");
        Require(new[] { 7, 8, 9 }.All(_inverted.Contains), "Front flip, barrel roll and backflip reached inverted poses");
        Require(_maximumPullback > 5, "Maximum aerial setting exercised");
        GD.Print($"Camera responsiveness playtest passed: ground={_groundFrames}, air={_airFrames}, slide={_slidingFrames}, inverted={string.Join(',', _inverted)}, maxPullback={_maximumPullback:F3}m.");
        _done = true; GetTree().Quit();
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Fail(Exception ex) { File.WriteAllLines(_output + "/trace.csv", _trace); GD.PushError(ex.ToString()); _done = true; GetTree().Quit(1); }
}
