using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Rendered lifecycle/scaling fixture using production vehicle presentation and the active map.</summary>
public sealed partial class BoostExhaustChecks : Node3D
{
    private readonly List<NetworkVehicleBody> _cars = new();
    private readonly List<double> _frameTimes = new();
    private Camera3D _camera = null!;
    private Label _label = null!;
    private float _time;
    private int _frame;
    private bool _capturePending;
    private int _capture;
    private bool _reseeded;
    private readonly float[] _captureTimes = [1, 2.12f, 2.31f, 3, 4.5f, 6.04f, 6.4f, 7, 8.2f, 9.32f, 13, 17, 21, 25];
    private readonly string[] _captureNames = ["idle", "deployment", "ignition", "sustain-a", "sustain-b", "release", "retract", "decayed", "cancelled", "reignite", "chase", "eight", "thirty-two", "cutoff"];

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        AddChild(new Arenas.EnvironmentPresentation());
        AddChild(Networking.MatchResourceLoader.LoadResource<PackedScene>(Arenas.ActiveMap.ScenePath).Instantiate());
        for (int i = 0; i < 32; i++)
        {
            var car = new NetworkVehicleBody { VehicleId = (ulong)i + 1 };
            AddChild(car);
            _cars.Add(car);
        }
        _camera = new Camera3D { Fov = 65, Position = new Vector3(5, 4, 9) };
        AddChild(_camera);
        _camera.MakeCurrent();
        var layer = new CanvasLayer();
        AddChild(layer);
        _label = new Label { Position = new Vector2(18, 18) };
        _label.AddThemeFontSizeOverride("font_size", 22);
        layer.AddChild(_label);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://.godot/ts225-vfx"));
    }

    public override void _Process(double delta)
    {
        try
        {
            _time += (float)delta;
            _frame++;
            bool active = _time is >= 2 and < 6 or >= 8 and < 8.15f or >= 8.3f and < 8.5f or >= 9 and < 24;
            int count = _time < 16 ? 1 : _time < 20 ? 8 : 32;
            float speed = _time is >= 4 and < 12 ? 6 : 0;
            var anchor = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), new Vector3(0, .9f, 96));
            anchor.Origin += anchor.Basis * new Vector3(0, 0, -Math.Clamp(_time - 4, 0, 8) * 6);
            for (int i = 0; i < _cars.Count; i++)
            {
                Vector3 position = anchor * new Vector3((i % 4) * 4, 0, -(i / 4) * 7);
                var rotation = anchor.Basis.GetRotationQuaternion();
                var physics = new VehiclePhysicsState(VehicleBody.ToCore(position), new N.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(-anchor.Basis.Z * speed), N.Vector3.Zero);
                var movement = new VehicleState((ulong)_frame, physics, true, false, 0, 0, SurfaceType.Asphalt,
                    nitro: active && i < count ? new NitroState(60, 18000, 1.4f, 1) : default);
                var state = new VehicleSnapshot((ulong)i + 1, _time < 26 ? 1ul : 2ul, movement, new VehicleDamageState(1000, _time < 26.5f ? 1000 : 0, null, null), physics);
                _cars[i].Apply(state);
                _cars[i].PresentRemote(physics);
                _cars[i].Visible = i < count;
                // Deliberately exercise the full rack sweep alongside Boost, independent of inventory gameplay.
                var item = _time < 5 || _time >= 10 ? Core.Items.HeldItem.Missile : Core.Items.HeldItem.Nitro;
                _cars[i].Rack.Observe(state.LifeId, state.CanInteract,
                    new Core.Items.ItemSlot(state.VehicleId, state.LifeId, item == Core.Items.HeldItem.Missile ? 1ul : 2ul, item), []);
                if (i == 0 && _time > 22 && !_reseeded)
                {
                    _cars[i].Reseed(state);
                    var exhaust = FindExhaust(_cars[i]);
                    if (exhaust.FlameVisible || exhaust.SmokeEmitting || exhaust.Deployment != 0) { throw new InvalidOperationException("Reseed retained historical exhaust."); }
                    _reseeded = true;
                }
            }
            _camera.Position = anchor * (_time < 12 ? new Vector3(5, 3.5f, 8) : _time < 16 ? new Vector3(0, 4.4f, 10) : _time < 20 ? new Vector3(8, 12, 20) : new Vector3(15, 23, 25));
            _camera.LookAt(anchor * (_time < 16 ? new Vector3(0, .6f, .6f) : new Vector3(6, 0, -12)));
            _label.Text = $"TS-225 · {(active ? "BOOST" : "RELEASE / IDLE")} · {count} vehicles · {speed:0} m/s\n{_time:0.00} s · {Engine.GetFramesPerSecond()} FPS · Space: restart sequence";
            if (_time is > 21 and < 24) { _frameTimes.Add(delta * 1000); }
            if (_capture < _captureTimes.Length && _time >= _captureTimes[_capture] && !_capturePending) { Capture(_capture++, active); }
            if (_time > 27)
            {
                foreach (var car in _cars)
                {
                    var exhaust = FindExhaust(car);
                    if (exhaust.FlameVisible || exhaust.SmokeEmitting || exhaust.Deployment != 0) { throw new InvalidOperationException("Cutoff/new-life left active exhaust."); }
                }
                _frameTimes.Sort();
                GD.Print($"Boost VFX checks passed: repeated release/reignite, 1/8/32 production network visuals, new life; 32-car frame p95={_frameTimes[(int)(_frameTimes.Count * .95)]:0.00} ms (60 FPS cap). Render captures in .godot/ts225-vfx.");
                if (OS.GetCmdlineUserArgs().Contains("--boost-loop")) { _time = 0; _capture = 0; _frameTimes.Clear(); _reseeded = false; }
                else { GetTree().Quit(); }
            }
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Space }) { _time = 0; _capture = 0; _reseeded = false; }
    }

    private async void Capture(int index, bool active)
    {
        _capturePending = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var exhaust = FindExhaust(_cars[0]);
        bool expectedFlame = active && _captureNames[index] != "deployment";
        if (exhaust.FlameVisible != expectedFlame || exhaust.SmokeEmitting != expectedFlame ||
            (_captureNames[index] == "deployment" && (exhaust.Deployment <= 0 || exhaust.Deployment >= 1)))
        {
            GD.PushError($"Boost presentation mismatch at {_captureNames[index]}.");
            GetTree().Quit(1);
            return;
        }
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"res://.godot/ts225-vfx/{_captureNames[index]}.png");
        _capturePending = false;
    }

    private static BoostExhaust FindExhaust(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is BoostExhaust exhaust) { return exhaust; }
            if (child is Node3D && child.GetChildCount() > 0)
            {
                var found = child.FindChild("BoostExhaust", true, false) as BoostExhaust;
                if (found is not null) { return found; }
            }
        }
        throw new InvalidOperationException("Production vehicle missing Boost exhaust.");
    }
}
